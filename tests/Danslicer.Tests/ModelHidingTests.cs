using System.Numerics;
using Danslicer.App.ViewModels;
using Danslicer.Core;
using Danslicer.Core.Geometry;
using Danslicer.Core.IO;
using Danslicer.Core.Printers;
using Danslicer.Core.Scene;
using Danslicer.Core.Slicing;
using Danslicer.Core.Supports;
using Xunit;

namespace Danslicer.Tests;

/// <summary>
/// Hiding a model takes it out of the viewport AND out of the print, and takes its supports with
/// it. The print half is the one that matters: resin holding up something that is not there is
/// worse than nothing.
/// </summary>
public class ModelHidingTests
{
    private static readonly PrinterDefinition Printer = PrinterDefinition.PhotonMonoX;

    private static Mesh Box(float x, float y, float z)
    {
        var p = new[]
        {
            new Vector3(0, 0, 0), new(x, 0, 0), new(x, y, 0), new(0, y, 0),
            new(0, 0, z), new(x, 0, z), new(x, y, z), new(0, y, z),
        };
        int[] indices =
        [
            0, 3, 2, 0, 2, 1, 4, 5, 6, 4, 6, 7,
            0, 1, 5, 0, 5, 4, 2, 3, 7, 2, 7, 6,
            0, 4, 7, 0, 7, 3, 1, 2, 6, 1, 6, 5,
        ];
        return new Mesh(p, indices);
    }

    private static SceneObject At(string name, Vector3 translation, float height = 2.5f)
    {
        var obj = new SceneObject(name, Box(10, 10, height));
        obj.Transform = Transform.Identity with { Translation = translation };
        return obj;
    }

    /// <summary>A trunk from the plate up to a tip, owned by <paramref name="owner"/>.</summary>
    private static void AddSupport(SupportGraph graph, SceneObject owner, Vector3 contact)
    {
        var origin = SupportOrigin.ManualFor(owner.Id);
        var tip = new SupportNode { Type = SupportNodeType.Tip, Position = contact, Origin = origin };
        var plate = new SupportNode
        {
            Type = SupportNodeType.Base,
            Position = contact with { Z = 0 },
            Origin = origin,
        };
        graph.AddNode(tip);
        graph.AddNode(plate);
        graph.AddSegment(new SupportSegment
        {
            Type = SupportSegmentType.Trunk,
            NodeA = tip.Id,
            NodeB = plate.Id,
            Diameter = 1.5f,
            Origin = origin,
        });
    }

    private static byte[][] LayerBytes(SliceResult result) =>
        result.Layers.Select(layer => layer.Rle).ToArray();

    [Fact]
    public void SlicingAHiddenModelProducesTheSameBytesAsNotHavingItAtAll()
    {
        var settings = PrintSettings.Default with { LayerHeight = 0.5f };
        var keep = At("keep", new Vector3(-20, -5, 0));
        var hide = At("hide", new Vector3(20, -5, 0));
        hide.RenderState = RenderState.Hidden;

        var withHidden = Slicer.Slice([keep, hide], Printer, settings);
        var without = Slicer.Slice([At("keep", new Vector3(-20, -5, 0))], Printer, settings);

        Assert.Equal(without.LayerCount, withHidden.LayerCount);
        Assert.Equal(LayerBytes(without), LayerBytes(withHidden));
    }

    [Fact]
    public void AHiddenModelsSupportsAreNotPrintedEither()
    {
        var settings = PrintSettings.Default with { LayerHeight = 0.5f };
        var keep = At("keep", new Vector3(-20, -5, 0));
        var hide = At("hide", new Vector3(20, -5, 0), height: 6f);

        // The same scene twice, so the two graphs differ only in the hidden model's supports.
        var bothVisible = new SupportGraph();
        AddSupport(bothVisible, keep, new Vector3(-15, 0, 2.5f));
        var hiddenOwnersSupports = new SupportGraph();
        AddSupport(hiddenOwnersSupports, keep, new Vector3(-15, 0, 2.5f));
        AddSupport(hiddenOwnersSupports, hide, new Vector3(25, 0, 6f));

        hide.RenderState = RenderState.Hidden;
        var withHiddenSupports = Slicer.Slice([keep, hide], Printer, settings,
            supports: hiddenOwnersSupports);
        var reference = Slicer.Slice([keep], Printer, settings, supports: bothVisible);

        // Same height and same pixels: the hidden model's trunk neither prints nor extends the job.
        Assert.Equal(reference.LayerCount, withHiddenSupports.LayerCount);
        Assert.Equal(LayerBytes(reference), LayerBytes(withHiddenSupports));
    }

