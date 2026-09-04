using System.Numerics;
using System.Runtime.InteropServices;
using Silk.NET.OpenGL;

namespace Danslicer.Render;

public sealed class ShaderProgram : IDisposable
{
    private readonly GL _gl;
    private readonly Dictionary<string, int> _uniforms = new();

    public uint Handle { get; }

    public ShaderProgram(GL gl, string vertexSource, string fragmentSource)
    {
        _gl = gl;
        var vs = Compile(ShaderType.VertexShader, vertexSource);
        var fs = Compile(ShaderType.FragmentShader, fragmentSource);

        Handle = gl.CreateProgram();
        gl.AttachShader(Handle, vs);
        gl.AttachShader(Handle, fs);
        gl.LinkProgram(Handle);
        gl.GetProgram(Handle, ProgramPropertyARB.LinkStatus, out var status);
        if (status == 0)
            throw new InvalidOperationException("Shader link failed: " + gl.GetProgramInfoLog(Handle));

        gl.DetachShader(Handle, vs);
        gl.DetachShader(Handle, fs);
        gl.DeleteShader(vs);
        gl.DeleteShader(fs);
    }

    private uint Compile(ShaderType type, string source)
    {
        var shader = _gl.CreateShader(type);
        _gl.ShaderSource(shader, source);
        _gl.CompileShader(shader);
        _gl.GetShader(shader, ShaderParameterName.CompileStatus, out var status);
        if (status == 0)
        {
            var log = _gl.GetShaderInfoLog(shader);
            _gl.DeleteShader(shader);
            throw new InvalidOperationException($"{type} compile failed: {log}\n{source}");
        }
        return shader;
    }

    public void Use() => _gl.UseProgram(Handle);

    private int Location(string name)
    {
        if (!_uniforms.TryGetValue(name, out var loc))
        {
            loc = _gl.GetUniformLocation(Handle, name);
            _uniforms[name] = loc;
        }
        return loc;
    }

    /// <summary>
    /// Uploads a System.Numerics matrix untransposed. GLSL then holds the column-major equivalent, so
    /// shaders multiply as <c>mat * vec</c>.
    /// </summary>
    public void Set(string name, in Matrix4x4 m)
    {
        var copy = m;
        var span = MemoryMarshal.CreateReadOnlySpan(ref copy.M11, 16);
        _gl.UniformMatrix4(Location(name), 1, false, span);
    }

    public void Set(string name, Vector2 v) => _gl.Uniform2(Location(name), v.X, v.Y);
    public void Set(string name, Vector3 v) => _gl.Uniform3(Location(name), v.X, v.Y, v.Z);
    public void Set(string name, Vector4 v) => _gl.Uniform4(Location(name), v.X, v.Y, v.Z, v.W);
    public void Set(string name, float f) => _gl.Uniform1(Location(name), f);
    public void Set(string name, int i) => _gl.Uniform1(Location(name), i);

    public void Dispose() => _gl.DeleteProgram(Handle);
}
