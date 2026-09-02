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

    public void Dispose()
    {
        _gl.DeleteBuffer(_vbo);
        _gl.DeleteVertexArray(_vao);
    }

    /// <summary>A flat quad in the XY plane, used for the build plate.</summary>
    public static Mesh CreatePlate(float width, float depth)
    {
        var hw = width * 0.5f;
        var hd = depth * 0.5f;
        var positions = new[]
        {
            new Vector3(-hw, -hd, 0), new Vector3(hw, -hd, 0),
            new Vector3(hw, hd, 0), new Vector3(-hw, hd, 0),
        };
        return new Mesh(positions, new[] { 0, 1, 2, 0, 2, 3 });
    }
}
