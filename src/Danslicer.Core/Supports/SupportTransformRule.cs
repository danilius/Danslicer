using System.Numerics;
using Danslicer.Core.Scene;

namespace Danslicer.Core.Supports;

/// <summary>
/// When an object moves, do its supports survive?
///
/// <para><b>The rule (user decision D13):</b> a transform discards the object's supports
/// <i>unless it maps contacts exactly</i>. Mirror maps exactly and keeps them. Rotation and
/// scaling do not, and discard them. Pure translation does not invalidate contacts and keeps
/// them.</para>
///
/// <para>This is stated as one principle on purpose. It is NOT a list of special cases —
/// "rotate deletes, mirror does not, scale deletes" is the consequence, not the rule, and
/// writing it that way is how the mirror behaviour gets broken by someone tidying up. A
/// contact is a point on the surface plus the surface normal there. A rigid translation
/// carries both to a still-valid contact. A reflection maps every point of the surface onto
/// a point of the reflected surface, so a reflected contact is exactly as valid as the
/// original — which is why <see cref="Document.MirrorSelection"/> reflects support nodes
/// rather than discarding them, and why that behaviour must not be "simplified" away.
/// Rotation and non-uniform scale move the surface under the contact: the stored point no
/// longer lies where the geometry now is, and the tip would hang in air or bury itself.</para>
///
/// <para>Mirror does not come through here. It is applied by baking a reflection into new mesh
/// geometry (<see cref="Document.MirrorSelection"/>), so the object's own transform is
/// unchanged and this predicate is never consulted for it. That is deliberate: the reflection
/// path already maps its supports exactly, and it stays the one place that knows how.</para>
/// </summary>
public static class SupportTransformRule
{
    /// <summary>Rotation quaternion components and scale factors closer than this count as equal.
    /// Loose enough to absorb float round-trips through matrix composition, tight enough that a
    /// rotation a user could see is never mistaken for none.</summary>
    private const float Epsilon = 1e-5f;

    /// <summary>
    /// True when moving from <paramref name="before"/> to <paramref name="after"/> carries every
    /// contact point and normal onto an equally valid contact, so the object's supports may be
    /// moved with it rather than discarded. Only a pure translation qualifies: rotation and scale
    /// must both be unchanged.
    /// </summary>
    public static bool MapsContactsExactly(Transform before, Transform after) =>
        SameRotation(before.Rotation, after.Rotation) && SameScale(before.Scale, after.Scale);

    /// <summary>q and -q are the same rotation, so compare on the absolute dot product.</summary>
    private static bool SameRotation(Quaternion a, Quaternion b) =>
        MathF.Abs(MathF.Abs(Quaternion.Dot(a, b)) - 1f) <= Epsilon;

    private static bool SameScale(Vector3 a, Vector3 b) =>
        MathF.Abs(a.X - b.X) <= Epsilon &&
        MathF.Abs(a.Y - b.Y) <= Epsilon &&
        MathF.Abs(a.Z - b.Z) <= Epsilon;
}
