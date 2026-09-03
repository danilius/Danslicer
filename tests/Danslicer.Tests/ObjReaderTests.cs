using System.Numerics;
using System.Text;
using Danslicer.Core.IO;

namespace Danslicer.Tests;

public class ObjReaderTests
{
    private static MemoryStream Stream(string text) => new(Encoding.UTF8.GetBytes(text));

    [Fact]
    public void ReadsTrianglesAndQuads()
    {
        // A unit quad in the XY plane as one f line, fan-triangulated into two triangles.
        var mesh = ObjReader.Read(Stream("""
            # comment
            v 0 0 0
            v 1 0 0
            v 1 1 0
            v 0 1 0
            f 1 2 3 4
            """));
        Assert.Equal(2, mesh.TriangleCount);
        Assert.Equal(4, mesh.VertexCount);
        Assert.Equal(Vector3.Zero, mesh.Bounds.Min);
        Assert.Equal(new Vector3(1, 1, 0), mesh.Bounds.Max);
        Assert.Equal(Vector3.UnitZ, mesh.FaceNormals[0]);
        Assert.Equal(Vector3.UnitZ, mesh.FaceNormals[1]);
    }

    [Fact]
    public void AcceptsSlashFormsAndNegativeIndices()
    {
        var mesh = ObjReader.Read(Stream("""
            v 0 0 0
            v 1 0 0
            v 0 1 0
            vt 0 0
            vn 0 0 1
            f 1/1/1 2/1/1 3//1
            f -3 -2 -1
            """));
        Assert.Equal(2, mesh.TriangleCount);
        Assert.Equal(3, mesh.VertexCount);
    }

    [Fact]
    public void SkipsDegenerateFanTriangles()
    {
        var mesh = ObjReader.Read(Stream("""
            v 0 0 0
            v 1 0 0
            v 0 1 0
            f 1 2 2 3
            """));
        Assert.Equal(1, mesh.TriangleCount);
    }

    [Fact]
    public void RejectsOutOfRangeIndex()
    {
        Assert.Throws<InvalidDataException>(() => ObjReader.Read(Stream("""
            v 0 0 0
            v 1 0 0
            f 1 2 5
            """)));
    }

    [Fact]
    public void RejectsFileWithoutFaces()
    {
        Assert.Throws<InvalidDataException>(() => ObjReader.Read(Stream("v 0 0 0\nv 1 0 0\nv 0 1 0\n")));
    }
}
