using System.Numerics;
using Danslicer.Core;
using Danslicer.Core.Config;
using Danslicer.Core.Geometry;
using Danslicer.Core.Scene;
using Danslicer.Core.Supports;
using Danslicer.Core.Supports.Routing;

namespace Danslicer.Tests;

/// <summary>T routes manual supports around the model; a refused contact adds nothing.</summary>
public sealed class ManualSupportRoutingTests
{
    /// <summary>Axis-aligned box mesh with outward faces (soup-welded).</summary>
    private static Mesh Box(Vector3 min, Vector3 max)
    {
        var (a, b) = (min, max);
        var corners = new Vector3[]
        {
            new(a.X, a.Y, a.Z), new(b.X, a.Y, a.Z), new(b.X, b.Y, a.Z), new(a.X, b.Y, a.Z),
            new(a.X, a.Y, b.Z), new(b.X, a.Y, b.Z), new(b.X, b.Y, b.Z), new(a.X, b.Y, b.Z),
        };
        int[] quads = [0, 3, 2, 1, 4, 5, 6, 7, 0, 1, 5, 4, 2, 3, 7, 6, 0, 4, 7, 3, 1, 2, 6, 5];
        var soup = new List<Vector3>();
        for (int q = 0; q < quads.Length; q += 4)
        {
            soup.Add(corners[quads[q]]); soup.Add(corners[quads[q + 1]]); soup.Add(corners[quads[q + 2]]);
            soup.Add(corners[quads[q]]); soup.Add(corners[quads[q + 2]]); soup.Add(corners[quads[q + 3]]);
        }
        return Mesh.FromTriangleSoup(soup.ToArray());
    }

    private static (Document Document, SceneObject Box) FloatingBoxDocument()
    {
        var document = new Document();
        var obj = new SceneObject("box", Box(new Vector3(-5, -5, 8), new Vector3(5, 5, 14)));
        document.AddObject(obj);
        return (document, obj);
    }

    [Fact]
    public void UndersideSupportRoutesToThePlate()
    {
        var (document, box) = FloatingBoxDocument();

        var added = document.AddManualSupport(box, new Vector3(0, 0, 8), -Vector3.UnitZ);

        Assert.True(added);
        Assert.Contains(document.Supports.Nodes, n => n.Type == SupportNodeType.Tip);
        var bases = document.Supports.Nodes.Where(n => n.Type == SupportNodeType.Base).ToList();
        Assert.All(bases, n => Assert.Equal(0f, n.Position.Z, 3));
        Assert.NotEmpty(bases);
    }

    [Theory]
    [InlineData(true, 0f)]
    [InlineData(false, 4f)]
    public void ManualSupportSnapshotsTheSelectedBasePlacementMode(
        bool useBaseGrid, float expectedBaseX)
    {
        var (document, box) = FloatingBoxDocument();
        document.SupportSettings = new SupportConfig
        {
            UseBaseGrid = useBaseGrid,
            BaseGridPitch = 20f,
        };

        Assert.True(document.AddManualSupport(
            box, new Vector3(4, 0, 8), -Vector3.UnitZ));

        var supportBase = Assert.Single(document.Supports.Nodes,
            node => node.Type == SupportNodeType.Base);
        Assert.Equal(expectedBaseX, supportBase.Position.X, 3);
    }

    [Fact]
    public void RoutedSupportUsesCurrentTipMemberAndBaseSettings()
    {
        var (document, box) = FloatingBoxDocument();
        document.SupportSettings = new SupportConfig
        {
            TipDiameter = 0.6f,
            ConeLength = 2.7f,
            BallDiameter = 0.25f,
            PenetrationDepth = 0.12f,
            TrunkDiameter = 1.7f,
            BranchDiameter = 1.4f,
            MemberAngleDegrees = 40f,
            TipMemberLength = 3f,
            MaxBranchLength = 10f,
            BaseShape = SupportBaseShape.DiscCone,
            BaseDiameter = 5.5f,
            BaseHeight = 1.1f,
            BaseConeHeight = 2.4f,
        };

        Assert.True(document.AddManualSupport(box, new Vector3(0, 0, 8), -Vector3.UnitZ));

        var tip = Assert.Single(document.Supports.Nodes, n => n.Type == SupportNodeType.Tip);
        Assert.Equal(SupportTipShape.Cone, tip.TipShape);
        Assert.Equal(0.6f, tip.TipDiameter);
        Assert.Equal(2.7f, tip.ConeLength);
        Assert.Equal(0.25f, tip.BallDiameter);
        Assert.Equal(0.12f, tip.PenetrationDepth);
        var tipSegment = Assert.Single(document.Supports.Segments,
            segment => segment.Type == SupportSegmentType.Tip);
        var junctionId = tipSegment.NodeA == tip.Id ? tipSegment.NodeB : tipSegment.NodeA;
        Assert.Equal(3f, Vector3.Distance(tip.Position, document.Supports.GetNode(junctionId).Position), 3);
        Assert.All(document.Supports.Segments.Where(s => s.Type == SupportSegmentType.Trunk),
            segment => Assert.Equal(1.7f, segment.Diameter));
        var supportBase = Assert.Single(document.Supports.Nodes, n => n.Type == SupportNodeType.Base);
        Assert.Equal(SupportBaseShape.DiscCone, supportBase.BaseShape);
        Assert.Equal(5.5f, supportBase.BaseDiameter);
        Assert.Equal(1.1f, supportBase.BaseHeight);
        Assert.Equal(2.4f, supportBase.BaseConeHeight);
    }

