using System.Numerics;
using Danslicer.Core.Geometry;
using Danslicer.Core.Supports.Generation;

namespace Danslicer.Core.Supports.Checks;

/// <summary>
/// Closest-point queries against a world-space mesh. Brute-force triangle walk; a BVH can
/// replace this type behind the same methods without changing callers.
/// </summary>
internal sealed class MeshDistanceQuery
{
    private readonly Mesh _mesh;
    private readonly Aabb[] _triBounds;

    public Mesh Mesh => _mesh;
    public Aabb Bounds => _mesh.Bounds;

    public MeshDistanceQuery(Mesh mesh)
    {
        _mesh = mesh;
        _triBounds = new Aabb[mesh.TriangleCount];
        for (int t = 0; t < mesh.TriangleCount; t++)
        {
            mesh.GetTriangle(t, out var a, out var b, out var c);
            _triBounds[t] = Aabb.Empty.Include(a).Include(b).Include(c);
        }
    }

    public float ClosestToPoint(Vector3 p, out Vector3 onMesh)
    {
        onMesh = default;
        var best = float.PositiveInfinity;
        for (int t = 0; t < _mesh.TriangleCount; t++)
        {
            var dBox = PointAabbDistance(p, _triBounds[t]);
            if (dBox * dBox >= best) continue;
            _mesh.GetTriangle(t, out var a, out var b, out var c);
            var q = MeshFeatures.ClosestPointOnTriangle(p, a, b, c);
            var d2 = Vector3.DistanceSquared(p, q);
            if (d2 < best)
            {
                best = d2;
                onMesh = q;
            }
        }
        return float.IsPositiveInfinity(best) ? float.PositiveInfinity : MathF.Sqrt(best);
    }

    public float ClosestToSegment(Vector3 p0, Vector3 p1, out Vector3 onSeg, out Vector3 onMesh)
    {
        onSeg = p0;
        onMesh = default;
        var best = float.PositiveInfinity;
        var segBox = Aabb.Empty.Include(p0).Include(p1);
        for (int t = 0; t < _mesh.TriangleCount; t++)
        {
            if (AabbSeparation(segBox, _triBounds[t]) * AabbSeparation(segBox, _triBounds[t]) >= best) continue;
            _mesh.GetTriangle(t, out var a, out var b, out var c);
            SegmentTriangleClosest(p0, p1, a, b, c, out var s, out var m);
            var d2 = Vector3.DistanceSquared(s, m);
            if (d2 < best)
            {
                best = d2;
                onSeg = s;
                onMesh = m;
            }
        }
        return float.IsPositiveInfinity(best) ? float.PositiveInfinity : MathF.Sqrt(best);
    }

    public float ClosestToMesh(MeshDistanceQuery other, out Vector3 onThis, out Vector3 onOther)
    {
        onThis = default;
        onOther = default;
        var best = float.PositiveInfinity;
        for (int i = 0; i < _mesh.TriangleCount; i++)
        {
            _mesh.GetTriangle(i, out var a0, out var b0, out var c0);
            for (int j = 0; j < other._mesh.TriangleCount; j++)
            {
                if (AabbSeparation(_triBounds[i], other._triBounds[j]) * AabbSeparation(_triBounds[i], other._triBounds[j]) >= best)
                    continue;
                other._mesh.GetTriangle(j, out var a1, out var b1, out var c1);
                TriangleTriangleClosest(a0, b0, c0, a1, b1, c1, out var p, out var q);
                var d2 = Vector3.DistanceSquared(p, q);
                if (d2 < best)
                {
                    best = d2;
                    onThis = p;
                    onOther = q;
                }
            }
        }
        return float.IsPositiveInfinity(best) ? float.PositiveInfinity : MathF.Sqrt(best);
    }

