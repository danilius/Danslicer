using System.Numerics;
using Danslicer.Core.Supports;
using Danslicer.Core.Supports.Routing;

namespace Danslicer.Tests;

public sealed class RoutingTreeTests
{
    private static RoutingResult Route(IEnumerable<RoutingTip> tips,
        TreeRoutingOptions? options = null, ICollisionScene? scene = null)
        => new TreeSupportRouter(scene ?? new LinearCollisionScene(), GrowthRuleSet.Default)
            .Route(tips, options ?? new TreeRoutingOptions());

    [Fact]
    public void FlatUndersideTipBecomesTipTrunkAndDiscBase()
    {
        var result = Route(new[] { new RoutingTip(new(3, 4, 10), Vector3.UnitZ, 0.4f) });

        Assert.Empty(result.Failures);
        Assert.Equal(3, result.Graph.NodeCount);
        Assert.Equal(2, result.Graph.SegmentCount);

        var tipSegment = Assert.Single(result.Graph.Segments,
            s => s.Type == SupportSegmentType.Tip);
        var trunk = Assert.Single(result.Graph.Segments, s => s.Type == SupportSegmentType.Trunk);
        var baseNode = Assert.Single(result.Graph.Nodes, n => n.Type == SupportNodeType.Base);

        // Tip member drops vertically for its 2 mm length; the trunk continues to the plate.
        var junction = Assert.Single(result.Graph.Nodes, n => n.Type == SupportNodeType.Junction);
        Assert.Equal(new Vector3(3, 4, 8), junction.Position);
        Assert.Equal(new Vector3(3, 4, 0), baseNode.Position);
        Assert.Equal(SupportBaseShape.Disc, baseNode.BaseShape);
        Assert.Equal(4f, baseNode.BaseDiameter);
        // Taper rule: the tip member is thinner than the trunk.
        Assert.True(tipSegment.Diameter < trunk.Diameter);
    }

    [Fact]
    public void SteepContactLeavesAtTheClampedMemberAngle()
    {
        // Outward normal 73° from straight down: the tip member is clamped to 45°.
        var outward = Vector3.Normalize(new Vector3(1, 0, -0.3f));
        var result = Route(new[] { new RoutingTip(new(0, 0, 10), -outward, 0.4f) });

        Assert.Empty(result.Failures);
        var tipNode = Assert.Single(result.Graph.Nodes, n => n.Type == SupportNodeType.Tip);
        var member = Assert.Single(result.Graph.SegmentsAt(tipNode.Id));
        var otherId = member.NodeA == tipNode.Id ? member.NodeB : member.NodeA;
        var direction = Vector3.Normalize(
            result.Graph.GetNode(otherId).Position - tipNode.Position);

        Assert.Equal(-MathF.Cos(MathF.PI / 4), direction.Z, 3);
        Assert.Equal(MathF.Sin(MathF.PI / 4), direction.X, 3);
    }

    [Fact]
    public void NearbyTipsShareOneTrunkThroughABranch()
    {
        var result = Route(new[]
        {
            new RoutingTip(new(0, 0, 10), Vector3.UnitZ, 0.4f),
            new RoutingTip(new(2, 0, 8), Vector3.UnitZ, 0.4f),
        });

        Assert.Empty(result.Failures);
        Assert.Single(result.BasePositions);
        Assert.Equal(2, result.Graph.Segments.Count(s => s.Type == SupportSegmentType.Tip));
        var branch = Assert.Single(result.Graph.Segments,
            s => s.Type == SupportSegmentType.Branch);
        // The shared trunk is split at the attachment junction.
        Assert.Equal(2, result.Graph.Segments.Count(s => s.Type == SupportSegmentType.Trunk));

        // The branch descends at the 45° member angle onto the trunk line x = 0.
        var a = result.Graph.GetNode(branch.NodeA).Position;
        var b = result.Graph.GetNode(branch.NodeB).Position;
        var (high, low) = a.Z >= b.Z ? (a, b) : (b, a);
        Assert.Equal(new Vector3(2, 0, 6), high);
        Assert.Equal(0f, low.X, 2);
        Assert.Equal(0f, low.Y, 2);
        Assert.Equal(4f, low.Z, 2);
    }

    [Fact]
    public void BothSupportsRemainConnectedComponentsOfOneTree()
    {
        var result = Route(new[]
        {
            new RoutingTip(new(0, 0, 10), Vector3.UnitZ, 0.4f),
            new RoutingTip(new(2, 0, 8), Vector3.UnitZ, 0.4f),
        });

        var anyNode = result.Graph.Nodes.First();
        var component = result.Graph.Component(anyNode.Id);
        Assert.Equal(result.Graph.NodeCount, component.Nodes.Count);
        Assert.Equal(result.Graph.SegmentCount, component.Segments.Count);
    }

    [Fact]
    public void BlockedColumnSwingsOneBranchToAClearDropLine()
    {
        // A 3 x 3 shelf at z = 5 blocks the straight drop; one 45° branch must clear it.
        var scene = new LinearCollisionScene();
        scene.AddTriangle(new(-1.5f, -1.5f, 5), new(1.5f, -1.5f, 5), new(1.5f, 1.5f, 5));
        scene.AddTriangle(new(-1.5f, -1.5f, 5), new(1.5f, 1.5f, 5), new(-1.5f, 1.5f, 5));

        var result = Route(new[] { new RoutingTip(new(0, 0, 10), Vector3.UnitZ, 0.4f) },
            scene: scene);

        Assert.Empty(result.Failures);
        Assert.Single(result.Graph.Segments, s => s.Type == SupportSegmentType.Branch);
        Assert.Single(result.Graph.Segments, s => s.Type == SupportSegmentType.Trunk);
        var baseNode = Assert.Single(result.Graph.Nodes, n => n.Type == SupportNodeType.Base);
        Assert.True(new Vector2(baseNode.Position.X, baseNode.Position.Y).Length() > 1.5f);
    }

