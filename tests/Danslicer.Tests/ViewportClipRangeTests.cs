using System.Numerics;
using Danslicer.Core;

namespace Danslicer.Tests;

public sealed class ViewportClipRangeTests
{
    [Fact]
    public void FullRangeIsInertEvenWhenSupportModeIsActive()
    {
        var range = new ViewportClipRange(-2, 20, -2, 20, Active: true);

        Assert.False(range.IsClipping);
        Assert.True(range.Contains(new Vector3(0, 0, -100)));
    }

    [Fact]
    public void InactiveRangeRemembersValuesWithoutFiltering()
    {
        var range = new ViewportClipRange(0, 20, 4, 12, Active: false);

        Assert.False(range.IsClipping);
        Assert.True(range.Contains(new Vector3(0, 0, 18)));
    }

    [Fact]
    public void SegmentIsClippedAtBothPlanes()
    {
        var range = new ViewportClipRange(0, 20, 4, 12, Active: true);

        Assert.True(range.TryClipSegment(new Vector3(2, 3, 1), new Vector3(2, 3, 17),
            out var a, out var b));
        Assert.Equal(new Vector3(2, 3, 4), a);
        Assert.Equal(new Vector3(2, 3, 12), b);
        Assert.Equal(new Vector3(2, 3, 8),
            range.VisibleSegmentMidpoint(new Vector3(2, 3, 1), new Vector3(2, 3, 17)));
    }

    [Fact]
    public void SegmentWhollyOutsideRangeIsRejected()
    {
        var range = new ViewportClipRange(0, 20, 4, 12, Active: true);

        Assert.False(range.TryClipSegment(new Vector3(0, 0, 13), new Vector3(0, 0, 18),
            out _, out _));
        Assert.Null(range.VisibleSegmentMidpoint(new Vector3(0, 0, -3), new Vector3(0, 0, 2)));
    }
}
