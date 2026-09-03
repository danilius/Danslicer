using System.Numerics;
using Danslicer.Core.Geometry;
using Danslicer.Core.Printers;
using Danslicer.Core.Scene;
using Silk.NET.OpenGL;

namespace Danslicer.Render;

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
    /// <summary>Tint faces that overhang more than <see cref="OverhangAngleDegrees"/> from vertical.</summary>
    public bool ShowOverhangs { get; init; }
    /// <summary>Overhang threshold measured from the vertical wall: 45 tints anything steeper.</summary>
    public float OverhangAngleDegrees { get; init; } = 45f;
}

/// <summary>
/// Forward renderer for milestone one: studio-lit flat-shaded meshes, build plate, grid and overlay
/// lines. Owns all GPU resources; the host supplies a GL proc-address resolver and a framebuffer.
/// </summary>
public sealed class SceneRenderer : IDisposable
{
    private static readonly Vector3 ObjectColor = new(0.70f, 0.71f, 0.74f);
    private static readonly Vector3 SelectedColor = new(0.96f, 0.60f, 0.18f);
    private static readonly Vector3 PlateColor = new(0.20f, 0.21f, 0.23f);

    private readonly GL _gl;
    private readonly ShaderProgram _meshShader;
    private readonly ShaderProgram _lineShader;
    private readonly LineBatch _depthLines;
    private readonly LineBatch _overlayLines;
    private readonly Dictionary<Mesh, GpuMesh> _meshes = new();
    private GpuMesh? _plate;
    private Vector3 _plateSize;

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
        _depthLines = new LineBatch(_gl);
        _overlayLines = new LineBatch(_gl);
    }

    public void Render(RenderFrame frame)
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

        PruneMeshCache(frame.Scene);
        DrawPlate(frame.Printer, view, projection);
        DrawObjects(frame, view, projection, ghosted: false);
        DrawObjects(frame, view, projection, ghosted: true);
        DrawLines(frame, view * projection);

        gl.BindVertexArray(0);
        gl.UseProgram(0);
    }

    private void DrawPlate(PrinterDefinition printer, in Matrix4x4 view, in Matrix4x4 projection)
    {
        if (_plate is null || _plateSize != printer.BuildVolume)
        {
            _plate?.Dispose();
            _plate = new GpuMesh(_gl, GpuMesh.CreatePlate(printer.BuildVolume.X, printer.BuildVolume.Y));
            _plateSize = printer.BuildVolume;
        }

        // Sit just under Z = 0 so grid lines on the plane do not fight it.
        var model = Matrix4x4.CreateTranslation(0, 0, -0.05f);
        BindMeshShader(model, view, projection, PlateColor, opacity: 1f, backfaceTint: 0f, warnBelowPlate: false, overhangCos: 2f);
        _plate.Draw();
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
            BindMeshShader(obj.Transform.ToMatrix(), view, projection, color, ghosted ? 0.25f : 1f, backfaceTint: 1f, warnBelowPlate: true, overhangCos);
            gpu.Draw();
        }

        if (ghosted)
        {
            gl.Disable(EnableCap.Blend);
            gl.DepthMask(true);
        }
    }

    private void BindMeshShader(in Matrix4x4 model, in Matrix4x4 view, in Matrix4x4 projection, Vector3 color, float opacity, float backfaceTint, bool warnBelowPlate, float overhangCos)
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
        _meshShader.Set("uWarnBelowPlate", warnBelowPlate ? 1f : 0f);
        _meshShader.Set("uOverhangCos", overhangCos);
    }

    private void DrawLines(RenderFrame frame, in Matrix4x4 viewProjection)
    {
        var gl = _gl;
        _depthLines.Clear();
        _overlayLines.Clear();
        AddGrid(frame.Printer);
        foreach (var line in frame.DepthOverlay) _depthLines.Add(line);
        foreach (var line in frame.Overlay) _overlayLines.Add(line);

        _lineShader.Use();
        _lineShader.Set("uViewProjection", viewProjection);

        gl.Enable(EnableCap.Blend);
        gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        gl.Enable(EnableCap.DepthTest);
        _depthLines.Draw();

        gl.Disable(EnableCap.DepthTest);
        _overlayLines.Draw();

        gl.Enable(EnableCap.DepthTest);
        gl.Disable(EnableCap.Blend);
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

    private void PruneMeshCache(Scene scene)
    {
        if (_meshes.Count == 0) return;
        var live = new HashSet<Mesh>(scene.Objects.Select(o => o.Mesh));
        foreach (var (mesh, gpu) in _meshes.ToList())
        {
            if (live.Contains(mesh)) continue;
            gpu.Dispose();
            _meshes.Remove(mesh);
        }
    }

    public void Dispose()
    {
        foreach (var gpu in _meshes.Values) gpu.Dispose();
        _meshes.Clear();
        _plate?.Dispose();
        _depthLines.Dispose();
        _overlayLines.Dispose();
        _meshShader.Dispose();
        _lineShader.Dispose();
    }
}
