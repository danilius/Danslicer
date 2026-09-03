using System.Numerics;
using Danslicer.Core.Geometry;

namespace Danslicer.Core.Supports.Checks;

/// <summary>
/// Brute-force closest-point queries against a world-space mesh. Production proximity
/// checks use <see cref="TriangleBvh"/>; this type stays as the agreement reference in tests.
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
            var q = TriangleQueries.ClosestPointOnTriangle(p, a, b, c);
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
            TriangleQueries.ClosestSegmentTriangle(p0, p1, a, b, c, out var s, out var m);
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
        return float.IsPositiveInfinity(best) ? float.PositiveInfinity : MathF.Sqrt(best);
    }

    public static void ClosestPointsOnSegments(
        Vector3 p1, Vector3 q1, Vector3 p2, Vector3 q2, out Vector3 c1, out Vector3 c2) =>
        TriangleQueries.ClosestPointsOnSegments(p1, q1, p2, q2, out c1, out c2);

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
