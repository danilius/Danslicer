using System.Numerics;
using Clipper2Lib;
using Danslicer.Core.Geometry;
using Danslicer.Core.Slicing;

namespace Danslicer.Core.Supports.Generation;

internal readonly record struct Island(Vector3 Centroid, float AreaMm2, float Z, int LayerIndex);

/// <summary>
/// Layer islands: a region of a layer whose XY does not overlap the previous layer (the plate
/// counts as support for layer 0 when the mesh sits on it). Contours and the newborn difference
/// come from <see cref="MeshSlicer.LayerPolygons"/> / <see cref="MeshSlicer.NewbornIslands"/>.
/// </summary>
internal static class IslandFinder
{
    public static List<Island> Find(
        Mesh mesh, float layerHeight, float minAreaMm2, float plateZ, float overhangAngleDegrees = 45f)
    {
        var layers = LayerStack.Slice(mesh, layerHeight);
        return Find(layers, mesh.Bounds.Min.Z, layerHeight, minAreaMm2, plateZ, overhangAngleDegrees);
    }

    public static List<Island> Find(
        IReadOnlyList<SliceLayer> layers,
        float meshMinZ,
        float layerHeight,
        float minAreaMm2,
        float plateZ,
        float overhangAngleDegrees = 45f)
    {
        var result = new List<Island>();
        if (layers.Count == 0) return result;

        var theta = Math.Clamp(overhangAngleDegrees, 1f, 89f) * Math.PI / 180.0;
        var inflateMm = layerHeight * Math.Tan(theta) + 0.02;
        var sitsOnPlate = meshMinZ <= plateZ + layerHeight + 1e-4;

        var polygons = new List<Paths64>(layers.Count);
        foreach (var layer in layers) polygons.Add(layer.Polygons);
        var newborn = MeshSlicer.NewbornIslands(polygons, inflateMm);

        for (int i = 0; i < layers.Count; i++)
        {
            if (i == 0 && sitsOnPlate) continue;
            CollectIslands(newborn[i], minAreaMm2, layers[i].Z, layers[i].Index, result);
        }

        return result;
    }

    /// <summary>
    /// Islands by the strict definition (user, 2026-09-09): a connected solid region of a layer
    /// that overlaps NOTHING below it, i.e. a place where printing starts off the plate. A
    /// region that overlaps the previous layer anywhere — a slanted plate advancing each layer,
    /// a cantilever growing out of its post, a table top over its legs — is an overhang, not an
    /// island. The previous layer is inflated by the overhang allowance before the test so a
    /// self-supporting rim counts as carried. The first solid layer sits on the plate when the
    /// mesh does; a mesh clear of the plate starts with islands.
    /// </summary>
    public static List<Island> FindStarts(
        IReadOnlyList<SliceLayer> layers,
        float meshMinZ,
        float layerHeight,
        float minAreaMm2,
        float plateZ,
        float overhangAngleDegrees = 45f)
    {
        var result = new List<Island>();
        if (layers.Count == 0) return result;

        var theta = Math.Clamp(overhangAngleDegrees, 1f, 89f) * Math.PI / 180.0;
        var inflateMm = layerHeight * Math.Tan(theta) + 0.02;
        var sitsOnPlate = meshMinZ <= plateZ + layerHeight + 1e-4;
        // Overlap below this is a numerical sliver, not support (a hundredth of a square millimetre).
        var minOverlap = 0.01 * MeshSlicer.UnitsPerMm * MeshSlicer.UnitsPerMm;

        Paths64? previous = null;
        var firstSolidSeen = false;
        foreach (var layer in layers)
        {
            var polygons = layer.Polygons;
            if (polygons.Count == 0)
            {
                previous = polygons;
                continue;
            }
            if (!firstSolidSeen)
            {
                firstSolidSeen = true;
                if (sitsOnPlate)
                {
                    previous = polygons;
                    continue;
                }
            }

            var carried = previous is null || previous.Count == 0
                ? null
                : Clipper.InflatePaths(previous, inflateMm * MeshSlicer.UnitsPerMm, JoinType.Round, EndType.Polygon);
            foreach (var (outer, holes) in Regions(polygons))
            {
                if (carried is not null)
                {
                    var region = new Paths64 { outer };
                    region.AddRange(holes);
                    var overlap = Clipper.Intersect(region, carried, FillRule.NonZero);
                    if (Math.Abs(MeshSlicer.AreaMm2(overlap)) * MeshSlicer.UnitsPerMm * MeshSlicer.UnitsPerMm > minOverlap)
                        continue;
                }
                var net = Clipper.Area(outer) + holes.Sum(Clipper.Area);
                var areaMm2 = net / (MeshSlicer.UnitsPerMm * MeshSlicer.UnitsPerMm);
                if (areaMm2 < minAreaMm2) continue;
                var c = CentroidOnSolid(outer, holes);
                result.Add(new Island(new Vector3(c.X, c.Y, layer.Z), (float)areaMm2, layer.Z, layer.Index));
            }
            previous = polygons;
        }
        return result;
    }