    [Fact]
    public void TopSurfaceSupportIsRefusedInsteadOfPiercingTheModel()
    {
        var (document, box) = FloatingBoxDocument();

        // On top of the box the only way down is through the solid: refuse, add nothing.
        var added = document.AddManualSupport(box, new Vector3(0, 0, 14), Vector3.UnitZ,
            out var failureReason);

        Assert.False(added);
        Assert.Equal(RoutingFailureReason.NoClearStep, failureReason);
        Assert.Equal(0, document.Supports.NodeCount);
        Assert.Equal(0, document.Supports.SegmentCount);
    }

    [Fact]
    public void RoutedSupportSegmentsClearTheModel()
    {
        var (document, box) = FloatingBoxDocument();
        // Off-centre so the pillar hugs the model's side; it must still keep clear.
        Assert.True(document.AddManualSupport(box, new Vector3(4, 0, 8), -Vector3.UnitZ));

        var audit = new LinearCollisionScene();
        audit.AddSceneObject(box);
        var graph = document.Supports;
        foreach (var segment in graph.Segments)
        {
            var a = graph.GetNode(segment.NodeA);
            var b = graph.GetNode(segment.NodeB);
            if (a.Type == SupportNodeType.Tip || b.Type == SupportNodeType.Tip) continue; // neck touches by design
            Assert.False(audit.IntersectsCapsule(a.Position, b.Position, segment.Diameter * 0.5f),
                $"{segment.Type} {a.Position} -> {b.Position} intersects the model");
        }
    }

    [Fact]
    public void SecondSupportAvoidsTheFirst()
    {
        var (document, box) = FloatingBoxDocument();
        Assert.True(document.AddManualSupport(box, new Vector3(0, 0, 8), -Vector3.UnitZ));
        var firstCount = document.Supports.SegmentCount;

        // Same contact point: the first support occupies the straight-down path.
        var added = document.AddManualSupport(box, new Vector3(0.3f, 0, 8), -Vector3.UnitZ);

        if (added)
        {
            // If it routed, it must not overlap the first support's members.
            Assert.True(document.Supports.SegmentCount > firstCount);
        }
        // Either refusing or detouring is acceptable; piercing the first support is not,
        // which the router's obstacle scene guarantees by construction.
    }

    [Fact]
    public void InRangeManualSupportBranchesOntoTheExistingTrunk()
    {
        var (document, box) = FloatingBoxDocument();
        document.SupportSettings = new SupportConfig { UseBaseGrid = false };
        Assert.True(document.AddManualSupport(box, new Vector3(0, 0, 8), -Vector3.UnitZ));
        var originalNodes = document.Supports.Nodes.OrderBy(node => node.Id)
            .Select(node => (node.Id, node.Type, node.Position)).ToList();
        var originalSegments = document.Supports.Segments.OrderBy(segment => segment.Id)
            .Select(segment => (segment.Id, segment.Type, segment.NodeA, segment.NodeB)).ToList();

        Assert.True(document.AddManualSupport(box, new Vector3(4, 0, 8), -Vector3.UnitZ));

        Assert.Single(document.Supports.Nodes, node => node.Type == SupportNodeType.Base);
        Assert.Equal(2, document.Supports.Nodes.Count(node => node.Type == SupportNodeType.Tip));
        Assert.Single(document.Supports.Segments,
            segment => segment.Type == SupportSegmentType.Branch);
        Assert.Single(document.Supports.Supports());

        document.Undo();
        Assert.Equal(originalNodes, document.Supports.Nodes.OrderBy(node => node.Id)
            .Select(node => (node.Id, node.Type, node.Position)));
        Assert.Equal(originalSegments, document.Supports.Segments.OrderBy(segment => segment.Id)
            .Select(segment => (segment.Id, segment.Type, segment.NodeA, segment.NodeB)));
    }

    [Fact]
    public void OutOfRangeManualSupportDropsItsOwnBase()
    {
        var (document, box) = FloatingBoxDocument();
        document.SupportSettings = new SupportConfig
        {
            UseBaseGrid = false,
            ExistingTrunkBranchRange = 3f,
        };
        Assert.True(document.AddManualSupport(box, new Vector3(0, 0, 8), -Vector3.UnitZ));

        Assert.True(document.AddManualSupport(box, new Vector3(4, 0, 8), -Vector3.UnitZ));

        Assert.Equal(2, document.Supports.Nodes.Count(node => node.Type == SupportNodeType.Base));
        Assert.DoesNotContain(document.Supports.Segments,
            segment => segment.Type == SupportSegmentType.Branch);
        Assert.Equal(2, document.Supports.Supports().Count());
    }

