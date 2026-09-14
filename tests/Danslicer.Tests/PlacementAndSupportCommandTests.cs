using System.Numerics;
using Danslicer.Core;
using Danslicer.Core.Config;
using Danslicer.Core.Geometry;
using Danslicer.Core.Scene;
using Danslicer.Core.Supports;
using Danslicer.Core.Supports.Generation;
using Danslicer.Core.Supports.Routing;

namespace Danslicer.Tests;

public sealed class PlacementAndSupportCommandTests
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
    public void AutoDropIsPartOfTheTransformUndoStep()
    {
        var doc = new Document();
        var obj = new SceneObject("box", Box(new(-1, -1, 0), new(1, 1, 2)));
        doc.AddObject(obj);
        var before = obj.Transform;
        var requested = before with { Translation = new Vector3(12, 3, 20) };

        doc.CommitTransform(obj, before, requested, "Move");

        Assert.Equal(12f, obj.Transform.Translation.X);
        Assert.Equal(0f, obj.WorldBounds.Min.Z, 5);
        Assert.Equal("Move", doc.History.UndoName);
        doc.Undo();
        Assert.Equal(before, obj.Transform);
    }

    /// <summary>
    /// Renamed and re-pointed 2026-09-06 for the support lifecycle rule (task 03, user decision
    /// D13). It used to assert that a rotate-and-scale CARRIED its supports to transformed
    /// positions; under <see cref="SupportTransformRule"/> such a transform cannot map contacts
    /// exactly and discards them instead. What is still worth pinning is the part that did not
    /// change: only the transformed object's own supports are touched, and it is all one undo step.
    /// </summary>
    [Fact]
    public void ObjectTransformTouchesOnlyItsOwnedSupportNodesInTheSameUndoStep()
    {
        var doc = new Document { PlacementMode = PlacementMode.Off };
        var obj = new SceneObject("owned", Box(new(-1), new(1)));
        var other = new SceneObject("other", Box(new(-1), new(1)));
        doc.AddObject(obj);
        doc.AddObject(other);
        var owned = new SupportNode
        {
            Type = SupportNodeType.Tip,
            Position = new(2, 0, 0),
            SurfaceNormal = Vector3.UnitX,
            Origin = SupportOrigin.ManualFor(obj.Id),
        };
        var untouched = new SupportNode
        {
            Type = SupportNodeType.Tip,
            Position = new(9, 9, 9),
            Origin = SupportOrigin.ManualFor(other.Id),
        };
        doc.Supports.AddNode(owned);
        doc.Supports.AddNode(untouched);
        var before = obj.Transform;
        var requested = before with
        {
            Translation = new(10, 0, 0),
            Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2),
            Scale = new(2),
        };

        doc.CommitTransform(obj, before, requested, "Move object and supports");

        // Rotation and scale: the owned support goes, the other object's stays put.
        Assert.DoesNotContain(doc.Supports.Nodes, n => n.Id == owned.Id);
        Assert.Contains(doc.Supports.Nodes, n => n.Id == untouched.Id);
        Assert.Equal(new Vector3(9, 9, 9), untouched.Position);
        Assert.Equal("Move object and supports", doc.History.UndoName);

        // One undo restores the transform AND the discarded support together, which is what makes
        // a silent discard with no confirmation dialog acceptable.
        doc.Undo();
        Assert.Equal(before, obj.Transform);
        Assert.Contains(doc.Supports.Nodes, n => n.Id == owned.Id);
        Assert.Equal(new Vector3(2, 0, 0), owned.Position);
        Assert.Equal(new Vector3(9, 9, 9), untouched.Position);

        doc.Redo();
        Assert.Equal(requested, obj.Transform);
        Assert.DoesNotContain(doc.Supports.Nodes, n => n.Id == owned.Id);
        Assert.Contains(doc.Supports.Nodes, n => n.Id == untouched.Id);
    }

    /// <summary>
    /// Also re-pointed for D13: a zero scale used to collapse the owned support onto the origin
    /// and keep it. Scaling cannot map contacts exactly, so it now discards — and the degenerate
    /// case is no longer special, which is the point of stating the rule once rather than
    /// enumerating transforms. (The non-invertible-matrix branch in AppendAssociatedSupportMatrix
    /// survives for Mirror, which still carries its supports.)
    /// </summary>
    [Fact]
    public void ScalingObjectToZeroDiscardsOwnedSupportsAndUndoRestoresThem()
    {
        var doc = new Document { PlacementMode = PlacementMode.Off };
        var obj = new SceneObject("owned", Box(new(-1), new(1)));
        doc.AddObject(obj);
        var node = new SupportNode
        {
            Type = SupportNodeType.Tip,
            Position = new(3, 2, 1),
            Origin = SupportOrigin.ManualFor(obj.Id),
        };
        doc.Supports.AddNode(node);

        doc.CommitTransform(obj, obj.Transform, obj.Transform with { Scale = Vector3.Zero });

        Assert.Empty(doc.Supports.Nodes);

        doc.Undo();
        Assert.Contains(doc.Supports.Nodes, n => n.Id == node.Id);
        Assert.Equal(new Vector3(3, 2, 1), node.Position);
    }

    [Fact]
    public void RaiseAndOffModesRespectTheRequestedTransform()
    {
        var doc = new Document();
        var obj = new SceneObject("box", Box(new(-1, -1, -1), new(1, 1, 1)));
        doc.AddObject(obj);
        var before = obj.Transform;
        var requested = before with { Translation = new Vector3(4, 5, 20) };

        doc.PlacementMode = PlacementMode.RaiseAbovePlate;
        doc.PlacementHeightMm = 7.5f;
        doc.CommitTransform(obj, before, requested);
        Assert.Equal(7.5f, obj.WorldBounds.Min.Z, 5);

        doc.PlacementMode = PlacementMode.Off;
        before = obj.Transform;
        requested = before with { Translation = new Vector3(4, 5, 13) };
        doc.CommitTransform(obj, before, requested);
        Assert.Equal(requested, obj.Transform);
    }

    [Fact]
    public void ExplicitDropToPlateIgnoresAutomaticPlacementOffset()
    {
        var doc = new Document
        {
            PlacementMode = PlacementMode.RaiseAbovePlate,
            PlacementHeightMm = 8,
        };
        var obj = new SceneObject("box", Box(new(-1, -1, 0), new(1, 1, 2)))
        {
            Transform = Transform.Identity with { Translation = new(0, 0, 5) },
        };
        doc.AddObject(obj);

        doc.DropSelectionToPlate();

        Assert.Equal(0f, obj.WorldBounds.Min.Z, 5);
        doc.Undo();
        Assert.Equal(5f, obj.WorldBounds.Min.Z, 5);
    }

    [Fact]
    public void HideUnselectedSupportsIsUndoable()
    {
        var doc = new Document();
        var selected = new SupportNode { Type = SupportNodeType.Tip, Position = new(0, 0, 5) };
        var other = new SupportNode { Type = SupportNodeType.Base, Position = Vector3.Zero };
        var unrelated = new SupportNode { Type = SupportNodeType.Base, Position = new(5, 0, 0) };
        var segment = new SupportSegment
            { Type = SupportSegmentType.Branch, NodeA = selected.Id, NodeB = other.Id };
        doc.Supports.AddNode(selected);
        doc.Supports.AddNode(other);
        doc.Supports.AddNode(unrelated);
        doc.Supports.AddSegment(segment);
        doc.SelectSupportElement(selected.Id);

        doc.HideUnselectedSupportElements();

        Assert.False(selected.Hidden);
        Assert.False(other.Hidden);
        Assert.False(segment.Hidden);
        Assert.True(unrelated.Hidden);
        Assert.Equal("Hide unselected supports", doc.History.UndoName);
        doc.Undo();
        Assert.False(other.Hidden);
        Assert.False(segment.Hidden);
        Assert.False(unrelated.Hidden);

        doc.HideUnselectedSupportElements();
        doc.UnhideAll();
        Assert.False(other.Hidden);
        Assert.False(segment.Hidden);
        Assert.False(unrelated.Hidden);
        Assert.Equal("Unhide supports", doc.History.UndoName);
    }

    [Fact]
    public void HideSelectedSupportElementsHidesTheWholeSupport()
    {
        // Selecting ANY element of a support hides the complete non-bracing component: a
        // support is one user-visible thing (a lone hidden trunk left tip and base floating).
        var doc = new Document();
        var a = new SupportNode { Type = SupportNodeType.Tip, Position = Vector3.UnitZ };
        var b = new SupportNode { Type = SupportNodeType.Base, Position = Vector3.Zero };
        var segment = new SupportSegment
            { Type = SupportSegmentType.Trunk, NodeA = a.Id, NodeB = b.Id };
        var otherTip = new SupportNode { Type = SupportNodeType.Tip, Position = new Vector3(5, 0, 1) };
        doc.Supports.AddNode(a);
        doc.Supports.AddNode(b);
        doc.Supports.AddSegment(segment);
        doc.Supports.AddNode(otherTip);
        doc.SelectSupportElements([segment.Id]);

        doc.HideSelectedSupportElements();

        Assert.True(a.Hidden);
        Assert.True(b.Hidden);
        Assert.True(segment.Hidden);
        Assert.False(otherTip.Hidden);
        Assert.Empty(doc.SupportSelection);
        Assert.Equal("Hide supports", doc.History.UndoName);

        doc.Undo();
        Assert.False(a.Hidden);
        Assert.False(b.Hidden);
        Assert.False(segment.Hidden);
    }

    [Fact]
    public void HidingASelectedBraceHidesOnlyTheBrace()
    {
        var doc = new Document();
        var left = new SupportNode { Type = SupportNodeType.Junction, Position = Vector3.UnitZ };
        var right = new SupportNode { Type = SupportNodeType.Junction, Position = new Vector3(5, 0, 1) };
        var brace = new SupportSegment
            { Type = SupportSegmentType.Bracing, NodeA = left.Id, NodeB = right.Id };
        doc.Supports.AddNode(left);
        doc.Supports.AddNode(right);
        doc.Supports.AddSegment(brace);
        doc.SelectSupportElements([brace.Id]);

        doc.HideSelectedSupportElements();

        Assert.True(brace.Hidden);
        Assert.False(left.Hidden);
        Assert.False(right.Hidden);
    }

    [Fact]
    public void HideUnselectedSupportsKeepsTheSelectedElementsWholeTreeVisible()
    {
        var doc = new Document();
        var tip = new SupportNode { Type = SupportNodeType.Tip, Position = new(0, 0, 5) };
        var junction = new SupportNode { Type = SupportNodeType.Junction, Position = new(0, 0, 3) };
        var unrelated = new SupportNode { Type = SupportNodeType.Base, Position = new(5, 0, 0) };
        var selected = new SupportSegment
            { Type = SupportSegmentType.Tip, NodeA = tip.Id, NodeB = junction.Id };
        var baseNode = new SupportNode { Type = SupportNodeType.Base, Position = Vector3.Zero };
        var trunk = new SupportSegment
            { Type = SupportSegmentType.Branch, NodeA = junction.Id, NodeB = baseNode.Id };
        doc.Supports.AddNode(tip);
        doc.Supports.AddNode(junction);
        doc.Supports.AddNode(baseNode);
        doc.Supports.AddNode(unrelated);
        doc.Supports.AddSegment(selected);
        doc.Supports.AddSegment(trunk);
        doc.SelectSupportElement(selected.Id);

        doc.HideUnselectedSupportElements();

        Assert.False(selected.Hidden);
        Assert.False(tip.Hidden);
        Assert.False(junction.Hidden);
        Assert.False(baseNode.Hidden);
        Assert.False(trunk.Hidden);
        Assert.True(unrelated.Hidden);
    }

    [Fact]
    public void GenerateSupportsCommitsOneNonManualUndoablePass()
    {
        var doc = new Document();
        var obj = new SceneObject("floating", Box(new(-5, -5, 5), new(5, 5, 15)));
        doc.AddObject(obj);

        var summary = doc.GenerateSupports(obj);

        Assert.True(summary.GeneratedTipCount > 0);
        Assert.Equal("Generate supports", doc.History.UndoName);
        Assert.All(doc.Supports.Nodes, node => Assert.False(node.Origin.IsManual));
        Assert.All(doc.Supports.Segments, segment => Assert.False(segment.Origin.IsManual));
        Assert.All(doc.Supports.Nodes, node => Assert.Equal(obj.Id, node.Origin.ObjectId));
        Assert.All(doc.Supports.Segments, segment => Assert.Equal(obj.Id, segment.Origin.ObjectId));
        doc.Undo();
        Assert.Equal(0, doc.Supports.NodeCount);
    }

    [Fact]
    public void IslandGenerationUsesCapturedProjectLayerHeight()
    {
        var doc = new Document();
        doc.PrintSettings = doc.PrintSettings with { LayerHeight = 0.2f };
        // Only the 0.2 mm stack intersects this thin component; the old hard-coded
        // 0.05 mm generation stack missed it entirely.
        var obj = new SceneObject("thin island", Box(new(-2, -2, 5.08f), new(2, 2, 5.12f)));
        doc.AddObject(obj);
        var detection = doc.CaptureIslandDetection(obj);
        var generation = doc.CaptureSupportGeneration(obj, scope: SupportGenerationScope.IslandsOnly);
        doc.PrintSettings = doc.PrintSettings with { LayerHeight = 0.05f };

        Assert.Equal(detection.LayerHeightMm, generation.LayerHeightMm);
        Assert.Single(Document.ComputeIslandDetection(detection));
        Assert.Equal(1, Document.ComputeSupportGeneration(generation).Summary.CandidateCount);
    }

    [Fact]
    public void IslandSupportGenerationUsesOneNamedUndoStep()
    {
        var doc = new Document();
        var obj = new SceneObject("floating", Box(new(-5, -5, 5), new(5, 5, 15)));
        doc.AddObject(obj);
        var request = doc.CaptureSupportGeneration(obj, scope: SupportGenerationScope.IslandsOnly);
        var prepared = Document.ComputeSupportGeneration(request);
        var batch = new SupportGenerationBatch(doc.Supports, doc.History, prepared, int.MaxValue);

        batch.CommitNextBatch();
        batch.Complete();

        Assert.Equal("Generate island supports", doc.History.UndoName);
        doc.Undo();
        Assert.Equal(0, doc.Supports.NodeCount);
    }

    [Fact]
    public void GenerationRequestSnapshotsSupportSettings()
    {
        var doc = new Document
        {
            SupportSettings = new SupportConfig
            {
                TipDiameter = 0.55f, ConeLength = 2.5f, BallDiameter = 0.2f,
                PenetrationDepth = 0.1f, TipNormalLeadInMm = 0.45f,
                TrunkDiameter = 1.6f, BranchDiameter = 1.3f,
                MemberAngleDegrees = 37f, TipMemberLength = 2.8f, MaxBranchLength = 12f,
                PreferExistingTrunks = false, ExistingTrunkBranchRange = 9f,
                MinMemberSeparationMm = 0.65f,
                UseBaseGrid = false, BaseGridPitch = 18f,
                ReinforceEnabled = true,
                ReinforceSeedSelector = ReinforceSeedSelector.CriticalTips,
                ReinforceCount = 5, ReinforceRingRadius = 4.5f,
                ReinforceRingDiameterMultiplier = 1.6f,
                BaseShape = SupportBaseShape.DiscCone, BaseDiameter = 5f, BaseHeight = 1f,
                BaseConeHeight = 2.3f, Spacing = 3.5f, OverhangAngleDegrees = 52f,
                MinIslandAreaMm2 = 0.75f,
            },
        };
        var obj = new SceneObject("floating", Box(new(-5, -5, 5), new(5, 5, 15)));
        doc.AddObject(obj);

        var request = doc.CaptureSupportGeneration(obj, seed: 4);
        doc.SupportSettings.TipDiameter = 9f;

        Assert.Equal(0.55f, request.Settings.TipDiameter);
        Assert.Equal(2.5f, request.Settings.ConeLength);
        Assert.Equal(0.2f, request.Settings.BallDiameter);
        Assert.Equal(0.1f, request.Settings.PenetrationDepth);
        Assert.Equal(0.45f, request.Settings.TipNormalLeadInMm);
        Assert.Equal(1.6f, request.Settings.TrunkDiameter);
        Assert.Equal(1.3f, request.Settings.BranchDiameter);
        Assert.Equal(37f, request.Settings.MemberAngleDegrees);
        Assert.Equal(2.8f, request.Settings.TipMemberLength);
        Assert.Equal(12f, request.Settings.MaxBranchLength);
        Assert.False(request.Settings.PreferExistingTrunks);
        Assert.Equal(9f, request.Settings.ExistingTrunkBranchRange);
        Assert.Equal(0.65f, request.Settings.MinMemberSeparationMm);
        Assert.False(request.Settings.UseBaseGrid);
        Assert.Equal(18f, request.Settings.BaseGridPitch);
        Assert.True(request.Settings.ReinforceEnabled);
        Assert.Equal(ReinforceSeedSelector.CriticalTips,
            request.Settings.ReinforceSeedSelector);
        Assert.Equal(5, request.Settings.ReinforceCount);
        Assert.Equal(4.5f, request.Settings.ReinforceRingRadius);
        Assert.Equal(1.6f, request.Settings.ReinforceRingDiameterMultiplier);
        Assert.Equal(SupportBaseShape.DiscCone, request.Settings.BaseShape);
        Assert.Equal(5f, request.Settings.BaseDiameter);
        Assert.Equal(1f, request.Settings.BaseHeight);
        Assert.Equal(2.3f, request.Settings.BaseConeHeight);
        Assert.Equal(3.5f, request.Settings.Spacing);
        Assert.Equal(52f, request.Settings.OverhangAngleDegrees);
        Assert.Equal(0.75f, request.Settings.MinIslandAreaMm2);
    }

    [Fact]
    public void GenerationRequestReadsGridSettingsChangedAfterDocumentStartup()
    {
        var doc = new Document();
        var obj = new SceneObject("floating", Box(new(-5, -5, 5), new(5, 5, 15)));
        doc.AddObject(obj);

        doc.SupportSettings.UseBaseGrid = false;
        doc.SupportSettings.BaseGridPitch = 11f;
        var request = doc.CaptureSupportGeneration(obj);

        Assert.False(request.Settings.UseBaseGrid);
        Assert.Equal(11f, request.Settings.BaseGridPitch);
    }

    [Fact]
    public void GeneratedSupportsUseCapturedTipRoutingAndBaseSettings()
    {
        var doc = new Document
        {
            SupportSettings = new SupportConfig
            {
                TipDiameter = 0.65f, ConeLength = 2.6f, BallDiameter = 0.2f,
                PenetrationDepth = 0.1f, TrunkDiameter = 1.7f, BranchDiameter = 1.35f,
                TipMemberLength = 2.7f, BaseShape = SupportBaseShape.DiscCone,
                BaseDiameter = 5.2f, BaseHeight = 1.1f, BaseConeHeight = 2.4f,
            },
        };
        var obj = new SceneObject("floating", Box(new(-5, -5, 5), new(5, 5, 15)));
        doc.AddObject(obj);

        var request = doc.CaptureSupportGeneration(obj);
        doc.SupportSettings = new SupportConfig();
        var prepared = Document.ComputeSupportGeneration(request);

        var tips = prepared.Nodes.Where(n => n.Type == SupportNodeType.Tip).ToList();
        Assert.NotEmpty(tips);
        Assert.All(tips, tip =>
        {
            Assert.Equal(0.65f, tip.TipDiameter);
            Assert.Equal(2.6f, tip.ConeLength);
            Assert.Equal(0.2f, tip.BallDiameter);
            Assert.Equal(0.1f, tip.PenetrationDepth);
        });
        Assert.All(prepared.Segments.Where(s => s.Type == SupportSegmentType.Trunk),
            segment => Assert.Equal(1.7f, segment.Diameter));
        var bases = prepared.Nodes.Where(n => n.Type == SupportNodeType.Base).ToList();
        Assert.NotEmpty(bases);
        Assert.All(bases, supportBase =>
        {
            Assert.Equal(SupportBaseShape.DiscCone, supportBase.BaseShape);
            Assert.Equal(5.2f, supportBase.BaseDiameter);
            Assert.Equal(1.1f, supportBase.BaseHeight);
            Assert.Equal(2.4f, supportBase.BaseConeHeight);
        });
    }

    [Fact]
    public void ReinforceConfigAddsGeometryToGenerationRenderAndSlicePaths()
    {
        var mesh = Box(new(-5, -5, 5), new(5, 5, 15));
        var disabled = PrepareReinforceCase(mesh, enabled: false);
        var enabled = PrepareReinforceCase(mesh, enabled: true);
        var disabledGraph = ToGraph(disabled);
        var enabledGraph = ToGraph(enabled);

        var disabledTips = disabledGraph.Segments.Count(segment =>
            segment.Type == SupportSegmentType.Tip);
        var enabledTips = enabledGraph.Segments.Count(segment =>
            segment.Type == SupportSegmentType.Tip);
        Assert.Equal(disabledTips + 3, enabledTips);
        var disabledTipMesh = Assert.Single(SupportRenderMesh.Build(disabledGraph), part =>
            part.Kind == SupportRenderKind.Tip).Mesh;
        var enabledTipMesh = Assert.Single(SupportRenderMesh.Build(enabledGraph), part =>
            part.Kind == SupportRenderKind.Tip).Mesh;
        Assert.True(enabledTipMesh.TriangleCount > disabledTipMesh.TriangleCount);
        Assert.True(SupportSliceGeometry.SectionsAt(enabledGraph, 4.9).Count >
                    SupportSliceGeometry.SectionsAt(disabledGraph, 4.9).Count);
    }

    private static PreparedSupportGeneration PrepareReinforceCase(Mesh mesh, bool enabled)
    {
        var document = new Document
        {
            SupportSettings = new SupportConfig
            {
                Spacing = 20f,
                IslandSpacingMm = 20f,
                UseBaseGrid = false,
                ReinforceEnabled = enabled,
                ReinforceCount = 3,
                ReinforceRingRadius = 2f,
                ReinforceRingDiameterMultiplier = 1.5f,
            },
        };
        var obj = new SceneObject("reinforce", mesh);
        document.AddObject(obj);
        return Document.ComputeSupportGeneration(document.CaptureSupportGeneration(obj, seed: 17));
    }

    private static SupportGraph ToGraph(PreparedSupportGeneration prepared)
    {
        var graph = new SupportGraph();
        foreach (var node in prepared.Nodes) graph.AddNode(node.Clone());
        foreach (var segment in prepared.Segments) graph.AddSegment(segment.Clone());
        return graph;
    }

    private sealed class Vector3Comparer(float tolerance) : IEqualityComparer<Vector3>
    {
        public bool Equals(Vector3 x, Vector3 y) => Vector3.Distance(x, y) <= tolerance;
        public int GetHashCode(Vector3 obj) => 0;
    }
}
