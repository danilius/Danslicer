using Danslicer.App.ViewModels;
using Danslicer.Core.Geometry;
using Danslicer.Core.Scene;
using Danslicer.Core.Supports;
using System.Numerics;

namespace Danslicer.Tests;

public sealed class LayerRangeClipViewModelTests
{
    [Fact]
    public void TheRangeFollowsTheModelsAsTheyAreMovedInLayout()
    {
        var viewModel = new MainViewModel { RegionBrushRadiusMm = 2 };
        var box = new SceneObject("box", UnitBox());
        viewModel.Document.AddObject(box);
        var before = viewModel.SupportClip.MaximumZ;

        var raised = box.Transform with { Translation = box.Transform.Translation + new Vector3(0, 0, 5) };
        viewModel.Document.CommitTransform(box, box.Transform, raised, "Move", applyPlacement: false);

        Assert.Equal(before + 5, viewModel.SupportClip.MaximumZ, 4);
    }

    [Fact]
    public void SupportsDoNotStretchTheRangeBeyondTheModels()
    {
        // Supports reach the plate and beyond the models; including them made the layer numbers
        // unrelatable to anything the user could point at.
        var viewModel = new MainViewModel();
        var box = new SceneObject("box", UnitBox());
        viewModel.Document.AddObject(box);
        var raised = box.Transform with { Translation = new Vector3(0, 0, 8) };
        viewModel.Document.CommitTransform(box, box.Transform, raised, "Move", applyPlacement: false);
        var top = viewModel.SupportClip.MaximumZ;
        var bottom = viewModel.SupportClip.MinimumZ;

        Assert.True(viewModel.Document.AddManualSupport(box, new Vector3(0, 0, 8), -Vector3.UnitZ));

        Assert.Equal(bottom, viewModel.SupportClip.MinimumZ, 4); // not dragged down to the plate
        Assert.Equal(top, viewModel.SupportClip.MaximumZ, 4);
    }

    [Fact]
    public void AHiddenModelIsNotPartOfTheRange()
    {
        var viewModel = new MainViewModel();
        var low = new SceneObject("low", UnitBox());
        var high = new SceneObject("high", UnitBox())
        {
            Transform = Transform.Identity with { Translation = new Vector3(0, 0, 20) },
        };
        viewModel.Document.AddObject(low);
        viewModel.Document.AddObject(high);
        Assert.Equal(21, viewModel.SupportClip.MaximumZ, 4);

        high.RenderState = RenderState.Hidden;
        viewModel.Document.NotifyTransientChange();

        Assert.Equal(1, viewModel.SupportClip.MaximumZ, 4);
    }

    private static Mesh UnitBox()
    {
        var p = new[]
        {
            new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(1, 1, 0), new Vector3(0, 1, 0),
            new Vector3(0, 0, 1), new Vector3(1, 0, 1), new Vector3(1, 1, 1), new Vector3(0, 1, 1),
        };
        int[] indices =
        [
            0, 3, 2, 0, 2, 1, 4, 5, 6, 4, 6, 7,
            0, 1, 5, 0, 5, 4, 2, 3, 7, 2, 7, 6,
            0, 4, 7, 0, 7, 3, 1, 2, 6, 1, 6, 5,
        ];
        return new Mesh(p, indices);
    }

    [Fact]
    public void TheBoxesReadInLayerNumbersNotMillimetres()
    {
        var model = new LayerRangeClipViewModel { LayerHeightMm = 0.05 };
        model.RefreshBounds(new Aabb(new Vector3(0, 0, 0), new Vector3(1, 1, 64.4f)), 100,
            reset: true);

        // 64.4 mm at 0.05 mm layers is layer 1288, which is what the box should say.
        Assert.Equal("1288", model.UpperField.Text);
        Assert.Equal("0", model.LowerField.Text);
        Assert.Equal(1288, model.UpperLayer);
    }

    [Fact]
    public void TypingALayerNumberMovesThePlaneToThatLayersTop()
    {
        var model = new LayerRangeClipViewModel { LayerHeightMm = 0.05 };
        model.RefreshBounds(new Aabb(new Vector3(0, 0, 0), new Vector3(1, 1, 100)), 100,
            reset: true);

        model.LowerField.Text = "200";

        Assert.Equal(10.0, model.LowerZ, 6); // 200 * 0.05 mm
        Assert.Equal(200, model.LowerLayer);
    }

    [Fact]
    public void TheRangeStartsAtThePlateEvenWhenGeometryHangsBelowIt()
    {
        // Rotating a model can push part of it under the plate. That is the build volume's
        // complaint to make; a clip handle numbered in negative layers is just a broken ruler,
        // so the range starts at layer zero whatever the bounding box says.
        var model = new LayerRangeClipViewModel { LayerHeightMm = 0.05 };
        model.RefreshBounds(new Aabb(new Vector3(0, 0, -21.118f), new Vector3(1, 1, 92.107f)), 100,
            reset: true);

        Assert.Equal(0, model.LowerLayer);
        Assert.Equal(0, model.MinimumZ);
        Assert.Equal(1843, model.UpperLayer);
    }

    [Fact]
    public void LayerNumberingPutsTheFirstPrintedLayerAtOne()
    {
        const double h = 0.05;

        Assert.Equal(0, LayerRangeClipViewModel.LayerAt(0, h));      // the plate itself
        Assert.Equal(1, LayerRangeClipViewModel.LayerAt(h, h));      // top of the first layer
        Assert.Equal(1, LayerRangeClipViewModel.LayerAt(h / 2, h));  // inside the first layer
        Assert.Equal(2, LayerRangeClipViewModel.LayerAt(h * 1.5, h));

        // Round trip: the top of layer n is layer n again.
        foreach (var layer in new[] { 1, 2, 37, 1288 })
            Assert.Equal(layer,
                LayerRangeClipViewModel.LayerAt(LayerRangeClipViewModel.ZOfLayer(layer, h), h));
    }

    [Fact]
    public void ChangingTheLayerHeightRelabelsWithoutMovingThePlanes()
    {
        var model = new LayerRangeClipViewModel { LayerHeightMm = 0.05 };
        model.RefreshBounds(new Aabb(new Vector3(0, 0, 0), new Vector3(1, 1, 10)), 100,
            reset: true);
        Assert.Equal("200", model.UpperField.Text);

        model.LayerHeightMm = 0.1;

        Assert.Equal(10, model.UpperZ); // the plane has not moved
        Assert.Equal("100", model.UpperField.Text);
    }

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
    public void GeneratingSupportsDoesNotMoveTheRange()
    {
        // This test used to assert the opposite: the range stretched down to a support's base at
        // the plate. The user's rule is that the clip range is the models' combined bounding box,
        // so supports appearing or being deleted must leave the numbers where they were.

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

        Assert.Equal(5, viewModel.SupportClip.MinimumZ);
        Assert.Equal(10, viewModel.SupportClip.MaximumZ);
        viewModel.SupportClip.Active = true;
        // A range at both extremes clips nothing, so the trunk running down to the plate below
        // the range is still drawn. Supports vanish only once a handle is actually dragged.
        Assert.False(viewModel.ViewportClipRange.IsClipping);

        viewModel.Document.Supports.RemoveNode(plate.Id);

        Assert.Equal(5, viewModel.SupportClip.MinimumZ);
        Assert.Equal(10, viewModel.SupportClip.MaximumZ);
        Assert.False(viewModel.ViewportClipRange.IsClipping);
    }
}
