using System.Numerics;
using Danslicer.Core.Config;
using Danslicer.Core.Geometry;
using Danslicer.Core.Scene;
using Silk.NET.OpenGL;

namespace Danslicer.Render;

public sealed partial class SceneRenderer
{
    private ModelShadowMap? _modelShadowMap;
    private ShadowProjection _shadowProjection;
    private float _modelShadowStrength;
    private float _shadowSoftnessPixels;
    private bool _modelShadowFailed;
    public bool ModelShadowsActive => _modelShadowStrength > 0;

    private void PrepareModelShadows(RenderFrame frame)
    {
        _modelShadowStrength = 0;
        var effects = frame.Shadows;
        if (_modelShadowFailed || effects.Mode == ModelShadowMode.Off || !float.IsFinite(effects.Strength) || effects.Strength <= 0) return;
        var bounds = Aabb.Empty;
        foreach (var obj in frame.Scene.Objects)
            if (obj.RenderState is not (RenderState.Hidden or RenderState.Ghosted)) bounds = bounds.Union(obj.WorldBounds);
        foreach (var aux in frame.AuxMeshes)
            if (aux.Opacity >= 1 && !aux.DepthOverlay) bounds = bounds.Union(aux.Mesh.Bounds);
        if (frame.ClipRange.IsClipping && !bounds.IsEmpty)
            bounds = new(new(bounds.Min.X, bounds.Min.Y, Math.Max(bounds.Min.Z, frame.ClipRange.LowerZ)),
                new(bounds.Max.X, bounds.Max.Y, Math.Min(bounds.Max.Z, frame.ClipRange.UpperZ)));
        if (bounds.IsEmpty) return;
        try
        {
            BindTexture(5, 0);
            _modelShadowMap ??= new ModelShadowMap(_gl, IsGles);
            BindTexture(5, 0); // never sample an attached shadow texture
            _shadowProjection = ShadowProjection.Fit(bounds, frame.Camera);
            _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _modelShadowMap.Fbo);
            _gl.Viewport(0, 0, ShadowProjection.Resolution, ShadowProjection.Resolution);
            _gl.Enable(EnableCap.DepthTest); _gl.DepthFunc(DepthFunction.Less); _gl.DepthMask(true);
            _gl.Disable(EnableCap.Blend); _gl.Disable(EnableCap.CullFace);
            _gl.Enable(EnableCap.PolygonOffsetFill); _gl.PolygonOffset(1.1f, 2f);
            _gl.Clear(ClearBufferMask.DepthBufferBit);
            var shader = _modelShadowMap.Shader;
            shader.Use(); shader.Set("uLightMatrix", _shadowProjection.Matrix); BindClip(shader, frame.ClipRange);
            foreach (var obj in frame.Scene.Objects)
                if (obj.RenderState is not (RenderState.Hidden or RenderState.Ghosted)) Draw(obj.Mesh, obj.Transform.ToMatrix());
            foreach (var aux in frame.AuxMeshes)
                if (aux.Opacity >= 1 && !aux.DepthOverlay) Draw(aux.Mesh, Matrix4x4.Identity);
            _modelShadowStrength = Math.Clamp(effects.Strength, 0, 0.7f);
            var softness = float.IsFinite(effects.SoftnessMm) ? Math.Clamp(effects.SoftnessMm, 0, 4) : 0.6f;
            _shadowSoftnessPixels = Math.Clamp(softness / _shadowProjection.WorldUnitsPerTexel, 0, 32);
            void Draw(Mesh mesh, Matrix4x4 matrix)
            {
                if (!_meshes.TryGetValue(mesh, out var gpu)) _meshes[mesh] = gpu = new GpuMesh(_gl, mesh);
                shader.Set("uModel", matrix); gpu.Draw();
            }
        }
        catch (Exception ex)
        {
            _modelShadowFailed = true; _modelShadowMap?.Dispose(); _modelShadowMap = null;
            Console.Error.WriteLine($"[render] Model shadows disabled for this session: {ex}");
        }
        finally
        {
            _gl.Disable(EnableCap.PolygonOffsetFill);
            _gl.BindFramebuffer(FramebufferTarget.Framebuffer, (uint)frame.Framebuffer);
        }
    }

    private void BindModelShadows(ShaderProgram shader, bool enabled)
    {
        shader.Set("uModelShadow", 5);
        shader.Set("uModelShadowStrength", enabled ? _modelShadowStrength : 0f);
        shader.Set("uLightMatrix", _shadowProjection.Matrix);
        shader.Set("uLightDirection", _shadowProjection.LightDirection);
        shader.Set("uShadowSoftnessPixels", _shadowSoftnessPixels);
        shader.Set("uShadowBiasMm", _shadowProjection.WorldUnitsPerTexel * 0.35f + 0.005f);
        if (_modelShadowStrength > 0 && _modelShadowMap is not null) BindTexture(5, _modelShadowMap.Depth);
    }
}
