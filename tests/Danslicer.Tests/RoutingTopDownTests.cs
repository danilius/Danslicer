using System.Numerics;
using Danslicer.Core.Supports;
using Danslicer.Core.Supports.Routing;

namespace Danslicer.Tests;

public sealed class RoutingTopDownTests
{
    [Fact]
    public void SingleTipDescendsToPlateInBoundedSteps()
    {
        var router = new TopDownSupportRouter(new LinearCollisionScene(), GrowthRuleSet.Default);

        var result = router.Route(new[] { new RoutingTip(new(2, -1, 9), -Vector3.UnitZ, 0.35f) },
            new TopDownRoutingOptions { StepHeight = 2, Seed = 42 });

        Assert.Empty(result.UnroutedTips);
        Assert.Single(result.BasePositions);
        Assert.Equal(0, result.BasePositions[0].Z);
        Assert.Single(result.Graph.Segments, segment => segment.Type == SupportSegmentType.Neck);
        Assert.All(result.Graph.Segments.Where(segment => segment.Type == SupportSegmentType.Pillar),
            segment => Assert.InRange(MathF.Abs(result.Graph.GetNode(segment.NodeA).Position.Z -
                                                result.Graph.GetNode(segment.NodeB).Position.Z), 0.99f, 2.01f));
        Assert.Equal(Vector3.UnitZ,
            Assert.Single(result.Graph.Nodes, node => node.Type == SupportNodeType.Tip).SurfaceNormal);
    }

    [Fact]
    public void NearbyTipsMergeAndPromoteSharedPathToConfiguredTrunk()
    {
        var rules = GrowthRuleSet.Default;
        rules.Find<MergeGrowthRule>()!.ResultingTrunkDiameter = 2.1f;
        var router = new TopDownSupportRouter(new LinearCollisionScene(), rules);
        var tips = new[]
        {
            new RoutingTip(new(0, 0, 10), -Vector3.UnitZ, 0.4f),
            new RoutingTip(new(1, 0, 10), -Vector3.UnitZ, 0.4f),
        };

        var result = router.Route(tips, new TopDownRoutingOptions { StepHeight = 2 });

        Assert.Empty(result.UnroutedTips);
        Assert.Single(result.BasePositions);
        Assert.Single(result.Graph.Supports());
        var trunks = result.Graph.Segments.Where(segment => segment.Type == SupportSegmentType.Trunk).ToList();
        Assert.NotEmpty(trunks);
        Assert.All(trunks, trunk => Assert.Equal(2.1f, trunk.Diameter));
    }

    [Fact]
    public void BlockedVerticalStepUsesLeanLimitedDetour()
    {
        var scene = new LinearCollisionScene();
        scene.AddTriangle(new(-0.1f, -0.1f, 7), new(0.1f, -0.1f, 7), new(0, 0.1f, 7));
        var rules = GrowthRuleSet.Default;
        rules.Find<ClearanceGrowthRule>()!.Enabled = false;
        var router = new TopDownSupportRouter(scene, rules);

        var result = router.Route(new[] { new RoutingTip(new(0, 0, 10), -Vector3.UnitZ, 0.4f) },
            new TopDownRoutingOptions
            {
                StepHeight = 2, PillarDiameter = 0.6f, DetourRings = 3, DirectionsPerRing = 16,
            });

        Assert.Empty(result.UnroutedTips);
        Assert.InRange(result.MaxLeanAngleDegrees, 0.01f, 45.001f);
    }

    [Fact]
    public void SameSeedAndInputProduceIdenticalGraph()
    {
        var options = new TopDownRoutingOptions { StepHeight = 1.5f, Seed = 8675309 };
        var tips = new[]
        {
            new RoutingTip(new(-3, 1, 11), -Vector3.UnitZ, 0.35f),
            new RoutingTip(new(4, 2, 13), -Vector3.UnitZ, 0.45f),
        };
        var router = new TopDownSupportRouter(new LinearCollisionScene(), GrowthRuleSet.Default);

        Assert.Equal(Snapshot(router.Route(tips, options).Graph),
            Snapshot(router.Route(tips, options).Graph));
    }

    private static string Snapshot(SupportGraph graph) => string.Join('|',
        graph.Nodes.OrderBy(node => node.Id).Select(node => $"N:{node.Id}:{node.Type}:{node.Position}")
            .Concat(graph.Segments.OrderBy(segment => segment.Id)
                .Select(segment => $"S:{segment.Id}:{segment.Type}:{segment.NodeA}:{segment.NodeB}:{segment.Diameter}")));
}
