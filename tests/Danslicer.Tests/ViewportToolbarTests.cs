using System.Numerics;
using Danslicer.App.ViewModels;
using Danslicer.Core;
using Danslicer.Core.Geometry;
using Danslicer.Core.Scene;
using Danslicer.Core.Supports.Generation;

namespace Danslicer.Tests;

public sealed class ViewportToolbarTests
{
    [Fact]
    public void LayoutModeShowsOnlyObjects()
    {
        Assert.Equal([ViewportTool.Objects],
            ViewportToolbarPolicy.ToolsFor(WorkspaceMode.Layout));
    }

    [Fact]
    public void SlicingModeAddsUvtoolsCheck()
    {
        Assert.Equal([ViewportTool.Objects, ViewportTool.UvtoolsCheck],
            ViewportToolbarPolicy.ToolsFor(WorkspaceMode.Slicing));
    }

    [Fact]
    public void SupportModeShowsTheCompleteContextualToolSet()
    {
        Assert.Equal(
            [ViewportTool.Objects, ViewportTool.Supports, ViewportTool.IslandSupport,
                ViewportTool.IslandDetection, ViewportTool.Visibility, ViewportTool.Rafts],
            ViewportToolbarPolicy.ToolsFor(WorkspaceMode.Support));
    }

    // Support mode gained object selection: the selected model is the support target, so the
    // list must be live there too. Slicing still has nothing to do with an object selection.
    [Theory]
    [InlineData(WorkspaceMode.Layout, true)]
    [InlineData(WorkspaceMode.Support, true)]
    [InlineData(WorkspaceMode.Slicing, false)]
    public void ObjectSelectionIsForTheModesWithObjectWork(WorkspaceMode mode, bool expected) =>
        Assert.Equal(expected, ViewportToolbarPolicy.CanSelectObjects(mode));

    [Theory]
    [InlineData(ViewportPopupCloseTrigger.HeaderButton, true)]
    [InlineData(ViewportPopupCloseTrigger.Escape, true)]
    [InlineData(ViewportPopupCloseTrigger.OutsidePointer, false)]
    [InlineData(ViewportPopupCloseTrigger.ContentAction, false)]
    public void PopupClosePolicyKeepsToolsPinnedUntilExplicitlyClosed(
        ViewportPopupCloseTrigger trigger, bool expected) =>
        Assert.Equal(expected, ViewportToolbarPolicy.ShouldClosePopup(trigger));

    [Fact]
    public void SupportPopupOpenStateSurvivesModeRoundTrip()
    {
        var popup = new ViewportPopupState(ViewportTool.Supports);

        popup.Toggle();

        Assert.True(popup.IsVisible(WorkspaceMode.Support));
        Assert.False(popup.IsVisible(WorkspaceMode.Layout));
        Assert.True(popup.IsOpen);
        Assert.True(popup.IsVisible(WorkspaceMode.Support));
    }

    [Fact]
    public void ClosedPopupStaysClosedAcrossModeRoundTrip()
    {
        var popup = new ViewportPopupState(ViewportTool.Supports);
        popup.Toggle();
        popup.Close(ViewportPopupCloseTrigger.HeaderButton);

        Assert.False(popup.IsVisible(WorkspaceMode.Layout));
        Assert.False(popup.IsVisible(WorkspaceMode.Support));
        Assert.False(popup.IsOpen);
    }

    [Theory]
    [InlineData(WorkspaceMode.Layout)]
    [InlineData(WorkspaceMode.Support)]
    [InlineData(WorkspaceMode.Slicing)]
    public void AllModePopupRemainsVisibleWhenOpen(WorkspaceMode mode)
    {
        var popup = new ViewportPopupState(ViewportTool.Objects);
        popup.Toggle();

        Assert.True(popup.IsVisible(mode));
    }

    [Fact]
    public void MainViewModelObjectListTracksTheDocumentAndSelectionBothWays()
    {
        var viewModel = new MainViewModel();
        var first = new SceneObject("first", Triangle());
        var second = new SceneObject("second", Triangle());

        viewModel.Document.AddObject(first);
        viewModel.Document.AddObject(second);

        Assert.Equal([first, second], viewModel.Objects);

        viewModel.SelectedObject = second;
        Assert.Equal([second], viewModel.Document.Selection);

        viewModel.Document.Select(first);
        Assert.Same(first, viewModel.SelectedObject);

        viewModel.Document.DeleteSelection();
        Assert.Equal([second], viewModel.Objects);
    }

    [Fact]
    public void GeometryChangesInvalidateIslandMarkers()
    {
        var viewModel = new MainViewModel
        {
            DetectedIslands = [new DetectedIsland(new Vector3(1, 2, 3), 0.8f, 4)],
        };

        viewModel.Document.NotifyTransientChange();

        Assert.Empty(viewModel.DetectedIslands);
    }

    private static Mesh Triangle() => new(
        [Vector3.Zero, Vector3.UnitX, Vector3.UnitY], [0, 1, 2]);
}
