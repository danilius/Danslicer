using System.Linq;
using System.Numerics;
using Danslicer.App.ViewModels;
using Danslicer.Core;
using Danslicer.Core.Geometry;
using Danslicer.Core.Scene;
using Danslicer.Core.Supports;
using Xunit;

namespace Danslicer.Tests;

/// <summary>
/// Support mode targets ONE model: the active object. Every other model on the plate is an
/// obstacle to route around, never a thing to grow supports on.
/// </summary>
public class SupportTargetTests
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
    public void GenerationTagsEveryElementWithTheTargetObject()
    {
        var doc = new Document();
        var a = new SceneObject("a", Box(new(-5, -5, 5), new(5, 5, 15)));
        var b = new SceneObject("b", Box(new(20, -5, 5), new(30, 5, 15)));
        doc.AddObject(a);
        doc.AddObject(b);

        doc.GenerateSupports(a);

        var tips = doc.Supports.Nodes.Where(n => n.Type == SupportNodeType.Tip).ToList();
        Assert.NotEmpty(tips);
        Assert.All(tips, n => Assert.Equal(a.Id, n.Origin.ObjectId));
        // Nothing may contact the other model: b sits entirely beyond x = 20.
        Assert.All(tips, n => Assert.True(n.Position.X < 19f, $"tip at {n.Position} touches the other model"));
    }

    [Fact]
    public void ManualPlacementRefusesAModelThatIsNotTheTarget()
    {
        var doc = new Document();
        var a = new SceneObject("a", Box(new(-5, -5, 5), new(5, 5, 15)));
        var b = new SceneObject("b", Box(new(20, -5, 5), new(30, 5, 15)));
        doc.AddObject(a);
        doc.AddObject(b);
        doc.Select(a);

        var placed = doc.AddManualSupport(b, new Vector3(25, 0, 5), new Vector3(0, 0, -1), out var reason);

        Assert.False(placed);
        Assert.Null(reason); // Not a routing failure: the click never reached the router.
        Assert.Empty(doc.Supports.Nodes);
    }

    [Fact]
    public void ManualPlacementOnTheTargetStillWorks()
    {
        var doc = new Document();
        var a = new SceneObject("a", Box(new(-5, -5, 5), new(5, 5, 15)));
        var b = new SceneObject("b", Box(new(20, -5, 5), new(30, 5, 15)));
        doc.AddObject(a);
        doc.AddObject(b);
        doc.Select(a);

        Assert.True(doc.AddManualSupport(a, new Vector3(0, 0, 5), new Vector3(0, 0, -1), out _));
        Assert.NotEmpty(doc.Supports.Nodes);
    }

    [Fact]
    public void NoTargetMeansNoRestriction()
    {
        // Single-model documents, the CLI and every older test never set a target. They must
        // keep placing supports exactly as before.
        var doc = new Document();
        var a = new SceneObject("a", Box(new(-5, -5, 5), new(5, 5, 15)));
        doc.AddObject(a);
        doc.ClearSelection(); // AddObject selects what it adds; this is the no-target case.

        Assert.Null(doc.SupportTarget);
        Assert.True(doc.AddManualSupport(a, new Vector3(0, 0, 5), new Vector3(0, 0, -1), out _));
    }

    [Fact]
    public void MultiSelectionIsNotATarget()
    {
        var doc = new Document();
        var a = new SceneObject("a", Box(new(-5, -5, 5), new(5, 5, 15)));
        var b = new SceneObject("b", Box(new(20, -5, 5), new(30, 5, 15)));
        doc.AddObject(a);
        doc.AddObject(b);
        doc.Select(a);
        doc.Select(b, additive: true);

        Assert.Null(doc.SupportTarget);
    }

    [Fact]
    public void TheRefusalNamesBothModels()
    {
        var a = new SceneObject("dragon", Box(new(-5, -5, 5), new(5, 5, 15)));
        var b = new SceneObject("gripper", Box(new(20, -5, 5), new(30, 5, 15)));

        Assert.Null(SupportTargetPolicy.RefusalMessage(a, a));
        Assert.Null(SupportTargetPolicy.RefusalMessage(null, b));
        var message = SupportTargetPolicy.RefusalMessage(a, b);
        Assert.NotNull(message);
        Assert.Contains("gripper", message);
        Assert.Contains("dragon", message);
    }

    [Fact]
    public void SupportModeKeepsTheActiveObjectSelectedAsTheTarget()
    {
        var viewModel = new MainViewModel();
        var a = new SceneObject("a", Box(new(-5, -5, 5), new(5, 5, 15)));
        var b = new SceneObject("b", Box(new(20, -5, 5), new(30, 5, 15)));
        viewModel.Document.AddObject(a);
        viewModel.Document.AddObject(b);
        viewModel.SelectedObject = a;

        viewModel.ViewMode = WorkspaceMode.Support;

        // The colour both render paths draw comes from IsSelected, so asserting selection here
        // is asserting that the target reads as selected in the viewport.
        Assert.Same(a, viewModel.Document.SupportTarget);
        Assert.True(viewModel.Document.IsSelected(a));
        Assert.False(viewModel.Document.IsSelected(b));

        // Changing the active object in the Objects list retargets without leaving Support mode.
        viewModel.SelectedObject = b;
        Assert.Same(b, viewModel.Document.SupportTarget);
        Assert.True(viewModel.Document.IsSelected(b));
        Assert.False(viewModel.Document.IsSelected(a));
    }

    [Fact]
    public void ObjectSelectionIsEnabledInSupportModeAndNotInSlicing()
    {
        Assert.True(ViewportToolbarPolicy.CanSelectObjects(WorkspaceMode.Layout));
        Assert.True(ViewportToolbarPolicy.CanSelectObjects(WorkspaceMode.Support));
        Assert.False(ViewportToolbarPolicy.CanSelectObjects(WorkspaceMode.Slicing));
    }

    [Fact]
    public void SelectAllTakesTheTargetsSupportsAndNotTheOtherModels()
    {
        // On a crowded plate, select-all reaching another model's supports is how a delete goes
        // wrong. Select-all in Support mode means "all of THIS model's supports".
        var doc = new Document();
        var a = new SceneObject("a", Box(new(-5, -5, 5), new(5, 5, 15)));
        var b = new SceneObject("b", Box(new(20, -5, 5), new(30, 5, 15)));
        doc.AddObject(a);
        doc.AddObject(b);
        doc.GenerateSupports(a);
        doc.GenerateSupports(b);
        doc.Select(a);

        doc.SelectAllSupportElements();

        Assert.NotEmpty(doc.SupportSelection);
        Assert.All(doc.SupportSelection,
            id => Assert.Equal(a.Id, doc.Supports.OwningObjectId(id)));
    }

    [Fact]
    public void WithNoTargetSelectAllStillTakesEverything()
    {
        var doc = new Document();
        var a = new SceneObject("a", Box(new(-5, -5, 5), new(5, 5, 15)));
        var b = new SceneObject("b", Box(new(20, -5, 5), new(30, 5, 15)));
        doc.AddObject(a);
        doc.AddObject(b);
        doc.GenerateSupports(a);
        doc.GenerateSupports(b);
        doc.ClearSelection();

        doc.SelectAllSupportElements();

        var owners = doc.SupportSelection
            .Select(id => doc.Supports.OwningObjectId(id))
            .Distinct()
            .ToList();
        Assert.Equal(2, owners.Count);
    }

    [Fact]
    public void AnotherModelsSupportsAreUnselectable()
    {
        // The target is the one model being worked on: its neighbour's supports are as inert as
        // its surface, whether reached by a click, a marquee, or a whole-support double-click.
        var doc = new Document();
        var a = new SceneObject("a", Box(new(-5, -5, 5), new(5, 5, 15)));
        var b = new SceneObject("b", Box(new(20, -5, 5), new(30, 5, 15)));
        doc.AddObject(a);
        doc.AddObject(b);
        doc.GenerateSupports(a);
        doc.GenerateSupports(b);
        doc.Select(a);

        var foreign = doc.Supports.Nodes.First(n => n.Origin.ObjectId == b.Id).Id;
        var mine = doc.Supports.Nodes.First(n => n.Origin.ObjectId == a.Id).Id;

        doc.SelectSupportElement(foreign);
        Assert.Empty(doc.SupportSelection);

        doc.SelectSupportComponent(foreign);
        Assert.Empty(doc.SupportSelection);

        doc.SelectSupportElements([mine, foreign]);
        Assert.Equal([mine], doc.SupportSelection);

        // Nor may an additive marquee sweep one in alongside the target's own.
        doc.SelectSupportElement(foreign, additive: true);
        Assert.Equal([mine], doc.SupportSelection);
    }

    [Fact]
    public void WithNoTargetEverySupportStaysSelectable()
    {
        var doc = new Document();
        var a = new SceneObject("a", Box(new(-5, -5, 5), new(5, 5, 15)));
        var b = new SceneObject("b", Box(new(20, -5, 5), new(30, 5, 15)));
        doc.AddObject(a);
        doc.AddObject(b);
        doc.GenerateSupports(a);
        doc.GenerateSupports(b);
        doc.ClearSelection();

        var foreign = doc.Supports.Nodes.First(n => n.Origin.ObjectId == b.Id).Id;
        doc.SelectSupportElement(foreign);

        Assert.Equal([foreign], doc.SupportSelection);
    }

    [Fact]
    public void SelectAllInSupportModeKeepsTheTargetSelected()
    {
        // Support mode's object selection IS the target, so select-all takes the supports and
        // leaves the model selected; dropping it here used to un-scope the very same call.
        var doc = new Document();
        var a = new SceneObject("a", Box(new(-5, -5, 5), new(5, 5, 15)));
        var b = new SceneObject("b", Box(new(20, -5, 5), new(30, 5, 15)));
        doc.AddObject(a);
        doc.AddObject(b);
        doc.GenerateSupports(a);
        doc.GenerateSupports(b);
        doc.Select(a);

        WorkspaceSelection.SelectAll(doc, WorkspaceMode.Support);

        Assert.Same(a, doc.SupportTarget);
        Assert.True(doc.IsSelected(a));
        Assert.NotEmpty(doc.SupportSelection);
        Assert.All(doc.SupportSelection,
            id => Assert.Equal(a.Id, doc.Supports.OwningObjectId(id)));
    }

    [Fact]
    public void ViewModelSelectAllInSupportModeKeepsTheTargetSelected()
    {
        var viewModel = new MainViewModel();
        var a = new SceneObject("a", Box(new(-5, -5, 5), new(5, 5, 15)));
        viewModel.Document.AddObject(a);
        viewModel.SelectedObject = a;
        viewModel.ViewMode = WorkspaceMode.Support;
        viewModel.Document.GenerateSupports(a);

        viewModel.SelectAllCommand.Execute(null);

        Assert.Same(a, viewModel.SelectedObject);
        Assert.Same(a, viewModel.Document.SupportTarget);
        Assert.True(viewModel.Document.IsSelected(a));
    }

    [Fact]
    public void GenerationTargetsTheActiveObjectOnly()
    {
        var doc = new Document();
        var a = new SceneObject("a", Box(new(-5, -5, 5), new(5, 5, 15)));
        var b = new SceneObject("b", Box(new(20, -5, 5), new(30, 5, 15)));
        doc.AddObject(a);
        doc.AddObject(b);

        doc.GenerateSupports(b);

        var tips = doc.Supports.Nodes.Where(n => n.Type == SupportNodeType.Tip).ToList();
        Assert.NotEmpty(tips);
        Assert.All(tips, n => Assert.Equal(b.Id, n.Origin.ObjectId));
        Assert.All(tips, n => Assert.True(n.Position.X > 19f, $"tip at {n.Position} is on the other model"));
    }
}
