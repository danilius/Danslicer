using System.Numerics;
using Danslicer.App.ViewModels;
using Danslicer.Core;
using Danslicer.Core.Geometry;
using Danslicer.Core.Printers;
using Danslicer.Core.Scene;
using Xunit;

namespace Danslicer.Tests;

/// <summary>File | New: empty the scene, keep the machine, and only ask when there is something
/// to lose.</summary>
public class NewProjectTests
{
    private static Mesh Box(Vector3 min, Vector3 max)
    {
        var p = new[]
        {
            new Vector3(min.X, min.Y, min.Z), new(max.X, min.Y, min.Z),
            new(max.X, max.Y, min.Z), new(min.X, max.Y, min.Z),
            new(min.X, min.Y, max.Z), new(max.X, min.Y, max.Z),
            new(max.X, max.Y, max.Z), new(min.X, max.Y, max.Z),
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
    public void AnEmptyDocumentHasNothingToLose()
    {
        Assert.False(new Document().HasContent);
    }

    [Fact]
    public void AModelOrASupportIsSomethingToLose()
    {
        var document = new Document();
        var box = new SceneObject("box", Box(new(-5, -5, 8), new(5, 5, 14)));
        document.AddObject(box);

        Assert.True(document.HasContent);

        Assert.True(document.AddManualSupport(box, new Vector3(0, 0, 8), -Vector3.UnitZ));
        document.Select(box);
        document.DeleteSelection();

        // The model is gone with its supports, so there is nothing left to warn about.
        Assert.False(document.HasContent);
    }

    [Fact]
    public void ClearEmptiesTheSceneTheSupportsAndTheHistory()
    {
        var document = new Document();
        var box = new SceneObject("box", Box(new(-5, -5, 8), new(5, 5, 14)));
        document.AddObject(box);
        Assert.True(document.AddManualSupport(box, new Vector3(0, 0, 8), -Vector3.UnitZ));

        document.Clear();

        Assert.Empty(document.Scene.Objects);
        Assert.Empty(document.Supports.Nodes);
        Assert.Empty(document.Selection);
        Assert.False(document.Undo()); // nothing to undo past a new project
        Assert.False(document.HasContent);
    }

    [Fact]
    public void ClearKeepsTheMachineSetup()
    {
        // The printer and resin describe the user's rig, not the model they were working on.
        var document = new Document { Printer = PrinterDefinition.PhotonMonoX with { Name = "Rig" } };
        var settings = document.PrintSettings with { LayerHeight = 0.07f };
        document.PrintSettings = settings;
        document.AddObject(new SceneObject("box", Box(new(-5, -5, 8), new(5, 5, 14))));

        document.Clear();

        Assert.Equal("Rig", document.Printer.Name);
        Assert.Equal(settings.LayerHeight, document.PrintSettings.LayerHeight);
    }

    [Fact]
    public void NewProjectResetsTheViewModelToAnUntitledLayout()
    {
        var viewModel = new MainViewModel();
        var box = new SceneObject("box", Box(new(-5, -5, 8), new(5, 5, 14)));
        viewModel.Document.AddObject(box);
        viewModel.SelectedObject = box;
        viewModel.ViewMode = WorkspaceMode.Support;

        viewModel.NewProject();

        Assert.Empty(viewModel.Document.Scene.Objects);
        Assert.Null(viewModel.SelectedObject);
        Assert.Null(viewModel.ProjectPath);
        Assert.Equal(WorkspaceMode.Layout, viewModel.ViewMode);
        Assert.Equal("Danslicer", viewModel.Title);
    }
}
