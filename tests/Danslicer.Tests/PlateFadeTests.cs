using System;
using System.Numerics;
using Danslicer.Render;
using Xunit;

namespace Danslicer.Tests;

/// <summary>
/// The plate fade is a question about the view direction, not the eye's height. The regression
/// these pin: orbiting a target that sits up on the model, zooming in shortens the arm and lifts
/// the eye back above z = 0, so an eye-height rule turned the plate solid mid-approach while the
/// view was still looking up into the model's underside.
/// </summary>
public class PlateFadeTests
{
    private const float FromBelow = 0.3f;

    private static Camera Looking(float pitchDegrees, float distance = 400f, float targetZ = 30f)
        => new()
        {
            Target = new Vector3(0, 0, targetZ),
            Distance = distance,
            Pitch = pitchDegrees * MathF.PI / 180f,
        };

    [Fact]
    public void LookingDownOnThePlate_ItDrawsSolid()
        => Assert.Equal(1f, PlateFade.OpacityFor(Looking(30f), FromBelow));

    [Fact]
    public void LookingUpFromUnderThePlate_ItFadesFully()
        => Assert.Equal(FromBelow, PlateFade.OpacityFor(Looking(-20f), FromBelow));

    [Fact]
    public void EdgeOn_ItFadesFully()
        => Assert.Equal(FromBelow, PlateFade.OpacityFor(Looking(0f), FromBelow));

    [Fact]
    public void ZoomingInOnTheModelFromBelow_DoesNotBringThePlateBack()
    {
        // Pitch -10 with a 400 mm arm puts the eye under the plate; at 50 mm the same view has the
        // eye back above it. The old rule flipped to solid here. The view has not changed.
        var far = Looking(-10f, distance: 400f);
        var near = Looking(-10f, distance: 50f);
        Assert.True(far.Eye.Z < 0f);
        Assert.True(near.Eye.Z > 0f);
        Assert.Equal(PlateFade.OpacityFor(far, FromBelow), PlateFade.OpacityFor(near, FromBelow));
    }

    [Fact]
    public void TheRampIsMonotonicAndHasNoStep()
    {
        var previous = PlateFade.OpacityFor(Looking(-5f), FromBelow);
        for (var pitch = -5f; pitch <= 25f; pitch += 0.25f)
        {
            var opacity = PlateFade.OpacityFor(Looking(pitch), FromBelow);
            Assert.InRange(opacity, previous - 1e-5f, previous + 0.05f);
            previous = opacity;
        }
        Assert.Equal(1f, previous, 3);
    }

    [Fact]
    public void PastTheSolidAngle_ItIsSolidAgain()
        => Assert.Equal(1f, PlateFade.OpacityFor(Looking(PlateFade.SolidAngleDegrees + 0.5f), FromBelow), 3);

    [Fact]
    public void AnOpacityOfOne_DisablesTheFadeEverywhere()
        => Assert.Equal(1f, PlateFade.OpacityFor(Looking(-45f), 1f));

    [Fact]
    public void AnEyeUnderThePlate_FadesEvenLookingUpAtATargetBelowIt()
    {
        // A model dragged under the plate puts the target below zero; pitch is then positive while
        // the eye is still underneath. Being under the plate settles it on its own.
        var camera = Looking(20f, distance: 100f, targetZ: -80f);
        Assert.True(camera.Eye.Z < 0f);
        Assert.Equal(FromBelow, PlateFade.OpacityFor(camera, FromBelow));
    }
}