    [Fact]
    public void ShowingTheModelAgainBringsItsSupportsBack()
    {
        var settings = PrintSettings.Default with { LayerHeight = 0.5f };
        var obj = At("model", new Vector3(-5, -5, 0));
        var graph = new SupportGraph();
        AddSupport(graph, obj, new Vector3(0, 0, 2.5f));

        var visible = Slicer.Slice([obj], Printer, settings, supports: graph);
        obj.RenderState = RenderState.Hidden;
        obj.RenderState = RenderState.Normal;
        var again = Slicer.Slice([obj], Printer, settings, supports: graph);

        Assert.Equal(LayerBytes(visible), LayerBytes(again));
    }

    [Fact]
    public void SupportsOwnedByNothingArePrintedWhateverIsHidden()
    {
        // Manual elements from before object ownership belong to no model, so no model's
        // visibility can decide their fate.
        var settings = PrintSettings.Default with { LayerHeight = 0.5f };
        var obj = At("model", new Vector3(-5, -5, 0));
        var graph = new SupportGraph();
        var tip = new SupportNode { Type = SupportNodeType.Tip, Position = new Vector3(0, 0, 4) };
        var plate = new SupportNode { Type = SupportNodeType.Base, Position = new Vector3(0, 0, 0) };
        graph.AddNode(tip);
        graph.AddNode(plate);
        graph.AddSegment(new SupportSegment
        {
            Type = SupportSegmentType.Trunk, NodeA = tip.Id, NodeB = plate.Id, Diameter = 1.5f,
        });

        var hiddenIds = SupportOwnerVisibility.HiddenObjectIds([obj]);
        obj.RenderState = RenderState.Hidden;
        var allHidden = SupportOwnerVisibility.HiddenObjectIds([obj]);

        Assert.Empty(hiddenIds);
        Assert.Single(allHidden);
        foreach (var segment in graph.Segments)
            Assert.False(SupportOwnerVisibility.IsOwnedByHidden(graph, segment.Id, allHidden));
    }

    [Fact]
    public void SlicingWithEverythingHiddenIsRefusedRatherThanPrintingNothing()
    {
        var obj = At("model", new Vector3(-5, -5, 0));
        obj.RenderState = RenderState.Hidden;

        var refusal = Assert.Throws<InvalidOperationException>(
            () => Slicer.Slice([obj], Printer, PrintSettings.Default));

        Assert.Contains("Nothing to slice", refusal.Message);
    }

    [Fact]
    public void TheSliceCommandIsUnavailableWhenEveryModelIsHidden()
    {
        var viewModel = new MainViewModel();
        var obj = At("model", new Vector3(-5, -5, 0));
        viewModel.Document.AddObject(obj);
        Assert.True(viewModel.SliceCommand.CanExecute(null));

        viewModel.Document.SetObjectHidden(obj, true);

        Assert.False(viewModel.SliceCommand.CanExecute(null));
    }

    // ----- The toggle -----

    [Fact]
    public void HidingOneModelIsOneUndoStepAndLeavesTheOthersAlone()
    {
        var document = new Document();
        var first = At("first", new Vector3(-20, -5, 0));
        var second = At("second", new Vector3(20, -5, 0));
        document.AddObject(first);
        document.AddObject(second);

        document.SetObjectHidden(first, true);

        Assert.Equal(RenderState.Hidden, first.RenderState);
        Assert.Equal(RenderState.Normal, second.RenderState);

        Assert.True(document.Undo());
        Assert.Equal(RenderState.Normal, first.RenderState);
    }

    [Fact]
    public void HidingTheSelectedModelDeselectsIt()
    {
        var document = new Document();
        var obj = At("model", Vector3.Zero);
        document.AddObject(obj);
        document.Select(obj);

        document.SetObjectHidden(obj, true);

        Assert.Empty(document.Selection);
    }