    [Fact]
    public void FullyBlockedTipRefusesInsteadOfLandingOnTheModel()
    {
        // A wide floor at z = 5 no branch can clear: the tip must refuse, never land on it.
        var scene = new LinearCollisionScene();
        scene.AddTriangle(new(-40, -40, 5), new(40, -40, 5), new(40, 40, 5));
        scene.AddTriangle(new(-40, -40, 5), new(40, 40, 5), new(-40, 40, 5));

        var result = Route(new[] { new RoutingTip(new(0, 0, 10), Vector3.UnitZ, 0.4f) },
            scene: scene);

        var failure = Assert.Single(result.Failures);
        Assert.Equal(RoutingFailureReason.NoClearStep, failure.Reason);
        Assert.Empty(result.Graph.Segments);
        Assert.Empty(result.Graph.Nodes);
    }

    [Fact]
    public void ContactBelowThePlateIsRefused()
    {
        var result = Route(new[] { new RoutingTip(new(0, 0, 0), Vector3.UnitZ, 0.4f) });
        Assert.Equal(RoutingFailureReason.BelowPlate, Assert.Single(result.Failures).Reason);
    }

    [Fact]
    public void VeryLowContactConnectsTipStraightToTheBase()
    {
        var result = Route(new[] { new RoutingTip(new(0, 0, 1), Vector3.UnitZ, 0.4f) });

        Assert.Empty(result.Failures);
        var segment = Assert.Single(result.Graph.Segments);
        Assert.Equal(SupportSegmentType.Tip, segment.Type);
        var baseNode = Assert.Single(result.Graph.Nodes, n => n.Type == SupportNodeType.Base);
        Assert.Equal(SupportBaseShape.Disc, baseNode.BaseShape);
    }

    [Fact]
    public void ConeShapeParametersFlowOntoTheTipNode()
    {
        var result = Route(new[]
        {
            new RoutingTip(new(0, 0, 10), Vector3.UnitZ, 0.5f,
                TipShape: SupportTipShape.Cone, ConeLength: 1.5f, BallDiameter: 0.7f),
        });

        var tipNode = Assert.Single(result.Graph.Nodes, n => n.Type == SupportNodeType.Tip);
        Assert.Equal(SupportTipShape.Cone, tipNode.TipShape);
        Assert.Equal(1.5f, tipNode.ConeLength);
        Assert.Equal(0.7f, tipNode.BallDiameter);
        Assert.Equal(0.5f, tipNode.TipDiameter);
    }

    [Fact]
    public void BaseShapeNoneEmitsBareBases()
    {
        var result = Route(new[] { new RoutingTip(new(0, 0, 10), Vector3.UnitZ, 0.4f) },
            new TreeRoutingOptions { BaseShape = SupportBaseShape.None });

        var baseNode = Assert.Single(result.Graph.Nodes, n => n.Type == SupportNodeType.Base);
        Assert.Equal(SupportBaseShape.None, baseNode.BaseShape);
    }

    [Fact]
    public void SameSeedProducesIdenticalGraphs()
    {
        var tips = new[]
        {
            new RoutingTip(new(0, 0, 10), Vector3.UnitZ, 0.4f),
            new RoutingTip(new(2, 0, 8), Vector3.Normalize(new Vector3(-1, 0, 1)), 0.4f),
            new RoutingTip(new(-4, 3, 6), Vector3.UnitZ, 0.4f),
        };

        var first = Route(tips, new TreeRoutingOptions { Seed = 7 });
        var second = Route(tips, new TreeRoutingOptions { Seed = 7 });

        Assert.Equal(
            first.Graph.Nodes.OrderBy(n => n.Id).Select(n => (n.Id, n.Type, n.Position)),
            second.Graph.Nodes.OrderBy(n => n.Id).Select(n => (n.Id, n.Type, n.Position)));
        Assert.Equal(
            first.Graph.Segments.OrderBy(s => s.Id).Select(s => (s.Id, s.Type, s.NodeA, s.NodeB)),
            second.Graph.Segments.OrderBy(s => s.Id).Select(s => (s.Id, s.Type, s.NodeA, s.NodeB)));
    }

    [Fact]
    public void EveryTrunkIsVerticalAndEveryAngledMemberRespectsTheLimit()
    {
        var tips = new[]
        {
            new RoutingTip(new(0, 0, 12), Vector3.UnitZ, 0.4f),
            new RoutingTip(new(1.5f, 1, 9), Vector3.Normalize(new Vector3(1, 1, 1)), 0.4f),
            new RoutingTip(new(-3, 2, 7), Vector3.UnitZ, 0.4f),
            new RoutingTip(new(4, -2, 5), Vector3.UnitZ, 0.4f),
        };
        var result = Route(tips);

        Assert.Empty(result.Failures);
        foreach (var segment in result.Graph.Segments)
        {
            var a = result.Graph.GetNode(segment.NodeA).Position;
            var b = result.Graph.GetNode(segment.NodeB).Position;
            var delta = b - a;
            var lean = MathF.Atan2(new Vector2(delta.X, delta.Y).Length(), MathF.Abs(delta.Z))
                * 180 / MathF.PI;
            if (segment.Type == SupportSegmentType.Trunk)
                Assert.True(lean < 0.01f, $"trunk leans {lean}°");
            else
                Assert.True(lean <= 45.01f, $"{segment.Type} leans {lean}°");
        }
    }
}
