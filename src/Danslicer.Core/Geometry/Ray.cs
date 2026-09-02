using System.Numerics;

namespace Danslicer.Core.Geometry;

public readonly record struct Ray(Vector3 Origin, Vector3 Direction)
{
    public Vector3 At(float t) => Origin + Direction * t;

    public Ray Transform(in Matrix4x4 m) => new(
        Vector3.Transform(Origin, m),
        Vector3.TransformNormal(Direction, m));

    /// <summary>Möller–Trumbore ray-triangle intersection. Returns distance along the ray or null.</summary>
    public float? IntersectTriangle(in Vector3 a, in Vector3 b, in Vector3 c)
    {
        const float epsilon = 1e-7f;
        var e1 = b - a;
        var e2 = c - a;
        var p = Vector3.Cross(Direction, e2);
        var det = Vector3.Dot(e1, p);
        if (MathF.Abs(det) < epsilon) return null;

        var invDet = 1f / det;
        var s = Origin - a;
        var u = Vector3.Dot(s, p) * invDet;
        if (u < 0 || u > 1) return null;

        var q = Vector3.Cross(s, e1);
        var v = Vector3.Dot(Direction, q) * invDet;
        if (v < 0 || u + v > 1) return null;

        var t = Vector3.Dot(e2, q) * invDet;
        return t > epsilon ? t : null;
    }

    /// <summary>Brute-force closest hit against a mesh in the mesh's local space.</summary>
    public float? IntersectMesh(Mesh mesh, out int triangleIndex)
    {
        triangleIndex = -1;
        float? best = null;
        for (int t = 0; t < mesh.TriangleCount; t++)
        {
            mesh.GetTriangle(t, out var a, out var b, out var c);
            var hit = IntersectTriangle(a, b, c);
            if (hit is { } d && (best is null || d < best))
            {
                best = d;
                triangleIndex = t;
            }
        }
        return best;
    }

    /// <summary>Intersects with the plane through <paramref name="point"/> with the given normal.</summary>
    public float? IntersectPlane(in Vector3 point, in Vector3 normal)
    {
        var denom = Vector3.Dot(normal, Direction);
        if (MathF.Abs(denom) < 1e-9f) return null;
        return Vector3.Dot(point - Origin, normal) / denom;
    }

    /// <summary>
    /// Parameter along the line (point + axis * s) closest to this ray. Used for axis-constrained dragging.
    /// </summary>
    public float ClosestParameterOnLine(in Vector3 point, in Vector3 axis)
    {
        // Solve for s minimising distance between ray and line.
        var w0 = Origin - point;
        var a = Vector3.Dot(Direction, Direction);
        var b = Vector3.Dot(Direction, axis);
        var c = Vector3.Dot(axis, axis);
        var d = Vector3.Dot(Direction, w0);
        var e = Vector3.Dot(axis, w0);
        var denom = a * c - b * b;
        if (MathF.Abs(denom) < 1e-9f) return 0; // parallel
        return (a * e - b * d) / denom;
    }
}
