using Danslicer.App.ViewModels;
using Danslicer.Core.Geometry;
using System.Numerics;

namespace Danslicer.Tests;

public sealed class LayerRangeClipViewModelTests
{
    [Fact]
    public void BoundsTrackUntouchedEndpointsAndKeepInteriorWorldHeights()
    {
        var model = new LayerRangeClipViewModel();
        model.RefreshBounds(new Aabb(new Vector3(0, 0, 2), new Vector3(1, 1, 20)), 50,
            reset: true);
        model.LowerZ = 5;

        model.RefreshBounds(new Aabb(new Vector3(0, 0, 1), new Vector3(1, 1, 30)), 50);

        Assert.Equal(1, model.MinimumZ);
        Assert.Equal(30, model.MaximumZ);
        Assert.Equal(5, model.LowerZ);
        Assert.Equal(30, model.UpperZ);
    }

    [Fact]
    public void NumericEditsClampWithoutCrossingAndResetToFullRange()
    {
        var model = new LayerRangeClipViewModel();
        model.RefreshBounds(new Aabb(new Vector3(0), new Vector3(1, 1, 10)), 50, reset: true);

        model.LowerZ = 4;
        model.UpperZ = 3;
        Assert.Equal(4, model.UpperZ);
        model.Reset();

        Assert.Equal(0, model.LowerZ);
        Assert.Equal(10, model.UpperZ);
        Assert.False(model.Range.IsClipping);
    }

    [Fact]
    public void LeavingSupportModeDisablesClippingWithoutForgettingTheRange()
    {
        var model = new LayerRangeClipViewModel();
        model.RefreshBounds(new Aabb(new Vector3(0), new Vector3(1, 1, 10)), 50, reset: true);
        model.LowerZ = 3;
        model.Active = true;
        Assert.True(model.Range.IsClipping);

        model.Active = false;

        Assert.False(model.Range.IsClipping);
        Assert.Equal(3, model.LowerZ);
    }
}
