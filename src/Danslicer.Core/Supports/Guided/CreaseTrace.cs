using System.Numerics;
using Danslicer.Core.Geometry;

namespace Danslicer.Core.Supports.Guided;

/// <summary>
/// Edge follow (SUPPORT-GEOMETRY-SPEC "Guided tip placement"): from a pick near a crease, the
/// feature line traced along sharp edges in both directions until it ends, turns more sharply
/// than the limit, or closes on itself. A crease is an edge whose dihedral angle exceeds the
/// sharp-edge threshold — the same rule stage-1 placement uses for its edge preference.
/// </summary>
public static class CreaseTrace
{
    /// <summary>
    /// A traced crease: <see cref="Forward"/> and <see cref="Backward"/> both start at the
    /// snapped pick point, so sampling each at pitch puts a tip where the user pointed and
    /// spaces outward from there.
    /// </summary>
    public sealed record Result(Vector3 Origin, SurfacePath Forward, SurfacePath Backward)
    {
        public float Length => Forward.Length + Backward.Length;
        public IReadOnlyList<SurfacePath> Paths => [Forward, Backward];
    }

    /// <summary>
    /// Traces the crease nearest <paramref name="near"/> on <paramref name="face"/> or its
    /// neighbours. Null when no sharp edge lies within <paramref name="snapDistanceMm"/>.
    /// At a vertex the trace continues along the sharp edge that turns least, provided the turn
    /// is within <paramref name="maxTurnDegrees"/>; a corner where every way on turns more than
    /// that ends the trace. Each point carries the adjacent face that points down the most, so
    /// a tip there aims up into the overhang the crease bounds.
    /// </summary>
    public static Result? Trace(Mesh mesh, Vector3 near, int face, float sharpEdgeDegrees,
        float snapDistanceMm, float maxTurnDegrees)
    {
        if ((uint)face >= (uint)mesh.TriangleCount) return null;
        var analysis = MeshAnalysis.For(mesh);
        var sharp = analysis.SharpEdgesAt(sharpEdgeDegrees);
        if (sharp.Count == 0) return null;

        // The nearest sharp edge among those touching the picked face's vertices.
        (int A, int B)? best = null;
        var bestDistance = snapDistanceMm * snapDistanceMm;
        var origin = near;
        for (var e = 0; e < 3; e++)
        {
            var v = mesh.Indices[face * 3 + e];
            foreach (var edge in SharpEdgesAt(mesh, analysis, sharp, v))
            {
                var p = ClosestOnSegment(near, mesh.Positions[edge.A], mesh.Positions[edge.B]);
                var d = Vector3.DistanceSquared(p, near);
                if (d < bestDistance)
                {
                    bestDistance = d;
                    best = edge;
                    origin = p;
                }
            }
        }
        if (best is not { } seed) return null;

        var minCos = MathF.Cos(maxTurnDegrees * MathF.PI / 180f);
        var forward = Walk(mesh, analysis, sharp, origin, seed.A, seed.B, minCos);
        var backward = Walk(mesh, analysis, sharp, origin, seed.B, seed.A, minCos);
        return new Result(origin, forward, backward);
    }

    /// <summary>Walks from <paramref name="origin"/> on edge (from, to) onward through <paramref name="to"/>.</summary>
    private static SurfacePath Walk(Mesh mesh, MeshAnalysis analysis, IReadOnlySet<(int A, int B)> sharp,
        Vector3 origin, int from, int to, float minCos)
    {
        var points = new List<Vector3> { origin };
        var faces = new List<int> { DownFace(mesh, analysis, from, to) };
        var length = 0f;
        var visited = new HashSet<int> { from };
        var direction = Vector3.Normalize(mesh.Positions[to] - origin);
        if (float.IsNaN(direction.X)) direction = Vector3.Normalize(mesh.Positions[to] - mesh.Positions[from]);
        var previous = from;
        var current = to;
        while (true)
        {
            var p = mesh.Positions[current];
            length += Vector3.Distance(points[^1], p);
            points.Add(p);
            faces.Add(DownFace(mesh, analysis, previous, current));
            // Back at the start: the crease is a closed loop; stop before repeating it.
            if (visited.Contains(current)) break;
            visited.Add(current);

            int? next = null;
            var bestCos = minCos;
            foreach (var edge in SharpEdgesAt(mesh, analysis, sharp, current))
            {
                var other = edge.A == current ? edge.B : edge.A;
                if (other == previous) continue;
                var d = Vector3.Normalize(mesh.Positions[other] - p);
                if (float.IsNaN(d.X)) continue;
                var cos = Vector3.Dot(direction, d);
                if (cos >= bestCos)
                {
                    bestCos = cos;
                    next = other;
                }
            }
            if (next is not { } onward) break;
            direction = Vector3.Normalize(mesh.Positions[onward] - p);
            previous = current;
            current = onward;
        }
        return new SurfacePath(points, faces, length);
    }

    private static IEnumerable<(int A, int B)> SharpEdgesAt(Mesh mesh, MeshAnalysis analysis,
        IReadOnlySet<(int A, int B)> sharp, int vertex)
    {
        foreach (var n in analysis.VertexNeighbors[vertex])
        {
            var key = vertex < n ? (vertex, n) : (n, vertex);
            if (sharp.Contains(key)) yield return key;
        }
    }

    /// <summary>Of the faces sharing an edge, the one whose normal points down the most.</summary>
    private static int DownFace(Mesh mesh, MeshAnalysis analysis, int a, int b)
    {
        if (!analysis.TryGetEdgeFaces(a, b, out var edgeFaces) || edgeFaces.Count == 0) return 0;
        var best = edgeFaces[0];
        foreach (var f in edgeFaces)
            if (mesh.FaceNormals[f].Z < mesh.FaceNormals[best].Z) best = f;
        return best;
    }

    private static Vector3 ClosestOnSegment(Vector3 p, Vector3 a, Vector3 b)
    {
        var ab = b - a;
        var l2 = ab.LengthSquared();
        if (l2 < 1e-12f) return a;
        var t = Math.Clamp(Vector3.Dot(p - a, ab) / l2, 0f, 1f);
        return a + ab * t;
    }
}
