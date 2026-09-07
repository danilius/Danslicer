using System.Numerics;
using Danslicer.Core.Supports;
using Danslicer.Core.Supports.Routing;

namespace Danslicer.Tests;

public sealed class MemberSeparationTests
{
    [Fact]
    public void CrossingMembersConflictUnlessTheyShareANode()
    {
        var shared = Guid.NewGuid();
        var crossing = MemberSeparation.AreTooClose(
            new(-1, -1, 0), new(1, 1, 0), 0,
            Guid.NewGuid(), Guid.NewGuid(),
            new(-1, 1, 0), new(1, -1, 0), 0,
            Guid.NewGuid(), Guid.NewGuid(), 0.01f);
        var incident = MemberSeparation.AreTooClose(
            Vector3.Zero, Vector3.UnitX, 0,
            shared, Guid.NewGuid(),
            Vector3.Zero, Vector3.UnitY, 0,
            shared, Guid.NewGuid(), 0.01f);

        Assert.True(crossing);
        Assert.False(incident);
    }

    [Fact]
    public void ParallelMembersInsideTheSurfaceGapConflict()
    {
        var close = MemberSeparation.AreTooClose(
            Vector3.Zero, Vector3.UnitX, 0.5f, Guid.NewGuid(), Guid.NewGuid(),
            new(0, 1.39f, 0), new(1, 1.39f, 0), 0.4f,
            Guid.NewGuid(), Guid.NewGuid(), 0.5f);
        var farEnough = MemberSeparation.AreTooClose(
            Vector3.Zero, Vector3.UnitX, 0.5f, Guid.NewGuid(), Guid.NewGuid(),
            new(0, 1.41f, 0), new(1, 1.41f, 0), 0.4f,
            Guid.NewGuid(), Guid.NewGuid(), 0.5f);

        Assert.True(close);
        Assert.False(farEnough);
    }

    [Fact]
    public void ThickMembersIntersectEvenWithNoAdditionalSurfaceGap()
    {
        var intersects = MemberSeparation.AreTooClose(
            Vector3.Zero, Vector3.UnitX, 0.6f, Guid.NewGuid(), Guid.NewGuid(),
            new(0, 0.6f, 0), new(1, 0.6f, 0), 0.6f,
            Guid.NewGuid(), Guid.NewGuid(), 0f);

        Assert.True(intersects);
    }

    [Fact]
    public void IncreasingDiameterCanMakeAnAcceptablePairConflict()
    {
        var thin = MemberSeparation.AreTooClose(
            Vector3.Zero, Vector3.UnitX, 0.2f, Guid.NewGuid(), Guid.NewGuid(),
            new(0, 1.4f, 0), new(1, 1.4f, 0), 0.2f,
            Guid.NewGuid(), Guid.NewGuid(), 0.5f);
        var thick = MemberSeparation.AreTooClose(
            Vector3.Zero, Vector3.UnitX, 0.5f, Guid.NewGuid(), Guid.NewGuid(),
            new(0, 1.4f, 0), new(1, 1.4f, 0), 0.5f,
            Guid.NewGuid(), Guid.NewGuid(), 0.5f);

        Assert.False(thin);
        Assert.True(thick);
    }

    [Fact]
    public void ReportingMetricIgnoresMemberDiameterWhileIntersectionCountDoesNot()
    {
        var thin = ParallelMembers(0.1f);
        var thick = ParallelMembers(1.2f);

        Assert.Equal(MemberSeparation.CountPairs(thin, 1f),
            MemberSeparation.CountPairs(thick, 1f));
        Assert.Equal(0, MemberSeparation.CountIntersections(thin));
        Assert.Equal(1, MemberSeparation.CountIntersections(thick));
    }

    [Fact]
    public void ZeroSettingIsBitIdenticalAndEnabledSettingRefusesTheNearCrossing()
    {
        var existing = ExistingMember();
        var tips = new[] { new RoutingTip(new(0.7f, 0, 10), Vector3.UnitZ, 0.4f) };
        var router = new TreeSupportRouter(new LinearCollisionScene(), GrowthRuleSet.Default);
        var baseline = router.Route(tips, new TreeRoutingOptions
        {
            UseBaseGrid = false,
            PreferExistingTrunks = false,
            TrunkDiameter = 0.1f,
            BranchDiameter = 0.1f,
            MaxBranchLength = 0.001f,
        }, existing);
        var explicitZero = router.Route(tips, new TreeRoutingOptions
        {
            UseBaseGrid = false,
            PreferExistingTrunks = false,
            TrunkDiameter = 0.1f,
            BranchDiameter = 0.1f,
            MaxBranchLength = 0.001f,
            MinMemberSeparationMm = 0,
        }, existing);
        var separated = router.Route(tips, new TreeRoutingOptions
        {
            UseBaseGrid = false,
            PreferExistingTrunks = false,
            TrunkDiameter = 0.1f,
            BranchDiameter = 0.1f,
            MaxBranchLength = 0.001f,
            MinMemberSeparationMm = 1f,
        }, existing);

        AssertGraphsEqual(baseline.Graph, explicitZero.Graph);
        Assert.Empty(baseline.Failures);
        Assert.True(MemberSeparation.CountPairs(baseline.Graph, 1f) > 0);
        Assert.Equal(RoutingFailureReason.MemberCrossing,
            Assert.Single(separated.Failures).Reason);
        Assert.Equal(0, MemberSeparation.CountPairs(separated.Graph, 1f));
    }

