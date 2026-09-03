using System.Numerics;
using Danslicer.Core;
using Danslicer.Core.Config;
using Danslicer.Core.Geometry;
using Danslicer.Core.Scene;
using Danslicer.Core.Supports;

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

    [Fact]
    public void ObjectTransformCarriesOnlyItsOwnedSupportNodesInTheSameUndoStep()
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

        Assert.Equal(new Vector3(10, 4, 0), owned.Position, new Vector3Comparer(1e-5f));
        Assert.Equal(Vector3.UnitY, owned.SurfaceNormal, new Vector3Comparer(1e-5f));
        Assert.Equal(new Vector3(9, 9, 9), untouched.Position);
        Assert.Equal("Move object and supports", doc.History.UndoName);

        doc.Undo();
        Assert.Equal(before, obj.Transform);
        Assert.Equal(new Vector3(2, 0, 0), owned.Position);
        Assert.Equal(new Vector3(9, 9, 9), untouched.Position);

        doc.Redo();
        Assert.Equal(requested, obj.Transform);
        Assert.Equal(new Vector3(10, 4, 0), owned.Position, new Vector3Comparer(1e-5f));
    }

    [Fact]
    public void ScalingObjectToZeroStillCarriesOwnedSupportPositions()
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

        Assert.Equal(Vector3.Zero, node.Position);
        doc.Undo();
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
    public void GenerationRequestSnapshotsSupportSettings()
    {
        var doc = new Document
        {
            SupportSettings = new SupportConfig
            {
                TipDiameter = 0.55f, ConeLength = 2.5f, BallDiameter = 0.2f,
                PenetrationDepth = 0.1f, TrunkDiameter = 1.6f, BranchDiameter = 1.3f,
                MemberAngleDegrees = 37f, TipMemberLength = 2.8f, MaxBranchLength = 12f,
                PreferExistingTrunks = false, ExistingTrunkBranchRange = 9f,
                BaseGridPitch = 18f,
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
        Assert.Equal(1.6f, request.Settings.TrunkDiameter);
        Assert.Equal(1.3f, request.Settings.BranchDiameter);
        Assert.Equal(37f, request.Settings.MemberAngleDegrees);
        Assert.Equal(2.8f, request.Settings.TipMemberLength);
        Assert.Equal(12f, request.Settings.MaxBranchLength);
        Assert.False(request.Settings.PreferExistingTrunks);
        Assert.Equal(9f, request.Settings.ExistingTrunkBranchRange);
        Assert.Equal(18f, request.Settings.BaseGridPitch);
        Assert.Equal(SupportBaseShape.DiscCone, request.Settings.BaseShape);
        Assert.Equal(5f, request.Settings.BaseDiameter);
        Assert.Equal(1f, request.Settings.BaseHeight);
        Assert.Equal(2.3f, request.Settings.BaseConeHeight);
        Assert.Equal(3.5f, request.Settings.Spacing);
        Assert.Equal(52f, request.Settings.OverhangAngleDegrees);
        Assert.Equal(0.75f, request.Settings.MinIslandAreaMm2);
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

    private sealed class Vector3Comparer(float tolerance) : IEqualityComparer<Vector3>
    {
        public bool Equals(Vector3 x, Vector3 y) => Vector3.Distance(x, y) <= tolerance;
        public int GetHashCode(Vector3 obj) => 0;
    }
}
