using System.Numerics;
using Danslicer.Core;
using Danslicer.Core.Commands;
using Danslicer.Core.Config;
using Danslicer.Core.Geometry;
using Danslicer.Core.Scene;
using Danslicer.Core.Supports;

namespace Danslicer.Tests;

public sealed class StructurePreviewTests
{
    private static Document Fixture()
    {
        var d = new Document();
        var model = new SceneObject("slab", new Mesh([new(-30, -10, 60), new(30, -10, 60), new(0, 20, 60)], [0, 2, 1]));
        d.AddObject(model); d.Select(model);
        d.SupportSettings = new SupportConfig { UseBaseGrid = false, AutoParenting = false, AutoBracing = false };
        for (var i = 0; i < 7; i++)
        {
            var tip = new SupportNode { Type = SupportNodeType.Tip, Position = new Vector3(i * 3 - 9, 0, 60), SurfaceNormal = -Vector3.UnitZ, Origin = SupportOrigin.ManualFor(model.Id), ContactObjectId = model.Id };
            var junction = new SupportNode { Type = SupportNodeType.Junction, Position = tip.Position - Vector3.UnitZ * 2, Origin = tip.Origin };
            var foot = new SupportNode { Type = SupportNodeType.Base, Position = new Vector3(tip.Position.X, 0, 0), Origin = tip.Origin };
            d.Supports.AddNode(tip); d.Supports.AddNode(junction); d.Supports.AddNode(foot);
            d.Supports.AddSegment(new SupportSegment { Type = SupportSegmentType.Tip, NodeA = tip.Id, NodeB = junction.Id, Origin = tip.Origin });
            d.Supports.AddSegment(new SupportSegment { Type = SupportSegmentType.Trunk, NodeA = junction.Id, NodeB = foot.Id, Origin = tip.Origin });
        }
        return d;
    }
    private static Guid[] Ids(SupportGraph graph) => graph.Nodes.Select(n => n.Id).Concat(graph.Segments.Select(s => s.Id)).Order().ToArray();

    [Fact]
    public void ParentThenBraceUndoAndRedoOneOperationAtATime()
    {
        var document = Fixture();
        document.History.Clear();
        document.History.RecordExecuted(new AddSupportElementsCommand(document.Supports,
            document.Supports.Nodes.ToList(), document.Supports.Segments.ToList(), "Create supports"));
        var singles = Ids(document.Supports);
        using var parent = new StructurePreview(document, false);
        parent.Update(document.SupportSettings with { CandelabraMaxTips = 3 });
        Assert.True(parent.Apply());
        var parented = Ids(document.Supports);
        using var brace = new StructurePreview(document, true);
        brace.Update(document.SupportSettings with { BracingClusterGapMm = 0, BracingNeighbourDistanceMm = 30 });
        Assert.Contains(brace.Graph!.Segments, s => s.Type == SupportSegmentType.Bracing);
        var braced = Ids(brace.Graph);
        Assert.True(document.Undo()); Assert.Equal(parented, Ids(document.Supports));
        Assert.False(document.HasPendingStructurePreview);
        Assert.True(document.Undo()); Assert.Equal(singles, Ids(document.Supports));
        Assert.True(document.Undo()); Assert.Empty(document.Supports.Nodes);
        Assert.True(document.Redo()); Assert.Equal(singles, Ids(document.Supports));
        Assert.True(document.Redo()); Assert.Equal(parented, Ids(document.Supports));
        Assert.True(document.Redo()); Assert.Equal(braced, Ids(document.Supports));
    }

    [Fact]
    public void UndoPendingParentingPreservesSupportsAndCanRedoPreview()
    {
        var document = Fixture(); var before = Ids(document.Supports);
        using var preview = new StructurePreview(document, false);
        preview.Update(document.SupportSettings);
        var expected = Ids(preview.Graph!);
        Assert.True(document.Undo()); Assert.Equal(before, Ids(document.Supports));
        Assert.True(document.Redo()); Assert.Equal(expected, Ids(document.Supports));
    }

    [Fact]
    public void UndoRefusedPreviewDoesNotConsumePreviousOperation()
    {
        var document = Fixture(); var before = Ids(document.Supports);
        var previous = document.History.UndoName;
        using var preview = new StructurePreview(document, false);
        preview.Update(document.SupportSettings, new Vector2(500, 0));
        Assert.False(preview.CanApply);
        Assert.True(document.Undo()); Assert.Equal(before, Ids(document.Supports));
        Assert.Equal(previous, document.History.UndoName);
        Assert.False(document.History.CanRedo);
    }

