using System.Numerics;
using Danslicer.App.Input;

namespace Danslicer.Tests;

public sealed class SixAxisMotionFilterTests
{
    [Fact]
    public void TiltRampsUpAndConvergesWithoutChangingSteadySensitivity()
    {
        var filter = new SixAxisMotionFilter();
        var input = new SixAxisMotion(Vector3.Zero, new(1, 0, 0));
        var first = filter.Apply(input, 0, 15).Rotation.X;
        Assert.InRange(first, 0.4f, 0.5f);
        var second = filter.Apply(input, 0, 15).Rotation.X;
        Assert.InRange(second, first, 1);
        for (var i = 0; i < 20; i++) filter.Apply(input, 0, 15);
        Assert.Equal(1, filter.Apply(input, 0, 15).Rotation.X, 5);
    }

    [Fact]
    public void SmoothingResponseDependsOnElapsedTimeRatherThanRefreshRate()
    {
        var fast = new SixAxisMotionFilter();
        var slow = new SixAxisMotionFilter();
        var input = new SixAxisMotion(new(0.3f), new(0.8f));
        SixAxisMotion fastOutput = default, slowOutput = default;
        for (var i = 0; i < 12; i++) fastOutput = fast.Apply(input, 0, 100f / 12);
        for (var i = 0; i < 6; i++) slowOutput = slow.Apply(input, 0, 100f / 6);
        Assert.Equal(fastOutput.Rotation.X, slowOutput.Rotation.X, 5);
        Assert.Equal(fastOutput.Translation.X, slowOutput.Translation.X, 5);
    }

    [Fact]
    public void ReleasedAxisStopsImmediatelyWhileOtherAxesContinue()
    {
        var filter = new SixAxisMotionFilter();
        filter.Apply(new(new(1), new(1)), 0.01f, 15);
        var result = filter.Apply(new(new(1, 0, 1), new(0, 1, 0.005f)), 0.01f, 15);
        Assert.Equal(0, result.Translation.Y);
        Assert.Equal(0, result.Rotation.X);
        Assert.Equal(0, result.Rotation.Z);
        Assert.True(result.Rotation.Y > 0);
        Assert.True(filter.Apply(default, 0.01f, 15).IsZero);
    }

    [Fact]
    public void ReversalDoesNotBrieflyContinueInOldDirectionAndResetClearsHistory()
    {
        var filter = new SixAxisMotionFilter();
        var positive = new SixAxisMotion(Vector3.Zero, new(1, 0, 0));
        for (var i = 0; i < 10; i++) filter.Apply(positive, 0, 15);
        Assert.True(filter.Apply(new(Vector3.Zero, new(-0.1f, 0, 0)), 0, 15).Rotation.X < 0);
        filter.Reset();
        Assert.Equal(new SixAxisMotionFilter().Apply(positive, 0, 15), filter.Apply(positive, 0, 15));
    }

    [Fact]
    public void CrossingDeadzoneStartsNearZeroRatherThanJumpingToThreshold()
    {
        var filter = new SixAxisMotionFilter();
        Assert.True(filter.Apply(new(new(0.099f), Vector3.Zero), 0.1f, 15).IsZero);
        var result = filter.Apply(new(new(0.101f), Vector3.Zero), 0.1f, 15);
        Assert.InRange(result.Translation.X, 0.0001f, 0.001f);
    }
}
