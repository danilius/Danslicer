using System.Numerics;
using Danslicer.Core.Geometry;
using Danslicer.Core.Supports.Routing;

namespace Danslicer.Core.Supports.Generation;

/// <summary>
/// Stage 1 of support generation (DESIGN.md §8.4): candidate tips on a region's faces.
/// Pure function of (mesh, region, parameters, existing graph, seed). Does not route or
/// mutate the graph.
/// </summary>
public static class TipPlacer
{
    private const float Oversample = 5f;
    private const int MaxSamplesPerTriangle = 800;

    public static IReadOnlyList<TipCandidate> Place(
        Mesh mesh,
        IReadOnlySet<int> faces,
        TipPlacementParameters parameters,
        SupportGraph? existingGraph = null,
        IReadOnlySet<int>? keepCleanFaces = null,
        int seed = 0)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(faces);
        ArgumentNullException.ThrowIfNull(parameters);

        if (mesh.TriangleCount == 0 || faces.Count == 0)
            return Array.Empty<TipCandidate>();

        var region = faces as HashSet<int> ?? [.. faces];
        foreach (var t in region)
        {
            if ((uint)t >= (uint)mesh.TriangleCount)
                throw new ArgumentOutOfRangeException(nameof(faces), $"Face {t} is not in the mesh.");
        }

        var keepClean = keepCleanFaces is null || keepCleanFaces.Count == 0
            ? null
            : keepCleanFaces as HashSet<int> ?? [.. keepCleanFaces];

        var spacing = MathF.Max(parameters.SpacingMm, 1e-3f);
        var minSpacing = MathF.Max(parameters.MinSpacingMm, 1e-4f);
        var features = MeshFeatures.Build(mesh, parameters.SharpEdgeDegrees);
        var bvh = features.Analysis.Bvh;
        var keepCleanBvh = keepClean is not null && parameters.KeepCleanDistanceMm > 0
            ? features.Analysis.BvhForTriangles(keepClean)
            : null;
        var keepCleanDistance = MathF.Max(0, parameters.KeepCleanDistanceMm);

        var graphGrid = new PointGrid(minSpacing);
        if (existingGraph is not null)
        {
            foreach (var node in existingGraph.Nodes)
            {
                if (node.Type != SupportNodeType.Tip) continue;
                graphGrid.Add(node.Position);
            }
        }

        var placedGrid = new PointGrid(MathF.Min(spacing, minSpacing));
        var accepted = new List<TipCandidate>();

        foreach (var island in IslandFinder.Find(
                     mesh, parameters.LayerHeightMm, parameters.MinIslandAreaMm2, parameters.PlateZ,
                     parameters.OverhangAngleDegrees)
                     .OrderByDescending(i => i.AreaMm2)
                     .ThenBy(i => i.Z)
                     .ThenBy(i => i.Centroid.X)
                     .ThenBy(i => i.Centroid.Y))
        {
            if (!TryProjectToRegion(mesh, bvh, region, island.Centroid, parameters, out var point, out var outward, out var face))
                continue;
            var score = 10f + MathF.Log(1f + island.AreaMm2);
            TryAcceptRequired(
                keepClean, keepCleanBvh, keepCleanDistance,
                graphGrid, placedGrid, accepted, minSpacing, parameters,
                point, outward, face, score, TipStrategy.Island);
        }

        foreach (var (vertex, position, outward, face) in features.LocalMinima(region, parameters.PlateZ, parameters.LayerHeightMm))
        {
            var score = 10f + features.Curvature[vertex];
            TryAcceptRequired(
                keepClean, keepCleanBvh, keepCleanDistance,
                graphGrid, placedGrid, accepted, minSpacing, parameters,
                position, outward, face, score, TipStrategy.LocalMinimum);
        }