    [Fact]
    public void RepeatedPreviewAndCancelLeaveDocumentAndHistoryUntouched()
    {
        var document = Fixture();
        document.SelectSupportElements(document.Supports.Nodes.Where(n => n.Type == SupportNodeType.Tip).Select(n => n.Id));
        var selection = document.SupportSelection.Order().ToArray();
        var before = Ids(document.Supports); var undo = document.History.UndoName;
        using var preview = new StructurePreview(document, false);
        preview.Update(document.SupportSettings);
        Assert.True(preview.CanApply);
        Assert.Single(preview.Graph!.Nodes, n => n.Type == SupportNodeType.Base);
        preview.Update(document.SupportSettings with { CandelabraMaxTips = 3 });
        Assert.True(preview.Graph.Nodes.Count(n => n.Type == SupportNodeType.Base) >= 3);
        Assert.DoesNotContain("Group exceeds", preview.ConstraintDetails);
        Assert.All(preview.HighlightedElements, id => Assert.True(preview.ElementConstraints.ContainsKey(id)));
        Assert.Equal(before, Ids(document.Supports)); Assert.Equal(selection, document.SupportSelection.Order());
        Assert.Equal(undo, document.History.UndoName);
    }

    [Fact]
    public void ApplyUsesExactlyPreviewedGraphAndUndoesInOneStep()
    {
        var document = Fixture(); var before = Ids(document.Supports);
        using var preview = new StructurePreview(document, false);
        preview.Update(document.SupportSettings);
        var expected = Ids(preview.Graph!);
        Assert.True(preview.Apply()); Assert.False(preview.Apply());
        Assert.Equal(expected, Ids(document.Supports));
        Assert.True(document.Undo()); Assert.Equal(before, Ids(document.Supports));
        Assert.True(document.Redo()); Assert.Equal(expected, Ids(document.Supports));
        // A single existing candelabra can be reconfigured without needing two components.
        using var reparent = new StructurePreview(document, false);
        reparent.Update(document.SupportSettings with { CandelabraMaxTips = 3 });
        Assert.True(reparent.CanApply);
        Assert.True(reparent.Graph!.Nodes.Count(n => n.Type == SupportNodeType.Base) >= 3);
    }

    [Fact]
    public void ChangedSelectionInvalidatesAnOldPreview()
    {
        var document = Fixture();
        using var preview = new StructurePreview(document, false);
        preview.Update(document.SupportSettings);
        document.SelectSupportElements([document.Supports.Nodes.First(n => n.Type == SupportNodeType.Tip).Id]);
        Assert.False(preview.CanApply); Assert.False(preview.Apply()); Assert.Null(preview.Graph);
    }

    [Fact]
    public void BracingPreviewReplacesDensityAndUndoRestoresOriginalBraces()
    {
        var document = Fixture();
        Assert.NotNull(document.BraceSupports());
        var before = Ids(document.Supports);
        var oldCount = document.Supports.Segments.Count(s => s.Type == SupportSegmentType.Bracing);
        using var preview = new StructurePreview(document, true);
        preview.Update(document.SupportSettings with { BracingSpacingMm = 25 });
        Assert.True(preview.CanApply);
        Assert.True(preview.Graph!.Segments.Count(s => s.Type == SupportSegmentType.Bracing) < oldCount);
        Assert.Equal(before, Ids(document.Supports));
        Assert.True(preview.Apply()); Assert.True(document.Undo()); Assert.Equal(before, Ids(document.Supports));
    }

    [Fact]
    public void RememberingArrangementDoesNotChangeContactOrMemberGeometry()
    {
        var source = new SupportConfig { TipDiameter = 2, TrunkDiameter = 5, BaseDiameter = 10, CandelabraMaxTips = 8 };
        var target = new SupportConfig(); var before = target with { };
        ArrangementSettings.CopyTo(source, target, false);
        Assert.Equal(8, target.CandelabraMaxTips);
        Assert.Equal(before.TipDiameter, target.TipDiameter); Assert.Equal(before.TrunkDiameter, target.TrunkDiameter);
        Assert.Equal(before.BaseDiameter, target.BaseDiameter); Assert.Equal(before.BracingSpacingMm, target.BracingSpacingMm);
    }

    [Fact]
    public void DropNowUsesRequestedHeightWithAutoDropOffAndUndoes()
    {
        var document = Fixture(); var before = Ids(document.Supports);
        document.PlacementMode = PlacementMode.Off;
        document.DropSelectionToHeight(6);
        Assert.Equal(6, document.Scene.Objects.Single().WorldBounds.Min.Z, 4);
        Assert.Equal(PlacementMode.Off, document.PlacementMode);
        Assert.True(document.Undo());
        Assert.Equal(60, document.Scene.Objects.Single().WorldBounds.Min.Z, 4);
        Assert.Equal(before, Ids(document.Supports));
    }

    [Fact]
    public void ImpossibleForcedCentreCannotBeApplied()
    {
        var document = Fixture();
        using var preview = new StructurePreview(document, false);
        preview.Update(document.SupportSettings, new Vector2(500, 0));
        Assert.False(preview.CanApply);
        Assert.NotEmpty(preview.HighlightedElements);
        Assert.Equal(Ids(document.Supports), Ids(preview.Graph!));
    }
}
