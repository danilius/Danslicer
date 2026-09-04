using System.Numerics;
using Danslicer.Core;
using Danslicer.Core.Config;
using Danslicer.Core.Printers;
using Danslicer.Core.Scene;
using Silk.NET.OpenGL;

namespace Danslicer.Render;

/// <summary>
/// The deferred path (design 6.2): geometry to a G-buffer, fullscreen lighting/cavity/outline
/// composite, forward ghosted and overlay passes depth-tested against the opaque scene, FXAA to the
/// host framebuffer. The classic path in SceneRenderer.cs is untouched; everything here is reached
/// only through <see cref="TryRenderDeferred"/>.
/// </summary>
public sealed partial class SceneRenderer
{
    private static readonly Vector3 BackgroundColor = new(0.155f, 0.16f, 0.175f);
    private static readonly Vector3 OutlineColor = new(0.02f, 0.025f, 0.035f);
    private static readonly Vector3 SelectionOutlineColor = new(1f, 0.75f, 0.30f);

    private DeferredPipeline? _deferred;
    private bool _deferredFailed;
    private ViewCube? _viewCube;
    // Index = the draw ID written to the G-buffer this frame; the entry is the scene object a
    // pick of that ID selects (null for background, plate and aux geometry). Rebuilt every
    // deferred frame so ids and targets cannot drift apart.
    private readonly List<SceneObject?> _pickTargets = [];
    private bool _pickTargetsValid;

    /// <summary>True when the last frame drew deferred, so <see cref="TryPickObject"/> can run.</summary>
    public bool CanPickDeferred => _pickTargetsValid && _deferred is not null;

    /// <summary>
    /// Design 6.5: resolves the object under a framebuffer pixel (origin bottom-left) from the
    /// last deferred frame's ID buffer. False when that frame is not available (classic path or
    /// pipeline failure) — the caller falls back to CPU ray picking. A true result with a null
    /// hit means background, plate or support/aux geometry. GL context must be current.
    /// </summary>
    public bool TryPickObject(int x, int y, out SceneObject? hit)
    {
        hit = null;
        if (!CanPickDeferred) return false;
        var pipeline = _deferred!;
        if (x < 0 || y < 0 || x >= pipeline.Width || y >= pipeline.Height) return false;
        var id = pipeline.ReadId(x, y);
        if (id >= 0 && id < _pickTargets.Count) hit = _pickTargets[id];
        return true;
    }

    private int RegisterPick(SceneObject? target)
    {
        _pickTargets.Add(target);
        return _pickTargets.Count - 1;
    }

    /// <summary>True when the frame was drawn deferred; false latches Classic for the session.</summary>
    private bool TryRenderDeferred(RenderFrame frame)
    {
        if (_deferredFailed) return false;
        try
        {
            RenderDeferredCore(frame);
            return true;
        }
        catch (Exception e)
        {
            // Driver or context cannot run the pipeline (incomplete FBO, compile failure). Log
            // once, drop the resources and let every later frame take the classic path.
            Console.Error.WriteLine($"[render] Deferred pipeline failed; falling back to Classic: {e}");
            _deferred?.Dispose();
            _deferred = null;
            _deferredFailed = true;
            return false;
        }
    }

    private void RenderDeferredCore(RenderFrame frame)
    {
        var gl = _gl;
        _deferred ??= new DeferredPipeline(gl, IsGles);
        var pipeline = _deferred;
        pipeline.EnsureTargets(frame.Width, frame.Height);

        var aspect = frame.Width / (float)Math.Max(frame.Height, 1);
        var view = frame.Camera.View;
        var projection = frame.Camera.Projection(aspect);
        _overhangColorA = frame.OverhangColorA;
        _overhangColorB = frame.OverhangColorB;
        _overhangCell = frame.OverhangCheckerSizeMm;

        PruneMeshCache(frame);
        var plateFaded = frame.Camera.Eye.Z < 0f && frame.PlateOpacityFromBelow < 1f;

        DrawGeometryPass(frame, view, projection, plateFaded);
        DrawCompositePass(frame, view, projection);
        DrawForwardPasses(frame, view, projection, plateFaded);
        ResolveToHost(frame);

        gl.BindVertexArray(0);
        gl.UseProgram(0);
        _pickTargetsValid = true; // full frame drawn; the ID buffer and registry now agree
    }

    /// <summary>All opaque geometry into the G-buffer: plate, normal objects, opaque aux meshes.</summary>
    private void DrawGeometryPass(RenderFrame frame, in Matrix4x4 view, in Matrix4x4 projection,
        bool plateFaded)
    {
        var gl = _gl;
        var pipeline = _deferred!;
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, pipeline.GBufferFbo);
        gl.Viewport(0, 0, (uint)frame.Width, (uint)frame.Height);
        gl.Enable(EnableCap.DepthTest);
        gl.DepthFunc(DepthFunction.Lequal);
        gl.Disable(EnableCap.CullFace);
        gl.Disable(EnableCap.Blend);
        gl.DepthMask(true);
        pipeline.ClearGBuffer(BackgroundColor);

