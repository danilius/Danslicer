using System.Numerics;
using Silk.NET.OpenGL;

namespace Danslicer.Render;

public readonly record struct OverlayLine(Vector3 A, Vector3 B, Vector4 Color);

/// <summary>Accumulates coloured line segments and draws them in one call.</summary>
public sealed unsafe class LineBatch : IDisposable
{
    private const int FloatsPerVertex = 7;
    private readonly GL _gl;
    private readonly uint _vao;
    private readonly uint _vbo;
    private readonly List<float> _data = new();
    private int _capacity;

    public LineBatch(GL gl)
    {
        _gl = gl;
        _vao = gl.GenVertexArray();
        _vbo = gl.GenBuffer();
        gl.BindVertexArray(_vao);
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
        const uint stride = FloatsPerVertex * sizeof(float);
        gl.EnableVertexAttribArray(0);
        gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, (void*)0);
        gl.EnableVertexAttribArray(1);
        gl.VertexAttribPointer(1, 4, VertexAttribPointerType.Float, false, stride, (void*)(3 * sizeof(float)));
        gl.BindVertexArray(0);
    }

    public int Count => _data.Count / (FloatsPerVertex * 2);

    public void Clear() => _data.Clear();

    public void Add(Vector3 a, Vector3 b, Vector4 color)
    {
        AddVertex(a, color);
        AddVertex(b, color);
    }

    public void Add(in OverlayLine line) => Add(line.A, line.B, line.Color);

    private void AddVertex(Vector3 p, Vector4 c)
    {
        _data.Add(p.X); _data.Add(p.Y); _data.Add(p.Z);
        _data.Add(c.X); _data.Add(c.Y); _data.Add(c.Z); _data.Add(c.W);
    }

    /// <summary>Uploads and draws the accumulated segments. The caller binds the shader.</summary>
    public void Draw()
    {
        if (_data.Count == 0) return;
        var array = _data.ToArray();
        _gl.BindVertexArray(_vao);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
        fixed (float* ptr = array)
        {
            if (array.Length > _capacity)
            {
                _capacity = Math.Max(array.Length, _capacity * 2);
                _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(_capacity * sizeof(float)), null, BufferUsageARB.DynamicDraw);
            }
            _gl.BufferSubData(BufferTargetARB.ArrayBuffer, 0, (nuint)(array.Length * sizeof(float)), ptr);
        }
        _gl.DrawArrays(PrimitiveType.Lines, 0, (uint)(array.Length / FloatsPerVertex));
        _gl.BindVertexArray(0);
    }

    public void Dispose()
    {
        _gl.DeleteBuffer(_vbo);
        _gl.DeleteVertexArray(_vao);
    }
}
