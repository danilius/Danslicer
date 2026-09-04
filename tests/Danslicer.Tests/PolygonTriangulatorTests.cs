using System.Numerics;
using Clipper2Lib;
using Danslicer.Core.Slicing;

namespace Danslicer.Tests;

public sealed class PolygonTriangulatorTests
{
    [Fact]
    public void TriangulatesMultipleOutersHolesAndNestedIslandsWithoutAreaLoss()
    {
        var polygons = new Paths64
        {
            Square(0, 0, 20, 20),
            Square(4, 4, 16, 16, positive: false),
            Square(7, 7, 13, 13),
            Square(30, 0, 35, 8),
        };

        var triangles = PolygonTriangulator.Triangulate(polygons);

        Assert.NotEmpty(triangles);
        Assert.All(triangles, triangle =>
        {
            Assert.Equal(3, triangle.Count);
            Assert.NotEqual(0, Clipper.Area(triangle));
        });
        Assert.Equal(NetArea(polygons), triangles.Sum(t => Math.Abs(Clipper.Area(t))), 6);
    }

    [Fact]
    public void OutputIsDeterministicForARealMeshCrossSection()
    {
        var mesh = Meshes.Merge(
            Meshes.Box(14, 12, 8),
            Meshes.Box(4, 5, 8, new Vector3(20, 3, 0)));
        var prepared = new MeshSlicer.PreparedMesh(mesh, Matrix4x4.Identity);
        var section = MeshSlicer.PolygonsAt(prepared, 3.25);

        var first = PolygonTriangulator.Triangulate(section);
        var second = PolygonTriangulator.Triangulate(section);

        Assert.Equal(NetArea(section), first.Sum(t => Math.Abs(Clipper.Area(t))), 6);
        Assert.Equal(Serialize(first), Serialize(second));
    }

    private static Path64 Square(long left, long bottom, long right, long top, bool positive = true)
    {
        var path = new Path64
        {
            new(left, bottom), new(right, bottom), new(right, top), new(left, top),
        };
        if (!positive) path.Reverse();
        return path;
    }

    private static double NetArea(Paths64 paths) => Math.Abs(paths.Sum(Clipper.Area));

    private static string Serialize(Paths64 paths) => string.Join("|", paths.Select(path =>
        string.Join(";", path.Select(point => $"{point.X},{point.Y}"))));
}
