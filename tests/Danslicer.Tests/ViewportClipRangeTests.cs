using System.Numerics;
using Danslicer.Core;
using Danslicer.Core.Geometry;

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

    [Fact]
    public void MeshPickingSkipsAClippedFrontHitAndFindsVisibleGeometryBehindIt()
    {
        var mesh = new Mesh(
        [
            new(-1, -1, 5), new(1, -1, 5), new(0, 1, 5),
            new(-1, -1, 1), new(1, -1, 1), new(0, 1, 1),
        ],
        [0, 1, 2, 3, 4, 5]);
        var ray = new Ray(new Vector3(0, 0, 10), -Vector3.UnitZ);
        var clip = new ViewportClipRange(0, 5, 0, 2, Active: true);

        var distance = ray.IntersectMesh(mesh, out var triangle,
            point => clip.Contains(point));

        Assert.Equal(9, distance);
        Assert.Equal(1, triangle);
    }
}
