using System.Numerics;
using Silk.NET.OpenGL;

namespace Danslicer.Render;

/// <summary>
/// GPU resources for the deferred path: the G-buffer, the composite/FXAA targets, the fullscreen
/// triangle and the procedural MatCap textures. Every format used (RGBA8, RGB10_A2,
/// DEPTH_COMPONENT24) is colour-renderable in both GL 3.3 core and GL ES 3.0, so no extensions are
/// required. Construction or resizing throws on any incomplete framebuffer; the caller treats that
/// as "fall back to the classic path".
/// </summary>
internal sealed unsafe class DeferredPipeline : IDisposable
{
    private readonly GL _gl;

    public uint GBufferFbo { get; private set; }
    public uint CompositeFbo { get; private set; }
    public uint ForwardFbo { get; private set; }
    public uint AlbedoTexture { get; private set; }
    public uint NormalTexture { get; private set; }
    public uint IdTexture { get; private set; }
    public uint DepthTexture { get; private set; }
    public uint SceneTexture { get; private set; }
    public int Width { get; private set; }
    public int Height { get; private set; }

    public ShaderProgram GBufferShader { get; }
    public ShaderProgram CompositeShader { get; }
    public ShaderProgram FxaaShader { get; }
    public ShaderProgram PassthroughShader { get; }

    private readonly uint _quadVao;
    private readonly uint _quadVbo;
    private readonly uint[] _matCaps = new uint[3];