    /// <summary>
    /// The connected solid regions of a layer: each outer contour (positive) with the holes
    /// (negative) directly inside it. Solids nested inside holes are regions of their own.
    /// </summary>
    private static IEnumerable<(Path64 Outer, List<Path64> Holes)> Regions(Paths64 polygons)
    {
        var tree = new PolyTree64();
        Clipper.BooleanOp(ClipType.Union, polygons, null, tree, FillRule.NonZero);
        var pending = new Stack<PolyPath64>();
        for (var i = 0; i < tree.Count; i++) pending.Push(tree[i]);
        while (pending.Count > 0)
        {
            var node = pending.Pop();
            if (node.Polygon is null) continue;
            var outer = LayerStack.OrientedPositive(node.Polygon);
            var holes = new List<Path64>();
            for (var h = 0; h < node.Count; h++)
            {
                var hole = node[h];
                if (hole.Polygon is not null) holes.Add(OrientedNegative(hole.Polygon));
                for (var k = 0; k < hole.Count; k++) pending.Push(hole[k]);
            }
            yield return (outer, holes);
        }
    }

    private static Path64 OrientedNegative(Path64 path)
    {
        if (Clipper.Area(path) <= 0) return path;
        var copy = new Path64(path);
        copy.Reverse();
        return copy;
    }

    /// <summary>
    /// Difference returns outers (positive area) and holes (negative). Net area of each outer
    /// minus its holes is the island; the centroid is rejected if it falls in a hole.
    /// </summary>
    private static void CollectIslands(Paths64 paths, float minAreaMm2, float z, int layerIndex, List<Island> result)
    {
        if (paths.Count == 0) return;
        var scale = MeshSlicer.UnitsPerMm * MeshSlicer.UnitsPerMm;
        var outers = new List<Path64>();
        var holes = new List<Path64>();
        foreach (var path in paths)
        {
            var a = Clipper.Area(path);
            if (a > 0) outers.Add(path);
            else if (a < 0) holes.Add(path);
        }

        foreach (var outer in outers)
        {
            var mine = new List<Path64>();
            double net = Clipper.Area(outer);
            foreach (var hole in holes)
            {
                if (hole.Count == 0) continue;
                if (Clipper.PointInPolygon(hole[0], outer) == PointInPolygonResult.IsOutside) continue;
                mine.Add(hole);
                net += Clipper.Area(hole);
            }
            var areaMm2 = net / scale;
            if (areaMm2 < minAreaMm2) continue;

            var c = CentroidOnSolid(outer, mine);
            result.Add(new Island(new Vector3(c.X, c.Y, z), (float)areaMm2, z, layerIndex));
        }
    }

    private static Vector2 CentroidOnSolid(Path64 outer, List<Path64> holes)
    {
        var c = CentroidMm(outer);
        var pt = new Point64(
            (long)Math.Round(c.X * MeshSlicer.UnitsPerMm),
            (long)Math.Round(c.Y * MeshSlicer.UnitsPerMm));
        if (Clipper.PointInPolygon(pt, outer) != PointInPolygonResult.IsOutside &&
            !holes.Any(h => Clipper.PointInPolygon(pt, h) != PointInPolygonResult.IsOutside))
            return c;

        foreach (var p in outer)
        {
            if (holes.Any(h => Clipper.PointInPolygon(p, h) != PointInPolygonResult.IsOutside)) continue;
            return new Vector2((float)(p.X / MeshSlicer.UnitsPerMm), (float)(p.Y / MeshSlicer.UnitsPerMm));
        }
        return new Vector2(
            (float)(outer[0].X / MeshSlicer.UnitsPerMm),
            (float)(outer[0].Y / MeshSlicer.UnitsPerMm));
    }

    private static Vector2 CentroidMm(Path64 path)
    {
        double a = 0, cx = 0, cy = 0;
        for (int i = 0, j = path.Count - 1; i < path.Count; j = i++)
        {
            double x0 = path[j].X, y0 = path[j].Y;
            double x1 = path[i].X, y1 = path[i].Y;
            double cr = x0 * y1 - x1 * y0;
            a += cr;
            cx += (x0 + x1) * cr;
            cy += (y0 + y1) * cr;
        }
        a *= 0.5;
        if (Math.Abs(a) < 1.0)
        {
            double sx = 0, sy = 0;
            foreach (var p in path) { sx += p.X; sy += p.Y; }
            var inv = 1.0 / Math.Max(path.Count, 1);
            return new Vector2((float)(sx * inv / MeshSlicer.UnitsPerMm), (float)(sy * inv / MeshSlicer.UnitsPerMm));
        }

        cx /= 6 * a;
        cy /= 6 * a;
        return new Vector2((float)(cx / MeshSlicer.UnitsPerMm), (float)(cy / MeshSlicer.UnitsPerMm));
    }
}
