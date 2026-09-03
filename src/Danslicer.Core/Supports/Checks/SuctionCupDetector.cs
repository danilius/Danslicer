using System.Numerics;
using Clipper2Lib;
using Danslicer.Core.Slicing;
using Danslicer.Core.Supports.Generation;

namespace Danslicer.Core.Supports.Checks;

internal static class SuctionCupDetector
{
    private enum Cell
    {
        Solid,
        Hole,
        ExteriorOnSolidLayer,
        EmptyLayer,
    }

    public static void Find(IReadOnlyList<SliceLayer> layers, PrintCheckParameters p, int objectIndex, List<CheckFinding> output)
    {
        if (layers.Count == 0) return;

        var sealedLayers = new Paths64[layers.Count];
        var holes = new List<Path64>[layers.Count];
        for (int i = 0; i < layers.Count; i++)
        {
            sealedLayers[i] = Seal(layers[i].Polygons, p.DrainOpeningMm);
            holes[i] = ExtractHoles(sealedLayers[i]);
        }

        var count = 0;
        var ids = new int[layers.Count][];
        for (int i = 0; i < layers.Count; i++)
        {
            ids[i] = new int[holes[i].Count];
            for (int h = 0; h < holes[i].Count; h++)
                ids[i][h] = count++;
        }
        if (count == 0) return;

        var uf = new int[count];
        for (int i = 0; i < count; i++) uf[i] = i;
        int Root(int x)
        {
            while (uf[x] != x) { uf[x] = uf[uf[x]]; x = uf[x]; }
            return x;
        }
        void Union(int a, int b)
        {
            a = Root(a); b = Root(b);
            if (a != b) uf[a] = b;
        }

        for (int i = 1; i < layers.Count; i++)
        {
            for (int a = 0; a < holes[i].Count; a++)
            for (int b = 0; b < holes[i - 1].Count; b++)
            {
                if (HolesOverlap(holes[i][a], holes[i - 1][b]))
                    Union(ids[i][a], ids[i - 1][b]);
            }
        }

        var groups = new Dictionary<int, List<(int Layer, int Hole)>>();
        for (int i = 0; i < layers.Count; i++)
        for (int h = 0; h < holes[i].Count; h++)
        {
            var r = Root(ids[i][h]);
            if (!groups.TryGetValue(r, out var list)) groups[r] = list = new List<(int, int)>();
            list.Add((i, h));
        }

        var scale = MeshSlicer.UnitsPerMm * MeshSlicer.UnitsPerMm;
        foreach (var members in groups.Values.OrderBy(g => g.Min(m => m.Layer)).ThenBy(g => g[0].Hole))
        {
            members.Sort((x, y) => x.Layer != y.Layer ? x.Layer.CompareTo(y.Layer) : x.Hole.CompareTo(y.Hole));
            var lo = members[0].Layer;
            var hi = members[^1].Layer;

            var sideVent = false;
            double volume = 0;
            Vector2 mouth = default;
            var mouthSet = false;
            foreach (var (layer, hole) in members)
            {
                volume += Math.Abs(Clipper.Area(holes[layer][hole])) / scale * p.LayerHeightMm;
                if (!mouthSet && layer == lo)
                {
                    mouth = HoleCentroid(holes[layer][hole]);
                    mouthSet = true;
                }
                if (Classify(holes[layer][hole], layer + 1, sealedLayers) == Cell.ExteriorOnSolidLayer)
                    sideVent = true;
                if (Classify(holes[layer][hole], layer - 1, sealedLayers) == Cell.ExteriorOnSolidLayer)
                    sideVent = true;
            }

            var top = Classify(holes[hi][members.Last(m => m.Layer == hi).Hole], hi + 1, sealedLayers);
            var bottom = Classify(holes[lo][members.First(m => m.Layer == lo).Hole], lo - 1, sealedLayers);

            var roof = top == Cell.Solid;
            var openUp = top == Cell.EmptyLayer;
            var openDown = lo == 0 || bottom == Cell.EmptyLayer;
            if (!roof || openUp || sideVent || !openDown) continue;
            if (volume < p.MinSuctionVolumeMm3) continue;

            var z = layers[lo].Z;
            output.Add(new CheckFinding
            {
                Kind = CheckKind.SuctionCup,
                Severity = CheckSeverity.Warning,
                ObjectIndex = objectIndex,
                LayerFrom = lo,
                LayerTo = hi,
                Point = new Vector3(mouth.X, mouth.Y, z),
                VolumeMm3 = (float)volume,
            });
        }
    }

