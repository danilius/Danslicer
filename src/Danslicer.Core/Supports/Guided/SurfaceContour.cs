using System.Numerics;
using Danslicer.Core.Geometry;

namespace Danslicer.Core.Supports.Guided;

/// <summary>
/// The contour of a mesh at a height (SUPPORT-GEOMETRY-SPEC "Guided tip placement", contour
/// tool): the horizontal plane cut, chained into runs, restricted to faces with a downward
/// component so a vertical wall or a top face contributes nothing. Runs break where the
/// surface turns upward and where the chain reaches a boundary; a closed run ends on its own
/// start point.
/// </summary>
public static class SurfaceContour
{
    /// <summary>Matches <see cref="Generation.RegionGridSampler"/>: how much of the normal must point down.</summary>
    private const float MinDownwardComponent = 1e-3f;

    public static IReadOnlyList<SurfacePath> AtHeight(Mesh mesh, float z)
    {
        // One segment per downward face the plane crosses.
        var segments = new List<(Vector3 A, Vector3 B, int Face)>();
        for (var face = 0; face < mesh.TriangleCount; face++)
        {
            if (mesh.FaceNormals[face].Z >= -MinDownwardComponent) continue;
            mesh.GetTriangle(face, out var a, out var b, out var c);
            var lo = MathF.Min(a.Z, MathF.Min(b.Z, c.Z));
            var hi = MathF.Max(a.Z, MathF.Max(b.Z, c.Z));
            if (z <= lo || z >= hi) continue;
            Span<Vector3> hits = stackalloc Vector3[3];
            var count = 0;
            Cross(a, b, z, hits, ref count);
            Cross(b, c, z, hits, ref count);
            Cross(c, a, z, hits, ref count);
            if (count != 2 || Vector3.DistanceSquared(hits[0], hits[1]) < 1e-12f) continue;
            segments.Add((hits[0], hits[1], face));
        }
        if (segments.Count == 0) return [];

        // Chain by shared endpoints. Endpoints are quantised so neighbouring faces meet.
        var byPoint = new Dictionary<(long, long, long), List<int>>();
        for (var i = 0; i < segments.Count; i++)
        {
            Add(Key(segments[i].A), i);
            Add(Key(segments[i].B), i);
        }
        var used = new bool[segments.Count];
        var runs = new List<SurfacePath>();
        // Open runs first, from their loose ends; whatever remains is closed loops.
        for (var pass = 0; pass < 2; pass++)
        {
            for (var start = 0; start < segments.Count; start++)
            {
                if (used[start]) continue;
                var loose = byPoint[Key(segments[start].A)].Count == 1 || byPoint[Key(segments[start].B)].Count == 1;
                if (pass == 0 && !loose) continue;
                // Begin at the loose end so an open run is walked once, end to end.
                var forward = pass == 1 || byPoint[Key(segments[start].A)].Count == 1;
                runs.Add(Walk(start, forward));
            }
        }
        return runs;

        SurfacePath Walk(int start, bool forward)
        {
            var points = new List<Vector3>();
            var faces = new List<int>();
            var length = 0f;
            var index = start;
            var (from, to, face) = forward ? (segments[start].A, segments[start].B, segments[start].Face)
                : (segments[start].B, segments[start].A, segments[start].Face);
            points.Add(from);
            faces.Add(face);
            while (true)
            {
                used[index] = true;
                length += Vector3.Distance(points[^1], to);
                points.Add(to);
                faces.Add(face);
                var next = byPoint[Key(to)].FirstOrDefault(i => !used[i], -1);
                if (next < 0) break;
                index = next;
                var s = segments[next];
                var atA = Key(s.A) == Key(to);
                (from, to, face) = atA ? (s.A, s.B, s.Face) : (s.B, s.A, s.Face);
            }
            return new SurfacePath(points, faces, length);
        }

        void Add((long, long, long) key, int i)
        {
            if (!byPoint.TryGetValue(key, out var list)) byPoint[key] = list = new List<int>(2);
            list.Add(i);
        }
    }

    private static (long, long, long) Key(Vector3 p) =>
        ((long)MathF.Round(p.X * 1e4f), (long)MathF.Round(p.Y * 1e4f), (long)MathF.Round(p.Z * 1e4f));

    private static void Cross(Vector3 p, Vector3 q, float z, Span<Vector3> hits, ref int count)
    {
        if ((p.Z < z) == (q.Z < z)) return;
        if (count >= 2) return;
        var t = (z - p.Z) / (q.Z - p.Z);
        hits[count++] = Vector3.Lerp(p, q, t);
    }
}