    [Fact]
    public void EnabledSeparationRoutingIsDeterministic()
    {
        var existing = ExistingMember();
        var tip = new RoutingTip(new(0.7f, 0, 10), Vector3.UnitZ, 0.4f);
        var options = new TreeRoutingOptions
        {
            UseBaseGrid = false,
            PreferExistingTrunks = false,
            TrunkDiameter = 0.1f,
            BranchDiameter = 0.1f,
            MaxBranchLength = 0.001f,
            MinMemberSeparationMm = 1f,
            Seed = 19,
        };
        var router = new TreeSupportRouter(new LinearCollisionScene(), GrowthRuleSet.Default);

        var first = router.Route([tip], options, existing);
        var second = router.Route([tip], options, existing);

        AssertGraphsEqual(first.Graph, second.Graph);
        Assert.Equal(first.Failures, second.Failures);
    }

    [Fact]
    public void RoutedBranchDoesNotLeaveItsTipTooCloseToItsOwnTrunk()
    {
        // 1.2 mm off the drop line: beyond snapping, so the tip gets a 45° branch to the
        // lattice trunk, whose centreline then passes 1.7 mm from the tip member. A 1 mm
        // surface gap between 1.2 mm members needs 2.2 mm, so the separated route must take
        // a shallower branch that lowers the trunk top and opens the gap.
        var tip = new RoutingTip(new(1.2f, 0, 10), Vector3.UnitZ, 0.4f);
        var router = new TreeSupportRouter(new LinearCollisionScene(), GrowthRuleSet.Default);
        var baseline = router.Route([tip], new TreeRoutingOptions { BaseGridPitch = 4f });
        var separated = router.Route([tip], new TreeRoutingOptions
        {
            BaseGridPitch = 4f,
            MinMemberSeparationMm = 1f,
        });

        Assert.True(MemberSeparation.CountPairs(baseline.Graph, 2.2f) > 0);
        Assert.Equal(0, MemberSeparation.CountPairs(separated.Graph, 2.2f));
        Assert.Empty(separated.Failures);
    }

    private static SupportGraph ExistingMember()
    {
        var graph = new SupportGraph();
        var low = new SupportNode
            { Id = Guid.Parse("10000000-0000-0000-0000-000000000001"), Type = SupportNodeType.Junction,
                Position = Vector3.Zero };
        var high = new SupportNode
            { Id = Guid.Parse("10000000-0000-0000-0000-000000000002"), Type = SupportNodeType.Junction,
                Position = new Vector3(0, 0, 8) };
        graph.AddNode(low);
        graph.AddNode(high);
        graph.AddSegment(new SupportSegment
        {
            Id = Guid.Parse("20000000-0000-0000-0000-000000000001"),
            Type = SupportSegmentType.Bracing,
            NodeA = low.Id,
            NodeB = high.Id,
            Diameter = 0.01f,
        });
        return graph;
    }

    private static SupportGraph ParallelMembers(float diameter)
    {
        var graph = new SupportGraph();
        var positions = new[]
        {
            Vector3.Zero, Vector3.UnitX,
            new Vector3(0, 0.6f, 0), new Vector3(1, 0.6f, 0),
        };
        var nodes = positions.Select((position, index) => new SupportNode
        {
            Id = Guid.Parse($"10000000-0000-0000-0000-{index + 1:000000000000}"),
            Type = SupportNodeType.Junction,
            Position = position,
        }).ToArray();
        foreach (var node in nodes) graph.AddNode(node);
        for (var index = 0; index < 2; index++)
        {
            graph.AddSegment(new SupportSegment
            {
                Id = Guid.Parse($"20000000-0000-0000-0000-{index + 1:000000000000}"),
                Type = SupportSegmentType.Branch,
                NodeA = nodes[index * 2].Id,
                NodeB = nodes[index * 2 + 1].Id,
                Diameter = diameter,
            });
        }
        return graph;
    }

    private static void AssertGraphsEqual(SupportGraph expected, SupportGraph actual)
    {
        Assert.Equal(
            expected.Nodes.OrderBy(node => node.Id)
                .Select(node => (node.Id, node.Type, node.Position)),
            actual.Nodes.OrderBy(node => node.Id)
                .Select(node => (node.Id, node.Type, node.Position)));
        Assert.Equal(
            expected.Segments.OrderBy(segment => segment.Id)
                .Select(segment => (segment.Id, segment.Type, segment.NodeA, segment.NodeB,
                    segment.Diameter)),
            actual.Segments.OrderBy(segment => segment.Id)
                .Select(segment => (segment.Id, segment.Type, segment.NodeA, segment.NodeB,
                    segment.Diameter)));
    }
}