    private static Paths64 Seal(Paths64 polygons, float drainOpeningMm)
    {
        if (polygons.Count == 0 || drainOpeningMm <= 1e-6f) return polygons;
        var delta = (drainOpeningMm * 0.5) * MeshSlicer.UnitsPerMm;
        var grown = Clipper.InflatePaths(polygons, delta, JoinType.Round, EndType.Polygon);
        return Clipper.Union(grown, FillRule.NonZero);
    }

    private static List<Path64> ExtractHoles(Paths64 polygons)
    {
        var holes = new List<Path64>();
        foreach (var path in polygons)
            if (Clipper.Area(path) < 0) holes.Add(path);
        return holes;
    }

    private static bool HolesOverlap(Path64 a, Path64 b)
    {
        var pa = new Paths64 { LayerStack.OrientedPositive(a) };
        var pb = new Paths64 { LayerStack.OrientedPositive(b) };
        var inter = Clipper.Intersect(pa, pb, FillRule.NonZero);
        return MeshSlicer.AreaMm2(inter) > 1e-6;
    }

    private static Cell Classify(Path64 hole, int neighborIndex, Paths64[] layers)
    {
        if (neighborIndex < 0) return Cell.EmptyLayer; // caller treats layer 0 as FEP, not this
        if (neighborIndex >= layers.Length) return Cell.EmptyLayer;
        var polys = layers[neighborIndex];
        if (polys.Count == 0) return Cell.EmptyLayer;

        var sample = SampleInside(hole);
        var winding = Winding(sample, polys);
        if (winding != 0) return Cell.Solid;

        foreach (var path in polys)
        {
            if (Clipper.Area(path) >= 0) continue;
            if (Clipper.PointInPolygon(sample, LayerStack.OrientedPositive(path)) != PointInPolygonResult.IsOutside)
                return Cell.Hole;
        }
        return Cell.ExteriorOnSolidLayer;
    }

    private static int Winding(Point64 pt, Paths64 polygons)
    {
        int w = 0;
        foreach (var path in polygons)
        {
            if (Clipper.PointInPolygon(pt, path) == PointInPolygonResult.IsOutside) continue;
            w += Clipper.Area(path) > 0 ? 1 : -1;
        }
        return w;
    }

    private static Point64 SampleInside(Path64 hole)
    {
        var positive = LayerStack.OrientedPositive(hole);
        var c = Centroid(positive);
        var pt = new Point64(
            (long)Math.Round(c.X * MeshSlicer.UnitsPerMm),
            (long)Math.Round(c.Y * MeshSlicer.UnitsPerMm));
        if (Clipper.PointInPolygon(pt, positive) != PointInPolygonResult.IsOutside)
            return pt;
        return hole.Count > 0 ? hole[0] : pt;
    }

    private static Vector2 HoleCentroid(Path64 hole) => Centroid(LayerStack.OrientedPositive(hole));

    private static Vector2 Centroid(Path64 path)
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
        if (Math.Abs(a) < 1)
        {
            double sx = 0, sy = 0;
            foreach (var p in path) { sx += p.X; sy += p.Y; }
            var inv = 1.0 / Math.Max(path.Count, 1);
            return new Vector2((float)(sx * inv / MeshSlicer.UnitsPerMm), (float)(sy * inv / MeshSlicer.UnitsPerMm));
        }
        return new Vector2(
            (float)(cx / (6 * a) / MeshSlicer.UnitsPerMm),
            (float)(cy / (6 * a) / MeshSlicer.UnitsPerMm));
    }
}
