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
    public void ParallelMembersInsideTheSurfaceGapThresholdConflict()
    {
        var close = MemberSeparation.AreTooClose(
            Vector3.Zero, Vector3.UnitX, 0.2f, Guid.NewGuid(), Guid.NewGuid(),
            new(0, 1.4f, 0), new(1, 1.4f, 0), 0.2f, Guid.NewGuid(), Guid.NewGuid(), 1.01f);
        var farEnough = MemberSeparation.AreTooClose(
            Vector3.Zero, Vector3.UnitX, 0.2f, Guid.NewGuid(), Guid.NewGuid(),
            new(0, 1.4f, 0), new(1, 1.4f, 0), 0.2f, Guid.NewGuid(), Guid.NewGuid(), 0.99f);

        Assert.True(close);
        Assert.False(farEnough);
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
