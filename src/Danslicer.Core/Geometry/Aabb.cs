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
}
