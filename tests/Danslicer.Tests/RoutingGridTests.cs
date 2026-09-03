using System.Numerics;
using Danslicer.Core.Supports;
using Danslicer.Core.Supports.Routing;

namespace Danslicer.Tests;

public sealed class RoutingGridTests
{
    [Fact]
    public void SingleTipProducesBasePillarNeckAndTip()
    {
        var router = new GridSupportRouter(new LinearCollisionScene(), GrowthRuleSet.Default);

        var result = router.Route(new[] { new RoutingTip(new(1, 1, 10), -Vector3.UnitZ, 0.4f) },
            new GridRoutingOptions { Spacing = 5, Seed = 42 });

        Assert.Empty(result.UnroutedTips);
        Assert.Single(result.BasePositions);
        Assert.Equal(3, result.Graph.NodeCount);
        Assert.Equal(2, result.Graph.SegmentCount);
        Assert.Contains(result.Graph.Nodes, n => n.Type == SupportNodeType.Base && n.Position == Vector3.Zero);
        Assert.Contains(result.Graph.Segments, s => s.Type == SupportSegmentType.Pillar);
        Assert.Contains(result.Graph.Segments, s => s.Type == SupportSegmentType.Neck);
        Assert.InRange(result.MaxLeanAngleDegrees, 0, 35.001f);
    }

    [Fact]
    public void TipsOnSameGridCellShareOneTrunk()
    {
        var router = new GridSupportRouter(new LinearCollisionScene(), GrowthRuleSet.Default);
        var tips = new[]
        {
            new RoutingTip(new(1, 0, 10), -Vector3.UnitZ, 0.4f),
            new RoutingTip(new(0, 1, 12), -Vector3.UnitZ, 0.4f),
        };

        var result = router.Route(tips, new GridRoutingOptions { Spacing = 5 });

        Assert.Single(result.BasePositions);
        Assert.Equal(2, result.Graph.Nodes.Count(n => n.Type == SupportNodeType.Tip));
        Assert.Contains(result.Graph.Segments, s => s.Type == SupportSegmentType.Trunk);
    }

    [Fact]
    public void BlockedNearestLatticePointFallsBackToNeighbour()
    {
        var scene = new LinearCollisionScene();
        scene.AddCapsule(new(0, 0, 0), new(0, 0, 9), 1);
        var router = new GridSupportRouter(scene, GrowthRuleSet.Default);

        var result = router.Route(new[] { new RoutingTip(new(1, 0, 10), -Vector3.UnitZ, 0.4f) },
            new GridRoutingOptions { Spacing = 5, CandidateRingCount = 2 });

        Assert.Empty(result.UnroutedTips);
        Assert.DoesNotContain(Vector3.Zero, result.BasePositions);
    }

    [Fact]
    public void SameSeedAndInputProduceIdenticalGraph()
    {
        var options = new GridRoutingOptions
        {
            Lattice = BaseLatticeType.Hexagonal,
            Spacing = 4,
            Offset = new(0.5f, -0.25f),
            RotationDegrees = 17,
            Seed = 8675309,
        };
        var tips = new[]
        {
            new RoutingTip(new(2, 2, 14), -Vector3.UnitZ, 0.35f),
            new RoutingTip(new(-3, 1, 11), -Vector3.UnitZ, 0.45f),
        };
        var router = new GridSupportRouter(new LinearCollisionScene(), GrowthRuleSet.Default);

        var first = router.Route(tips, options);
        var second = router.Route(tips, options);

        Assert.Equal(Snapshot(first.Graph), Snapshot(second.Graph));
    }

    [Fact]
    public void BranchRuleCanLeaveTipUnrouted()
    {
        var rules = GrowthRuleSet.Default;
        rules.Find<BranchGrowthRule>()!.TriggerDistance = 0.1f;
        var router = new GridSupportRouter(new LinearCollisionScene(), rules);

        var result = router.Route(new[] { new RoutingTip(new(2, 2, 10), -Vector3.UnitZ, 0.4f) },
            new GridRoutingOptions { Spacing = 5, CandidateRingCount = 0 });

        Assert.Single(result.UnroutedTips);
        Assert.Empty(result.Graph.Nodes);
    }

    [Fact]
    public void BranchLimitMovesAdditionalTipToAnotherBase()
    {
        var rules = GrowthRuleSet.Default;
        rules.Find<BranchGrowthRule>()!.MaxBranchesPerTrunk = 1;
        var router = new GridSupportRouter(new LinearCollisionScene(), rules);
        var tips = new[]
        {
            new RoutingTip(new(0.5f, 0, 10), -Vector3.UnitZ, 0.4f),
            new RoutingTip(new(0, 0.5f, 10), -Vector3.UnitZ, 0.4f),
        };

        var result = router.Route(tips, new GridRoutingOptions { Spacing = 5 });

        Assert.Empty(result.UnroutedTips);
        Assert.Equal(2, result.BasePositions.Count);
    }

    [Fact]
    public void SnapToleranceAlignsJunctionBelowNearbyTip()
    {
        var tip = new RoutingTip(new(0.2f, 0.1f, 10), -Vector3.UnitZ, 0.4f);
        var router = new GridSupportRouter(new LinearCollisionScene(), GrowthRuleSet.Default);

        var snapped = router.Route(new[] { tip }, new GridRoutingOptions { SnapTolerance = 0.25f });
        var unsnapped = router.Route(new[] { tip }, new GridRoutingOptions { SnapTolerance = 0 });
        var snappedJunction = Assert.Single(snapped.Graph.Nodes, n => n.Type == SupportNodeType.Junction);
        var unsnappedJunction = Assert.Single(unsnapped.Graph.Nodes, n => n.Type == SupportNodeType.Junction);

        Assert.Equal(tip.SurfacePoint.X, snappedJunction.Position.X);
        Assert.Equal(tip.SurfacePoint.Y, snappedJunction.Position.Y);
        Assert.Equal(Vector3.Zero.X, unsnappedJunction.Position.X);
        Assert.Equal(Vector3.Zero.Y, unsnappedJunction.Position.Y);
    }

    private static string Snapshot(SupportGraph graph) => string.Join('|',
        graph.Nodes.OrderBy(n => n.Id).Select(n => $"N:{n.Id}:{n.Type}:{n.Position}")
            .Concat(graph.Segments.OrderBy(s => s.Id)
                .Select(s => $"S:{s.Id}:{s.Type}:{s.NodeA}:{s.NodeB}:{s.Diameter}")));
}
