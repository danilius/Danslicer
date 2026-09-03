using System.Numerics;
using Danslicer.Core.Geometry;
using Danslicer.Core.Scene;

namespace Danslicer.Core.Supports.Routing;

/// <summary>The closest obstacle point returned by a collision query.</summary>
public readonly record struct ObstacleNearestPoint(Vector3 Point, float Distance, object? Tag = null);

/// <summary>
/// Read-only collision queries used by support routing. A capsule includes both hemispherical
/// end caps. Clearance is expressed by increasing its radius.
/// </summary>
public interface ICollisionScene
{
    bool IntersectsCapsule(Vector3 start, Vector3 end, float radius,
        Func<object?, bool>? obstacleFilter = null);
    ObstacleNearestPoint? NearestObstacle(Vector3 point, Func<object?, bool>? obstacleFilter = null);
}

/// <summary>
/// Simple, exact collision scene with AABB broad-phase rejection. It is deliberately behind
/// <see cref="ICollisionScene"/> so a BVH can replace the linear scan without changing routing.
/// </summary>
public sealed class LinearCollisionScene : ICollisionScene
{
    private readonly List<TriangleObstacle> _triangles = new();
    private readonly List<CapsuleObstacle> _capsules = new();

    public int TriangleCount => _triangles.Count;
    public int CapsuleCount => _capsules.Count;

    public void AddMesh(Mesh mesh, Matrix4x4 transform, object? tag = null)
    {
        for (var i = 0; i < mesh.TriangleCount; i++)
        {
            mesh.GetTriangle(i, out var a, out var b, out var c);
            AddTriangle(Vector3.Transform(a, transform), Vector3.Transform(b, transform),
                Vector3.Transform(c, transform), tag);
        }
    }

    public void AddSceneObject(SceneObject sceneObject) =>
        AddMesh(sceneObject.Mesh, sceneObject.Transform.ToMatrix(), sceneObject.Id);

    public void AddTriangle(Vector3 a, Vector3 b, Vector3 c, object? tag = null) =>
        _triangles.Add(new TriangleObstacle(a, b, c, Bounds(a, b, c), tag));

    public void AddCapsule(Vector3 start, Vector3 end, float radius, object? tag = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(radius);
        _capsules.Add(new CapsuleObstacle(start, end, radius, CapsuleBounds(start, end, radius), tag));
    }

    public void AddSupportGraph(SupportGraph graph)
    {
        foreach (var segment in graph.Segments.Where(segment => !segment.Disabled))
        {
            AddCapsule(graph.GetNode(segment.NodeA).Position, graph.GetNode(segment.NodeB).Position,
                segment.Diameter * 0.5f, segment.Id);
        }
    }

    public bool IntersectsCapsule(Vector3 start, Vector3 end, float radius,
        Func<object?, bool>? obstacleFilter = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(radius);
        var bounds = CapsuleBounds(start, end, radius);
        var radiusSquared = radius * radius;

        foreach (var triangle in _triangles)
        {
            if (obstacleFilter is not null && !obstacleFilter(triangle.Tag)) continue;
            if (!Overlaps(bounds, triangle.Bounds)) continue;
            if (GeometryDistance.SegmentTriangleSquared(start, end, triangle.A, triangle.B, triangle.C)
                <= radiusSquared) return true;
        }

        foreach (var capsule in _capsules)
        {
            if (obstacleFilter is not null && !obstacleFilter(capsule.Tag)) continue;
            if (!Overlaps(bounds, capsule.Bounds)) continue;
            var sum = radius + capsule.Radius;
            if (GeometryDistance.SegmentSegmentSquared(start, end, capsule.Start, capsule.End)
                <= sum * sum) return true;
        }

        return false;
    }

    public ObstacleNearestPoint? NearestObstacle(Vector3 point,
        Func<object?, bool>? obstacleFilter = null)
    {
        ObstacleNearestPoint? nearest = null;
        foreach (var triangle in _triangles)
        {
            if (obstacleFilter is not null && !obstacleFilter(triangle.Tag)) continue;
            var candidate = GeometryDistance.ClosestPointOnTriangle(point, triangle.A, triangle.B, triangle.C);
            Consider(candidate, Vector3.Distance(point, candidate), triangle.Tag, ref nearest);
        }

        foreach (var capsule in _capsules)
        {
            if (obstacleFilter is not null && !obstacleFilter(capsule.Tag)) continue;
            var axisPoint = GeometryDistance.ClosestPointOnSegment(point, capsule.Start, capsule.End);
            var delta = point - axisPoint;
            var length = delta.Length();
            var surface = length > 1e-7f
                ? axisPoint + delta * (capsule.Radius / length)
                : axisPoint + Vector3.UnitX * capsule.Radius;
            Consider(surface, MathF.Max(0, length - capsule.Radius), capsule.Tag, ref nearest);
        }

        return nearest;
    }

    private static void Consider(Vector3 point, float distance, object? tag,
        ref ObstacleNearestPoint? nearest)
    {
        if (nearest is null || distance < nearest.Value.Distance)
            nearest = new ObstacleNearestPoint(point, distance, tag);
    }

    private static Aabb Bounds(Vector3 a, Vector3 b, Vector3 c) =>
        new(Vector3.Min(a, Vector3.Min(b, c)), Vector3.Max(a, Vector3.Max(b, c)));

