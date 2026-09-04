using System.Numerics;
using Danslicer.Core;
using Danslicer.Core.Config;
using Danslicer.Core.Geometry;
using Danslicer.Core.Printers;
using Danslicer.Core.Scene;
using Silk.NET.OpenGL;

namespace Danslicer.Render;

/// <summary>
/// A derived mesh drawn in world space with a flat colour (e.g. support capsules). With
/// <paramref name="DepthOverlay"/> the mesh passes the depth test at EQUAL depth too, so a
/// highlight copy of geometry already drawn this frame wins cleanly instead of z-fighting.
/// </summary>
public readonly record struct AuxMeshDraw(Mesh Mesh, Vector3 Color, float Opacity,
    bool DepthOverlay = false);

public sealed class RenderFrame
{
    public required int Framebuffer { get; init; }
    public required int Width { get; init; }
    public required int Height { get; init; }
    public required Camera Camera { get; init; }
    public required Scene Scene { get; init; }
    public required Func<SceneObject, bool> IsSelected { get; init; }
    public required PrinterDefinition Printer { get; init; }
    public IReadOnlyList<OverlayLine> Overlay { get; init; } = Array.Empty<OverlayLine>();
    /// <summary>Lines drawn with depth testing, so scene geometry occludes them (e.g. supports).</summary>
    public IReadOnlyList<OverlayLine> DepthOverlay { get; init; } = Array.Empty<OverlayLine>();
    /// <summary>Derived world-space meshes, e.g. support capsules. Drawn between the opaque and ghosted passes.</summary>
    public IReadOnlyList<AuxMeshDraw> AuxMeshes { get; init; } = Array.Empty<AuxMeshDraw>();
    /// <summary>Tint faces that overhang more than <see cref="OverhangAngleDegrees"/> from vertical.</summary>
    public bool ShowOverhangs { get; init; }
    /// <summary>Overhang threshold measured from the vertical wall: 45 tints anything steeper.</summary>
    public float OverhangAngleDegrees { get; init; } = 45f;
    /// <summary>Plate opacity when the camera is below it: 0 invisible, 1 opaque (no fade).</summary>
    public float PlateOpacityFromBelow { get; init; } = 1f;
    /// <summary>The two overhang checker colours and the checker cell edge in millimetres.</summary>
    public Vector3 OverhangColorA { get; init; } = new(0.98f, 0.80f, 0.15f);
    public Vector3 OverhangColorB { get; init; } = new(0.90f, 0.12f, 0.10f);
    public float OverhangCheckerSizeMm { get; init; } = 2f;
    /// <summary>Which pipeline draws this frame. Deferred falls back to Classic on GL failure.</summary>
    public RenderPathMode RenderPath { get; init; } = RenderPathMode.Classic;
    /// <summary>Shading and effect settings for the deferred path; ignored by Classic.</summary>
    public DeferredEffects Deferred { get; init; } = DeferredEffects.Default;
    /// <summary>Support-mode world-Z isolation. Full/inactive ranges leave output bit-identical.</summary>
    public ViewportClipRange ClipRange { get; init; }
    /// <summary>Close visible horizontal clip cuts with viewport-only faces. Mirrors the config flag.</summary>
    public bool CapInterior { get; init; } = true;
    /// <summary>
    /// Which cap technique to use. Sliced expects the caller to have supplied exact geometry via
    /// <see cref="AuxMeshes"/> already; Painted asks the deferred renderer to fill the cut in
    /// screen space instead (see <see cref="ClipCapPolicy"/> for the Classic-path fallback).
    /// </summary>
    public ClipCapStyle CapStyle { get; init; } = ClipCapStyle.Sliced;
    /// <summary>Hovered model-surface world Z, or null when the Support waterline is inactive.</summary>
    public float? WaterlineZ { get; init; }
    /// <summary>Wireframe overlay on visible objects; identical on both paths.</summary>
    public bool WireframeEnabled { get; init; }
    /// <summary>Corner view cube (design 6.2); drawn over the finished frame on both paths.</summary>
    public bool ShowViewCube { get; init; } = true;
    /// <summary>Hovered view-cube region from <see cref="ViewCube.HitRegion"/>, or -1.</summary>
    public int ViewCubeHover { get; init; } = -1;
    /// <summary>Host DPI scale, so fixed-pixel overlays keep their physical size.</summary>
    public double RenderScaling { get; init; } = 1.0;
}

