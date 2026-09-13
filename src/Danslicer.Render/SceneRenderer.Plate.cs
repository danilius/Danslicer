using System.Numerics;
using Danslicer.Core.Scene;
using Silk.NET.OpenGL;

namespace Danslicer.Render;

public sealed partial class SceneRenderer
{
    private PlateReflectionTarget? _reflection;
    private float _reflectionStrength;
    private Vector2 _frameSize;
    private bool _reflectionFailed;

    private void PreparePlateReflection(RenderFrame frame)
    {
        _frameSize = new(frame.Width, frame.Height);
        _reflectionStrength = 0;
        var visibility = PlateFade.SurfaceOpacityFor(frame.Camera);
        if (_reflectionFailed || !frame.PlateReflectionsEnabled || visibility <= 0.001f) return;
        var strength = float.IsFinite(frame.PlateReflectionStrength) ? Math.Clamp(frame.PlateReflectionStrength, 0, 0.3f) : 0.12f;
        if (strength <= 0) return;
        try
        {
            _reflection ??= new PlateReflectionTarget(_gl);
            // Unbind the sampled target before attaching/drawing it; no feedback even on strict ES drivers.
            BindTexture(6, 0);
            _reflection.Ensure(frame.Width, frame.Height);
            BindTexture(6, 0);
            _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _reflection.Fbo);
            _gl.Viewport(0, 0, (uint)_reflection.Width, (uint)_reflection.Height);
            _gl.Enable(EnableCap.DepthTest); _gl.DepthFunc(DepthFunction.Lequal); _gl.DepthMask(true);
            _gl.Disable(EnableCap.Blend); _gl.Disable(EnableCap.CullFace);
            _gl.ClearColor(0, 0, 0, 0);
            _gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
            var view = frame.Camera.View;
            var projection = frame.Camera.Projection(frame.Width / (float)frame.Height);
            foreach (var obj in frame.Scene.Objects)
            {
                if (obj.RenderState is RenderState.Hidden or RenderState.Ghosted) continue;
                Draw(obj.Mesh, obj.Transform.ToMatrix(), ObjectColor);
            }
            foreach (var aux in frame.AuxMeshes)
                if (aux.Opacity >= 1 && !aux.DepthOverlay) Draw(aux.Mesh, Matrix4x4.Identity, aux.Color);
            _reflectionStrength = strength * visibility;

            void Draw(Core.Geometry.Mesh mesh, Matrix4x4 model, Vector3 color)
            {
                if (!_meshes.TryGetValue(mesh, out var gpu)) _meshes[mesh] = gpu = new GpuMesh(_gl, mesh);
                BindMeshShader(model, view, projection, color, 1, 0, false, 2, frame.ClipRange);
                _meshShader.Set("uMirror", 1f);
                gpu.Draw();
                _meshShader.Set("uMirror", 0f);
            }
        }
        catch (Exception ex)
        {
            _reflectionFailed = true; _reflection?.Dispose(); _reflection = null;
            Console.Error.WriteLine($"[render] Plate reflections disabled for this session: {ex}");
        }
        finally { _gl.BindFramebuffer(FramebufferTarget.Framebuffer, (uint)frame.Framebuffer); }
    }

    private void BindPlateMaterial(ShaderProgram shader, bool plate)
    {
        shader.Set("uMirror", 0f);
        shader.Set("uPlateMaterial", plate ? 1f : 0f);
        shader.Set("uReflectionStrength", plate ? _reflectionStrength : 0f);
        shader.Set("uReflectionTex", 6);
        shader.Set("uViewportSize", _frameSize);
        shader.Set("uReflectionTexel", _reflection is null ? Vector2.One : new(1f / _reflection.Width, 1f / _reflection.Height));
        // During reflection rendering the texture must remain unbound.
        if (plate && _reflectionStrength > 0 && _reflection is not null) BindTexture(6, _reflection.Texture);
    }
}