    public static void ClosestPointsOnSegments(
        Vector3 p1, Vector3 q1, Vector3 p2, Vector3 q2, out Vector3 c1, out Vector3 c2)
    {
        var d1 = q1 - p1;
        var d2 = q2 - p2;
        var r = p1 - p2;
        var a = d1.LengthSquared();
        var e = d2.LengthSquared();
        var f = Vector3.Dot(d2, r);
        float s, t;
        if (a <= 1e-12f && e <= 1e-12f)
        {
            c1 = p1;
            c2 = p2;
            return;
        }
        if (a <= 1e-12f)
        {
            s = 0;
            t = Math.Clamp(f / e, 0f, 1f);
        }
        else
        {
            var c = Vector3.Dot(d1, r);
            if (e <= 1e-12f)
            {
                t = 0;
                s = Math.Clamp(-c / a, 0f, 1f);
            }
            else
            {
                var b = Vector3.Dot(d1, d2);
                var denom = a * e - b * b;
                s = denom != 0 ? Math.Clamp((b * f - c * e) / denom, 0f, 1f) : 0f;
                t = (b * s + f) / e;
                if (t < 0)
                {
                    t = 0;
                    s = Math.Clamp(-c / a, 0f, 1f);
                }
                else if (t > 1)
                {
                    t = 1;
                    s = Math.Clamp((b - c) / a, 0f, 1f);
                }
            }
        }
        c1 = p1 + d1 * s;
        c2 = p2 + d2 * t;
    }

    private static void SegmentTriangleClosest(
        Vector3 p0, Vector3 p1, Vector3 a, Vector3 b, Vector3 c, out Vector3 onSeg, out Vector3 onTri)
    {
        var best = float.PositiveInfinity;
        var bestS = p0;
        var bestT = a;

        void Consider(Vector3 s, Vector3 t)
        {
            var d2 = Vector3.DistanceSquared(s, t);
            if (d2 < best)
            {
                best = d2;
                bestS = s;
                bestT = t;
            }
        }

        Consider(p0, MeshFeatures.ClosestPointOnTriangle(p0, a, b, c));
        Consider(p1, MeshFeatures.ClosestPointOnTriangle(p1, a, b, c));
        ClosestPointsOnSegments(p0, p1, a, b, out var s0, out var t0); Consider(s0, t0);
        ClosestPointsOnSegments(p0, p1, b, c, out var s1, out var t1); Consider(s1, t1);
        ClosestPointsOnSegments(p0, p1, c, a, out var s2, out var t2); Consider(s2, t2);
        onSeg = bestS;
        onTri = bestT;
    }

    private static void TriangleTriangleClosest(
        Vector3 a0, Vector3 b0, Vector3 c0, Vector3 a1, Vector3 b1, Vector3 c1, out Vector3 p, out Vector3 q)
    {
        var best = float.PositiveInfinity;
        var bestP = a0;
        var bestQ = a1;

        void Consider(Vector3 s, Vector3 t)
        {
            var d2 = Vector3.DistanceSquared(s, t);
            if (d2 < best)
            {
                best = d2;
                bestP = s;
                bestQ = t;
            }
        }

        Consider(a0, MeshFeatures.ClosestPointOnTriangle(a0, a1, b1, c1));
        Consider(b0, MeshFeatures.ClosestPointOnTriangle(b0, a1, b1, c1));
        Consider(c0, MeshFeatures.ClosestPointOnTriangle(c0, a1, b1, c1));
        Consider(MeshFeatures.ClosestPointOnTriangle(a1, a0, b0, c0), a1);
        Consider(MeshFeatures.ClosestPointOnTriangle(b1, a0, b0, c0), b1);
        Consider(MeshFeatures.ClosestPointOnTriangle(c1, a0, b0, c0), c1);

        void Edge(Vector3 u0, Vector3 u1, Vector3 v0, Vector3 v1)
        {
            ClosestPointsOnSegments(u0, u1, v0, v1, out var s, out var t);
            Consider(s, t);
        }
        Edge(a0, b0, a1, b1); Edge(a0, b0, b1, c1); Edge(a0, b0, c1, a1);
        Edge(b0, c0, a1, b1); Edge(b0, c0, b1, c1); Edge(b0, c0, c1, a1);
        Edge(c0, a0, a1, b1); Edge(c0, a0, b1, c1); Edge(c0, a0, c1, a1);
        p = bestP;
        q = bestQ;
    }

    private static float PointAabbDistance(Vector3 p, Aabb b)
    {
        var q = Vector3.Clamp(p, b.Min, b.Max);
        return Vector3.Distance(p, q);
    }

    private static float AabbSeparation(Aabb a, Aabb b)
    {
        var dx = MathF.Max(0, MathF.Max(a.Min.X - b.Max.X, b.Min.X - a.Max.X));
        var dy = MathF.Max(0, MathF.Max(a.Min.Y - b.Max.Y, b.Min.Y - a.Max.Y));
        var dz = MathF.Max(0, MathF.Max(a.Min.Z - b.Max.Z, b.Min.Z - a.Max.Z));
        return MathF.Sqrt(dx * dx + dy * dy + dz * dz);
    }
}
