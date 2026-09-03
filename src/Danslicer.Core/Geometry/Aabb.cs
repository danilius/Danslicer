using System.Numerics;

namespace Danslicer.Core.Geometry;

/// <summary>Axis-aligned bounding box. An empty box has Min > Max.</summary>
public readonly record struct Aabb(Vector3 Min, Vector3 Max)
{
    public static Aabb Empty => new(
        new Vector3(float.PositiveInfinity),
        new Vector3(float.NegativeInfinity));

    public bool IsEmpty => Min.X > Max.X || Min.Y > Max.Y || Min.Z > Max.Z;
    public Vector3 Center => (Min + Max) * 0.5f;
    public Vector3 Size => Max - Min;
    public float Radius => IsEmpty ? 0 : Size.Length() * 0.5f;

    public Aabb Include(Vector3 p) => new(Vector3.Min(Min, p), Vector3.Max(Max, p));

    public Aabb Union(Aabb other)
    {
        if (IsEmpty) return other;
        if (other.IsEmpty) return this;
        return new Aabb(Vector3.Min(Min, other.Min), Vector3.Max(Max, other.Max));
    }

    public static Aabb FromPoints(ReadOnlySpan<Vector3> points)
    {
        var min = new Vector3(float.PositiveInfinity);
        var max = new Vector3(float.NegativeInfinity);
        foreach (var p in points)
        {
            min = Vector3.Min(min, p);
            max = Vector3.Max(max, p);
        }
        return new Aabb(min, max);
    }

    /// <summary>Transforms all eight corners and returns their bounding box.</summary>
    public Aabb Transform(in Matrix4x4 m)
    {
        if (IsEmpty) return this;
        var result = Empty;
        for (int i = 0; i < 8; i++)
        {
            var corner = new Vector3(
                (i & 1) == 0 ? Min.X : Max.X,
                (i & 2) == 0 ? Min.Y : Max.Y,
                (i & 4) == 0 ? Min.Z : Max.Z);
            result = result.Include(Vector3.Transform(corner, m));
        }
        return result;
    }

    public bool Overlaps(in Aabb other) =>
        !IsEmpty && !other.IsEmpty &&
        Min.X <= other.Max.X && Max.X >= other.Min.X &&
        Min.Y <= other.Max.Y && Max.Y >= other.Min.Y &&
        Min.Z <= other.Max.Z && Max.Z >= other.Min.Z;

    /// <summary>Euclidean distance from a point to the box (zero if inside).</summary>
    public float DistanceSquared(Vector3 point)
    {
        var closest = Vector3.Clamp(point, Min, Max);
        return Vector3.DistanceSquared(point, closest);
    }

    /// <summary>Euclidean gap between two boxes (zero if they overlap).</summary>
    public float Separation(in Aabb other)
    {
        var dx = MathF.Max(0, MathF.Max(Min.X - other.Max.X, other.Min.X - Max.X));
        var dy = MathF.Max(0, MathF.Max(Min.Y - other.Max.Y, other.Min.Y - Max.Y));
        var dz = MathF.Max(0, MathF.Max(Min.Z - other.Max.Z, other.Min.Z - Max.Z));
        return MathF.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    /// <summary>
    /// Slab test. <paramref name="tEnter"/> is clamped to 0 so hits behind the origin are ignored.
    /// </summary>
    public bool IntersectsRay(in Ray ray, out float tEnter, out float tExit)
    {
        tEnter = 0f;
        tExit = float.PositiveInfinity;
        if (IsEmpty) return false;
        if (!Slab(ray.Origin.X, ray.Direction.X, Min.X, Max.X, ref tEnter, ref tExit)) return false;
        if (!Slab(ray.Origin.Y, ray.Direction.Y, Min.Y, Max.Y, ref tEnter, ref tExit)) return false;
        if (!Slab(ray.Origin.Z, ray.Direction.Z, Min.Z, Max.Z, ref tEnter, ref tExit)) return false;
        return tExit >= tEnter;
    }

    private static bool Slab(float origin, float dir, float min, float max, ref float tEnter, ref float tExit)
    {
        const float epsilon = 1e-12f;
        if (MathF.Abs(dir) < epsilon)
            return origin >= min && origin <= max;

        var inv = 1f / dir;
        var t0 = (min - origin) * inv;
        var t1 = (max - origin) * inv;
        if (t0 > t1) (t0, t1) = (t1, t0);
        tEnter = MathF.Max(tEnter, t0);
        tExit = MathF.Min(tExit, t1);
        return tEnter <= tExit;
    }
}
