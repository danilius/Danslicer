using System.Numerics;
using Danslicer.Core.Geometry;

namespace Danslicer.Core.Supports.Guided;

/// <summary>
/// A polyline lying on a mesh surface: the route a guided placement tool lays tips along
/// (SUPPORT-GEOMETRY-SPEC "Guided tip placement"). <see cref="Faces"/>[i] is the face
/// <see cref="Points"/>[i] lies on — for a point on an edge, the face the path enters there —
/// so a tip at any point along the path has a face normal to take.
/// </summary>
public sealed record SurfacePath(IReadOnlyList<Vector3> Points, IReadOnlyList<int> Faces, float Length)
{
    /// <summary>A path of one point, the degenerate case a gesture starts from.</summary>
    public static SurfacePath Single(Vector3 point, int face) => new([point], [face], 0f);

    /// <summary>A straight chord between two picks, for when no surface path can be found.</summary>
    public static SurfacePath Chord(Vector3 a, int faceA, Vector3 b, int faceB) =>
        new([a, b], [faceA, faceB], Vector3.Distance(a, b));

    /// <summary>
    /// The surface path between two points of <paramref name="mesh"/>: the contour where the
    /// <b>vertical plane through both points</b> cuts the mesh, walked face to face from
    /// <paramref name="a"/> to <paramref name="b"/> across shared edges (spec decision). Two
    /// walks leave <paramref name="faceA"/>, one through each edge the plane crosses; the
    /// shorter one that reaches <paramref name="faceB"/> is the path. Null when neither arrives —
    /// the points lie on pieces of surface the plane does not join (a fold, a hole, a boundary
    /// in between), which the caller may bridge some other way.
    ///
    /// <para>Because the walk only ever steps across a shared edge it cannot bleed through a
    /// thin wall onto the far side, for the same reason the region brush cannot.</para>
    /// </summary>
    public static SurfacePath? Between(Mesh mesh, Vector3 a, int faceA, Vector3 b, int faceB)
    {
        if ((uint)faceA >= (uint)mesh.TriangleCount || (uint)faceB >= (uint)mesh.TriangleCount)
            return null;
        if (faceA == faceB) return Chord(a, faceA, b, faceB);

        var normal = VerticalPlaneNormal(a, b);
        var adjacency = SupportRegionSelection.FaceAdjacencyOf(mesh);

        SurfacePath? best = null;
        foreach (var exit in CrossedEdges(mesh, faceA, a, normal))
        {
            var walked = Walk(mesh, adjacency, a, faceA, b, faceB, normal, exit);
            if (walked is not null && (best is null || walked.Length < best.Length)) best = walked;
        }
        return best;
    }