        // IDs are frame-local; they only need to differ between adjacent draws for the outline
        // and picking passes. The registry records what each ID selects: 0 stays background,
        // then plate, objects and aux meshes in draw order.
        _pickTargets.Clear();
        _pickTargets.Add(null); // 0 = background
        var plateId = RegisterPick(null);
        if (!plateFaded)
        {
            EnsurePlateMesh(frame.Printer);
            var model = Matrix4x4.CreateTranslation(0, 0, -0.05f);
            BindGBufferShader(model, view, projection, PlateColor, backfaceTint: 0f,
                warnBelowPlate: false, overhangCos: 2f, plateId, selected: false, clip: default);
            _plate!.Draw();
        }

        foreach (var obj in frame.Scene.Objects)
        {
            var skip = obj.RenderState is RenderState.Hidden or RenderState.Ghosted;
            var id = RegisterPick(skip ? null : obj);
            if (skip) continue;

            if (!_meshes.TryGetValue(obj.Mesh, out var gpu))
            {
                gpu = new GpuMesh(gl, obj.Mesh);
                _meshes[obj.Mesh] = gpu;
            }

            var selected = frame.IsSelected(obj);
            var color = selected ? SelectedColor
                : obj.RenderState == RenderState.Highlighted ? Vector3.Lerp(ObjectColor, SelectedColor, 0.4f)
                : ObjectColor;
            var overhangCos = frame.ShowOverhangs
                ? MathF.Sin(Math.Clamp(frame.OverhangAngleDegrees, 1f, 89f) * MathF.PI / 180f)
                : 2f;
            BindGBufferShader(obj.Transform.ToMatrix(), view, projection, color, backfaceTint: 1f,
                warnBelowPlate: true, overhangCos, id, selected, frame.ClipRange);
            gpu.Draw();
        }

