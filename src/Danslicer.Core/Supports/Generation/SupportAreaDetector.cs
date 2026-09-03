using System.Numerics;
using Danslicer.Core.Geometry;

namespace Danslicer.Core.Supports.Generation;

/// <summary>
/// Segments a region's faces into connected AREAS that need support (recipes groundwork).
/// Support-needing faces are overhangs plus faces over islands and local minima; they are
/// grouped into components via the adjacency cache, staying inside planar patches and not
/// crossing sharp edges so CAD faces stay separate while organic overhangs merge.
/// </summary>
public static class SupportAreaDetector
{
    public static IReadOnlyList<SupportArea> Detect(
        Mesh mesh,
        IReadOnlySet<int> regionFaces,
        SupportAreaParameters? parameters = null)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(regionFaces);
        var p = parameters ?? SupportAreaParameters.Default;

        if (mesh.TriangleCount == 0 || regionFaces.Count == 0)
            return Array.Empty<SupportArea>();

        var region = regionFaces as HashSet<int> ?? [.. regionFaces];
        foreach (var t in region)
        {
            if ((uint)t >= (uint)mesh.TriangleCount)
                throw new ArgumentOutOfRangeException(nameof(regionFaces), $"Face {t} is not in the mesh.");
        }

        var analysis = MeshAnalysis.For(mesh);
        var features = MeshFeatures.Build(mesh, p.SharpEdgeDegrees);
        var sharp = features.SharpEdges;
        var cutoff = p.PlateZ + p.LayerHeightMm + 1e-4f;

        var needing = new bool[mesh.TriangleCount];
        var islandFace = new bool[mesh.TriangleCount];
        var minimumFace = new bool[mesh.TriangleCount];

        foreach (var t in region)
        {
            if (FaceMaxZ(mesh, t) <= cutoff) continue;
            if (TipPlacementParameters.OverhangDegrees(mesh.FaceNormals[t]) > p.OverhangAngleDegrees)
                needing[t] = true;
        }

        foreach (var (_, _, _, face) in features.LocalMinima(region, p.PlateZ, p.LayerHeightMm))
        {
            needing[face] = true;
            minimumFace[face] = true;
        }

        var bvh = analysis.Bvh;
        foreach (var island in IslandFinder.Find(
                     mesh, p.LayerHeightMm, p.MinIslandAreaMm2, p.PlateZ, p.OverhangAngleDegrees))
        {
            if (!TryProjectIsland(mesh, bvh, region, island.Centroid, out var face)) continue;
            needing[face] = true;
            islandFace[face] = true;
        }

        var needingInPatch = new Dictionary<int, List<int>>();
        for (int t = 0; t < mesh.TriangleCount; t++)
        {
            if (!needing[t]) continue;
            var pid = analysis.PatchId[t];
            if (!needingInPatch.TryGetValue(pid, out var list))
                needingInPatch[pid] = list = new List<int>();
            list.Add(t);
        }
        foreach (var list in needingInPatch.Values) list.Sort();

        var visited = new bool[mesh.TriangleCount];
        var raw = new List<(List<int> Faces, HashSet<int> Patches, bool Island, bool Minimum)>();
        var queue = new Queue<int>();

        foreach (var seed in region.OrderBy(i => i))
        {
            if (!needing[seed] || visited[seed]) continue;
            var faces = new List<int>();
            var patches = new HashSet<int>();
            var hasIsland = false;
            var hasMinimum = false;
            visited[seed] = true;
            queue.Enqueue(seed);
            while (queue.Count > 0)
            {
                var t = queue.Dequeue();
                faces.Add(t);
                patches.Add(analysis.PatchId[t]);
                if (islandFace[t]) hasIsland = true;
                if (minimumFace[t]) hasMinimum = true;

                int ia = mesh.Indices[t * 3], ib = mesh.Indices[t * 3 + 1], ic = mesh.Indices[t * 3 + 2];
                WalkEdge(ia, ib);
                WalkEdge(ib, ic);
                WalkEdge(ic, ia);

                if (needingInPatch.TryGetValue(analysis.PatchId[t], out var samePatch))
                {
                    foreach (var n in samePatch)
                    {
                        if (visited[n]) continue;
                        visited[n] = true;
                        queue.Enqueue(n);
                    }
                }

                void WalkEdge(int a, int b)
                {
                    var key = a < b ? (a, b) : (b, a);
                    if (sharp.Contains(key)) return;
                    if (!analysis.TryGetEdgeFaces(a, b, out var adj)) return;
                    foreach (var n in adj.OrderBy(x => x))
                    {
                        if (visited[n] || !needing[n]) continue;
                        visited[n] = true;
                        queue.Enqueue(n);
                    }
                }
            }

            faces.Sort();
            raw.Add((faces, patches, hasIsland, hasMinimum));
        }