/// <summary>
/// Forward renderer for milestone one: studio-lit flat-shaded meshes, build plate, grid and overlay
/// lines. Owns all GPU resources; the host supplies a GL proc-address resolver and a framebuffer.
/// </summary>
public sealed partial class SceneRenderer : IDisposable
{
    private static readonly Vector3 ObjectColor = new(0.70f, 0.71f, 0.74f);
    private static readonly Vector3 SelectedColor = new(0.96f, 0.60f, 0.18f);
    private static readonly Vector3 PlateColor = new(0.20f, 0.21f, 0.23f);

    private readonly GL _gl;
    private readonly ShaderProgram _meshShader;
    private readonly ShaderProgram _lineShader;
    private readonly ShaderProgram _wireShader;
    private readonly LineBatch _depthLines;
    private readonly LineBatch _overlayLines;
    private readonly Dictionary<Mesh, GpuMesh> _meshes = new();
    private GpuMesh? _plate;
    private Vector3 _plateSize;
    private Vector3 _overhangColorA;
    private Vector3 _overhangColorB;
    private float _overhangCell = 2f;
    private Vector3 _buildVolume;

    public string GlVersion { get; }
    public bool IsGles { get; }

    public SceneRenderer(Func<string, nint> getProcAddress)
    {
        _gl = GL.GetApi(getProcAddress);
        GlVersion = _gl.GetStringS(StringName.Version) ?? "unknown";
        IsGles = GlVersion.Contains("OpenGL ES", StringComparison.OrdinalIgnoreCase);

        var preamble = Shaders.Preamble(IsGles);
        _meshShader = new ShaderProgram(_gl, preamble + Shaders.MeshVertex, preamble + Shaders.MeshFragment);
        _lineShader = new ShaderProgram(_gl, preamble + Shaders.LineVertex, preamble + Shaders.LineFragment);
        _wireShader = new ShaderProgram(_gl, preamble + Shaders.WireVertex, preamble + Shaders.WireFragment);
        _depthLines = new LineBatch(_gl);
        _overlayLines = new LineBatch(_gl);
    }

    public void Render(RenderFrame frame)
    {
        // The deferred path lives in SceneRenderer.Deferred.cs and is opt-in per frame; any GL
        // failure there logs, latches off and falls back so a frame is always produced.
        _pickTargetsValid = false; // only a completed deferred frame re-arms ID picking
        var deferredDrawn = frame.RenderPath == RenderPathMode.Deferred && TryRenderDeferred(frame);
        if (!deferredDrawn) RenderClassic(frame);

        if (frame.ShowViewCube)
        {
            // Last over the finished frame, whichever path drew it.
            _gl.BindFramebuffer(FramebufferTarget.Framebuffer, (uint)frame.Framebuffer);
            _viewCube ??= new ViewCube(_gl, IsGles);
            _viewCube.Draw(frame.Width, frame.Height, frame.RenderScaling, frame.Camera.View,
                frame.ViewCubeHover);
        }
    }

    private void RenderClassic(RenderFrame frame)
    {
        var gl = _gl;
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, (uint)frame.Framebuffer);
        gl.Viewport(0, 0, (uint)frame.Width, (uint)frame.Height);
        gl.Enable(EnableCap.DepthTest);
        gl.DepthFunc(DepthFunction.Lequal);
        gl.Disable(EnableCap.CullFace);
        gl.Disable(EnableCap.Blend);
        gl.DepthMask(true);
        gl.ClearColor(0.155f, 0.16f, 0.175f, 1f);
        gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

        var aspect = frame.Width / (float)Math.Max(frame.Height, 1);
        var view = frame.Camera.View;
        var projection = frame.Camera.Projection(aspect);
        _overhangColorA = frame.OverhangColorA;
        _overhangColorB = frame.OverhangColorB;
        _overhangCell = frame.OverhangCheckerSizeMm;
        _buildVolume = frame.Printer.BuildVolume;

