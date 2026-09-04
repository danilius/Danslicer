using System.Numerics;
using Danslicer.App.ViewModels;
using Danslicer.Core;
using Danslicer.Core.Geometry;
using Danslicer.Core.Scene;

namespace Danslicer.Tests;

public sealed class ViewportToolbarTests
{
    [Theory]
    [InlineData(WorkspaceMode.Layout)]
    [InlineData(WorkspaceMode.Slicing)]
    public void NonSupportModesShowOnlyObjects(WorkspaceMode mode)
    {
        Assert.Equal([ViewportTool.Objects], ViewportToolbarPolicy.ToolsFor(mode));
    }

    [Fact]
    public void SupportModeShowsTheCompleteContextualToolSet()
    {
        Assert.Equal(
            [ViewportTool.Objects, ViewportTool.Supports, ViewportTool.Visibility, ViewportTool.Rafts],
            ViewportToolbarPolicy.ToolsFor(WorkspaceMode.Support));
    }

    [Theory]
    [InlineData(WorkspaceMode.Layout, true)]
    [InlineData(WorkspaceMode.Support, false)]
    [InlineData(WorkspaceMode.Slicing, false)]
    public void ObjectSelectionIsOwnedByLayout(WorkspaceMode mode, bool expected) =>
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

    private static Mesh Triangle() => new(
        [Vector3.Zero, Vector3.UnitX, Vector3.UnitY], [0, 1, 2]);
}