    private static Aabb CapsuleBounds(Vector3 a, Vector3 b, float radius)
    {
        var r = new Vector3(radius);
        return new Aabb(Vector3.Min(a, b) - r, Vector3.Max(a, b) + r);
    }

    private static bool Overlaps(Aabb a, Aabb b) =>
        a.Min.X <= b.Max.X && a.Max.X >= b.Min.X &&
        a.Min.Y <= b.Max.Y && a.Max.Y >= b.Min.Y &&
        a.Min.Z <= b.Max.Z && a.Max.Z >= b.Min.Z;

    private readonly record struct TriangleObstacle(
        Vector3 A, Vector3 B, Vector3 C, Aabb Bounds, object? Tag);
    private readonly record struct CapsuleObstacle(
        Vector3 Start, Vector3 End, float Radius, Aabb Bounds, object? Tag);
}

internal static class GeometryDistance
{
    public static Vector3 ClosestPointOnSegment(Vector3 point, Vector3 a, Vector3 b)
    {
        var ab = b - a;
        var denominator = Vector3.Dot(ab, ab);
        if (denominator <= 1e-12f) return a;
        return a + ab * Math.Clamp(Vector3.Dot(point - a, ab) / denominator, 0, 1);
    }

    // Real-Time Collision Detection, Christer Ericson, closest point on triangle regions.
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
            return a + ab * (d1 / (d1 - d3));

        var cp = p - c;
        var d5 = Vector3.Dot(ab, cp);
        var d6 = Vector3.Dot(ac, cp);
        if (d6 >= 0 && d5 <= d6) return c;

        var vb = d5 * d2 - d1 * d6;
        if (vb <= 0 && d2 >= 0 && d6 <= 0)
            return a + ac * (d2 / (d2 - d6));

        var va = d3 * d6 - d5 * d4;
        if (va <= 0 && d4 - d3 >= 0 && d5 - d6 >= 0)
            return b + (c - b) * ((d4 - d3) / ((d4 - d3) + (d5 - d6)));

        var denominator = 1f / (va + vb + vc);
        return a + ab * (vb * denominator) + ac * (vc * denominator);
    }

    public static float SegmentTriangleSquared(
        Vector3 p0, Vector3 p1, Vector3 a, Vector3 b, Vector3 c)
    {
        if (SegmentIntersectsTriangle(p0, p1, a, b, c)) return 0;
        var best = MathF.Min(Vector3.DistanceSquared(p0, ClosestPointOnTriangle(p0, a, b, c)),
            Vector3.DistanceSquared(p1, ClosestPointOnTriangle(p1, a, b, c)));
        best = MathF.Min(best, SegmentSegmentSquared(p0, p1, a, b));
        best = MathF.Min(best, SegmentSegmentSquared(p0, p1, b, c));
        return MathF.Min(best, SegmentSegmentSquared(p0, p1, c, a));
    }

    public static float SegmentSegmentSquared(Vector3 p1, Vector3 q1, Vector3 p2, Vector3 q2)
    {
        const float epsilon = 1e-8f;
        var d1 = q1 - p1;
        var d2 = q2 - p2;
        var r = p1 - p2;
        var a = Vector3.Dot(d1, d1);
        var e = Vector3.Dot(d2, d2);
        var f = Vector3.Dot(d2, r);
        float s, t;
        if (a <= epsilon && e <= epsilon) return Vector3.DistanceSquared(p1, p2);
        if (a <= epsilon)
        {
            s = 0;
            t = Math.Clamp(f / e, 0, 1);
        }
        else
        {
            var c = Vector3.Dot(d1, r);
            if (e <= epsilon)
            {
                t = 0;
                s = Math.Clamp(-c / a, 0, 1);
            }
            else
            {
                var b = Vector3.Dot(d1, d2);
                var denominator = a * e - b * b;
                s = denominator == 0 ? 0 : Math.Clamp((b * f - c * e) / denominator, 0, 1);
                t = (b * s + f) / e;
                if (t < 0) { t = 0; s = Math.Clamp(-c / a, 0, 1); }
                else if (t > 1) { t = 1; s = Math.Clamp((b - c) / a, 0, 1); }
            }
        }
        var c1 = p1 + d1 * s;
        var c2 = p2 + d2 * t;
        return Vector3.DistanceSquared(c1, c2);
    }

    private static bool SegmentIntersectsTriangle(
        Vector3 p0, Vector3 p1, Vector3 a, Vector3 b, Vector3 c)
    {
        const float epsilon = 1e-7f;
        var direction = p1 - p0;
        var edge1 = b - a;
        var edge2 = c - a;
        var h = Vector3.Cross(direction, edge2);
        var determinant = Vector3.Dot(edge1, h);
        if (MathF.Abs(determinant) < epsilon) return false;
        var inverse = 1 / determinant;
        var s = p0 - a;
        var u = inverse * Vector3.Dot(s, h);
        if (u < 0 || u > 1) return false;
        var q = Vector3.Cross(s, edge1);
        var v = inverse * Vector3.Dot(direction, q);
        if (v < 0 || u + v > 1) return false;
        var t = inverse * Vector3.Dot(edge2, q);
        return t >= 0 && t <= 1;
    }
}
