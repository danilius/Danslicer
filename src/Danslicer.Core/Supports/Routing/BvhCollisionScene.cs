using System.Numerics;
using Danslicer.Core.Geometry;
using Danslicer.Core.Scene;

namespace Danslicer.Core.Supports.Routing;

/// <summary>
/// Collision scene accelerated by a median-split bounding-volume hierarchy. Additions invalidate
/// the hierarchy; the next query rebuilds it, making batches of queries fast while preserving the
/// same exact narrow-phase behavior as <see cref="LinearCollisionScene"/>.
/// </summary>
public sealed class BvhCollisionScene : ICollisionScene
{
    private const int LeafSize = 8;
    private readonly List<TriangleObstacle> _triangles = new();
    private readonly List<CapsuleObstacle> _capsules = new();
    private readonly List<SphereObstacle> _spheres = new();
    private Primitive[] _ordered = Array.Empty<Primitive>();
    private Node? _root;
    private bool _dirty = true;

    public int TriangleCount => _triangles.Count;
    public int CapsuleCount => _capsules.Count;
    public int SphereCount => _spheres.Count;

    public void AddMesh(Mesh mesh, Matrix4x4 transform, object? tag = null)
    {
        for (var i = 0; i < mesh.TriangleCount; i++)
        {
            SupportGenerationMonitor.Check();
            mesh.GetTriangle(i, out var a, out var b, out var c);
            AddTriangle(Vector3.Transform(a, transform), Vector3.Transform(b, transform),
                Vector3.Transform(c, transform), tag);
        }
    }

    public void AddSceneObject(SceneObject sceneObject) =>
        AddMesh(sceneObject.Mesh, sceneObject.Transform.ToMatrix(), sceneObject.Id);

    public void AddTriangle(Vector3 a, Vector3 b, Vector3 c, object? tag = null)
    {
        _triangles.Add(new TriangleObstacle(a, b, c, Bounds(a, b, c), tag));
        _dirty = true;
    }

    public void AddCapsule(Vector3 start, Vector3 end, float radius, object? tag = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(radius);
        _capsules.Add(new CapsuleObstacle(start, end, radius, CapsuleBounds(start, end, radius), tag));
        _dirty = true;
    }

    public void AddSphere(Vector3 centre, float radius, object? tag = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(radius);
        _spheres.Add(new SphereObstacle(centre, radius, SphereBounds(centre, radius), tag));
        _dirty = true;
    }

    public void AddSupportGraph(SupportGraph graph)
    {
        foreach (var segment in graph.Segments.Where(segment => !segment.Disabled))
        {
            SupportGenerationMonitor.Check();
            var a = graph.GetNode(segment.NodeA);
            var b = graph.GetNode(segment.NodeB);
            if (SupportSliceGeometry.TryConeTip(a, b, out var tip, out var other))
            {
                // A cone's sections carry its real envelope, up to the ball it grows from.
                foreach (var section in TipBodyGeometry.Sections(tip, other,
                             SupportSliceGeometry.TipJunctionDiameter(graph, segment) * 0.5f,
                             embedContact: false))
                    AddCapsule(section.Start, section.End,
                        MathF.Max(section.StartRadius, section.EndRadius), segment.Id);
            }
            else
                AddCapsule(a.Position, b.Position, segment.Diameter * 0.5f, segment.Id);
        }
        foreach (var (centre, radius, id) in graph.ContactBallsExceedingNeck())
            AddSphere(centre, radius, id);
    }

    public bool IntersectsCapsule(Vector3 start, Vector3 end, float radius,
        Func<object?, bool>? obstacleFilter = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(radius);
        EnsureBuilt();
        if (_root is null) return false;
        var queryBounds = CapsuleBounds(start, end, radius);
        var stack = new Stack<Node>();
        stack.Push(_root);
        while (stack.Count > 0)
        {
            SupportGenerationMonitor.Check();
            var node = stack.Pop();
            if (!Overlaps(queryBounds, node.Bounds)) continue;
            if (node.IsLeaf)
            {
                for (var i = node.Start; i < node.Start + node.Count; i++)
                    if (Included(_ordered[i], obstacleFilter) &&
                        Intersects(_ordered[i], start, end, radius)) return true;
                continue;
            }
            stack.Push(node.Left!);
            stack.Push(node.Right!);
        }
        return false;
    }

