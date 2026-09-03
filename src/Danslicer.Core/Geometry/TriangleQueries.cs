using System.Numerics;

namespace Danslicer.Core.Geometry;

/// <summary>
/// Closest-point helpers shared by the triangle BVH and the brute-force agreement path.
/// Triangle closest-point is Ericson's region method (Real-Time Collision Detection).
/// </summary>
public static class TriangleQueries
{
    public static Vector3 ClosestPointOnTriangle(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
    {
        var ab = b - a;
        var ac = c - a;
        var ap = p - a;
        var d1 = Vector3.Dot(ab, ap);
        var d2 = Vector3.Dot(ac, ap);
        if (d1 <= 0 && d2 <= 0) return a;

        var bp = p - b;
        var d3 = Vector3.Dot(ab, bp);
        var d4 = Vector3.Dot(ac, bp);
        if (d3 >= 0 && d4 <= d3) return b;

        var vc = d1 * d4 - d3 * d2;
        if (vc <= 0 && d1 >= 0 && d3 <= 0)
        {
            var v = d1 / (d1 - d3);
            return a + v * ab;
        }

        var cp = p - c;
        var d5 = Vector3.Dot(ab, cp);
        var d6 = Vector3.Dot(ac, cp);
        if (d6 >= 0 && d5 <= d6) return c;

        var vb = d5 * d2 - d1 * d6;
        if (vb <= 0 && d2 >= 0 && d6 <= 0)
        {
            var w = d2 / (d2 - d6);
            return a + w * ac;
        }

        var va = d3 * d6 - d5 * d4;
        if (va <= 0 && (d4 - d3) >= 0 && (d5 - d6) >= 0)
        {
            var w = (d4 - d3) / ((d4 - d3) + (d5 - d6));
            return b + w * (c - b);
        }

        var denom = 1f / (va + vb + vc);
        return a + ab * (vb * denom) + ac * (vc * denom);
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

    public static void ClosestSegmentTriangle(
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

        Consider(p0, ClosestPointOnTriangle(p0, a, b, c));
        Consider(p1, ClosestPointOnTriangle(p1, a, b, c));
        ClosestPointsOnSegments(p0, p1, a, b, out var s0, out var t0); Consider(s0, t0);
        ClosestPointsOnSegments(p0, p1, b, c, out var s1, out var t1); Consider(s1, t1);
        ClosestPointsOnSegments(p0, p1, c, a, out var s2, out var t2); Consider(s2, t2);
        onSeg = bestS;
        onTri = bestT;
    }

    public static void ClosestTriangleTriangle(
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

        Consider(a0, ClosestPointOnTriangle(a0, a1, b1, c1));
        Consider(b0, ClosestPointOnTriangle(b0, a1, b1, c1));
        Consider(c0, ClosestPointOnTriangle(c0, a1, b1, c1));
        Consider(ClosestPointOnTriangle(a1, a0, b0, c0), a1);
        Consider(ClosestPointOnTriangle(b1, a0, b0, c0), b1);
        Consider(ClosestPointOnTriangle(c1, a0, b0, c0), c1);

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

    public static Aabb TriangleBounds(Vector3 a, Vector3 b, Vector3 c) =>
        new(Vector3.Min(a, Vector3.Min(b, c)), Vector3.Max(a, Vector3.Max(b, c)));
}