    [Fact]
    public void TheVisibilityFlagFollowsTheRenderStateAndNotifies()
    {
        var obj = At("model", Vector3.Zero);
        var changes = new List<string?>();
        obj.PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        obj.RenderState = RenderState.Hidden;

        Assert.False(obj.IsVisible);
        Assert.Contains(nameof(SceneObject.IsVisible), changes);

        changes.Clear();
        obj.RenderState = RenderState.Hidden; // no change, no notification
        Assert.Empty(changes);
    }

    [Fact]
    public void TheToggleCommandWorksInSupportModeToo()
    {
        // Support mode's H hides support elements, so the Objects list is the only way to hide a
        // model there. It must work from both modes.
        var viewModel = new MainViewModel();
        var obj = At("model", Vector3.Zero);
        viewModel.Document.AddObject(obj);
        viewModel.SelectedObject = obj;
        viewModel.ViewMode = WorkspaceMode.Support;

        viewModel.ToggleObjectVisibilityCommand.Execute(obj);
        Assert.False(obj.IsVisible);

        viewModel.ToggleObjectVisibilityCommand.Execute(obj);
        Assert.True(obj.IsVisible);
    }

    [Fact]
    public void UnhideAllStillBringsHiddenModelsBack()
    {
        var document = new Document();
        var first = At("first", new Vector3(-20, -5, 0));
        var second = At("second", new Vector3(20, -5, 0));
        document.AddObject(first);
        document.AddObject(second);
        document.SetObjectHidden(first, true);
        document.SetObjectHidden(second, true);

        document.UnhideAll();

        Assert.True(first.IsVisible);
        Assert.True(second.IsVisible);
    }

    [Fact]
    public void EveryDrawnAndPickableFormOfASupportIsFilteredByItsOwner()
    {
        // The quirk from screen testing: meshes were filtered but the contact markers were not,
        // so hiding a model left its tips floating. One rule, applied wherever an element is
        // drawn or picked.
        var document = new Document();
        var obj = At("model", new Vector3(0, 0, 8)); // floating, so its underside can take a tip
        document.AddObject(obj);
        Assert.True(document.AddManualSupport(obj, new Vector3(5, 5, 8), -Vector3.UnitZ));

        document.SetObjectHidden(obj, true);
        var hidden = SupportOwnerVisibility.HiddenObjectIds(document.Scene.Objects);

        Assert.All(document.Supports.Nodes,
            node => Assert.True(SupportOwnerVisibility.IsOwnedByHidden(node, hidden)));
        Assert.All(document.Supports.Segments,
            segment => Assert.True(
                SupportOwnerVisibility.IsOwnedByHidden(document.Supports, segment.Id, hidden)));

        document.SetObjectHidden(obj, false);
        var shown = SupportOwnerVisibility.HiddenObjectIds(document.Scene.Objects);
        Assert.All(document.Supports.Nodes,
            node => Assert.False(SupportOwnerVisibility.IsOwnedByHidden(node, shown)));
    }

    [Fact]
    public void HidingAModelRaisesTheDocumentChangeTheViewportRedrawsOn()
    {
        // Supports used to stay on screen until something else rebuilt them — a mode switch,
        // typically. The viewport rebuilds on Document.Changed, so hiding must raise it.
        var document = new Document();
        var obj = At("model", Vector3.Zero);
        document.AddObject(obj);
        var changes = 0;
        document.Changed += () => changes++;

        document.SetObjectHidden(obj, true);

        Assert.True(changes > 0);
    }

    [Fact]
    public void HiddenStateSurvivesAProjectRoundTrip()
    {
        var dir = Path.Combine(Path.GetTempPath(), "danslicer-hiding-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var document = new Document();
            var shown = At("shown", new Vector3(-20, -5, 0));
            var hidden = At("hidden", new Vector3(20, -5, 0));
            document.AddObject(shown);
            document.AddObject(hidden);
            document.SetObjectHidden(hidden, true);

            var path = Path.Combine(dir, "scene.danslicer");
            ProjectFile.Save(path, document, new ProjectViewState());
            var loaded = ProjectFile.Load(path).Document;

            Assert.True(loaded.Scene.Objects.Single(o => o.Name == "shown").IsVisible);
            Assert.False(loaded.Scene.Objects.Single(o => o.Name == "hidden").IsVisible);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
        }
    }
}
