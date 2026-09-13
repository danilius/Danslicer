using Silk.NET.OpenGL;

namespace Danslicer.Render;

internal sealed unsafe class ModelShadowMap : IDisposable
{
    private readonly GL _gl;
    public uint Fbo { get; private set; }
    public uint Depth { get; private set; }
    public ShaderProgram Shader { get; }
    public ModelShadowMap(GL gl, bool gles)
    {
        _gl = gl;
        Shader = new ShaderProgram(gl, Shaders.Preamble(gles) + ShadowShaders.Vertex, Shaders.Preamble(gles) + ShadowShaders.Fragment);
        try
        {
            Depth = gl.GenTexture();
            gl.BindTexture(TextureTarget.Texture2D, Depth);
            gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.DepthComponent24, ShadowProjection.Resolution, ShadowProjection.Resolution, 0, PixelFormat.DepthComponent, PixelType.UnsignedInt, null);
            gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
            gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
            gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
            Fbo = gl.GenFramebuffer();
            gl.BindFramebuffer(FramebufferTarget.Framebuffer, Fbo);
            gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment, TextureTarget.Texture2D, Depth, 0);
            var buffers = stackalloc GLEnum[] { GLEnum.None };
            gl.DrawBuffers(1, buffers); gl.ReadBuffer(ReadBufferMode.None);
            if (gl.CheckFramebufferStatus(FramebufferTarget.Framebuffer) != GLEnum.FramebufferComplete)
                throw new InvalidOperationException("Incomplete model shadow framebuffer");
        }
        catch { Dispose(); throw; }
    }
    public void Dispose()
    {
        if (Fbo != 0) _gl.DeleteFramebuffer(Fbo);
        if (Depth != 0) _gl.DeleteTexture(Depth);
        Fbo = Depth = 0; Shader.Dispose();
    }
}