        foreach (var draw in frame.AuxMeshes)
        {
            var id = RegisterPick(null);
            if (draw.Opacity < 1f) continue; // transparent aux meshes blend in the forward stage

            if (!_meshes.TryGetValue(draw.Mesh, out var gpu))
            {
                gpu = new GpuMesh(gl, draw.Mesh);
                _meshes[draw.Mesh] = gpu;
            }
            // A DepthOverlay draw (the selection highlight twin) wins EQUAL depth under Lequal,
            // which is already this pass's depth func; it flags selected for the outline colour.
            BindGBufferShader(Matrix4x4.Identity, view, projection, draw.Color, backfaceTint: 0f,
                warnBelowPlate: false, overhangCos: 2f, id, selected: draw.DepthOverlay,
                frame.ClipRange);
            gpu.Draw();
        }
    }

    private void BindGBufferShader(in Matrix4x4 model, in Matrix4x4 view, in Matrix4x4 projection,
        Vector3 color, float backfaceTint, bool warnBelowPlate, float overhangCos, int id, bool selected,
        ViewportClipRange clip)
    {
        Matrix4x4.Invert(model * view, out var inverse);
        var normalMatrix = Matrix4x4.Transpose(inverse);
        Matrix4x4.Invert(model, out var modelInverse);
        var modelNormalMatrix = Matrix4x4.Transpose(modelInverse);

        var shader = _deferred!.GBufferShader;
        shader.Use();
        shader.Set("uModel", model);
        shader.Set("uView", view);
        shader.Set("uProjection", projection);
        shader.Set("uNormalMatrix", normalMatrix);
        shader.Set("uModelNormalMatrix", modelNormalMatrix);
        shader.Set("uColor", color);
        shader.Set("uBackfaceTint", backfaceTint);
        shader.Set("uWarnBelowPlate", warnBelowPlate ? 1f : 0f);
        shader.Set("uOverhangCos", overhangCos);
        shader.Set("uOverhangColorA", _overhangColorA);
        shader.Set("uOverhangColorB", _overhangColorB);
        shader.Set("uOverhangCell", _overhangCell);
        shader.Set("uId", DeferredIds.Pack(id));
        shader.Set("uSelected", selected ? 1f : 0f);
        BindClip(shader, clip);
    }

    /// <summary>Fullscreen lighting (studio or MatCap), cavity and outlines into the scene target.</summary>
    private void DrawCompositePass(RenderFrame frame, in Matrix4x4 view, in Matrix4x4 projection)
    {
        var gl = _gl;
        var pipeline = _deferred!;
        var effects = frame.Deferred;
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, pipeline.CompositeFbo);
        gl.Disable(EnableCap.DepthTest);
        gl.DepthMask(false);

        BindTexture(0, pipeline.AlbedoTexture);
        BindTexture(1, pipeline.NormalTexture);
        BindTexture(2, pipeline.IdTexture);
        BindTexture(3, pipeline.DepthTexture);
        BindTexture(4, pipeline.MatCap(effects.Shading switch
        {
            ViewportShadingMode.MatCapMetal => MatCapStyle.Metal,
            ViewportShadingMode.MatCapPearl => MatCapStyle.Pearl,
            _ => MatCapStyle.Clay,
        }));

        Matrix4x4.Invert(projection, out var invProjection);
        Matrix4x4.Invert(view, out var invView);
        var shader = pipeline.CompositeShader;
        shader.Use();
        shader.Set("uInvProjection", invProjection);
        shader.Set("uInvView", invView);
        shader.Set("uWaterlineEnabled", frame.WaterlineZ.HasValue ? 1f : 0f);
        shader.Set("uWaterlineZ", frame.WaterlineZ.GetValueOrDefault());
        shader.Set("uTexel", TexelSize(frame));
        shader.Set("uShadingMode", effects.Shading == ViewportShadingMode.Studio ? 0 : 1);
        shader.Set("uCavityRidge", effects.CavityEnabled ? effects.CavityRidgeStrength : 0f);
        shader.Set("uCavityValley", effects.CavityEnabled ? effects.CavityValleyStrength : 0f);
        shader.Set("uCavityRadius", effects.CavityRadiusPixels);
        shader.Set("uOutlineStrength", effects.OutlinesEnabled ? effects.OutlineStrength : 0f);
        shader.Set("uOutlineColor", OutlineColor);
        shader.Set("uSelectColor", SelectionOutlineColor);
        pipeline.DrawFullscreen();

        gl.ActiveTexture(TextureUnit.Texture0);
        gl.Enable(EnableCap.DepthTest);
        gl.DepthMask(true);
    }

    /// <summary>
    /// Ghosted and transparent geometry plus the overlay lines, blended over the composited scene
    /// while depth-testing against the opaque depth buffer. Reuses the classic passes verbatim, in
    /// the classic order.
    /// </summary>
    private void DrawForwardPasses(RenderFrame frame, in Matrix4x4 view, in Matrix4x4 projection,
        bool plateFaded)
    {
        var gl = _gl;
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, _deferred!.ForwardFbo);
        gl.DepthFunc(DepthFunction.Lequal);

        DrawTransparentAuxMeshes(frame, view, projection);
        if (plateFaded && frame.PlateOpacityFromBelow > 0.001f)
            DrawPlate(frame.Printer, view, projection, frame.PlateOpacityFromBelow);
        DrawObjects(frame, view, projection, ghosted: true);
        DrawLines(frame, view * projection);
    }

    /// <summary>The classic aux-mesh pass restricted to the blended draws.</summary>
    private void DrawTransparentAuxMeshes(RenderFrame frame, in Matrix4x4 view, in Matrix4x4 projection)
    {
        var gl = _gl;
        foreach (var draw in frame.AuxMeshes)
        {
            if (draw.Opacity >= 1f) continue;
            if (!_meshes.TryGetValue(draw.Mesh, out var gpu))
            {
                gpu = new GpuMesh(gl, draw.Mesh);
                _meshes[draw.Mesh] = gpu;
            }

            gl.Enable(EnableCap.Blend);
            gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
            gl.DepthMask(false);
            if (draw.DepthOverlay) gl.DepthFunc(DepthFunction.Lequal);
            // Clip but no waterline, matching the classic DrawAuxMeshes pass exactly.
            BindMeshShader(Matrix4x4.Identity, view, projection, draw.Color, draw.Opacity,
                backfaceTint: 0f, warnBelowPlate: false, overhangCos: 2f,
                frame.ClipRange);
            gpu.Draw();
            if (draw.DepthOverlay) gl.DepthFunc(DepthFunction.Less);
            gl.Disable(EnableCap.Blend);
            gl.DepthMask(true);
        }
    }

    /// <summary>FXAA (or a plain copy) from the scene target into the host framebuffer.</summary>
    private void ResolveToHost(RenderFrame frame)
    {
        var gl = _gl;
        var pipeline = _deferred!;
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, (uint)frame.Framebuffer);
        gl.Disable(EnableCap.DepthTest);
        gl.Disable(EnableCap.Blend);
        gl.DepthMask(false);

        BindTexture(0, pipeline.SceneTexture);
        var shader = frame.Deferred.FxaaEnabled ? pipeline.FxaaShader : pipeline.PassthroughShader;
        shader.Use();
        if (frame.Deferred.FxaaEnabled) shader.Set("uTexel", TexelSize(frame));
        pipeline.DrawFullscreen();

        // Leave the state the classic path would: depth on, writes on, blend off.
        gl.Enable(EnableCap.DepthTest);
        gl.DepthMask(true);
    }

    private void BindTexture(int unit, uint texture)
    {
        _gl.ActiveTexture(TextureUnit.Texture0 + unit);
        _gl.BindTexture(TextureTarget.Texture2D, texture);
    }

    private static Vector2 TexelSize(RenderFrame frame) =>
        new(1f / Math.Max(frame.Width, 1), 1f / Math.Max(frame.Height, 1));

    /// <summary>Same plate cache rule as the classic DrawPlate, without drawing.</summary>
    private void EnsurePlateMesh(PrinterDefinition printer)
    {
        if (_plate is not null && _plateSize == printer.BuildVolume) return;
        _plate?.Dispose();
        _plate = new GpuMesh(_gl, GpuMesh.CreatePlate(printer.BuildVolume.X, printer.BuildVolume.Y));
        _plateSize = printer.BuildVolume;
    }
}
