using System.Numerics;
using Danslicer.Core.Geometry;

namespace Danslicer.Core.Supports.Guided;

/// <summary>
/// Geometry behind polygon fill (SUPPORT-GEOMETRY-SPEC "Guided tip placement"): what a closed
/// loop of surface paths encloses. Two answers, used together:
/// <list type="bullet">
/// <item><see cref="EnclosedFaces"/> — the face set at face granularity, which the painted-region
/// grid sampler takes as it takes a painted region.</item>
/// <item><see cref="InsideLoop"/> — the finer test a sampled point must pass, so a small polygon on
/// a large CAD face fills the polygon and not the whole face.</item>
/// </list>
/// </summary>
public static class SurfacePolygon
{
    /// <summary>
    /// The faces the loop passes over — a ring one face wide.
    /// </summary>
    public static IReadOnlySet<int> RingFaces(IReadOnlyList<SurfacePath> loop)
    {
        var ring = new HashSet<int>();
        foreach (var path in loop)
            foreach (var face in path.Faces) ring.Add(face);
        return ring;
    }

    /// <summary>
    /// The faces inside a ring: of the edge-connected components the ring cuts the surface
    /// into, the smallest by area that touches the ring. A ring that cuts nothing off (a loop
    /// small enough to sit within one face, or one that does not close) encloses no face, and
    /// the fill is the ring itself. The ring's own faces are always part of the result.
    /// </summary>
    public static IReadOnlySet<int> EnclosedFaces(Mesh mesh, IReadOnlySet<int> ring)
    {
        var adjacency = SupportRegionSelection.FaceAdjacencyOf(mesh);
        var component = new int[mesh.TriangleCount];
        Array.Fill(component, -1);
        var areas = new List<float>();
        var touchesRing = new List<bool>();
        var stack = new Stack<int>();
        for (var seed = 0; seed < mesh.TriangleCount; seed++)
        {
            if (component[seed] >= 0 || ring.Contains(seed)) continue;
            var id = areas.Count;
            areas.Add(0f);
            touchesRing.Add(false);
            component[seed] = id;
            stack.Push(seed);
            while (stack.Count > 0)
            {
                var face = stack.Pop();
                areas[id] += Area(mesh, face);
                foreach (var next in adjacency[face])
                {
                    if (ring.Contains(next)) { touchesRing[id] = true; continue; }
                    if (component[next] >= 0) continue;
                    component[next] = id;
                    stack.Push(next);
                }
            }
        }

        var result = new HashSet<int>(ring);
        var candidates = Enumerable.Range(0, areas.Count).Where(id => touchesRing[id]).ToList();
        // One component touching the ring means the ring cut nothing off.
        if (candidates.Count < 2) return result;
        var inside = candidates.MinBy(id => areas[id]);
        for (var face = 0; face < component.Length; face++)
            if (component[face] == inside) result.Add(face);
        return result;
    }

    /// <summary>
    /// A point-in-polygon test for the loop seen flat: the loop's points are projected onto their
    /// best-fit plane (Newell normal) and the point is tested against that 2D polygon. Points
    /// on or within a hair of the boundary count as inside. Null when the loop is degenerate
    /// (collinear, fewer than three points), in which case nothing is inside.
    /// </summary>
    public static Func<Vector3, bool> InsideLoop(IReadOnlyList<SurfacePath> loop)
    {
        var points = new List<Vector3>();
        foreach (var path in loop)
            for (var i = 0; i < path.Points.Count; i++)
            {
                // Consecutive paths share their join point; keep one copy.
                if (points.Count > 0 && Vector3.DistanceSquared(points[^1], path.Points[i]) < 1e-10f) continue;
                points.Add(path.Points[i]);
            }
        if (points.Count > 1 && Vector3.DistanceSquared(points[0], points[^1]) < 1e-10f)
            points.RemoveAt(points.Count - 1);
        if (points.Count < 3) return _ => false;

        // Newell's method: robust plane normal for a non-planar loop.
        var normal = Vector3.Zero;
        for (var i = 0; i < points.Count; i++)
        {
            var p = points[i];
            var q = points[(i + 1) % points.Count];
            normal += new Vector3((p.Y - q.Y) * (p.Z + q.Z), (p.Z - q.Z) * (p.X + q.X), (p.X - q.X) * (p.Y + q.Y));
        }
        if (normal.LengthSquared() < 1e-12f) return _ => false;
        normal = Vector3.Normalize(normal);
        var u = Vector3.Normalize(Vector3.Cross(normal,
            MathF.Abs(normal.Z) < 0.9f ? Vector3.UnitZ : Vector3.UnitX));
        var v = Vector3.Cross(normal, u);
        var origin = points[0];
        var polygon = points.Select(p => new Vector2(Vector3.Dot(p - origin, u), Vector3.Dot(p - origin, v))).ToArray();

        return world =>
        {
            var p = new Vector2(Vector3.Dot(world - origin, u), Vector3.Dot(world - origin, v));
            return Contains(polygon, p) || NearBoundary(polygon, p, 1e-3f);
        };
    }

    private static bool Contains(Vector2[] polygon, Vector2 p)
    {
        var inside = false;
        for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
        {
            var a = polygon[i];
            var b = polygon[j];
            if ((a.Y > p.Y) != (b.Y > p.Y) &&
                p.X < (b.X - a.X) * (p.Y - a.Y) / (b.Y - a.Y) + a.X)
                inside = !inside;
        }
        return inside;
    }

    private static bool NearBoundary(Vector2[] polygon, Vector2 p, float tolerance)
    {
        for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
        {
            var a = polygon[i];
            var b = polygon[j];
            var ab = b - a;
            var t = ab.LengthSquared() < 1e-12f ? 0f : Math.Clamp(Vector2.Dot(p - a, ab) / ab.LengthSquared(), 0f, 1f);
            if (Vector2.DistanceSquared(p, a + ab * t) <= tolerance * tolerance) return true;
        }
        return false;
    }

    private static float Area(Mesh mesh, int face)
    {
        mesh.GetTriangle(face, out var a, out var b, out var c);
        return Vector3.Cross(b - a, c - a).Length() * 0.5f;
    }
}