        PruneMeshCache(frame);
        // Looking up from under the plate, the plate fades to the configured opacity so the
        // model stays visible; it then draws after the opaque passes so blending sees them.
        var plateFaded = frame.Camera.Eye.Z < 0f && frame.PlateOpacityFromBelow < 1f;
        if (!plateFaded) DrawPlate(frame.Printer, view, projection, 1f);
        DrawObjects(frame, view, projection, ghosted: false);
        DrawWireframe(frame, view, projection);
        DrawAuxMeshes(frame, view, projection);
        if (plateFaded && frame.PlateOpacityFromBelow > 0.001f)
            DrawPlate(frame.Printer, view, projection, frame.PlateOpacityFromBelow);
        DrawObjects(frame, view, projection, ghosted: true);
        DrawLines(frame, view * projection);

        gl.BindVertexArray(0);
        gl.UseProgram(0);
    }

    private void DrawPlate(PrinterDefinition printer, in Matrix4x4 view, in Matrix4x4 projection, float opacity)
    {
        if (_plate is null || _plateSize != printer.BuildVolume)
        {
            _plate?.Dispose();
            _plate = new GpuMesh(_gl, GpuMesh.CreatePlate(printer.BuildVolume.X, printer.BuildVolume.Y));
            _plateSize = printer.BuildVolume;
        }

        var gl = _gl;
        var faded = opacity < 1f;
        if (faded)
        {
            gl.Enable(EnableCap.Blend);
            gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
            gl.DepthMask(false);
        }
        // Sit just under Z = 0 so grid lines on the plane do not fight it.
        var model = Matrix4x4.CreateTranslation(0, 0, -0.05f);
        BindMeshShader(model, view, projection, PlateColor, opacity, backfaceTint: 0f,
            warnOutsideBuildVolume: false, overhangCos: 2f, clip: default);
        _plate.Draw();
        if (faded)
        {
            gl.Disable(EnableCap.Blend);
            gl.DepthMask(true);
        }
    }

    private void DrawObjects(RenderFrame frame, in Matrix4x4 view, in Matrix4x4 projection, bool ghosted)
    {
        var gl = _gl;
        if (ghosted)
        {
            gl.Enable(EnableCap.Blend);
            gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
            gl.DepthMask(false);
        }

        foreach (var obj in frame.Scene.Objects)
        {
            if (obj.RenderState == RenderState.Hidden) continue;
            if ((obj.RenderState == RenderState.Ghosted) != ghosted) continue;

            if (!_meshes.TryGetValue(obj.Mesh, out var gpu))
            {
                gpu = new GpuMesh(gl, obj.Mesh);
                _meshes[obj.Mesh] = gpu;
            }

            var selected = frame.IsSelected(obj);
            var color = selected ? SelectedColor
                : obj.RenderState == RenderState.Highlighted ? Vector3.Lerp(ObjectColor, SelectedColor, 0.4f)
                : ObjectColor;

            // dot(normal, down) equals sin(lean-from-vertical), so the threshold enters as a sine.
            var overhangCos = frame.ShowOverhangs
                ? MathF.Sin(Math.Clamp(frame.OverhangAngleDegrees, 1f, 89f) * MathF.PI / 180f)
                : 2f;
            BindMeshShader(obj.Transform.ToMatrix(), view, projection, color,
                ghosted ? 0.25f : 1f, backfaceTint: 1f, warnOutsideBuildVolume: true,
                overhangCos, frame.ClipRange, frame.WaterlineZ);
            gpu.Draw();
        }

        if (ghosted)
        {
            gl.Disable(EnableCap.Blend);
            gl.DepthMask(true);
        }
    }

    private void DrawAuxMeshes(RenderFrame frame, in Matrix4x4 view, in Matrix4x4 projection)
    {
        if (frame.AuxMeshes.Count == 0) return;
        var gl = _gl;
        foreach (var draw in frame.AuxMeshes)
        {
            if (!_meshes.TryGetValue(draw.Mesh, out var gpu))
            {
                gpu = new GpuMesh(gl, draw.Mesh);
                _meshes[draw.Mesh] = gpu;
            }

            var faded = draw.Opacity < 1f;
            if (faded)
            {
                gl.Enable(EnableCap.Blend);
                gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
                gl.DepthMask(false);
            }
            if (draw.DepthOverlay) gl.DepthFunc(DepthFunction.Lequal);
            BindMeshShader(Matrix4x4.Identity, view, projection, draw.Color, draw.Opacity,
                backfaceTint: 0f, warnOutsideBuildVolume: false, overhangCos: 2f,
                clip: frame.ClipRange);
            gpu.Draw();
            if (draw.DepthOverlay) gl.DepthFunc(DepthFunction.Less);
            if (faded)
            {
                gl.Disable(EnableCap.Blend);
                gl.DepthMask(true);
            }
        }
    }

    private void BindMeshShader(in Matrix4x4 model, in Matrix4x4 view,
        in Matrix4x4 projection, Vector3 color, float opacity, float backfaceTint,
        bool warnOutsideBuildVolume, float overhangCos, ViewportClipRange clip,
        float? waterlineZ = null)
    {
        Matrix4x4.Invert(model * view, out var inverse);
        var normalMatrix = Matrix4x4.Transpose(inverse);
        Matrix4x4.Invert(model, out var modelInverse);
        var modelNormalMatrix = Matrix4x4.Transpose(modelInverse);

        _meshShader.Use();
        _meshShader.Set("uModel", model);
        _meshShader.Set("uView", view);
        _meshShader.Set("uProjection", projection);
        _meshShader.Set("uNormalMatrix", normalMatrix);
        _meshShader.Set("uModelNormalMatrix", modelNormalMatrix);
        _meshShader.Set("uColor", color);
        _meshShader.Set("uOpacity", opacity);
        _meshShader.Set("uBackfaceTint", backfaceTint);
        _meshShader.Set("uWarnOutsideBuildVolume", warnOutsideBuildVolume ? 1f : 0f);
        _meshShader.Set("uBuildVolume", _buildVolume);
        _meshShader.Set("uOverhangCos", overhangCos);
        _meshShader.Set("uOverhangColorA", _overhangColorA);
        _meshShader.Set("uOverhangColorB", _overhangColorB);
        _meshShader.Set("uOverhangCell", _overhangCell);
        BindClip(_meshShader, clip);
        _meshShader.Set("uWaterlineEnabled", waterlineZ.HasValue ? 1f : 0f);
        _meshShader.Set("uWaterlineZ", waterlineZ.GetValueOrDefault());
    }

    /// <summary>
    /// Wireframe overlay: the triangulation's edges over every visible (non-ghosted) object,
    /// clip-aware like the surfaces they sit on. Shared by both paths — classic calls it after
    /// the opaque objects, deferred at the start of its forward stage, where the depth buffer
    /// holds the same opaque scene.
    /// </summary>
    private void DrawWireframe(RenderFrame frame, in Matrix4x4 view, in Matrix4x4 projection)
    {
        if (!frame.WireframeEnabled) return;
        _wireShader.Use();
        _wireShader.Set("uView", view);
        _wireShader.Set("uProjection", projection);
        _wireShader.Set("uColor", new Vector3(0.05f, 0.06f, 0.08f));
        BindClip(_wireShader, frame.ClipRange);
        foreach (var obj in frame.Scene.Objects)
        {
            if (obj.RenderState is RenderState.Hidden or RenderState.Ghosted) continue;
            if (!_meshes.TryGetValue(obj.Mesh, out var gpu)) continue;
            _wireShader.Set("uModel", obj.Transform.ToMatrix());
            gpu.DrawEdges();
        }
    }

    private void DrawLines(RenderFrame frame, in Matrix4x4 viewProjection)
    {
        var gl = _gl;
        _lineShader.Use();
        _lineShader.Set("uViewProjection", viewProjection);

        gl.Enable(EnableCap.Blend);
        gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        gl.Enable(EnableCap.DepthTest);

        _depthLines.Clear();
        AddGrid(frame.Printer);
        BindClip(_lineShader, default);
        _depthLines.Draw();

        _depthLines.Clear();
        foreach (var line in frame.DepthOverlay) _depthLines.Add(line);
        BindClip(_lineShader, frame.ClipRange);
        _depthLines.Draw();

        gl.Disable(EnableCap.DepthTest);
        _overlayLines.Clear();
        foreach (var line in frame.Overlay) _overlayLines.Add(line);
        BindClip(_lineShader, default);
        _overlayLines.Draw();

        gl.Enable(EnableCap.DepthTest);
        gl.Disable(EnableCap.Blend);
    }

    private static void BindClip(ShaderProgram shader, ViewportClipRange clip)
    {
        shader.Set("uClipEnabled", clip.IsClipping ? 1f : 0f);
        shader.Set("uClipLowerZ", clip.LowerZ);
        shader.Set("uClipUpperZ", clip.UpperZ);
    }

    private void AddGrid(PrinterDefinition printer)
    {
        var hw = printer.BuildVolume.X * 0.5f;
        var hd = printer.BuildVolume.Y * 0.5f;
        var height = printer.BuildVolume.Z;

        var minor = new Vector4(1f, 1f, 1f, 0.07f);
        var major = new Vector4(1f, 1f, 1f, 0.16f);
        var border = new Vector4(1f, 1f, 1f, 0.35f);
        var volume = new Vector4(1f, 1f, 1f, 0.10f);

        for (float x = 0; x <= hw + 1e-3f; x += 10f)
        {
            var c = x % 50f < 1e-3f ? major : minor;
            _depthLines.Add(new Vector3(x, -hd, 0), new Vector3(x, hd, 0), c);
            if (x > 0) _depthLines.Add(new Vector3(-x, -hd, 0), new Vector3(-x, hd, 0), c);
        }
        for (float y = 0; y <= hd + 1e-3f; y += 10f)
        {
            var c = y % 50f < 1e-3f ? major : minor;
            _depthLines.Add(new Vector3(-hw, y, 0), new Vector3(hw, y, 0), c);
            if (y > 0) _depthLines.Add(new Vector3(-hw, -y, 0), new Vector3(hw, -y, 0), c);
        }

        // Plate border and build volume.
        var corners = new[] { new Vector3(-hw, -hd, 0), new Vector3(hw, -hd, 0), new Vector3(hw, hd, 0), new Vector3(-hw, hd, 0) };
        for (int i = 0; i < 4; i++)
        {
            var a = corners[i];
            var b = corners[(i + 1) % 4];
            _depthLines.Add(a, b, border);
            _depthLines.Add(a + new Vector3(0, 0, height), b + new Vector3(0, 0, height), volume);
            _depthLines.Add(a, a + new Vector3(0, 0, height), volume);
        }

        // Axes at the plate origin.
        _depthLines.Add(Vector3.Zero, new Vector3(hw, 0, 0), new Vector4(0.95f, 0.30f, 0.30f, 0.9f));
        _depthLines.Add(Vector3.Zero, new Vector3(0, hd, 0), new Vector4(0.45f, 0.85f, 0.35f, 0.9f));
        _depthLines.Add(Vector3.Zero, new Vector3(0, 0, 20f), new Vector4(0.35f, 0.55f, 0.95f, 0.9f));
    }

    private void PruneMeshCache(RenderFrame frame)
    {
        if (_meshes.Count == 0) return;
        var live = new HashSet<Mesh>(frame.Scene.Objects.Select(o => o.Mesh));
        foreach (var draw in frame.AuxMeshes) live.Add(draw.Mesh);
        foreach (var (mesh, gpu) in _meshes.ToList())
        {
            if (live.Contains(mesh)) continue;
            gpu.Dispose();
            _meshes.Remove(mesh);
        }
    }

    public void Dispose()
    {
        _viewCube?.Dispose();
        _deferred?.Dispose();
        foreach (var gpu in _meshes.Values) gpu.Dispose();
        _meshes.Clear();
        _plate?.Dispose();
        _depthLines.Dispose();
        _overlayLines.Dispose();
        _meshShader.Dispose();
        _lineShader.Dispose();
        _wireShader.Dispose();
    }
}