    public ObstacleNearestPoint? NearestObstacle(Vector3 point,
        Func<object?, bool>? obstacleFilter = null)
    {
        EnsureBuilt();
        if (_root is null) return null;
        ObstacleNearestPoint? nearest = null;
        var stack = new Stack<Node>();
        stack.Push(_root);
        while (stack.Count > 0)
        {
            SupportGenerationMonitor.Check();
            var node = stack.Pop();
            if (nearest is not null && DistanceSquared(point, node.Bounds) >
                nearest.Value.Distance * nearest.Value.Distance) continue;
            if (node.IsLeaf)
            {
                for (var i = node.Start; i < node.Start + node.Count; i++)
                    if (Included(_ordered[i], obstacleFilter))
                        Consider(_ordered[i], point, ref nearest);
                continue;
            }

            var leftDistance = DistanceSquared(point, node.Left!.Bounds);
            var rightDistance = DistanceSquared(point, node.Right!.Bounds);
            if (leftDistance <= rightDistance)
            {
                stack.Push(node.Right);
                stack.Push(node.Left);
            }
            else
            {
                stack.Push(node.Left);
                stack.Push(node.Right);
            }
        }
        return nearest;
    }

    public ObstacleRayHit? Raycast(Vector3 origin, Vector3 direction, float maxDistance,
        Func<object?, bool>? obstacleFilter = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxDistance);
        if (direction.LengthSquared() <= 1e-12f)
            throw new ArgumentException("Ray direction must be non-zero.", nameof(direction));
        direction = Vector3.Normalize(direction);
        EnsureBuilt();
        if (_root is null) return null;

