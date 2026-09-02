using System.Numerics;

namespace Danslicer.Core.Scene;

/// <summary>
/// Translation, rotation and scale. Matrices follow System.Numerics row-vector convention:
/// a point is transformed as scale, then rotation, then translation.
/// </summary>
public readonly record struct Transform(Vector3 Translation, Quaternion Rotation, Vector3 Scale)
{
    public static Transform Identity => new(Vector3.Zero, Quaternion.Identity, Vector3.One);

    public Matrix4x4 ToMatrix() =>
        Matrix4x4.CreateScale(Scale) *
        Matrix4x4.CreateFromQuaternion(Rotation) *
        Matrix4x4.CreateTranslation(Translation);

    public Transform WithTranslation(Vector3 t) => this with { Translation = t };
    public Transform WithRotation(Quaternion r) => this with { Rotation = r };
    public Transform WithScale(Vector3 s) => this with { Scale = s };

    /// <summary>Euler angles in degrees, applied in X, Y, Z order (Blender XYZ).</summary>
    public Vector3 EulerDegrees
    {
        get => Rotations.ToEulerXyzDegrees(Rotation);
        init => Rotation = Rotations.FromEulerXyzDegrees(value);
    }
}

public static class Rotations
{
    /// <summary>Rotation that applies <paramref name="first"/>, then <paramref name="second"/>.</summary>
    public static Quaternion Compose(Quaternion first, Quaternion second) =>
        Quaternion.Normalize(Quaternion.Concatenate(first, second));

    public static Quaternion FromEulerXyzDegrees(Vector3 degrees)
    {
        var r = degrees * (MathF.PI / 180f);
        var m = Matrix4x4.CreateRotationX(r.X) * Matrix4x4.CreateRotationY(r.Y) * Matrix4x4.CreateRotationZ(r.Z);
        return Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(m));
    }

    public static Vector3 ToEulerXyzDegrees(Quaternion q)
    {
        var m = Matrix4x4.CreateFromQuaternion(Quaternion.Normalize(q));
        // Row-vector matrix M = Rx * Ry * Rz is the transpose of the column-vector R = Rz Ry Rx.
        var sy = Math.Clamp(-m.M13, -1f, 1f);
        var y = MathF.Asin(sy);
        float x, z;
        if (MathF.Abs(sy) < 0.999999f)
        {
            x = MathF.Atan2(m.M23, m.M33);
            z = MathF.Atan2(m.M12, m.M11);
        }
        else
        {
            // Gimbal lock: fold Z into X.
            x = MathF.Atan2(-m.M32, m.M22);
            z = 0;
        }
        return new Vector3(x, y, z) * (180f / MathF.PI);
    }

    public static Quaternion AboutAxis(Vector3 axis, float radians) =>
        Quaternion.CreateFromAxisAngle(Vector3.Normalize(axis), radians);
}
