using Danslicer.App.ViewModels;
using Danslicer.Core.Geometry;
using System.Numerics;

namespace Danslicer.Tests;

public sealed class IsolationHeightEditorTests
{
    [Fact]
    public void LayerAndMillimetreFieldsShareTheClampedRange()
    {
        var clip = new LayerRangeClipViewModel();
        clip.RefreshBounds(new Aabb(Vector3.Zero, new Vector3(20)), 20);
        clip.LowerMmField.Text = "0.2cm";
        Assert.Equal(2, clip.LowerZ);
        Assert.Equal("40", clip.LowerField.Text);
        clip.UpperField.Text = "100";
        Assert.Equal(5, clip.UpperZ);
        Assert.Equal("5", clip.UpperMmField.Text);
        clip.LowerMmField.Text = "999";
        Assert.Equal(5, clip.LowerZ);
        Assert.Equal("5", clip.LowerMmField.Text);
        clip.UpperMmField.Text = "-10";
        Assert.Equal(clip.LowerZ, clip.UpperZ);
        clip.Reset();
        Assert.Equal("0", clip.LowerMmField.Text);
        Assert.Equal("20", clip.UpperMmField.Text);
    }

    [Fact]
    public void LayerThicknessRelabelsWithoutMovingMmHeightAndInvalidTextDoesNotMoveRange()
    {
        var clip = new LayerRangeClipViewModel();
        clip.RefreshBounds(new Aabb(Vector3.Zero, new Vector3(20)), 20);
        clip.UpperMmField.Text = "2.125";
        var thicknessNotified = false;
        clip.PropertyChanged += (_, e) => thicknessNotified |= e.PropertyName == nameof(clip.LayerHeightMm);
        clip.LayerHeightMm = 0.025;
        Assert.True(thicknessNotified);
        Assert.Equal("85", clip.UpperField.Text);
        Assert.Equal("2.125", clip.UpperMmField.Text);
        clip.UpperMmField.Text = "invalid";
        Assert.Equal(2.125, clip.UpperZ);
        Assert.Equal("2.125", clip.UpperMmField.Text);
    }
}