    /// <summary>
    /// Points at every whole <paramref name="pitch"/> of arc length from the start of the first
    /// path, continuing across the joins so a polyline is spaced as one line. The start always
    /// carries a point; the very end gets one only when it is at least
    /// <paramref name="minEndSpacing"/> past the previous, so a corner or an end never doubles up.
    /// </summary>
    public static IReadOnlyList<(Vector3 Point, int Face)> SampleAtPitch(
        IReadOnlyList<SurfacePath> paths, float pitch, float minEndSpacing)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pitch);
        var samples = new List<(Vector3, int)>();
        if (paths.Count == 0 || paths[0].Points.Count == 0) return samples;

        samples.Add((paths[0].Points[0], paths[0].Faces[0]));
        var walked = 0f;          // arc length at the start of the current segment
        var nextMark = pitch;     // arc length of the next sample
        var lastSampleAt = 0f;
        (Vector3 Point, int Face) end = samples[0];
        foreach (var path in paths)
        {
            for (var i = 0; i + 1 < path.Points.Count; i++)
            {
                var p = path.Points[i];
                var q = path.Points[i + 1];
                var face = path.Faces[i + 1];
                var segment = Vector3.Distance(p, q);
                while (segment > 0 && nextMark <= walked + segment + 1e-6f)
                {
                    var t = Math.Clamp((nextMark - walked) / segment, 0f, 1f);
                    samples.Add((Vector3.Lerp(p, q, t), face));
                    lastSampleAt = nextMark;
                    nextMark += pitch;
                }
                walked += segment;
                end = (q, face);
            }
        }
        if (walked > 0 && walked - lastSampleAt >= minEndSpacing) samples.Add(end);
        return samples;
    }

    /// <summary>
    /// The face whose closest point to <paramref name="p"/> is nearest — how a gesture that
    /// starts on an existing tip learns which face that contact sits on. One full scan; meant
    /// for once per gesture, not per mouse move.
    /// </summary>
    public static int NearestFace(Mesh mesh, Vector3 p)
    {
        var best = 0;
        var bestDistance = float.PositiveInfinity;
        for (var face = 0; face < mesh.TriangleCount; face++)
        {
            mesh.GetTriangle(face, out var a, out var b, out var c);
            var d = Vector3.DistanceSquared(p, Generation.MeshFeatures.ClosestPointOnTriangle(p, a, b, c));
            if (d < bestDistance)
            {
                bestDistance = d;
                best = face;
            }
        }
        return best;
    }

    private static Vector3 VerticalPlaneNormal(Vector3 a, Vector3 b)
    {
        var d = b - a;
        var horizontal = new Vector3(d.X, d.Y, 0f);
        // Two points on one vertical: every vertical plane contains them; take the X normal.
        if (horizontal.LengthSquared() < 1e-12f) return Vector3.UnitX;
        return Vector3.Normalize(new Vector3(horizontal.Y, -horizontal.X, 0f));
    }

    private static float Side(Vector3 p, Vector3 origin, Vector3 normal) => Vector3.Dot(normal, p - origin);

    /// <summary>The edges of <paramref name="face"/> the plane crosses, as (vertex, vertex) pairs.</summary>
    private static List<(int I, int J)> CrossedEdges(Mesh mesh, int face, Vector3 origin, Vector3 normal)
    {
        var result = new List<(int, int)>(2);
        for (var e = 0; e < 3; e++)
        {
            var i = mesh.Indices[face * 3 + e];
            var j = mesh.Indices[face * 3 + (e + 1) % 3];
            var si = Side(mesh.Positions[i], origin, normal);
            var sj = Side(mesh.Positions[j], origin, normal);
            // A vertex exactly on the plane counts as the negative side, consistently, so each
            // triangle the plane passes through reports exactly two crossings.
            if ((si > 0) != (sj > 0)) result.Add((i, j));
        }
        return result;
    }

    private static Vector3 CrossingPoint(Mesh mesh, (int I, int J) edge, Vector3 origin, Vector3 normal)
    {
        var p = mesh.Positions[edge.I];
        var q = mesh.Positions[edge.J];
        var si = Side(p, origin, normal);
        var sj = Side(q, origin, normal);
        var denominator = si - sj;
        var t = MathF.Abs(denominator) < 1e-12f ? 0f : Math.Clamp(si / denominator, 0f, 1f);
        return Vector3.Lerp(p, q, t);
    }

    private static bool SameEdge((int I, int J) x, (int I, int J) y) =>
        (x.I == y.I && x.J == y.J) || (x.I == y.J && x.J == y.I);

    private static int? Neighbour(Mesh mesh, IReadOnlyList<IReadOnlyList<int>> adjacency, int face,
        (int I, int J) edge, HashSet<int> visited)
    {
        foreach (var candidate in adjacency[face])
        {
            if (visited.Contains(candidate)) continue;
            var shares = 0;
            for (var e = 0; e < 3; e++)
            {
                var v = mesh.Indices[candidate * 3 + e];
                if (v == edge.I || v == edge.J) shares++;
            }
            if (shares == 2) return candidate;
        }
        return null;
    }

    private static SurfacePath? Walk(Mesh mesh, IReadOnlyList<IReadOnlyList<int>> adjacency,
        Vector3 a, int faceA, Vector3 b, int faceB, Vector3 normal, (int I, int J) exit)
    {
        var points = new List<Vector3> { a };
        var faces = new List<int> { faceA };
        var visited = new HashSet<int> { faceA };
        var length = 0f;
        var face = faceA;
        // Every face is left at most once, so the walk is bounded by the triangle count.
        for (var step = 0; step < mesh.TriangleCount; step++)
        {
            if (Neighbour(mesh, adjacency, face, exit, visited) is not { } entered) return null;
            var crossing = CrossingPoint(mesh, exit, a, normal);
            length += Vector3.Distance(points[^1], crossing);
            points.Add(crossing);
            faces.Add(entered);
            if (entered == faceB)
            {
                length += Vector3.Distance(crossing, b);
                points.Add(b);
                faces.Add(faceB);
                return new SurfacePath(points, faces, length);
            }
            visited.Add(entered);
            (int I, int J)? onward = null;
            foreach (var edge in CrossedEdges(mesh, entered, a, normal))
                if (!SameEdge(edge, exit)) { onward = edge; break; }
            if (onward is not { } forward) return null;
            exit = forward;
            face = entered;
        }
        return null;
    }
}
