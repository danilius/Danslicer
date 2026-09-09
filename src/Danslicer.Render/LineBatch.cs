using System.Numerics;
using Silk.NET.OpenGL;

namespace Danslicer.Render;

/// <summary>A coloured segment; <paramref name="Width"/> is in pixels, 1 for a hairline.</summary>
public readonly record struct OverlayLine(Vector3 A, Vector3 B, Vector4 Color, float Width = 1f);

/// <summary>
/// Accumulates coloured line segments and draws them in one call per kind. Hairlines go
/// through GL lines; wider lines are two triangles each, expanded to their pixel width in the
/// vertex shader, because core-profile line width is one pixel on most drivers.
/// </summary>
public sealed unsafe class LineBatch : IDisposable
{
    private const int FloatsPerVertex = 7;
    private const int FloatsPerWideVertex = 11; // position, other end, side sign + width, colour
    private readonly GL _gl;
    private readonly uint _vao;
    private readonly uint _vbo;
    private readonly uint _wideVao;
    private readonly uint _wideVbo;
    private readonly List<float> _data = new();
    private readonly List<float> _wide = new();
    private int _capacity;
    private int _wideCapacity;

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

        _wideVao = gl.GenVertexArray();
        _wideVbo = gl.GenBuffer();
        gl.BindVertexArray(_wideVao);
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, _wideVbo);
        const uint wideStride = FloatsPerWideVertex * sizeof(float);
        gl.EnableVertexAttribArray(0);
        gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, wideStride, (void*)0);
        gl.EnableVertexAttribArray(1);
        gl.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, wideStride, (void*)(3 * sizeof(float)));
        gl.EnableVertexAttribArray(2);
        gl.VertexAttribPointer(2, 1, VertexAttribPointerType.Float, false, wideStride, (void*)(6 * sizeof(float)));
        gl.EnableVertexAttribArray(3);
        gl.VertexAttribPointer(3, 4, VertexAttribPointerType.Float, false, wideStride, (void*)(7 * sizeof(float)));
        gl.BindVertexArray(0);
    }

    public int Count => _data.Count / (FloatsPerVertex * 2) + _wide.Count / (FloatsPerWideVertex * 6);

    public bool HasWide => _wide.Count > 0;

    public void Clear()
    {
        _data.Clear();
        _wide.Clear();
    }

    public void Add(Vector3 a, Vector3 b, Vector4 color)
    {
        AddVertex(a, color);
        AddVertex(b, color);
    }

    public void Add(in OverlayLine line)
    {
        if (line.Width <= 1.01f) Add(line.A, line.B, line.Color);
        else AddWide(line.A, line.B, line.Color, line.Width);
    }

    /// <summary>Two triangles; each vertex carries its own end, the other end and a signed half-width.</summary>
    private void AddWide(Vector3 a, Vector3 b, Vector4 color, float width)
    {
        var half = width * 0.5f;
        AddWideVertex(a, b, -half, color);
        AddWideVertex(a, b, half, color);
        AddWideVertex(b, a, half, color);
        AddWideVertex(b, a, half, color);
        AddWideVertex(b, a, -half, color);
        AddWideVertex(a, b, -half, color);
    }

    private void AddWideVertex(Vector3 p, Vector3 other, float side, Vector4 c)
    {
        _wide.Add(p.X); _wide.Add(p.Y); _wide.Add(p.Z);
        _wide.Add(other.X); _wide.Add(other.Y); _wide.Add(other.Z);
        _wide.Add(side);
        _wide.Add(c.X); _wide.Add(c.Y); _wide.Add(c.Z); _wide.Add(c.W);
    }

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

    /// <summary>Uploads and draws the wide segments. The caller binds the wide-line shader.</summary>
    public void DrawWide()
    {
        if (_wide.Count == 0) return;
        var array = _wide.ToArray();
        _gl.BindVertexArray(_wideVao);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _wideVbo);
        fixed (float* ptr = array)
        {
            if (array.Length > _wideCapacity)
            {
                _wideCapacity = Math.Max(array.Length, _wideCapacity * 2);
                _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(_wideCapacity * sizeof(float)), null, BufferUsageARB.DynamicDraw);
            }
            _gl.BufferSubData(BufferTargetARB.ArrayBuffer, 0, (nuint)(array.Length * sizeof(float)), ptr);
        }
        _gl.DrawArrays(PrimitiveType.Triangles, 0, (uint)(array.Length / FloatsPerWideVertex));
        _gl.BindVertexArray(0);
    }

    public void Dispose()
    {
        _gl.DeleteBuffer(_vbo);
        _gl.DeleteVertexArray(_vao);
        _gl.DeleteBuffer(_wideVbo);
        _gl.DeleteVertexArray(_wideVao);
    }
}
