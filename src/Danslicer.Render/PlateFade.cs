namespace Danslicer.Render;

/// <summary>
/// How solid the build plate draws for a given view.
///
/// The plate is a reference surface while you look down on it, and an occluder as soon as you look
/// along it: near grazing it spreads across the lower screen and hides whatever lies beyond, which
/// from under a supported model is the supports themselves. So the fade is a question about the
/// view DIRECTION, not about where the eye happens to sit. Asking where the eye sits was the old
/// rule and it broke on approach: the camera orbits its target, the target rides up on the model,
/// so zooming in shortens the arm and lifts the eye back over the plate — the plate turned solid
/// again in one step while the view was still looking up into the model's underside.
/// </summary>
public static class PlateFade
{
    /// <summary>Production surface visibility: smooth in angle and eye height, always zero below.</summary>
    public static float SurfaceOpacityFor(Camera camera)
    {
        ArgumentNullException.ThrowIfNull(camera);
        var angle = Math.Clamp(-camera.ViewDirection.Z / SolidSine, 0, 1);
        var height = Math.Clamp(camera.Eye.Z / 2f, 0, 1);
        return angle * angle * (3 - 2 * angle) * height * height * (3 - 2 * height);
    }

    /// <summary>Shadows ease out before the surface starts fading, avoiding a 12-degree pop.</summary>
    public static float ShadowStrengthFor(Camera camera)
    {
        var t = Math.Clamp((-camera.ViewDirection.Z - SolidSine) / SolidSine, 0, 1);
        return t * t * (3 - 2 * t);
    }

    /// <summary>Looking down on the plate by more than this, it is a floor and draws solid.</summary>
    public const float SolidAngleDegrees = 12f;

    private static readonly float SolidSine =
        MathF.Sin(SolidAngleDegrees * MathF.PI / 180f);

    /// <summary>
    /// Plate opacity for this view, between <paramref name="opacityFromBelow"/> and 1.
    /// <paramref name="opacityFromBelow"/> of 1 disables the fade entirely, as the config intends.
    /// </summary>
    public static float OpacityFor(Camera camera, float opacityFromBelow)
    {
        ArgumentNullException.ThrowIfNull(camera);
        if (opacityFromBelow >= 1f) return 1f;

        // Under the plate there is nothing to argue about, whatever the view direction does.
        if (camera.Eye.Z <= 0f) return opacityFromBelow;

        // -ViewDirection.Z is the sine of the angle the view makes with the plate plane: positive
        // looking down onto it, zero edge-on, negative looking up from beneath.
        var lookDown = -camera.ViewDirection.Z;
        var t = Math.Clamp(lookDown / SolidSine, 0f, 1f);
        var smooth = t * t * (3f - 2f * t); // smoothstep, so the plate never pops
        return opacityFromBelow + (1f - opacityFromBelow) * smooth;
    }
}
