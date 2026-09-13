using System.Numerics;
using Clipper2Lib;
using Danslicer.Core.Geometry;
using Danslicer.Core.Slicing;

namespace Danslicer.Core.Supports.Generation;

internal readonly record struct Island(Vector3 Centroid, float AreaMm2, float Z, int LayerIndex)
{
    public Paths64 Footprint { get; init; } = new();
}

/// <summary>Layer regions requiring support. Generation includes overhang strips;
/// standalone detection considers disconnected components only.</summary>
internal static class IslandFinder
{
    public static List<Island> Find(
        Mesh mesh, float layerHeight, float minAreaMm2, float plateZ, float overhangAngleDegrees = 45f)
    {
        var layers = LayerStack.Slice(mesh, layerHeight);
        return Find(layers, mesh.Bounds.Min.Z, layerHeight, minAreaMm2, plateZ, overhangAngleDegrees);
    }

    public static List<Island> Find(
        IReadOnlyList<SliceLayer> layers, float meshMinZ, float layerHeight,
        float minAreaMm2, float plateZ, float overhangAngleDegrees = 45f,
        bool includeOverhangs = true)
    {
        var result = new List<Island>();
        var theta = Math.Clamp(overhangAngleDegrees, 1f, 89f) * Math.PI / 180.0;
        var inflateMm = layerHeight * Math.Tan(theta) + 0.02;
        var newborn = includeOverhangs
            ? MeshSlicer.NewbornIslands(layers.Select(l => l.Polygons).ToList(), inflateMm)
            : null;

        for (int i = 0; i < layers.Count; i++)
        {
            // Only the layer straddling the plate can obtain support from it.
            if (layers[i].Z - layerHeight / 2 <= plateZ + 1e-4 &&
                meshMinZ <= plateZ + 1e-4) continue;
            var regions = PolygonComponents.Split(newborn is null ? layers[i].Polygons : newborn[i]);
            foreach (var region in regions)
            {
                if (newborn is null && i > 0 && MeshSlicer.AreaMm2(
                    Clipper.Intersect(region, layers[i - 1].Polygons, FillRule.NonZero)) > 0)
                    continue;
                var area = MeshSlicer.AreaMm2(region);
                if (area < minAreaMm2) continue;
                var point = PointOnSolid(region);
                result.Add(new Island(new Vector3(point, layers[i].Z), (float)area,
                    layers[i].Z, layers[i].Index) { Footprint = region });
            }
        }
        return result;
    }

    private static Vector2 PointOnSolid(Paths64 region)
    {
        double crossSum = 0, x = 0, y = 0;
        foreach (var path in region)
        for (int i = 0, j = path.Count - 1; i < path.Count; j = i++)
        {
            double cross = (double)path[j].X * path[i].Y - (double)path[i].X * path[j].Y;
            crossSum += cross;
            x += (path[j].X + path[i].X) * cross;
            y += (path[j].Y + path[i].Y) * cross;
        }
        var center = new Point64(x / (3 * crossSum), y / (3 * crossSum));
        if (Clipper.PointInPolygon(center, region[0]) == PointInPolygonResult.IsInside &&
            region.Skip(1).All(h => Clipper.PointInPolygon(center, h) == PointInPolygonResult.IsOutside))
            return Mm(center.X, center.Y);

        // A triangle interior stays on solid material even for rings and concave regions.
        var triangles = PolygonTriangulator.Triangulate(region);
        var triangle = triangles.MaxBy(t => Math.Abs(Clipper.Area(t)))!;
        return Mm(triangle.Average(p => (double)p.X), triangle.Average(p => (double)p.Y));
    }

    private static Vector2 Mm(double x, double y) =>
        new((float)(x / MeshSlicer.UnitsPerMm), (float)(y / MeshSlicer.UnitsPerMm));
}
