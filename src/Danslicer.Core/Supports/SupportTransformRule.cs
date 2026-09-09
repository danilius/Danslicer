using System.Numerics;
using Danslicer.Core.Scene;

namespace Danslicer.Core.Supports;

/// <summary>
/// When an object moves, do its supports survive?
///
/// <para><b>The rule (user decision D13, refined 2026-09-06):</b> a transform discards the
/// object's supports <i>unless it maps every contact onto an equally valid contact and leaves
/// the support tree standing</i>. Two conditions, and they are one idea: the contact must still
/// lie on the surface with the same normal, and the world must still be the right way up
/// underneath it. Mirror maps exactly and keeps them. Scaling does not, and discards them.</para>
///
/// <para>This is stated as one principle on purpose. It is NOT a list of special cases —
/// "rotate deletes, mirror does not, scale deletes" is the consequence, not the rule, and
/// writing it that way is how the mirror behaviour gets broken by someone tidying up. A
/// contact is a point on the surface plus the surface normal there. A rigid translation
/// carries both to a still-valid contact. A reflection maps every point of the surface onto
/// a point of the reflected surface, so a reflected contact is exactly as valid as the
/// original — which is why <see cref="Document.MirrorSelection"/> reflects support nodes
/// rather than discarding them, and why that behaviour must not be "simplified" away.
/// Scale moves the surface under the contact: the stored point no longer lies where the
/// geometry now is, and the tip would hang in air or bury itself.</para>
///
/// <para>Translation splits on the axis too: a move across the plate carries the tree; a move up
/// or down (user decision 2026-09-09) discards it, because the tree stands on the plate and
/// its trunks are the height they are — see <see cref="SameHeight"/>.</para>
///
/// <para>Rotation splits on the axis, and this is where the second half of the rule earns its
/// keep. A support tree is not just a set of contacts: it stands on the plate, its trunks are
/// vertical, and which faces need supporting at all depends on which way is down. A rotation
/// about the world Z axis leaves every one of those facts intact — contacts, normals, trunks
/// and bases all turn together about the vertical, so the tree that fitted before fits after.
/// It keeps its supports even when the turn carries them off the plate; off-plate is the build
/// volume's complaint to make, not a reason to destroy work. Any rotation that tilts the object
/// changes which way gravity pulls through the tree: trunks would lean, bases would leave the
/// plate at an angle, and faces that needed no support would start to overhang. Those discard.
/// </para>
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
    /// contact point and normal onto an equally valid contact and leaves the tree standing, so
    /// the object's supports may be moved with it rather than discarded. Translation and rotation
    /// about the vertical qualify; scale and any tilt do not.
    /// </summary>
    public static bool MapsContactsExactly(Transform before, Transform after) =>
        SameScale(before.Scale, after.Scale) && KeepsVerticalAxis(before.Rotation, after.Rotation) &&
        SameHeight(before.Translation.Z, after.Translation.Z);

    /// <summary>
    /// A lift or a drop is the one translation that does not leave the tree standing: the
    /// contacts still sit on the surface, but every trunk would need a new length and every
    /// base would leave the plate or sink into it. Those supports are discarded (user decision
    /// 2026-09-09); a move in X and Y carries them along.
    /// </summary>
    private static bool SameHeight(float before, float after) => MathF.Abs(before - after) <= HeightEpsilon;

    /// <summary>A hundredth of a layer: float noise from matrix round-trips, never a move.</summary>
    private const float HeightEpsilon = 5e-4f;

    /// <summary>
    /// The change in orientation, as a world rotation: does it leave the up direction alone? That
    /// is exactly the question "is this a turn about the vertical", asked in the form that also
    /// answers "will the trunks still be vertical afterwards". No rotation at all passes trivially.
    /// </summary>
    private static bool KeepsVerticalAxis(Quaternion before, Quaternion after)
    {
        if (SameRotation(before, after)) return true;
        var delta = Quaternion.Concatenate(Quaternion.Inverse(before), after);
        var up = Vector3.Transform(Vector3.UnitZ, delta);
        return Vector3.Distance(up, Vector3.UnitZ) <= UpEpsilon;
    }

    /// <summary>A looser bound than <see cref="Epsilon"/>: this one measures a transported unit
    /// vector rather than comparing quaternion components, so it absorbs the extra rounding of
    /// the rotation itself. Still far tighter than any tilt a user could produce by hand.</summary>
    private const float UpEpsilon = 1e-4f;

    /// <summary>q and -q are the same rotation, so compare on the absolute dot product.</summary>
    private static bool SameRotation(Quaternion a, Quaternion b) =>
        MathF.Abs(MathF.Abs(Quaternion.Dot(a, b)) - 1f) <= Epsilon;

    private static bool SameScale(Vector3 a, Vector3 b) =>
        MathF.Abs(a.X - b.X) <= Epsilon &&
        MathF.Abs(a.Y - b.Y) <= Epsilon &&
        MathF.Abs(a.Z - b.Z) <= Epsilon;
}
