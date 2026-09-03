using System.Numerics;

namespace Danslicer.Core.Geometry;

/// <summary>
/// Median-split triangle BVH over a mesh (or a subset of its faces). Queries are exact in the
/// narrow phase; the hierarchy is only a prune. Built once; the mesh is assumed immutable.
/// </summary>
public sealed class TriangleBvh
{
    private const int LeafSize = 8;
    private readonly Mesh _mesh;
    private readonly int[] _triangles;
    private readonly Aabb[] _bounds;
    private readonly Node? _root;

    public Mesh Mesh => _mesh;
    public int TriangleCount => _triangles.Length;
    public Aabb Bounds => _root?.Bounds ?? Aabb.Empty;

    private TriangleBvh(Mesh mesh, int[] triangles, Aabb[] bounds, Node? root)
    {
        _mesh = mesh;
        _triangles = triangles;
        _bounds = bounds;
        _root = root;
    }

    public static TriangleBvh Build(Mesh mesh)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        var indices = new int[mesh.TriangleCount];
        for (int i = 0; i < indices.Length; i++) indices[i] = i;
        return Build(mesh, indices);
    }

    public static TriangleBvh Build(Mesh mesh, IReadOnlyCollection<int> triangles)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(triangles);
        var indices = new int[triangles.Count];
        int n = 0;
        foreach (var t in triangles)
        {
            if ((uint)t >= (uint)mesh.TriangleCount)
                throw new ArgumentOutOfRangeException(nameof(triangles), $"Face {t} is not in the mesh.");
            indices[n++] = t;
        }
        return Build(mesh, indices);
    }

    private static TriangleBvh Build(Mesh mesh, int[] triangles)
    {
        var bounds = new Aabb[triangles.Length];
        for (int i = 0; i < triangles.Length; i++)
        {
            mesh.GetTriangle(triangles[i], out var a, out var b, out var c);
            bounds[i] = TriangleQueries.TriangleBounds(a, b, c);
        }

        Node? root = null;
        if (triangles.Length > 0)
            root = BuildNode(mesh, triangles, bounds, 0, triangles.Length);
        return new TriangleBvh(mesh, triangles, bounds, root);
    }

    private static Node BuildNode(Mesh mesh, int[] triangles, Aabb[] bounds, int start, int count)
    {
        var box = bounds[start];
        var centroidMin = box.Center;
        var centroidMax = centroidMin;
        for (int i = start + 1; i < start + count; i++)
        {
            box = box.Union(bounds[i]);
            var c = bounds[i].Center;
            centroidMin = Vector3.Min(centroidMin, c);
            centroidMax = Vector3.Max(centroidMax, c);
        }
        if (count <= LeafSize) return new Node(box, start, count, null, null);

        var extent = centroidMax - centroidMin;
        var axis = extent.X >= extent.Y && extent.X >= extent.Z ? 0 : extent.Y >= extent.Z ? 1 : 2;
        SortByCentroid(triangles, bounds, start, count, axis);

        var leftCount = count / 2;
        return new Node(box, 0, 0,
            BuildNode(mesh, triangles, bounds, start, leftCount),
            BuildNode(mesh, triangles, bounds, start + leftCount, count - leftCount));
    }

    private static void SortByCentroid(int[] triangles, Aabb[] bounds, int start, int count, int axis)
    {
        var keys = new (float Key, int Triangle, Aabb Bounds)[count];
        for (int i = 0; i < count; i++)
        {
            var c = bounds[start + i].Center;
            var key = axis == 0 ? c.X : axis == 1 ? c.Y : c.Z;
            keys[i] = (key, triangles[start + i], bounds[start + i]);
        }
        Array.Sort(keys, (a, b) =>
        {
            var cmp = a.Key.CompareTo(b.Key);
            return cmp != 0 ? cmp : a.Triangle.CompareTo(b.Triangle);
        });
        for (int i = 0; i < count; i++)
        {
            triangles[start + i] = keys[i].Triangle;
            bounds[start + i] = keys[i].Bounds;
        }
    }

    /// <summary>
    /// Closest hit along the ray. <paramref name="accept"/> filters by mesh triangle index
    /// (region membership, downward faces, …). Returns false on a miss.
    /// </summary>
    public bool RayCast(in Ray ray, out int triangleIndex, out float t, Func<int, bool>? accept = null)
    {
        triangleIndex = -1;
        t = float.PositiveInfinity;
        if (_root is null) return false;

        var stack = new Stack<Node>();
        stack.Push(_root);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            if (!node.Bounds.IntersectsRay(ray, out var tEnter, out var tExit) || tExit < 0) continue;
            if (tEnter >= t) continue;

            if (node.IsLeaf)
            {
                for (int i = node.Start; i < node.Start + node.Count; i++)
                {
                    var tri = _triangles[i];
                    if (accept is not null && !accept(tri)) continue;
                    _mesh.GetTriangle(tri, out var a, out var b, out var c);
                    var hit = ray.IntersectTriangle(a, b, c);
                    if (hit is { } d && d < t)
                    {
                        t = d;
                        triangleIndex = tri;
                    }
                }
                continue;
            }

            stack.Push(node.Right!);
            stack.Push(node.Left!);
        }

        return triangleIndex >= 0;
    }

    public float ClosestPoint(Vector3 point, out Vector3 onMesh, out int triangleIndex, Func<int, bool>? accept = null)
    {
        onMesh = default;
        triangleIndex = -1;
        if (_root is null) return float.PositiveInfinity;

        var best = float.PositiveInfinity;
        var stack = new Stack<Node>();
        stack.Push(_root);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            if (node.Bounds.DistanceSquared(point) >= best) continue;
            if (node.IsLeaf)
            {
                for (int i = node.Start; i < node.Start + node.Count; i++)
                {
                    var tri = _triangles[i];
                    if (accept is not null && !accept(tri)) continue;
                    _mesh.GetTriangle(tri, out var a, out var b, out var c);
                    var q = TriangleQueries.ClosestPointOnTriangle(point, a, b, c);
                    var d2 = Vector3.DistanceSquared(point, q);
                    if (d2 < best)
                    {
                        best = d2;
                        onMesh = q;
                        triangleIndex = tri;
                    }
                }
                continue;
            }

            var leftD = node.Left!.Bounds.DistanceSquared(point);
            var rightD = node.Right!.Bounds.DistanceSquared(point);
            if (leftD <= rightD)
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

        return float.IsPositiveInfinity(best) ? best : MathF.Sqrt(best);
    }

    public float ClosestToSegment(Vector3 p0, Vector3 p1, out Vector3 onSeg, out Vector3 onMesh, out int triangleIndex)
    {
        onSeg = p0;
        onMesh = default;
        triangleIndex = -1;
        if (_root is null) return float.PositiveInfinity;

        var segBox = Aabb.Empty.Include(p0).Include(p1);
        var best = float.PositiveInfinity;
        var stack = new Stack<Node>();
        stack.Push(_root);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            var sep = node.Bounds.Separation(segBox);
            if (sep * sep >= best) continue;
            if (node.IsLeaf)
            {
                for (int i = node.Start; i < node.Start + node.Count; i++)
                {
                    var tri = _triangles[i];
                    _mesh.GetTriangle(tri, out var a, out var b, out var c);
                    TriangleQueries.ClosestSegmentTriangle(p0, p1, a, b, c, out var s, out var m);
                    var d2 = Vector3.DistanceSquared(s, m);
                    if (d2 < best)
                    {
                        best = d2;
                        onSeg = s;
                        onMesh = m;
                        triangleIndex = tri;
                    }
                }
                continue;
            }

            var leftSep = node.Left!.Bounds.Separation(segBox);
            var rightSep = node.Right!.Bounds.Separation(segBox);
            if (leftSep <= rightSep)
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

        return float.IsPositiveInfinity(best) ? best : MathF.Sqrt(best);
    }

    public float ClosestTo(TriangleBvh other, out Vector3 onThis, out Vector3 onOther)
    {
        onThis = default;
        onOther = default;
        if (_root is null || other._root is null) return float.PositiveInfinity;

        var best = float.PositiveInfinity;
        var stack = new Stack<(Node A, Node B)>();
        stack.Push((_root, other._root));
        while (stack.Count > 0)
        {
            var (na, nb) = stack.Pop();
            var sep = na.Bounds.Separation(nb.Bounds);
            if (sep * sep >= best) continue;

            if (na.IsLeaf && nb.IsLeaf)
            {
                for (int i = na.Start; i < na.Start + na.Count; i++)
                {
                    _mesh.GetTriangle(_triangles[i], out var a0, out var b0, out var c0);
                    for (int j = nb.Start; j < nb.Start + nb.Count; j++)
                    {
                        other._mesh.GetTriangle(other._triangles[j], out var a1, out var b1, out var c1);
                        TriangleQueries.ClosestTriangleTriangle(a0, b0, c0, a1, b1, c1, out var p, out var q);
                        var d2 = Vector3.DistanceSquared(p, q);
                        if (d2 < best)
                        {
                            best = d2;
                            onThis = p;
                            onOther = q;
                        }
                    }
                }
                continue;
            }

            if (!na.IsLeaf && (nb.IsLeaf || Volume(na.Bounds) >= Volume(nb.Bounds)))
            {
                stack.Push((na.Right!, nb));
                stack.Push((na.Left!, nb));
            }
            else
            {
                stack.Push((na, nb.Right!));
                stack.Push((na, nb.Left!));
            }
        }

        return float.IsPositiveInfinity(best) ? best : MathF.Sqrt(best);
    }

    /// <summary>
    /// Brute-force closest point over the triangles this BVH holds. Tests use this as the
    /// agreement reference against the hierarchy.
    /// </summary>
    public float BruteForceClosestPoint(Vector3 point, out Vector3 onMesh, out int triangleIndex)
    {
        onMesh = default;
        triangleIndex = -1;
        var best = float.PositiveInfinity;
        for (int i = 0; i < _triangles.Length; i++)
        {
            var tri = _triangles[i];
            _mesh.GetTriangle(tri, out var a, out var b, out var c);
            var q = TriangleQueries.ClosestPointOnTriangle(point, a, b, c);
            var d2 = Vector3.DistanceSquared(point, q);
            if (d2 < best)
            {
                best = d2;
                onMesh = q;
                triangleIndex = tri;
            }
        }
        return float.IsPositiveInfinity(best) ? best : MathF.Sqrt(best);
    }

    public float BruteForceClosestToSegment(Vector3 p0, Vector3 p1, out Vector3 onSeg, out Vector3 onMesh)
    {
        onSeg = p0;
        onMesh = default;
        var best = float.PositiveInfinity;
        for (int i = 0; i < _triangles.Length; i++)
        {
            _mesh.GetTriangle(_triangles[i], out var a, out var b, out var c);
            TriangleQueries.ClosestSegmentTriangle(p0, p1, a, b, c, out var s, out var m);
            var d2 = Vector3.DistanceSquared(s, m);
            if (d2 < best)
            {
                best = d2;
                onSeg = s;
                onMesh = m;
            }
        }
        return float.IsPositiveInfinity(best) ? best : MathF.Sqrt(best);
    }

    public float BruteForceClosestTo(TriangleBvh other, out Vector3 onThis, out Vector3 onOther)
    {
        onThis = default;
        onOther = default;
        var best = float.PositiveInfinity;
        for (int i = 0; i < _triangles.Length; i++)
        {
            _mesh.GetTriangle(_triangles[i], out var a0, out var b0, out var c0);
            for (int j = 0; j < other._triangles.Length; j++)
            {
                other._mesh.GetTriangle(other._triangles[j], out var a1, out var b1, out var c1);
                TriangleQueries.ClosestTriangleTriangle(a0, b0, c0, a1, b1, c1, out var p, out var q);
                var d2 = Vector3.DistanceSquared(p, q);
                if (d2 < best)
                {
                    best = d2;
                    onThis = p;
                    onOther = q;
                }
            }
        }
        return float.IsPositiveInfinity(best) ? best : MathF.Sqrt(best);
    }

    public float BruteForceRayCast(in Ray ray, out int triangleIndex, Func<int, bool>? accept = null)
    {
        triangleIndex = -1;
        float? best = null;
        for (int i = 0; i < _triangles.Length; i++)
        {
            var tri = _triangles[i];
            if (accept is not null && !accept(tri)) continue;
            _mesh.GetTriangle(tri, out var a, out var b, out var c);
            var hit = ray.IntersectTriangle(a, b, c);
            if (hit is { } d && (best is null || d < best))
            {
                best = d;
                triangleIndex = tri;
            }
        }
        return best ?? float.PositiveInfinity;
    }

    private static float Volume(Aabb b)
    {
        var s = b.Size;
        return MathF.Max(0, s.X) * MathF.Max(0, s.Y) * MathF.Max(0, s.Z);
    }

    private sealed record Node(Aabb Bounds, int Start, int Count, Node? Left, Node? Right)
    {
        public bool IsLeaf => Left is null;
    }
}