        var patchArea = features.OverhangPatchArea(region, parameters);
        var rng = new Random(seed);
        var samples = parameters.Grid is not null
            ? CollectGridSamples(mesh, bvh, region, parameters, features, patchArea, graphGrid, minSpacing, spacing)
            : CollectOverhangSamples(mesh, region, parameters, features, patchArea, rng, graphGrid, minSpacing, spacing);
        foreach (var sample in samples
                     .OrderByDescending(s => s.Score)
                     .ThenBy(s => s.Point.X)
                     .ThenBy(s => s.Point.Y)
                     .ThenBy(s => s.Point.Z)
                     .ThenBy(s => s.FaceIndex))
        {
            if (ViolatesKeepClean(sample.FaceIndex, sample.Point, keepClean, keepCleanBvh, keepCleanDistance))
                continue;
            if (IsOnPlate(sample.Point, parameters)) continue;
            if (graphGrid.AnyWithin(sample.Point, minSpacing)) continue;
            var limit = sample.Strategy == TipStrategy.GridProjection ? minSpacing : spacing;
            if (placedGrid.AnyWithin(sample.Point, limit)) continue;
            placedGrid.Add(sample.Point);
            accepted.Add(sample);
        }

        return accepted;
    }

    private static void TryAcceptRequired(
        HashSet<int>? keepClean,
        TriangleBvh? keepCleanBvh,
        float keepCleanDistance,
        PointGrid graphGrid,
        PointGrid placedGrid,
        List<TipCandidate> accepted,
        float minSpacing,
        TipPlacementParameters parameters,
        Vector3 point,
        Vector3 outward,
        int face,
        float score,
        TipStrategy strategy)
    {
        if (ViolatesKeepClean(face, point, keepClean, keepCleanBvh, keepCleanDistance)) return;
        if (IsOnPlate(point, parameters)) return;
        if (graphGrid.AnyWithin(point, minSpacing)) return;
        if (placedGrid.AnyWithin(point, minSpacing)) return;

        var inward = Inward(outward);
        var diameter = DiameterFor(parameters.TipDiameterMm, strategy);
        placedGrid.Add(point);
        accepted.Add(new TipCandidate(point, inward, diameter, score, strategy, face));
    }

    private static List<TipCandidate> CollectOverhangSamples(
        Mesh mesh,
        HashSet<int> region,
        TipPlacementParameters parameters,
        MeshFeatures features,
        float[] patchArea,
        Random rng,
        PointGrid graphGrid,
        float minSpacing,
        float spacing)
    {
        var list = new List<TipCandidate>();
        var edgeEpsilon = MathF.Max(1e-3f, spacing * 0.02f);
        var forceEdges = parameters.ForceEdgePlacement;
        var edgePref = Math.Clamp(parameters.EdgePreference, 0f, 1f);

        foreach (var t in region.OrderBy(i => i))
        {
            var outward = mesh.FaceNormals[t];
            if (!parameters.IsOverhang(outward)) continue;
            mesh.GetTriangle(t, out var a, out var b, out var c);
            var area = Vector3.Cross(b - a, c - a).Length() * 0.5f;
            if (area < 1e-12f) continue;

            void Consider(Vector3 p)
            {
                if (IsOnPlate(p, parameters)) return;
                if (graphGrid.AnyWithin(p, minSpacing)) return;
                var strategy = features.FeatureAt(p, t, edgeEpsilon);
                if (forceEdges && strategy == TipStrategy.Overhang) return;
                var score = Score(p, outward, t, strategy, parameters, features, patchArea, graphGrid, spacing, edgePref);
                var diameter = DiameterFor(parameters.TipDiameterMm, strategy);
                list.Add(new TipCandidate(p, Inward(outward), diameter, score, strategy, t));
            }

            // Vertices and centroid are deterministic and catch CAD corners even at seed 0.
            Consider(a);
            Consider(b);
            Consider(c);
            Consider((a + b + c) / 3f);

            if (forceEdges) continue;

            var extra = (int)MathF.Ceiling(area / (spacing * spacing) * Oversample);
            extra = Math.Min(extra, MaxSamplesPerTriangle);
            for (int i = 0; i < extra; i++)
                Consider(RandomPointOnTriangle(rng, a, b, c));
        }

        if (edgePref > 0f || forceEdges)
            SampleSharpEdges(mesh, region, parameters, features, patchArea, graphGrid, minSpacing, spacing, edgePref, list);

        return list;
    }

    private static void SampleSharpEdges(
        Mesh mesh,
        HashSet<int> region,
        TipPlacementParameters parameters,
        MeshFeatures features,
        float[] patchArea,
        PointGrid graphGrid,
        float minSpacing,
        float spacing,
        float edgePref,
        List<TipCandidate> list)
    {
        var edgeEpsilon = MathF.Max(1e-3f, spacing * 0.02f);
        foreach (var (ia, ib) in features.SharpEdges.OrderBy(e => e.A).ThenBy(e => e.B))
        {
            if (!features.TryGetEdgeFaces(ia, ib, out var faces)) continue;
            int face = -1;
            Vector3 outward = default;
            foreach (var t in faces.OrderBy(x => x))
            {
                if (!region.Contains(t)) continue;
                if (!parameters.IsOverhang(mesh.FaceNormals[t])) continue;
                face = t;
                outward = mesh.FaceNormals[t];
                break;
            }
            if (face < 0) continue;

            var pa = mesh.Positions[ia];
            var pb = mesh.Positions[ib];
            var len = (pb - pa).Length();
            if (len < 1e-6f) continue;
            var steps = Math.Max(1, (int)MathF.Round(len / spacing));
            for (int i = 0; i <= steps; i++)
            {
                var p = Vector3.Lerp(pa, pb, i / (float)steps);
                if (IsOnPlate(p, parameters)) continue;
                if (graphGrid.AnyWithin(p, minSpacing)) continue;
                var strategy = features.FeatureAt(p, face, edgeEpsilon);
                if (strategy == TipStrategy.Overhang) strategy = TipStrategy.Edge;
                var score = Score(p, outward, face, strategy, parameters, features, patchArea, graphGrid, spacing, edgePref);
                var diameter = DiameterFor(parameters.TipDiameterMm, strategy);
                list.Add(new TipCandidate(p, Inward(outward), diameter, score, strategy, face));
            }
        }
    }

    private static float Score(
        Vector3 point,
        Vector3 outward,
        int face,
        TipStrategy strategy,
        TipPlacementParameters parameters,
        MeshFeatures features,
        float[] patchArea,
        PointGrid graphGrid,
        float spacing,
        float edgePref)
    {
        var overhang = TipPlacementParameters.OverhangDegrees(outward) / 90f;
        var curvature = features.MaxVertexCurvature(face);
        var island = MathF.Min(1f, MathF.Log(1f + patchArea[face]) / 4f);
        // Farther from already-known tips scores higher so the greedy Poisson fills gaps.
        var dist = NearestDistance(graphGrid, point, spacing * 4f);
        var gap = Math.Clamp(dist / spacing, 0f, 1f);
        var edge = strategy switch
        {
            TipStrategy.Corner => 1f,
            TipStrategy.Edge => 0.65f,
            _ => 0f,
        };
        return overhang
               + 0.5f * curvature
               + 0.25f * island
               + 0.2f * gap
               + edgePref * edge;
    }

    private static float NearestDistance(PointGrid grid, Vector3 p, float cap)
    {
        // AnyWithin is a boolean query; walk a few radii to estimate distance for scoring.
        for (int k = 1; k <= 8; k++)
        {
            var r = cap * (k / 8f);
            if (grid.AnyWithin(p, r)) return r;
        }
        return cap;
    }

    private static bool TryProjectToRegion(
        Mesh mesh,
        TriangleBvh bvh,
        HashSet<int> region,
        Vector3 xyAtZ,
        TipPlacementParameters parameters,
        out Vector3 point,
        out Vector3 outward,
        out int face)
    {
        var origin = new Vector3(xyAtZ.X, xyAtZ.Y, mesh.Bounds.Min.Z - 1f);
        var ray = new Ray(origin, Vector3.UnitZ);
        bool DownwardRegion(int t) => region.Contains(t) && mesh.FaceNormals[t].Z < -1e-3f;

        if (bvh.RayCast(ray, out face, out var tHit, DownwardRegion) && face >= 0)
        {
            point = ray.At(tHit);
            outward = mesh.FaceNormals[face];
            return parameters.IsOverhang(outward) || mesh.FaceNormals[face].Z < -1e-3f;
        }

        var probe = xyAtZ;
        if (bvh.ClosestPoint(probe, out point, out face, DownwardRegion) < float.PositiveInfinity && face >= 0)
        {
            outward = mesh.FaceNormals[face];
            return true;
        }

        face = -1;
        point = default;
        outward = default;
        return false;
    }

    private static List<TipCandidate> CollectGridSamples(
        Mesh mesh,
        TriangleBvh bvh,
        HashSet<int> region,
        TipPlacementParameters parameters,
        MeshFeatures features,
        float[] patchArea,
        PointGrid graphGrid,
        float minSpacing,
        float spacing)
    {
        var grid = parameters.Grid ?? throw new ArgumentException("Grid options are required.", nameof(parameters));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(grid.Spacing);

        var list = new List<TipCandidate>();
        var edgeEpsilon = MathF.Max(1e-3f, spacing * 0.02f);
        var forceEdges = parameters.ForceEdgePlacement;
        var edgePref = Math.Clamp(parameters.EdgePreference, 0f, 1f);

        var regionBox = Aabb.Empty;
        foreach (var t in region)
        {
            mesh.GetTriangle(t, out var a, out var b, out var c);
            regionBox = regionBox.Include(a).Include(b).Include(c);
        }
        if (regionBox.IsEmpty) return list;

        var originZ = MathF.Min(mesh.Bounds.Min.Z, parameters.PlateZ) - 1f;
        bool DownwardRegion(int t) => region.Contains(t) && mesh.FaceNormals[t].Z < -1e-3f;

        foreach (var xy in BaseLattice.WorldPointsCovering(
                     new Vector2(regionBox.Min.X, regionBox.Min.Y),
                     new Vector2(regionBox.Max.X, regionBox.Max.Y),
                     grid))
        {
            var ray = new Ray(new Vector3(xy.X, xy.Y, originZ), Vector3.UnitZ);
            if (!bvh.RayCast(ray, out var face, out var tHit, DownwardRegion)) continue;
            var p = ray.At(tHit);
            if (IsOnPlate(p, parameters)) continue;
            if (graphGrid.AnyWithin(p, minSpacing)) continue;

            var outward = mesh.FaceNormals[face];
            var feature = features.FeatureAt(p, face, edgeEpsilon);
            if (forceEdges && feature == TipStrategy.Overhang) continue;
            var score = Score(p, outward, face, feature, parameters, features, patchArea, graphGrid, spacing, edgePref);
            list.Add(new TipCandidate(p, Inward(outward), parameters.TipDiameterMm, score, TipStrategy.GridProjection, face));
        }

        return list;
    }

    private static bool ViolatesKeepClean(
        int face,
        Vector3 point,
        HashSet<int>? keepClean,
        TriangleBvh? keepCleanBvh,
        float distance)
    {
        if (keepClean is not null && keepClean.Contains(face)) return true;
        if (keepCleanBvh is null || distance <= 0) return false;
        return keepCleanBvh.ClosestPoint(point, out _, out _) < distance;
    }

    private static Vector3 RandomPointOnTriangle(Random rng, Vector3 a, Vector3 b, Vector3 c)
    {
        var u = rng.NextSingle();
        var v = rng.NextSingle();
        if (u + v > 1f)
        {
            u = 1f - u;
            v = 1f - v;
        }
        return a + (b - a) * u + (c - a) * v;
    }

    private static Vector3 Inward(Vector3 outward)
    {
        var n = -outward;
        var len = n.Length();
        return len > 1e-12f ? n / len : -Vector3.UnitZ;
    }

    private static float DiameterFor(float baseDiameter, TipStrategy strategy) => strategy switch
    {
        TipStrategy.Corner => baseDiameter * 0.7f,
        TipStrategy.Edge => baseDiameter * 0.85f,
        _ => baseDiameter,
    };

    private static bool IsOnPlate(Vector3 p, TipPlacementParameters parameters) =>
        p.Z <= parameters.PlateZ + parameters.LayerHeightMm + 1e-4f;
}