        var built = new List<SupportArea>(raw.Count);
        foreach (var (faces, patches, hasIsland, hasMinimum) in raw)
        {
            float area = 0, maxOverhang = 0, overhangSum = 0;
            var centroid = Vector3.Zero;
            var normalSum = Vector3.Zero;
            foreach (var t in faces)
            {
                mesh.GetTriangle(t, out var a, out var b, out var c);
                var triArea = Vector3.Cross(b - a, c - a).Length() * 0.5f;
                area += triArea;
                centroid += (a + b + c) / 3f * triArea;
                normalSum += mesh.FaceNormals[t] * triArea;
                var oh = TipPlacementParameters.OverhangDegrees(mesh.FaceNormals[t]);
                if (oh > maxOverhang) maxOverhang = oh;
                overhangSum += oh * triArea;
            }
            if (area < p.MinAreaMm2) continue;
            centroid /= area;
            var nlen = normalSum.Length();
            var meanNormal = nlen > 1e-12f ? normalSum / nlen : Vector3.UnitZ;
            var meanOverhang = overhangSum / area;
            var severity = hasIsland || maxOverhang >= 80f
                ? SupportAreaSeverity.High
                : hasMinimum || maxOverhang >= 60f
                    ? SupportAreaSeverity.Medium
                    : SupportAreaSeverity.Low;
            built.Add(new SupportArea(
                Id: 0,
                Faces: faces.ToArray(),
                AreaMm2: area,
                Centroid: centroid,
                MeanNormal: meanNormal,
                MaxOverhangDegrees: maxOverhang,
                MeanOverhangDegrees: meanOverhang,
                ContainsIsland: hasIsland,
                ContainsLocalMinimum: hasMinimum,
                Severity: severity,
                PatchIds: patches.OrderBy(x => x).ToArray()));
        }

        built.Sort((a, b) =>
        {
            var area = b.AreaMm2.CompareTo(a.AreaMm2);
            if (area != 0) return area;
            var x = a.Centroid.X.CompareTo(b.Centroid.X);
            if (x != 0) return x;
            var y = a.Centroid.Y.CompareTo(b.Centroid.Y);
            if (y != 0) return y;
            var z = a.Centroid.Z.CompareTo(b.Centroid.Z);
            if (z != 0) return z;
            return a.Faces[0].CompareTo(b.Faces[0]);
        });

        for (int i = 0; i < built.Count; i++)
            built[i] = built[i] with { Id = i };
        return built;
    }

    private static float FaceMaxZ(Mesh mesh, int t)
    {
        int ia = mesh.Indices[t * 3], ib = mesh.Indices[t * 3 + 1], ic = mesh.Indices[t * 3 + 2];
        return MathF.Max(mesh.Positions[ia].Z, MathF.Max(mesh.Positions[ib].Z, mesh.Positions[ic].Z));
    }

    private static bool TryProjectIsland(
        Mesh mesh, TriangleBvh bvh, HashSet<int> region, Vector3 xyAtZ, out int face)
    {
        bool DownwardRegion(int t) => region.Contains(t) && mesh.FaceNormals[t].Z < -1e-3f;
        var origin = new Vector3(xyAtZ.X, xyAtZ.Y, mesh.Bounds.Min.Z - 1f);
        var ray = new Ray(origin, Vector3.UnitZ);
        if (bvh.RayCast(ray, out face, out _, DownwardRegion) && face >= 0)
            return true;
        if (bvh.ClosestPoint(xyAtZ, out _, out face, DownwardRegion) < float.PositiveInfinity && face >= 0)
            return true;
        face = -1;
        return false;
    }
}
