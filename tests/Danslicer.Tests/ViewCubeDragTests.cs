using Danslicer.Render;

namespace Danslicer.Tests;

/// <summary>
/// Drag-orbit on the view cube. The gesture is where a click turns into a drag, and the camera
/// rotation it produces has to be the viewport's own orbit with a gain — not a second rotation
/// rule with a second pitch clamp. Both halves are testable without a GPU; the feel is not.
/// </summary>
public sealed class ViewCubeDragTests
{
    private const int Size = ViewCube.DefaultSizePixels;

    private static float Degrees(float radians) => radians * 180f / MathF.PI;

    [Fact]
    public void MovementBelowTheThresholdStaysAClick()
    {
        var gesture = ViewCubeDragGesture.Begin(13, 100, 100, Size);

        // Nudges that stay inside the threshold produce no rotation at all, and the gesture is
        // still a click — so an imprecise press on a face still snaps to that face on release.
        Assert.Null(gesture.Move(102, 100));
        Assert.Null(gesture.Move(100, 102.5f));
        Assert.False(gesture.IsDragging);
        Assert.True(gesture.IsClick);
        Assert.Equal(13, gesture.Region);
    }

    [Fact]
    public void MovementPastTheThresholdBecomesADrag()
    {
        var gesture = ViewCubeDragGesture.Begin(13, 100, 100, Size);

        Assert.Null(gesture.Move(100 + ViewCubeDragGesture.ClickThresholdPixels - 0.5f, 100));
        Assert.NotNull(gesture.Move(100 + ViewCubeDragGesture.ClickThresholdPixels + 0.5f, 100));
        Assert.True(gesture.IsDragging);
        Assert.False(gesture.IsClick);

        // Once it is a drag it stays one, even if the pointer comes back to where it started.
        Assert.NotNull(gesture.Move(100, 100));
        Assert.False(gesture.IsClick);
    }

    [Fact]
    public void TheMoveThatCrossesTheThresholdReportsItsWholeTravel()
    {
        var gesture = ViewCubeDragGesture.Begin(13, 0, 0, Size);
        Assert.Null(gesture.Move(3, 0));
        var crossing = gesture.Move(20, 0);
        Assert.NotNull(gesture.Move(20, 0)); // a later no-op move reports zero, not a jump

        // 20px from the press point, not 17px from the last sub-threshold position: the cube
        // picks up where the pointer actually is.
        var expected = 20 * ViewCube.DragDegreesPerPixel(Size) * MathF.PI / 180f
                       / Camera.OrbitRadiansPerPixel;
        Assert.Equal(expected, crossing!.Value.DxPixels, 3);

        // Subsequent moves are incremental.
        var next = gesture.Move(30, 0);
        var expectedStep = 10 * ViewCube.DragDegreesPerPixel(Size) * MathF.PI / 180f
                           / Camera.OrbitRadiansPerPixel;
        Assert.Equal(expectedStep, next!.Value.DxPixels, 3);
    }

    [Fact]
    public void ADragDeltaTurnsTheCameraByTheAdvertisedDegreesPerPixel()
    {
        var camera = new Camera();
        var yaw0 = Degrees(camera.Yaw);
        var pitch0 = Degrees(camera.Pitch);

        var gesture = ViewCubeDragGesture.Begin(13, 0, 0, Size);
        var delta = gesture.Move(40, 20);
        Assert.NotNull(delta);
        camera.Orbit(delta!.Value.DxPixels, delta.Value.DyPixels);

        var rate = ViewCube.DragDegreesPerPixel(Size);
        // Same signs as a viewport drag: right drags the model right (yaw down), down tips the
        // top of the cube toward the viewer (pitch up).
        Assert.Equal(yaw0 - 40 * rate, Degrees(camera.Yaw), 2);
        Assert.Equal(pitch0 + 20 * rate, Degrees(camera.Pitch), 2);
    }

    [Theory]
    [InlineData(4000f)]
    [InlineData(-4000f)]
    public void DraggingIntoAPoleStopsAtTheViewportsOwnClamp(float dy)
    {
        // The clamp belongs to Camera.Orbit; the point of this test is that a cube drag goes
        // through it rather than round it, so the camera cannot flip over the pole.
        var camera = new Camera();
        var gesture = ViewCubeDragGesture.Begin(13, 0, 0, Size);
        var delta = gesture.Move(0, dy)!.Value;
        camera.Orbit(delta.DxPixels, delta.DyPixels);

        Assert.Equal(MathF.Sign(dy) * 89.9f, Degrees(camera.Pitch), 2);

        // A reference viewport orbit of the same magnitude lands in exactly the same place.
        var reference = new Camera();
        reference.Orbit(0, dy * 1000f);
        Assert.Equal(Degrees(reference.Pitch), Degrees(camera.Pitch), 3);
    }

    [Fact]
    public void TheRateFollowsTheCubeSizeAndRespectsItsClamps()
    {
        // Grab-and-turn means a bigger cube gives finer control: the rate is inversely
        // proportional to the on-screen size.
        Assert.Equal(2 * ViewCube.DragDegreesPerPixel(192), ViewCube.DragDegreesPerPixel(96), 3);
        Assert.True(ViewCube.DragDegreesPerPixel(48) > ViewCube.DragDegreesPerPixel(120));

        // Sizes outside the configurable range clamp the same way Rect does, so an out-of-range
        // config cannot produce an absurd rotation rate.
        Assert.Equal(ViewCube.DragDegreesPerPixel(ViewCube.MinSizePixels),
            ViewCube.DragDegreesPerPixel(1), 4);
        Assert.Equal(ViewCube.DragDegreesPerPixel(ViewCube.MaxSizePixels),
            ViewCube.DragDegreesPerPixel(10_000), 4);

        // The documented number for the default cube, so a change to the derivation is visible.
        Assert.Equal(1.42f, ViewCube.DragDegreesPerPixel(), 2);
    }
}
