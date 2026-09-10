using System.Numerics;
using Danslicer.Core.Geometry;
using Silk.NET.OpenGL;

namespace Danslicer.Render;

/// <summary>
/// GPU copy of a mesh, expanded to one vertex per triangle corner with the face normal, so shading is
/// flat per face. This reads well on CAD parts and is honest about the actual triangulation.
/// </summary>
public sealed unsafe class GpuMesh : IDisposable
{
    private readonly GL _gl;
    private readonly uint _vao;
    private readonly uint _vbo;
    private readonly uint _vertexCount;
    private uint _edgeVao;
    private uint _edgeEbo;
    private uint _edgeIndexCount;

    public GpuMesh(GL gl, Mesh mesh)
    {
        _gl = gl;
        var data = new float[mesh.TriangleCount * 3 * 6];
        var k = 0;
        for (int t = 0; t < mesh.TriangleCount; t++)
        {
            var n = mesh.FaceNormals[t];
            for (int c = 0; c < 3; c++)
            {
                var p = mesh.Positions[mesh.Indices[t * 3 + c]];
                data[k++] = p.X; data[k++] = p.Y; data[k++] = p.Z;
                data[k++] = n.X; data[k++] = n.Y; data[k++] = n.Z;
            }
        }
        _vertexCount = (uint)(mesh.TriangleCount * 3);

        _vao = gl.GenVertexArray();
        _vbo = gl.GenBuffer();
        gl.BindVertexArray(_vao);
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
        fixed (float* ptr = data)
            gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(data.Length * sizeof(float)), ptr, BufferUsageARB.StaticDraw);

        const uint stride = 6 * sizeof(float);
        gl.EnableVertexAttribArray(0);
        gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, (void*)0);
        gl.EnableVertexAttribArray(1);
        gl.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, stride, (void*)(3 * sizeof(float)));
        gl.BindVertexArray(0);
    }

    public void Draw()
    {
        _gl.BindVertexArray(_vao);
        _gl.DrawArrays(PrimitiveType.Triangles, 0, _vertexCount);
        _gl.BindVertexArray(0);
    }

    /// <summary>
    /// Draws the triangulation's edges as lines (the wireframe overlay). The edge index buffer
    /// is built on first use only, so meshes that never show a wireframe pay nothing; shared
    /// edges draw twice, which is invisible at equal colour and depth.
    /// </summary>
    public void DrawEdges()
    {
        if (_edgeVao == 0) BuildEdges();
        _gl.BindVertexArray(_edgeVao);
        _gl.DrawElements(PrimitiveType.Lines, _edgeIndexCount, DrawElementsType.UnsignedInt, (void*)0);
        _gl.BindVertexArray(0);
    }

    /// <summary>Line indices over the per-corner vertex layout: three edges per triangle.</summary>
    public static uint[] EdgeIndices(int triangleCount)
    {
        var indices = new uint[triangleCount * 6];
        for (var t = 0; t < triangleCount; t++)
        {
            var v = (uint)(t * 3);
            var k = t * 6;
            indices[k] = v; indices[k + 1] = v + 1;
            indices[k + 2] = v + 1; indices[k + 3] = v + 2;
            indices[k + 4] = v + 2; indices[k + 5] = v;
        }
        return indices;
    }

    private void BuildEdges()
    {
        var gl = _gl;
        var indices = EdgeIndices((int)(_vertexCount / 3));
        _edgeIndexCount = (uint)indices.Length;
        _edgeVao = gl.GenVertexArray();
        _edgeEbo = gl.GenBuffer();
        gl.BindVertexArray(_edgeVao);
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
        gl.EnableVertexAttribArray(0);
        gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 6 * sizeof(float), (void*)0);
        gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _edgeEbo);
        fixed (uint* ptr = indices)
            gl.BufferData(BufferTargetARB.ElementArrayBuffer, (nuint)(indices.Length * sizeof(uint)),
                ptr, BufferUsageARB.StaticDraw);
        gl.BindVertexArray(0);
    }

    public void Dispose()
    {
        if (_edgeVao != 0)
        {
            _gl.DeleteBuffer(_edgeEbo);
            _gl.DeleteVertexArray(_edgeVao);
        }
        _gl.DeleteBuffer(_vbo);
        _gl.DeleteVertexArray(_vao);
    }

    /// <summary>Viewport-only 2 mm slab, chamfered 0.6 mm below its exact printable top footprint.</summary>
    public static Mesh CreatePlate(float width, float depth)
    {
        var hw = width * 0.5f;
        var hd = depth * 0.5f;
        var positions = new List<Vector3>();
        foreach (var (inset, z) in new[] { (0f, 0f), (0f, -1.4f), (0.6f, -2f) })
            positions.AddRange([new(-hw + inset, -hd + inset, z), new(hw - inset, -hd + inset, z),
                new(hw - inset, hd - inset, z), new(-hw + inset, hd - inset, z)]);
        var indices = new List<int> { 0, 1, 2, 0, 2, 3, 8, 10, 9, 8, 11, 10 };
        for (var ring = 0; ring < 2; ring++)
            for (var i = 0; i < 4; i++)
            {
                int a = ring * 4 + i, b = ring * 4 + (i + 1) % 4;
                indices.AddRange([a, a + 4, b + 4, a, b + 4, b]);
            }
        return new Mesh(positions.ToArray(), indices.ToArray());
    }
}
