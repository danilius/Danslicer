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
    public void EachMemberUsesItsConfiguredParentDiameter()
    {
        var rules = GrowthRuleSet.Default;
        rules.Find<TaperGrowthRule>()!.TipToPillarDiameterRatio = 0.5f;
        var result = new TreeSupportRouter(new LinearCollisionScene(), rules).Route(new[]
        {
            new RoutingTip(new(0, 0, 10), Vector3.UnitZ, 0.4f),
            new RoutingTip(new(2, 0, 8), Vector3.UnitZ, 0.4f),
        }, new TreeRoutingOptions { TrunkDiameter = 0.8f, BranchDiameter = 1.6f });

        Assert.Empty(result.Failures);
        Assert.All(result.Graph.Segments.Where(s => s.Type == SupportSegmentType.Trunk),
            segment => Assert.Equal(0.8f, segment.Diameter));
        Assert.All(result.Graph.Segments.Where(s => s.Type == SupportSegmentType.Branch),
            segment => Assert.Equal(1.6f, segment.Diameter));

        var directTip = result.Graph.Nodes.Single(n =>
            n.Type == SupportNodeType.Tip && n.Position.X == 0);
        var branchedTip = result.Graph.Nodes.Single(n =>
            n.Type == SupportNodeType.Tip && n.Position.X == 2);
        Assert.Equal(0.4f, Assert.Single(result.Graph.SegmentsAt(directTip.Id)).Diameter);
        Assert.Equal(0.8f, Assert.Single(result.Graph.SegmentsAt(branchedTip.Id)).Diameter);
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
    public void SiblingBranchesMayFuseNearTheirSharedTrunk()
    {
        var result = Route(new[]
        {
            new RoutingTip(new(0, 0, 12), Vector3.UnitZ, 0.4f),
            new RoutingTip(new(2, 0, 10), Vector3.UnitZ, 0.4f),
            new RoutingTip(new(0, 2, 9), Vector3.UnitZ, 0.4f),
        });

        Assert.Empty(result.Failures);
        Assert.Single(result.BasePositions);
        Assert.Equal(2, result.Graph.Segments.Count(
            segment => segment.Type == SupportSegmentType.Branch));
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
    public void BranchFanTriesShallowerAnglesWhenMaximumAngleIsBlocked()
    {
        var result = Route(new[] { new RoutingTip(new(0, 0, 10), Vector3.UnitZ, 0.4f) },
            scene: new SteepBranchBlockScene());

        Assert.Empty(result.Failures);
        var branch = Assert.Single(result.Graph.Segments,
            segment => segment.Type == SupportSegmentType.Branch);
        var a = result.Graph.GetNode(branch.NodeA).Position;
        var b = result.Graph.GetNode(branch.NodeB).Position;
        var delta = b - a;
        var lean = MathF.Atan2(new Vector2(delta.X, delta.Y).Length(), MathF.Abs(delta.Z))
            * 180 / MathF.PI;
        Assert.Equal(30f, lean, 2);
    }

    private sealed class SteepBranchBlockScene : ICollisionScene
    {
        public bool IntersectsCapsule(Vector3 start, Vector3 end, float radius,
            Func<object?, bool>? obstacleFilter = null)
        {
            var delta = end - start;
            var horizontal = new Vector2(delta.X, delta.Y).Length();
            if (horizontal <= 1e-4f)
                return MathF.Min(start.Z, end.Z) <= 0.01f &&
                    new Vector2(start.X, start.Y).Length() <= 1e-4f;
            var lean = MathF.Atan2(horizontal, MathF.Abs(delta.Z)) * 180 / MathF.PI;
            return lean > 35f;
        }

        public ObstacleNearestPoint? NearestObstacle(Vector3 point,
            Func<object?, bool>? obstacleFilter = null) => null;

        public ObstacleRayHit? Raycast(Vector3 origin, Vector3 direction, float maxDistance,
            Func<object?, bool>? obstacleFilter = null) => null;
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
    public void BaseDiscShrinksToClearNearbyModelGeometry()
    {
        // A low wall 1.2 mm from the drop line: the trunk clears it but the full 2 mm-radius
        // disc cannot. The disc must shrink, not sink into the model and not refuse the drop.
        var scene = new LinearCollisionScene();
        scene.AddTriangle(new(1.2f, -3, 0), new(1.2f, 3, 0), new(1.2f, 0, 2));

        var result = Route(new[] { new RoutingTip(new(0, 0, 10), Vector3.UnitZ, 0.4f) },
            scene: scene);

        Assert.Empty(result.Failures);
        var baseNode = Assert.Single(result.Graph.Nodes, n => n.Type == SupportNodeType.Base);
        Assert.Equal(SupportBaseShape.Disc, baseNode.BaseShape);
        Assert.True(baseNode.BaseDiameter < 4f, $"disc did not shrink: {baseNode.BaseDiameter}");
        // The fitted disc really clears the wall (plus the 0.25 model clearance).
        Assert.True(baseNode.BaseDiameter * 0.5f + 0.25f <= 1.2f + 1e-3f,
            $"fitted diameter {baseNode.BaseDiameter} still overlaps the wall");
    }

    [Fact]
    public void BaseShrinksToMemberWidthInTightSpots()
    {
        // Walls 0.7 mm away on both sides: only a member-width disc fits (the trunk itself
        // proved that width clear). A disc always survives at least at the member diameter.
        var scene = new LinearCollisionScene();
        scene.AddTriangle(new(0.7f, -3, 0), new(0.7f, 3, 0), new(0.7f, 0, 2));
        scene.AddTriangle(new(-0.7f, -3, 0), new(-0.7f, 3, 0), new(-0.7f, 0, 2));

        var result = Route(new[] { new RoutingTip(new(0, 0, 10), Vector3.UnitZ, 0.4f) },
            new TreeRoutingOptions { TrunkDiameter = 0.6f, BranchDiameter = 0.6f },
            scene);

        Assert.Empty(result.Failures);
        var baseNode = Assert.Single(result.Graph.Nodes, n => n.Type == SupportNodeType.Base);
        Assert.Equal(SupportBaseShape.Disc, baseNode.BaseShape);
        Assert.Equal(0.6f, baseNode.BaseDiameter, 3);
    }

    [Fact]
    public void RoughContactFallsBackToAShortTipMember()
    {
        // Every full-length departure is blocked near the contact (spiky terrain, e.g. teeth);
        // the short-member fallback must still get the support off the surface and route.
        var contact = new Vector3(0, 0, 10);
        var result = new TreeSupportRouter(new NearContactBlockScene(contact),
            GrowthRuleSet.Default).Route(
            new[] { new RoutingTip(contact, Vector3.UnitZ, 0.4f) }, new TreeRoutingOptions());

        Assert.Empty(result.Failures);
        var tipNode = Assert.Single(result.Graph.Nodes, n => n.Type == SupportNodeType.Tip);
        var member = Assert.Single(result.Graph.SegmentsAt(tipNode.Id));
        var otherId = member.NodeA == tipNode.Id ? member.NodeB : member.NodeA;
        var length = Vector3.Distance(tipNode.Position, result.Graph.GetNode(otherId).Position);
        Assert.True(length < 1f, $"expected a short tip member, got {length} mm");
    }

    /// <summary>Blocks any queried capsule lying wholly in the slab just below the contact.</summary>
    private sealed class NearContactBlockScene(Vector3 contact) : ICollisionScene
    {
        public bool IntersectsCapsule(Vector3 start, Vector3 end, float radius,
            Func<object?, bool>? obstacleFilter = null) =>
            InSlab(start.Z) && InSlab(end.Z);

        private bool InSlab(float z) => z > contact.Z - 2.1f && z < contact.Z - 0.2f;

        public ObstacleNearestPoint? NearestObstacle(Vector3 point,
            Func<object?, bool>? obstacleFilter = null) => null;

        public ObstacleRayHit? Raycast(Vector3 origin, Vector3 direction, float maxDistance,
            Func<object?, bool>? obstacleFilter = null) => null;
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
