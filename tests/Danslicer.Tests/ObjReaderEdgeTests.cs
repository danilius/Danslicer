using System;
using System.IO;
using System.Linq;
using System.Text;
using Danslicer.Core.IO;
using Xunit;

namespace Danslicer.Tests;

public class ObjReaderEdgeTests
{
    [Fact]
    public void QuadAndPentagonFanTriangulation()
    {
        var objContent = @"
v 0 0 0
v 1 0 0
v 1 1 0
v 0 1 0
v 0.5 0.5 0
f 1 2 3 4
f 1 2 3 4 5";

        var mesh = ObjReader.Read(new MemoryStream(Encoding.UTF8.GetBytes(objContent)));

        Assert.Equal(5, mesh.Positions.Length);
        Assert.Equal(15, mesh.Indices.Length); // 2 triangles for quad, 3 for pentagon, but each triangle uses 3 indices
    }

    [Fact]
    public void DifferentFaceIndexFormats()
    {
        var objContent = @"
v 0 0 0
v 1 0 0
v 0 1 0
f 1 2 3
f 1/1 2/2 3/3
f 1//1 2//2 3//3
f 1/1/1 2/2/2 3/3/3";

        var mesh = ObjReader.Read(new MemoryStream(Encoding.UTF8.GetBytes(objContent)));

        Assert.Equal(3, mesh.Positions.Length);
        Assert.Equal(12, mesh.Indices.Length); // 4 triangles (each face is the same triangle)
    }

    [Fact]
    public void NegativeIndices()
    {
        var objContentPositive = @"
v 0 0 0
v 1 0 0
v 0 1 0
f 1 2 3";

        var objContentNegative = @"
v 0 0 0
v 1 0 0
v 0 1 0
f -3 -2 -1";

        var meshPositive = ObjReader.Read(new MemoryStream(Encoding.UTF8.GetBytes(objContentPositive)));
        var meshNegative = ObjReader.Read(new MemoryStream(Encoding.UTF8.GetBytes(objContentNegative)));

        Assert.Equal(meshPositive.Positions, meshNegative.Positions);
        Assert.Equal(meshPositive.Indices, meshNegative.Indices);
    }

    [Fact]
    public void IgnoredLines()
    {
        var objContent = @"
# This is a comment
v 0 0 0
v 1 0 0
v 0 1 0
g some_group
s 1
f 1 2 3";

        var mesh = ObjReader.Read(new MemoryStream(Encoding.UTF8.GetBytes(objContent)));

        Assert.Equal(3, mesh.Positions.Length);
        Assert.Equal(3, mesh.Indices.Length);
    }

    [Fact]
    public void FaceIndexOutOfRange()
    {
        var objContent = @"
v 0 0 0
v 1 0 0
v 0 1 0
f 1 2 4";

        Assert.Throws<InvalidDataException>(() => ObjReader.Read(new MemoryStream(Encoding.UTF8.GetBytes(objContent))));
    }
}
