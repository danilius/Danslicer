using System.Numerics;
using Clipper2Lib;
using Danslicer.Core.Slicing;

namespace Danslicer.Tests;

public sealed class ClipCapBuilderTests
{
    [Theory]
    [InlineData(ClipCapFace.Lower, -1f)]
    [InlineData(ClipCapFace.Upper, 1f)]
    public void CubeCapHasTrueSectionAreaWindingAndInset(ClipCapFace face, float expectedNormalZ)
    {
        var transform = Matrix4x4.CreateScale(2, 3, 1) * Matrix4x4.CreateTranslation(5, -4, 7);
        var mesh = ClipCapBuilder.Build(Meshes.Box(4, 5, 6), transform, 10, face);

        Assert.NotNull(mesh);
        Assert.Equal(120.0, AreaXy(mesh!), 3);
        Assert.All(mesh.FaceNormals, normal => Assert.Equal(expectedNormalZ, normal.Z, 5));
        var expectedZ = 10 + (face == ClipCapFace.Upper
            ? -ClipCapBuilder.PlaneInsetMm
            : ClipCapBuilder.PlaneInsetMm);
        Assert.All(mesh.Positions, point => Assert.Equal(expectedZ, point.Z, 5));
        Assert.All(mesh.FaceNormals, normal => Assert.True(Math.Abs(normal.Z) > 0.999f));
    }

    [Fact]
    public void PolygonCapPreservesHoleAreaAndRejectsDegenerateTriangles()
    {
        var outer = Clipper.MakePath([0, 0, 10000, 0, 10000, 10000, 0, 10000]);
        var hole = Clipper.MakePath([2000, 2000, 2000, 8000, 8000, 8000, 8000, 2000]);

        var mesh = ClipCapBuilder.Build(new Paths64 { outer, hole }, 2, ClipCapFace.Upper);

        Assert.NotNull(mesh);
        Assert.Equal(64.0, AreaXy(mesh!), 3);
        Assert.All(mesh.FaceNormals, normal => Assert.True(float.IsFinite(normal.Z) && normal.Z > 0.999f));
    }

    private static double AreaXy(Danslicer.Core.Geometry.Mesh mesh)
    {
        double area = 0;
        for (int i = 0; i < mesh.TriangleCount; i++)
        {
            mesh.GetTriangle(i, out var a, out var b, out var c);
            area += Math.Abs(Vector3.Cross(b - a, c - a).Z) * 0.5;
        }
        return area;
    }
}
