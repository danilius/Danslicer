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
    [Fact]
    public void SavedZigzagPitchUsesFittedAlternatingBracesInPreview()
    {
        var document = Fixture();
        document.SupportSettings.BracingPattern = BracingPattern.Zigzag;
        document.SupportSettings.BracingSpacingMm = 7.22f;
        foreach (var node in document.Supports.Nodes)
            node.Position = node.Position with { Z = node.Position.Z * (0.5f + (node.Position.X + 9) / 36f) };
        using var preview = new StructurePreview(document, true);
        preview.Update(document.SupportSettings);
        Assert.True(preview.CanApply);
        var graph = preview.Graph!;
        var pairs = graph.Segments.Where(s => s.Type == SupportSegmentType.Bracing)
            .Select(s => (Foot: graph.GetNode(s.NodeA).Position, Head: graph.GetNode(s.NodeB).Position))
            .GroupBy(r => (MathF.Min(r.Foot.X, r.Head.X), MathF.Max(r.Foot.X, r.Head.X))).ToArray();
        Assert.Equal(6, pairs.Length);
        foreach (var pair in pairs)
        {
            var rungs = pair.OrderByDescending(r => r.Head.Z).ToArray();
            Assert.True(rungs.Length >= 2);
            for (var i = 1; i < rungs.Length; i++)
            {
                Assert.Equal(rungs[i - 1].Foot.X, rungs[i].Head.X);
                Assert.Equal(2f, rungs[i - 1].Foot.Z - rungs[i].Head.Z, 3);
            }
        }
    }

    [Fact]
    public void LegacyZigzagNormalizesToAutomaticAndKeepsEndpointGap()
    {
        var settings = new SupportConfig
        {
            BracingPattern = BracingPattern.Zigzag, BracingSpacingMm = 7.22f,
            BracingEndpointGapMm = 3f,
        };
        settings.Normalize();
        Assert.Equal(BracingPattern.Automatic, settings.BracingPattern);
        Assert.Equal(3f, settings.BracingEndpointGapMm);
        var config = new UserConfig { Supports = settings };
        var vm = new Danslicer.App.ViewModels.ConfigViewModel(config, () => { });
        Assert.True(vm.IsAutomaticBracing);
        Assert.DoesNotContain(BracingPattern.Zigzag, vm.BracingPatterns);
    }

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
    public void BracingPreviewReplacesEndpointGapAndUndoRestoresOriginalBraces()
    {
        var document = Fixture();
        Assert.NotNull(document.BraceSupports());
        var before = Ids(document.Supports);
        var oldCount = document.Supports.Segments.Count(s => s.Type == SupportSegmentType.Bracing);
        using var preview = new StructurePreview(document, true);
        preview.Update(document.SupportSettings with { BracingEndpointGapMm = 25 });
        Assert.True(preview.CanApply);
        Assert.True(preview.Graph!.Segments.Count(s => s.Type == SupportSegmentType.Bracing) < oldCount);
        Assert.Equal(before, Ids(document.Supports));
        Assert.True(preview.Apply()); Assert.True(document.Undo()); Assert.Equal(before, Ids(document.Supports));
    }

    [Fact]
    public void BracingSelectedPairPreservesNeighbourConnectionsThroughPreviewApplyAndUndo()
    {
        var document = Fixture();
        document.BraceSupports();
        var tips = document.Supports.Nodes.Where(n => n.Type == SupportNodeType.Tip).OrderBy(n => n.Position.X).ToArray();
        document.SelectSupportElements([tips[2].Id, tips[3].Id]);
        var before = Ids(document.Supports);
        var internalBraces = SupportBracing.BracesBetween(document.Supports, document.SupportTarget!.Id,
            document.StructureOperands()).Segments.ToHashSet();
        var touching = SupportBracing.BracesOf(document.Supports, document.SupportTarget.Id,
            document.StructureOperands()).Segments;
        var neighbours = touching.Where(id => !internalBraces.Contains(id)).ToArray();
        Assert.NotEmpty(internalBraces);
        Assert.NotEmpty(neighbours);
        var preserved = document.Supports.Segments.Where(s => s.Type == SupportSegmentType.Bracing &&
            !internalBraces.Contains(s.Id)).Select(s => (s.Id, s.NodeA, s.NodeB, s.Diameter)).ToArray();
        using var preview = new StructurePreview(document, true);
        preview.Update(document.SupportSettings with { BracingEndpointGapMm = 20 });
        AssertPreserved(preview.Graph!);
        Assert.Equal(before, Ids(document.Supports));
        Assert.True(preview.Apply());
        AssertPreserved(document.Supports);
        Assert.True(document.Undo());
        Assert.Equal(before, Ids(document.Supports));
        Assert.True(document.Redo());
        AssertPreserved(document.Supports);

        void AssertPreserved(SupportGraph graph)
        {
            foreach (var brace in preserved)
            {
                Assert.True(graph.TryGetSegment(brace.Id, out var segment));
                Assert.Equal((brace.NodeA, brace.NodeB, brace.Diameter),
                    (segment.NodeA, segment.NodeB, segment.Diameter));
                Assert.True(graph.TryGetNode(brace.NodeA, out _));
                Assert.True(graph.TryGetNode(brace.NodeB, out _));
            }
        }
    }

    [Fact]
    public void RebuildingPairKeepsAnEndpointSharedWithANeighbourBrace()
    {
        var document = Fixture();
        var tips = document.Supports.Nodes.Where(n => n.Type == SupportNodeType.Tip).OrderBy(n => n.Position.X).Take(3).ToArray();
        var ends = tips.Select(tip => new SupportNode
        {
            Type = SupportNodeType.BraceEnd, Position = tip.Position with { Z = 30 }, Origin = tip.Origin,
        }).ToArray();
        foreach (var end in ends) document.Supports.AddNode(end);
        var internalBrace = new SupportSegment { Type = SupportSegmentType.Bracing, NodeA = ends[0].Id, NodeB = ends[1].Id };
        var neighbour = new SupportSegment { Type = SupportSegmentType.Bracing, NodeA = ends[1].Id, NodeB = ends[2].Id };
        document.Supports.AddSegment(internalBrace); document.Supports.AddSegment(neighbour);
        var removal = SupportBracing.BracesBetween(document.Supports, document.SupportTarget!.Id, [tips[0].Id, tips[1].Id]);
        Assert.Equal([internalBrace.Id], removal.Segments);
        Assert.DoesNotContain(ends[1].Id, removal.Nodes);
        var command = new RemoveSupportElementsCommand(document.Supports, removal.Nodes, removal.Segments);
        command.Execute();
        Assert.True(document.Supports.TryGetSegment(neighbour.Id, out _));
        Assert.True(document.Supports.TryGetNode(ends[1].Id, out _));
        command.Undo();
        Assert.True(document.Supports.TryGetSegment(internalBrace.Id, out _));
    }

    [Fact]
    public void BracingOneSelectedSupportDoesNotDeleteItsNeighbourBraces()
    {
        var document = Fixture();
        document.BraceSupports();
        var tip = document.Supports.Nodes.First(n => n.Type == SupportNodeType.Tip);
        document.SelectSupportElements([tip.Id]);
        var before = Ids(document.Supports);
        using var preview = new StructurePreview(document, true);
        preview.Update(document.SupportSettings);
        Assert.Equal(before, Ids(preview.Graph!));
        Assert.False(preview.CanApply);
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
