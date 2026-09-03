using System.Numerics;
using Clipper2Lib;
using Danslicer.Core.Geometry;
using Danslicer.Core.Slicing;

namespace Danslicer.Core.Supports.Generation;

internal readonly record struct Island(Vector3 Centroid, float AreaMm2, float Z);

/// <summary>
/// Layer islands via <see cref="MeshSlicer"/>'s public API: a region of a layer whose XY does
/// not overlap the previous layer (the plate counts as support for layer 0 when the mesh sits
/// on it). Slicer internals are not modified; see Grok/QUESTIONS.md for a possible helper.
/// </summary>
internal static class IslandFinder
{
    public static List<Island> Find(
        Mesh mesh, float layerHeight, float minAreaMm2, float plateZ, float overhangAngleDegrees = 45f)
    {
        var result = new List<Island>();
        if (layerHeight <= 1e-6f) return result;

        var prepared = new MeshSlicer.PreparedMesh(mesh, Matrix4x4.Identity);
        var h = (double)layerHeight;
        if (prepared.MaxZ <= plateZ + 1e-6) return result;

        var layerCount = (int)Math.Ceiling(prepared.MaxZ / h - 1e-6);
        if (layerCount <= 0) return result;

        // Inflate the previous layer by the XY growth of a slope at the overhang threshold so
        // self-supporting rims are not reported as islands; true gaps (table tops, cantilevers)
        // remain. A 20 µm floor swallows Clipper quantisation slivers.
        var theta = Math.Clamp(overhangAngleDegrees, 1f, 89f) * Math.PI / 180.0;
        var inflateMm = layerHeight * Math.Tan(theta) + 0.02;

        var buckets = MeshSlicer.BucketTriangles(prepared, h, layerCount);
        var segments = new List<MeshSlicer.Segment>();
        Paths64? previous = null;
        var sitsOnPlate = prepared.MinZ <= plateZ + layerHeight + 1e-4;

        for (int i = 0; i < layerCount; i++)
        {
            var z = (i + 0.5) * h;
            segments.Clear();
            MeshSlicer.CollectSegments(prepared, buckets[i], z, segments);
            var polygons = MeshSlicer.Finish(MeshSlicer.ChainSegments(segments), 0);

            Paths64 newborn;
            if (i == 0 && sitsOnPlate)
            {
                newborn = new Paths64();
            }
            else if (previous is null || previous.Count == 0)
            {
                newborn = polygons;
            }
            else
            {
                var supported = Clipper.InflatePaths(
                    previous, inflateMm * MeshSlicer.UnitsPerMm, JoinType.Round, EndType.Polygon);
                newborn = Clipper.Difference(polygons, supported, FillRule.NonZero);
            }

            CollectIslands(newborn, minAreaMm2, (float)z, result);
            previous = polygons;
        }

        return result;
    }

    /// <summary>
    /// Difference returns outers (positive area) and holes (negative). Net area of each outer
    /// minus its holes is the island; the centroid is rejected if it falls in a hole.
    /// </summary>
    private static void CollectIslands(Paths64 paths, float minAreaMm2, float z, List<Island> result)
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
            result.Add(new Island(new Vector3(c.X, c.Y, z), (float)areaMm2, z));
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