    public DeferredPipeline(GL gl, bool gles)
    {
        _gl = gl;
        var preamble = Shaders.Preamble(gles);
        GBufferShader = new ShaderProgram(gl,
            preamble + Shaders.MeshVertex, preamble + DeferredShaders.GBufferFragment);
        CompositeShader = new ShaderProgram(gl,
            preamble + DeferredShaders.FullscreenVertex, preamble + DeferredShaders.CompositeFragment);
        FxaaShader = new ShaderProgram(gl,
            preamble + DeferredShaders.FullscreenVertex, preamble + DeferredShaders.FxaaFragment);
        PassthroughShader = new ShaderProgram(gl,
            preamble + DeferredShaders.FullscreenVertex, preamble + DeferredShaders.PassthroughFragment);

        // Sampler units are fixed for the life of the programs.
        CompositeShader.Use();
        CompositeShader.Set("uAlbedo", 0);
        CompositeShader.Set("uNormalTex", 1);
        CompositeShader.Set("uIdTex", 2);
        CompositeShader.Set("uDepthTex", 3);
        CompositeShader.Set("uMatCap", 4);
        FxaaShader.Use();
        FxaaShader.Set("uScene", 0);
        PassthroughShader.Use();
        PassthroughShader.Set("uScene", 0);
        gl.UseProgram(0);

        // One oversized triangle covers the viewport without a seam down the diagonal.
        _quadVao = gl.GenVertexArray();
        _quadVbo = gl.GenBuffer();
        gl.BindVertexArray(_quadVao);
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, _quadVbo);
        var verts = stackalloc float[] { -1f, -1f, 3f, -1f, -1f, 3f };
        gl.BufferData(BufferTargetARB.ArrayBuffer, 6 * sizeof(float), verts, BufferUsageARB.StaticDraw);
        gl.EnableVertexAttribArray(0);
        gl.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 2 * sizeof(float), (void*)0);
        gl.BindVertexArray(0);

        CreateMatCaps();
    }

    public uint MatCap(MatCapStyle style) => _matCaps[(int)style];

    private void CreateMatCaps()
    {
        var gl = _gl;
        gl.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
        foreach (var style in new[] { MatCapStyle.Clay, MatCapStyle.Metal, MatCapStyle.Pearl })
        {
            var tex = gl.GenTexture();
            gl.BindTexture(TextureTarget.Texture2D, tex);
            var data = MatCapGenerator.Generate(style);
            fixed (byte* ptr = data)
                gl.TexImage2D(TextureTarget.Texture2D, 0, (int)InternalFormat.Rgb8,
                    MatCapGenerator.Size, MatCapGenerator.Size, 0,
                    PixelFormat.Rgb, PixelType.UnsignedByte, ptr);
            SetFilters(TextureMinFilter.Linear, TextureMagFilter.Linear);
            _matCaps[(int)style] = tex;
        }
        gl.PixelStore(PixelStoreParameter.UnpackAlignment, 4);
        gl.BindTexture(TextureTarget.Texture2D, 0);
    }

    public void EnsureTargets(int width, int height)
    {
        if (width == Width && height == Height && GBufferFbo != 0) return;
        DeleteTargets();
        Width = width;
        Height = height;

        var gl = _gl;
        AlbedoTexture = CreateTarget(InternalFormat.Rgba8, PixelFormat.Rgba,
            PixelType.UnsignedByte, nearest: true);
        NormalTexture = CreateTarget(InternalFormat.Rgb10A2, PixelFormat.Rgba,
            PixelType.UnsignedInt2101010Rev, nearest: true);
        IdTexture = CreateTarget(InternalFormat.Rgba8, PixelFormat.Rgba,
            PixelType.UnsignedByte, nearest: true);
        // ES 3.0 defines depth sampling only with NEAREST filters (no compare mode).
        DepthTexture = CreateTarget(InternalFormat.DepthComponent24, PixelFormat.DepthComponent,
            PixelType.UnsignedInt, nearest: true);
        // FXAA reads between texels, so the scene target filters bilinearly.
        SceneTexture = CreateTarget(InternalFormat.Rgba8, PixelFormat.Rgba,
            PixelType.UnsignedByte, nearest: false);

        GBufferFbo = gl.GenFramebuffer();
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, GBufferFbo);
        gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0,
            TextureTarget.Texture2D, AlbedoTexture, 0);
        gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment1,
            TextureTarget.Texture2D, NormalTexture, 0);
        gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment2,
            TextureTarget.Texture2D, IdTexture, 0);
        gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment,
            TextureTarget.Texture2D, DepthTexture, 0);
        var drawBuffers = stackalloc GLEnum[]
            { GLEnum.ColorAttachment0, GLEnum.ColorAttachment1, GLEnum.ColorAttachment2 };
        gl.DrawBuffers(3, drawBuffers);
        Check("G-buffer");

        // The composite target carries no depth attachment: the composite pass samples the depth
        // texture, and sampling an attached depth buffer would be a framebuffer feedback loop.
        CompositeFbo = gl.GenFramebuffer();
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, CompositeFbo);
        gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0,
            TextureTarget.Texture2D, SceneTexture, 0);
        Check("composite");

        // Forward passes (ghosted, transparent, lines) blend over the composited colour while
        // depth-testing against the opaque scene: same colour texture, plus the G-buffer depth.
        ForwardFbo = gl.GenFramebuffer();
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, ForwardFbo);
        gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0,
            TextureTarget.Texture2D, SceneTexture, 0);
        gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment,
            TextureTarget.Texture2D, DepthTexture, 0);
        Check("forward");

        gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
    }

    private uint CreateTarget(InternalFormat internalFormat, PixelFormat format, PixelType type,
        bool nearest)
    {
        var gl = _gl;
        var tex = gl.GenTexture();
        gl.BindTexture(TextureTarget.Texture2D, tex);
        gl.TexImage2D(TextureTarget.Texture2D, 0, (int)internalFormat, (uint)Width, (uint)Height, 0,
            format, type, null);
        SetFilters(nearest ? TextureMinFilter.Nearest : TextureMinFilter.Linear,
            nearest ? TextureMagFilter.Nearest : TextureMagFilter.Linear);
        return tex;
    }

    private void SetFilters(TextureMinFilter min, TextureMagFilter mag)
    {
        var gl = _gl;
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)min);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)mag);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS,
            (int)TextureWrapMode.ClampToEdge);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT,
            (int)TextureWrapMode.ClampToEdge);
    }

    private void Check(string stage)
    {
        var status = _gl.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
        if (status != GLEnum.FramebufferComplete)
            throw new InvalidOperationException($"Deferred {stage} framebuffer incomplete: {status}");
    }

    /// <summary>Clears all G-buffer attachments; the caller has the G-buffer FBO bound.</summary>
    public void ClearGBuffer(Vector3 background)
    {
        var gl = _gl;
        var albedo = stackalloc float[] { background.X, background.Y, background.Z, 0f };
        gl.ClearBuffer(GLEnum.Color, 0, albedo);
        var normal = stackalloc float[] { 0.5f, 0.5f, 0.5f, 0f };
        gl.ClearBuffer(GLEnum.Color, 1, normal);
        var id = stackalloc float[] { 0f, 0f, 0f, 0f };
        gl.ClearBuffer(GLEnum.Color, 2, id);
        var depth = 1f;
        gl.ClearBuffer(GLEnum.Depth, 0, &depth);
    }

    /// <summary>Draws the fullscreen triangle; the caller binds the shader and textures.</summary>
    public void DrawFullscreen()
    {
        _gl.BindVertexArray(_quadVao);
        _gl.DrawArrays(PrimitiveType.Triangles, 0, 3);
        _gl.BindVertexArray(0);
    }

    private void DeleteTargets()
    {
        var gl = _gl;
        if (GBufferFbo != 0) gl.DeleteFramebuffer(GBufferFbo);
        if (CompositeFbo != 0) gl.DeleteFramebuffer(CompositeFbo);
        if (ForwardFbo != 0) gl.DeleteFramebuffer(ForwardFbo);
        if (AlbedoTexture != 0) gl.DeleteTexture(AlbedoTexture);
        if (NormalTexture != 0) gl.DeleteTexture(NormalTexture);
        if (IdTexture != 0) gl.DeleteTexture(IdTexture);
        if (DepthTexture != 0) gl.DeleteTexture(DepthTexture);
        if (SceneTexture != 0) gl.DeleteTexture(SceneTexture);
        GBufferFbo = CompositeFbo = ForwardFbo = 0;
        AlbedoTexture = NormalTexture = IdTexture = DepthTexture = SceneTexture = 0;
    }

    public void Dispose()
    {
        DeleteTargets();
        _gl.DeleteVertexArray(_quadVao);
        _gl.DeleteBuffer(_quadVbo);
        foreach (var tex in _matCaps)
            if (tex != 0) _gl.DeleteTexture(tex);
        GBufferShader.Dispose();
        CompositeShader.Dispose();
        FxaaShader.Dispose();
        PassthroughShader.Dispose();
    }
}
