using Silk.NET.OpenGL;

namespace Danslicer.Render;

/// <summary>Quarter-resolution planar reflection; RGBA8 plus depth24, both GL3.3/ES3 compatible.</summary>
internal sealed unsafe class PlateReflectionTarget(GL gl) : IDisposable
{
    public uint Fbo { get; private set; }
    public uint Texture { get; private set; }
    private uint _depth;
    public int Width { get; private set; }
    public int Height { get; private set; }
    public void Ensure(int width, int height)
    {
        width = Math.Max(1, width / 4); height = Math.Max(1, height / 4);
        if (Fbo != 0 && width == Width && height == Height) return;
        Dispose(); Width = width; Height = height;
        Texture = gl.GenTexture();
        gl.BindTexture(TextureTarget.Texture2D, Texture);
        gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba8, (uint)width, (uint)height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, null);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
        _depth = gl.GenRenderbuffer();
        gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, _depth);
        gl.RenderbufferStorage(RenderbufferTarget.Renderbuffer, InternalFormat.DepthComponent24, (uint)width, (uint)height);
        Fbo = gl.GenFramebuffer();
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, Fbo);
        gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, Texture, 0);
        gl.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment, RenderbufferTarget.Renderbuffer, _depth);
        if (gl.CheckFramebufferStatus(FramebufferTarget.Framebuffer) != GLEnum.FramebufferComplete)
            throw new InvalidOperationException("Incomplete plate reflection framebuffer");
    }
    public void Dispose()
    {
        if (Fbo != 0) gl.DeleteFramebuffer(Fbo);
        if (Texture != 0) gl.DeleteTexture(Texture);
        if (_depth != 0) gl.DeleteRenderbuffer(_depth);
        Fbo = Texture = _depth = 0;
    }
}