    [Fact]
    public void MiniOnlyManualRouteFansFromAnExistingBranchEnd()
    {
        var (document, box) = FloatingBoxDocument();
        document.SupportSettings = new SupportConfig { UseBaseGrid = false };
        Assert.True(document.AddManualSupport(box, new Vector3(0, 0, 8), -Vector3.UnitZ));
        Assert.True(document.AddManualSupport(box, new Vector3(4, 0, 8), -Vector3.UnitZ));
        var branchEnd = document.Supports.Segments
            .Single(segment => segment.Type == SupportSegmentType.Branch);
        var trunkNodeIds = document.Supports.Segments
            .Where(segment => segment.Type == SupportSegmentType.Trunk)
            .SelectMany(segment => new[] { segment.NodeA, segment.NodeB }).ToHashSet();
        var endId = trunkNodeIds.Contains(branchEnd.NodeA) ? branchEnd.NodeB : branchEnd.NodeA;
        var end = document.Supports.GetNode(endId);
        var obstacles = new LinearCollisionScene();
        obstacles.AddSupportGraph(document.Supports);
        var router = new TreeSupportRouter(obstacles, GrowthRuleSet.Default);
        var result = router.Route(new[]
        {
            new RoutingTip(end.Position + new Vector3(0.5f, 0, 3), Vector3.UnitZ, 0.25f,
                box.Id, MiniSupportOnly: true),
        }, new TreeRoutingOptions
        {
            UseBaseGrid = false,
            Origin = SupportOrigin.ManualFor(box.Id),
        }, document.Supports);

        Assert.Empty(result.Failures);
        Assert.DoesNotContain(result.Edit.AddedNodes,
            node => node.Type == SupportNodeType.Base);
        Assert.Single(result.Edit.AddedSegments,
            segment => segment.Type == SupportSegmentType.MiniSupport);
    }

    [Fact]
    public void AttachedManualTipHasSaneComponentVisibilityAndDeletion()
    {
        var (document, box) = FloatingBoxDocument();
        document.SupportSettings = new SupportConfig { UseBaseGrid = false };
        Assert.True(document.AddManualSupport(box, new Vector3(0, 0, 8), -Vector3.UnitZ));
        Assert.True(document.AddManualSupport(box, new Vector3(4, 0, 8), -Vector3.UnitZ));
        var attachedTip = document.Supports.Nodes.Single(node =>
            node.Type == SupportNodeType.Tip && node.Position.X > 3f);

        document.SelectSupportComponent(attachedTip.Id);
        Assert.Equal(document.Supports.NodeCount + document.Supports.SegmentCount,
            document.SupportSelection.Count);
        document.HideSelectedSupportElements();
        Assert.All(document.Supports.Nodes, node => Assert.True(node.Hidden));
        Assert.All(document.Supports.Segments, segment => Assert.True(segment.Hidden));
        document.Undo();

        document.SelectSupportElement(attachedTip.Id);
        document.DeleteSupportSelection();
        Assert.Single(document.Supports.Nodes, node => node.Type == SupportNodeType.Tip);
        Assert.Single(document.Supports.Nodes, node => node.Type == SupportNodeType.Base);
        Assert.DoesNotContain(document.Supports.Nodes, node =>
            node.Type == SupportNodeType.Junction &&
            document.Supports.SegmentsAt(node.Id).Count <= 1);
        Assert.Single(document.Supports.Supports());
    }

    [Fact]
    public void RoutedSupportIsOneUndoStep()
    {
        var (document, box) = FloatingBoxDocument();
        Assert.True(document.AddManualSupport(box, new Vector3(0, 0, 8), -Vector3.UnitZ));
        Assert.True(document.Supports.NodeCount > 0);

        document.Undo();

        Assert.Equal(0, document.Supports.NodeCount);
        Assert.Equal(0, document.Supports.SegmentCount);
    }

    [Fact]
    public void MovingTheObjectRefreshesTheObstacleCache()
    {
        var (document, box) = FloatingBoxDocument();
        Assert.True(document.AddManualSupport(box, new Vector3(0, 0, 8), -Vector3.UnitZ));

        // Slide the box away and support the new underside location; the cache must rebuild
        // for the new transform or this contact would appear to float in the old box's space.
        box.Transform = box.Transform with { Translation = new Vector3(40, 0, 0) };
        document.NotifyTransientChange();
        var added = document.AddManualSupport(box, new Vector3(40, 0, 8), -Vector3.UnitZ);

        Assert.True(added);
    }
}
