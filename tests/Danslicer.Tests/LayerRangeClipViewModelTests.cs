using Danslicer.App.ViewModels;
using Danslicer.Core.Geometry;
using Danslicer.Core.Scene;
using Danslicer.Core.Supports;
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

    [Fact]
    public void MainViewModelRangeTracksVisibleSupportGenerationAndDeletion()
    {
        var viewModel = new MainViewModel();
        viewModel.Document.AddObject(new SceneObject("floating", new Mesh(
            [new(0, 0, 5), new(1, 0, 5), new(0, 1, 10)], [0, 1, 2])));

        Assert.Equal(5, viewModel.SupportClip.MinimumZ);
        Assert.Equal(10, viewModel.SupportClip.MaximumZ);

        var contact = new SupportNode { Type = SupportNodeType.Tip, Position = new(0, 0, 5) };
        var plate = new SupportNode { Type = SupportNodeType.Base, Position = Vector3.Zero };
        viewModel.Document.Supports.AddNode(contact);
        viewModel.Document.Supports.AddNode(plate);
        viewModel.Document.Supports.AddSegment(new SupportSegment
        {
            Type = SupportSegmentType.Trunk,
            NodeA = contact.Id,
            NodeB = plate.Id,
            Diameter = 1,
        });

        Assert.Equal(-0.5, viewModel.SupportClip.MinimumZ, precision: 6);
        Assert.Equal(10, viewModel.SupportClip.MaximumZ);
        viewModel.SupportClip.Active = true;
        Assert.False(viewModel.ViewportClipRange.IsClipping);

        viewModel.Document.Supports.RemoveNode(plate.Id);

        Assert.Equal(5, viewModel.SupportClip.MinimumZ);
        Assert.Equal(10, viewModel.SupportClip.MaximumZ);
        Assert.False(viewModel.ViewportClipRange.IsClipping);
    }
}
