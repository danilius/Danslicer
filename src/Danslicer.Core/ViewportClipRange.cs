using System.Numerics;

namespace Danslicer.Core;

/// <summary>
/// Session-only world-Z viewport isolation. A range at its full bounds is deliberately inert so
/// entering Support mode does not alter the established full-scene rendering.
/// </summary>
public readonly record struct ViewportClipRange(
    float MinimumZ,
    float MaximumZ,
    float LowerZ,
    float UpperZ,
    bool Active)
{
    private const float Epsilon = 1e-5f;

    public bool IsClipping => Active && MaximumZ > MinimumZ + Epsilon &&
        (LowerZ > MinimumZ + Epsilon || UpperZ < MaximumZ - Epsilon);

    public bool Contains(Vector3 point) =>
        !IsClipping || point.Z >= LowerZ - Epsilon && point.Z <= UpperZ + Epsilon;

    /// <summary>Clips a member centreline to the visible Z slab for picking and marquee logic.</summary>
    public bool TryClipSegment(Vector3 a, Vector3 b, out Vector3 clippedA, out Vector3 clippedB)
    {
        clippedA = a;
        clippedB = b;
        if (!IsClipping) return true;

        var dz = b.Z - a.Z;
        if (MathF.Abs(dz) < Epsilon) return Contains(a);

        var lowerT = (LowerZ - a.Z) / dz;
        var upperT = (UpperZ - a.Z) / dz;
        var enter = MathF.Max(0f, MathF.Min(lowerT, upperT));
        var leave = MathF.Min(1f, MathF.Max(lowerT, upperT));
        if (enter > leave + Epsilon) return false;

        clippedA = Vector3.Lerp(a, b, Math.Clamp(enter, 0f, 1f));
        clippedB = Vector3.Lerp(a, b, Math.Clamp(leave, 0f, 1f));
        return true;
    }

    public Vector3? VisibleSegmentMidpoint(Vector3 a, Vector3 b) =>
        TryClipSegment(a, b, out var clippedA, out var clippedB)
            ? (clippedA + clippedB) * 0.5f
            : null;
}