        var ray = new Ray(origin, direction);
        ObstacleRayHit? nearest = null;
        var nearestDistance = maxDistance;
        var nearestTriangleIndex = int.MaxValue;
        var stack = new Stack<Node>();
        stack.Push(_root);
        while (stack.Count > 0)
        {
            SupportGenerationMonitor.Check();
            var node = stack.Pop();
            if (!node.Bounds.IntersectsRay(ray, out var enter, out _) || enter > nearestDistance) continue;
            if (!node.IsLeaf)
            {
                stack.Push(node.Left!);
                stack.Push(node.Right!);
                continue;
            }

            for (var i = node.Start; i < node.Start + node.Count; i++)
            {
                SupportGenerationMonitor.Check();
                var primitive = _ordered[i];
                if (primitive.Kind != PrimitiveKind.Triangle) continue;
                var triangle = _triangles[primitive.Index];
                if (obstacleFilter is not null && !obstacleFilter(triangle.Tag)) continue;
                if (!GeometryDistance.RaycastTriangle(origin, direction, triangle.A, triangle.B,
                        triangle.C, out var distance) || distance > nearestDistance) continue;
                if (distance == nearestDistance && primitive.Index >= nearestTriangleIndex) continue;
                var normal = Vector3.Cross(triangle.B - triangle.A, triangle.C - triangle.A);
                normal = normal.LengthSquared() > 1e-12f ? Vector3.Normalize(normal) : Vector3.UnitZ;
                nearestDistance = distance;
                nearestTriangleIndex = primitive.Index;
                nearest = new ObstacleRayHit(origin + direction * distance, normal, distance, triangle.Tag);
            }
        }
        return nearest;
    }

    private void EnsureBuilt()
    {
        if (!_dirty) return;
        _ordered = _triangles.Select((triangle, index) =>
                new Primitive(PrimitiveKind.Triangle, index, triangle.Bounds, Center(triangle.Bounds)))
            .Concat(_capsules.Select((capsule, index) =>
                new Primitive(PrimitiveKind.Capsule, index, capsule.Bounds, Center(capsule.Bounds))))
            .Concat(_spheres.Select((sphere, index) =>
                new Primitive(PrimitiveKind.Sphere, index, sphere.Bounds, sphere.Centre)))
            .ToArray();
        _root = _ordered.Length == 0 ? null : Build(0, _ordered.Length);
        _dirty = false;
    }

    private Node Build(int start, int count)
    {
        var bounds = _ordered[start].Bounds;
        var centroidMin = _ordered[start].Centroid;
        var centroidMax = centroidMin;
        for (var i = start + 1; i < start + count; i++)
        {
            SupportGenerationMonitor.Check();
            bounds = Union(bounds, _ordered[i].Bounds);
            centroidMin = Vector3.Min(centroidMin, _ordered[i].Centroid);
            centroidMax = Vector3.Max(centroidMax, _ordered[i].Centroid);
        }
        if (count <= LeafSize) return new Node(bounds, start, count, null, null);

        var extent = centroidMax - centroidMin;
        var axis = extent.X >= extent.Y && extent.X >= extent.Z ? 0 : extent.Y >= extent.Z ? 1 : 2;
        Array.Sort(_ordered, start, count, Comparer<Primitive>.Create((a, b) =>
        {
            var comparison = Axis(a.Centroid, axis).CompareTo(Axis(b.Centroid, axis));
            if (comparison != 0) return comparison;
            comparison = a.Kind.CompareTo(b.Kind);
            return comparison != 0 ? comparison : a.Index.CompareTo(b.Index);
        }));
        var leftCount = count / 2;
        return new Node(bounds, 0, 0, Build(start, leftCount), Build(start + leftCount, count - leftCount));
    }

    private bool Intersects(Primitive primitive, Vector3 start, Vector3 end, float radius)
    {
        switch (primitive.Kind)
        {
            case PrimitiveKind.Triangle:
            {
                var triangle = _triangles[primitive.Index];
                return GeometryDistance.SegmentTriangleSquared(start, end,
                    triangle.A, triangle.B, triangle.C) <= radius * radius;
            }
            case PrimitiveKind.Capsule:
            {
                var capsule = _capsules[primitive.Index];
                var sum = radius + capsule.Radius;
                return GeometryDistance.SegmentSegmentSquared(start, end, capsule.Start, capsule.End)
                       <= sum * sum;
            }
            default:
            {
                var sphere = _spheres[primitive.Index];
                var closest = GeometryDistance.ClosestPointOnSegment(sphere.Centre, start, end);
                var sum = radius + sphere.Radius;
                return Vector3.DistanceSquared(closest, sphere.Centre) <= sum * sum;
            }
        }
    }

    private bool Included(Primitive primitive, Func<object?, bool>? obstacleFilter)
    {
        if (obstacleFilter is null) return true;
        var tag = primitive.Kind switch
        {
            PrimitiveKind.Triangle => _triangles[primitive.Index].Tag,
            PrimitiveKind.Capsule => _capsules[primitive.Index].Tag,
            _ => _spheres[primitive.Index].Tag,
        };
        return obstacleFilter(tag);
    }

    private void Consider(Primitive primitive, Vector3 point, ref ObstacleNearestPoint? nearest)
    {
        switch (primitive.Kind)
        {
            case PrimitiveKind.Triangle:
            {
                var triangle = _triangles[primitive.Index];
                var candidate = GeometryDistance.ClosestPointOnTriangle(point,
                    triangle.A, triangle.B, triangle.C);
                Update(candidate, Vector3.Distance(point, candidate), triangle.Tag, ref nearest);
                return;
            }
            case PrimitiveKind.Capsule:
            {
                var capsule = _capsules[primitive.Index];
                var axisPoint = GeometryDistance.ClosestPointOnSegment(point, capsule.Start, capsule.End);
                var delta = point - axisPoint;
                var length = delta.Length();
                var surface = length > 1e-7f
                    ? axisPoint + delta * (capsule.Radius / length)
                    : axisPoint + Vector3.UnitX * capsule.Radius;
                Update(surface, MathF.Max(0, length - capsule.Radius), capsule.Tag, ref nearest);
                return;
            }
            default:
            {
                var sphere = _spheres[primitive.Index];
                var delta = point - sphere.Centre;
                var length = delta.Length();
                var surface = length > 1e-7f
                    ? sphere.Centre + delta * (sphere.Radius / length)
                    : sphere.Centre + Vector3.UnitX * sphere.Radius;
                Update(surface, MathF.Max(0, length - sphere.Radius), sphere.Tag, ref nearest);
                return;
            }
        }
    }

    private static void Update(Vector3 point, float distance, object? tag,
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

    private static Aabb SphereBounds(Vector3 centre, float radius)
    {
        var r = new Vector3(radius);
        return new Aabb(centre - r, centre + r);
    }

    private static Aabb Union(Aabb a, Aabb b) =>
        new(Vector3.Min(a.Min, b.Min), Vector3.Max(a.Max, b.Max));

    private static Vector3 Center(Aabb bounds) => (bounds.Min + bounds.Max) * 0.5f;
    private static float Axis(Vector3 value, int axis) => axis == 0 ? value.X : axis == 1 ? value.Y : value.Z;

    private static bool Overlaps(Aabb a, Aabb b) =>
        a.Min.X <= b.Max.X && a.Max.X >= b.Min.X &&
        a.Min.Y <= b.Max.Y && a.Max.Y >= b.Min.Y &&
        a.Min.Z <= b.Max.Z && a.Max.Z >= b.Min.Z;

    private static float DistanceSquared(Vector3 point, Aabb bounds)
    {
        var closest = Vector3.Clamp(point, bounds.Min, bounds.Max);
        return Vector3.DistanceSquared(point, closest);
    }

    private enum PrimitiveKind { Triangle, Capsule, Sphere }
    private readonly record struct Primitive(PrimitiveKind Kind, int Index, Aabb Bounds, Vector3 Centroid);
    private readonly record struct TriangleObstacle(Vector3 A, Vector3 B, Vector3 C, Aabb Bounds, object? Tag);
    private readonly record struct CapsuleObstacle(Vector3 Start, Vector3 End, float Radius, Aabb Bounds, object? Tag);
    private readonly record struct SphereObstacle(Vector3 Centre, float Radius, Aabb Bounds, object? Tag);

    private sealed record Node(Aabb Bounds, int Start, int Count, Node? Left, Node? Right)
    {
        public bool IsLeaf => Left is null;
    }
}
