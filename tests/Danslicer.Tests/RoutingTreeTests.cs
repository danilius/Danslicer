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
    public void IslandPriorityIsDeterministicAndPrecedesHigherOrdinaryTips()
    {
        var tips = new[]
        {
            new RoutingTip(new(0, 0, 14), Vector3.UnitZ, 0.4f),
            new RoutingTip(new(5, 0, 10), Vector3.UnitZ, 0.4f, IsIslandPriority: true),
        };

        var first = Route(tips, new TreeRoutingOptions { UseBaseGrid = false });
        var second = Route(tips, new TreeRoutingOptions { UseBaseGrid = false });

        var firstTip = first.Graph.Nodes.First(node => node.Type == SupportNodeType.Tip);
        Assert.Equal(new Vector3(5, 0, 10), firstTip.Position);
        Assert.Equal(first.Graph.Nodes.Select(node => (node.Id, node.Type, node.Position)),
            second.Graph.Nodes.Select(node => (node.Id, node.Type, node.Position)));
    }

    [Theory]
    [InlineData(true, 0f, 1)]
    [InlineData(false, 4f, 0)]
    public void NewBasesFollowTheSelectedPlacementMode(
        bool useBaseGrid, float expectedBaseX, int expectedBranches)
    {
        var result = Route(new[] { new RoutingTip(new(4, 0, 10), Vector3.UnitZ, 0.4f) },
            new TreeRoutingOptions
            {
                UseBaseGrid = useBaseGrid, BaseGridPitch = 10f, MaxBranchLength = 8f,
            });

        Assert.Empty(result.Failures);
        var supportBase = Assert.Single(result.Graph.Nodes,
            node => node.Type == SupportNodeType.Base);
        Assert.Equal(expectedBaseX, supportBase.Position.X, 3);
        Assert.Equal(expectedBranches, result.Graph.Segments.Count(
            segment => segment.Type == SupportSegmentType.Branch));
    }

    [Fact]
    public void TipBeyondEveryGridPointGetsABaseOffTheGrid()
    {
        // No lattice point is within branch reach; rather than refuse, the trunk drops straight
        // from the junction and the base leaves the grid (user decision 2026-09-07).
        var result = Route(new[] { new RoutingTip(new(10, 10, 10), Vector3.UnitZ, 0.4f) },
            new TreeRoutingOptions { BaseGridPitch = 20f, MaxBranchLength = 8f });

        Assert.Empty(result.Failures);
        var supportBase = Assert.Single(result.Graph.Nodes, n => n.Type == SupportNodeType.Base);
        Assert.Equal(new Vector3(10, 10, 0), supportBase.Position);
        Assert.DoesNotContain(result.Graph.Segments, s => s.Type == SupportSegmentType.Branch);
    }

    [Fact]
    public void FlatUndersideTipBecomesTipTrunkAndDiscBase()
    {
        var result = Route(new[] { new RoutingTip(new(0, 0, 10), Vector3.UnitZ, 0.4f) });

        Assert.Empty(result.Failures);
        Assert.Equal(3, result.Graph.NodeCount);
        Assert.Equal(2, result.Graph.SegmentCount);

        var tipSegment = Assert.Single(result.Graph.Segments,
            s => s.Type == SupportSegmentType.Tip);
        var trunk = Assert.Single(result.Graph.Segments, s => s.Type == SupportSegmentType.Trunk);
        var baseNode = Assert.Single(result.Graph.Nodes, n => n.Type == SupportNodeType.Base);

        // Tip member drops vertically for its 2 mm length; the trunk continues to the plate.
        var junction = Assert.Single(result.Graph.Nodes, n => n.Type == SupportNodeType.Junction);
        Assert.Equal(new Vector3(0, 0, 8), junction.Position);
        Assert.Equal(new Vector3(0, 0, 0), baseNode.Position);
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

    [Theory]
    [InlineData(true, 10f, 1)]
    [InlineData(true, 5f, 2)]
    [InlineData(false, 10f, 2)]
    public void ExistingTrunkPreferenceAndRangeAreIndependent(
        bool preferExisting, float range, int expectedBases)
    {
        var result = Route(new[]
        {
            new RoutingTip(new(0, 0, 12), Vector3.UnitZ, 0.4f),
            new RoutingTip(new(6, 0, 10), Vector3.UnitZ, 0.4f),
        }, new TreeRoutingOptions
        {
            BaseGridPitch = 10f,
            PreferExistingTrunks = preferExisting,
            ExistingTrunkBranchRange = range,
        });

        Assert.Empty(result.Failures);
        Assert.Equal(expectedBases, result.BasePositions.Count);
    }

    [Fact]
    public void NearTiedExistingTrunksPreferTheTipsLeanDirection()
    {
        // Grid mode (pitch 5): trunks at x = -5 and x = 5. The leaning tip's own drop line at
        // the origin is blocked by a shelf and branches to fresh trunks are disabled, so it
        // must join one of the two; the bend rule picks the one its cone points toward.
        var scene = new LinearCollisionScene();
        scene.AddTriangle(new(-2, -2, 5), new(1.5f, -2, 5), new(1.5f, 2, 5));
        scene.AddTriangle(new(-2, -2, 5), new(1.5f, 2, 5), new(-2, 2, 5));
        var result = Route(new[]
        {
            new RoutingTip(new(-5, 0, 14), Vector3.UnitZ, 0.4f),
            new RoutingTip(new(5, 0, 13), Vector3.UnitZ, 0.4f),
            new RoutingTip(new(-2, 0, 10), Vector3.Normalize(new Vector3(-1, 0, 1)), 0.4f),
        }, new TreeRoutingOptions
        {
            UseBaseGrid = true, BaseGridPitch = 5f, MaxBranchLength = 0.001f,
        }, scene);

        Assert.Empty(result.Failures);
        var leanedTip = result.Graph.Nodes.Single(node =>
            node.Type == SupportNodeType.Tip && node.Position.X == -2);
        var component = result.Graph.Component(leanedTip.Id);
        var supportBase = Assert.Single(component.Nodes.Select(result.Graph.GetNode),
            node => node.Type == SupportNodeType.Base);
        Assert.Equal(5f, supportBase.Position.X, 3);
    }

    [Fact]
    public void TipThatCannotReachTheGridIsRoutedOffGrid()
    {
        var tips = new[]
        {
            // Creates a reachable grid trunk and a branch end at (5, 0, 12).
            new RoutingTip(new(5, 0, 14), Vector3.UnitZ, 0.4f),
            // Its own junction cannot reach the 20 mm grid.
            new RoutingTip(new(9, 0, 12.2f), Vector3.UnitZ, 0.4f),
        };
        var options = new TreeRoutingOptions
        {
            BaseGridPitch = 20f,
            MaxBranchLength = 8f,
            PreferExistingTrunks = false,
        };

        var result = Route(tips, options);

        // The off-grid last resort routes it as a regular cone.
        Assert.Empty(result.Failures);
        Assert.Equal(2, result.Graph.Segments.Count(s => s.Type == SupportSegmentType.Tip));
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
            new TreeRoutingOptions { BaseGridPitch = 5f }, scene);

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
            new TreeRoutingOptions { BaseGridPitch = 4f }, new SteepBranchBlockScene());

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

    [Fact]
    public void FreeBranchFanPrefersTheShortestShallowCandidate()
    {
        var result = Route(new[] { new RoutingTip(new(0, 0, 10), Vector3.UnitZ, 0.4f) },
            new TreeRoutingOptions { UseBaseGrid = false }, new SteepBranchBlockScene());

        Assert.Empty(result.Failures);
        var branch = Assert.Single(result.Graph.Segments,
            segment => segment.Type == SupportSegmentType.Branch);
        var a = result.Graph.GetNode(branch.NodeA).Position;
        var b = result.Graph.GetNode(branch.NodeB).Position;
        var delta = b - a;
        var lean = MathF.Atan2(new Vector2(delta.X, delta.Y).Length(), MathF.Abs(delta.Z))
            * 180 / MathF.PI;
        Assert.Equal(2f, delta.Length(), 3);
        Assert.Equal(15f, lean, 2);
    }

    [Fact]
    public void ProjectedBranchNearPassCatchesAnXAtDifferentHeights()
    {
        var crossing = TreeSupportRouter.ProjectedSegmentsPassTooClose(
            new(-2, -2, 10), new(2, 2, 8), new(-2, 2, 5), new(2, -2, 3), 1.2f);
        var separated = TreeSupportRouter.ProjectedSegmentsPassTooClose(
            new(-2, -2, 10), new(2, 2, 8), new(2, -2, 5), new(5, -5, 3), 1.2f);

        Assert.True(crossing); // Collision-clear in 3D, but visually forms an X in XY.
        Assert.False(separated);
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

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void FullyBlockedTipRefusesInsteadOfLandingOnTheModel(bool useBaseGrid)
    {
        // A wide floor at z = 5 no branch can clear: the tip must refuse, never land on it.
        var scene = new LinearCollisionScene();
        scene.AddTriangle(new(-40, -40, 5), new(40, -40, 5), new(40, 40, 5));
        scene.AddTriangle(new(-40, -40, 5), new(40, 40, 5), new(-40, 40, 5));

        var result = Route(new[] { new RoutingTip(new(0, 0, 10), Vector3.UnitZ, 0.4f) },
            new TreeRoutingOptions { UseBaseGrid = useBaseGrid, BaseGridPitch = 4f }, scene);

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
    public void NearPlateBaseTransitionUsesTheDiameterOfItsTipMember()
    {
        var rules = GrowthRuleSet.Default;
        rules.Find<TaperGrowthRule>()!.TipToPillarDiameterRatio = 0.5f;
        var result = new TreeSupportRouter(new LinearCollisionScene(), rules).Route(
            new[] { new RoutingTip(new(0, 0, 1), Vector3.UnitZ, 0.4f) },
            new TreeRoutingOptions
            {
                TrunkDiameter = 0.8f,
                BranchDiameter = 2f,
                BaseShape = SupportBaseShape.DiscCone,
            });

        Assert.Empty(result.Failures);
        var member = Assert.Single(result.Graph.Segments);
        Assert.Equal(SupportSegmentType.Tip, member.Type);
        Assert.Equal(0.4f, member.Diameter);
        var baseNode = Assert.Single(result.Graph.Nodes, n => n.Type == SupportNodeType.Base);
        var basePart = Assert.Single(SupportRenderMesh.Build(result.Graph),
            part => part.Kind == SupportRenderKind.Base);
        var coneTopZ = baseNode.BaseHeight + baseNode.BaseConeHeight;
        var topRadius = basePart.Mesh.Positions
            .Where(position => MathF.Abs(position.Z - coneTopZ) < 1e-4f)
            .Max(position => new Vector2(position.X, position.Y).Length());
        Assert.Equal(member.Diameter * 0.5f, topRadius, 3);
    }

    [Fact]
    public void ConeShapeParametersFlowOntoTheTipNode()
    {
        var result = Route(new[]
        {
            new RoutingTip(new(0, 0, 10), Vector3.UnitZ, 0.5f,
                TipShape: SupportTipShape.Cone, ConeLength: 1.5f, BallDiameter: 0.7f,
                TipNormalLeadIn: 0.3f),
        });

        var tipNode = Assert.Single(result.Graph.Nodes, n => n.Type == SupportNodeType.Tip);
        Assert.Equal(SupportTipShape.Cone, tipNode.TipShape);
        Assert.Equal(1.5f, tipNode.ConeLength);
        Assert.Equal(0.7f, tipNode.BallDiameter);
        Assert.Equal(0.5f, tipNode.TipDiameter);
        Assert.Equal(0.3f, tipNode.TipNormalLeadIn);
    }

    [Fact]
    public void ObstructedNormalLeadInShortensWithoutChangingTheRoute()
    {
        var contact = new Vector3(0, 0, 10);
        var result = Route(new[]
        {
            new RoutingTip(contact, -Vector3.UnitZ, 0.4f,
                TipShape: SupportTipShape.Cone, ConeLength: 1f, TipNormalLeadIn: 0.3f),
        }, new TreeRoutingOptions { UseBaseGrid = false }, new LeadInOnlyBlockScene());

        Assert.Empty(result.Failures);
        var tipNode = Assert.Single(result.Graph.Nodes, node => node.Type == SupportNodeType.Tip);
        Assert.Equal(0.15f, tipNode.TipNormalLeadIn, 5);
    }

    /// <summary>
    /// The legacy route check stops below z=9.5 and passes. The post-route bend check sees only
    /// lead-ins which rise above that plane, forcing deterministic shortening without changing
    /// the selected trunk.
    /// </summary>
    private sealed class LeadInOnlyBlockScene : ICollisionScene
    {
        public bool IntersectsCapsule(Vector3 start, Vector3 end, float radius,
            Func<object?, bool>? obstacleFilter = null) =>
            radius < 0.3f && MathF.Max(start.Z, end.Z) > 9.5f;

        public ObstacleNearestPoint? NearestObstacle(Vector3 point,
            Func<object?, bool>? obstacleFilter = null) => null;

        public ObstacleRayHit? Raycast(Vector3 origin, Vector3 direction, float maxDistance,
            Func<object?, bool>? obstacleFilter = null) => null;
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
    public void BlockedFullSizeBaseRelocatesWithoutShrinking()
    {
        // A low wall 1.2 mm from the drop line: the trunk clears it but the full 2 mm-radius
        // disc cannot. The straight drop is rejected and the branch fan finds another line.
        var scene = new LinearCollisionScene();
        scene.AddTriangle(new(1.2f, -3, 0), new(1.2f, 3, 0), new(1.2f, 0, 2));

        var result = Route(new[] { new RoutingTip(new(0, 0, 10), Vector3.UnitZ, 0.4f) },
            new TreeRoutingOptions { BaseGridPitch = 5f }, scene);

        Assert.Empty(result.Failures);
        var baseNode = Assert.Single(result.Graph.Nodes, n => n.Type == SupportNodeType.Base);
        Assert.Equal(SupportBaseShape.Disc, baseNode.BaseShape);
        Assert.Equal(4f, baseNode.BaseDiameter);
        Assert.NotEqual(Vector2.Zero, new Vector2(baseNode.Position.X, baseNode.Position.Y));
        Assert.Single(result.Graph.Segments, s => s.Type == SupportSegmentType.Branch);
        Assert.False(scene.IntersectsCapsule(baseNode.Position,
            baseNode.Position + Vector3.UnitZ * baseNode.BaseHeight, 2.25f));
    }

    [Fact]
    public void TightSpotStillEmitsTheConfiguredFullSizeBase()
    {
        // Walls 0.7 mm away on both sides block the old member-width shrink location. A viable
        // route must move elsewhere and preserve the configured diameter.
        var scene = new LinearCollisionScene();
        scene.AddTriangle(new(0.7f, -3, 0), new(0.7f, 3, 0), new(0.7f, 0, 2));
        scene.AddTriangle(new(-0.7f, -3, 0), new(-0.7f, 3, 0), new(-0.7f, 0, 2));

        var result = Route(new[] { new RoutingTip(new(0, 0, 10), Vector3.UnitZ, 0.4f) },
            new TreeRoutingOptions
            {
                TrunkDiameter = 0.6f, BranchDiameter = 0.6f, BaseGridPitch = 4f,
            },
            scene);

        Assert.Empty(result.Failures);
        var baseNode = Assert.Single(result.Graph.Nodes, n => n.Type == SupportNodeType.Base);
        Assert.Equal(SupportBaseShape.Disc, baseNode.BaseShape);
        Assert.Equal(4f, baseNode.BaseDiameter, 3);
        Assert.NotEqual(Vector2.Zero, new Vector2(baseNode.Position.X, baseNode.Position.Y));
        Assert.False(scene.IntersectsCapsule(baseNode.Position,
            baseNode.Position + Vector3.UnitZ * baseNode.BaseHeight, 2.25f));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void NearPlateTipAnglesToAFullSizeBaseLocation(bool useBaseGrid)
    {
        var result = Route(new[] { new RoutingTip(new(0, 0, 1), Vector3.UnitZ, 0.4f) },
            new TreeRoutingOptions
                { UseBaseGrid = useBaseGrid, BaseDiameter = 1f, BaseGridPitch = 1f },
            new BaseOnlyBlockScene());

        Assert.Empty(result.Failures);
        var baseNode = Assert.Single(result.Graph.Nodes, n => n.Type == SupportNodeType.Base);
        Assert.Equal(1f, baseNode.BaseDiameter);
        Assert.True(new Vector2(baseNode.Position.X, baseNode.Position.Y).Length() > 0.6f);
        var tip = Assert.Single(result.Graph.Nodes, n => n.Type == SupportNodeType.Tip);
        var member = Assert.Single(result.Graph.Segments);
        var other = result.Graph.GetNode(member.NodeA == tip.Id ? member.NodeB : member.NodeA);
        Assert.NotEqual(tip.Position.X, other.Position.X);
    }

    [Fact]
    public void NearPlateTipRefusesWhenNoFullSizeBaseFits()
    {
        var result = Route(new[] { new RoutingTip(new(0, 0, 1), Vector3.UnitZ, 0.4f) },
            new TreeRoutingOptions { BaseDiameter = 1f }, new BaseOnlyBlockScene(allPositions: true));

        Assert.Single(result.Failures);
        Assert.Empty(result.Graph.Nodes);
        Assert.Empty(result.Graph.Segments);
    }

    private sealed class BaseOnlyBlockScene(bool allPositions = false) : ICollisionScene
    {
        public bool IntersectsCapsule(Vector3 start, Vector3 end, float radius,
            Func<object?, bool>? obstacleFilter = null) =>
            radius > 0.7f && MathF.Max(start.Z, end.Z) <= 0.81f &&
            (allPositions || new Vector2(start.X, start.Y).Length() < 0.6f);

        public ObstacleNearestPoint? NearestObstacle(Vector3 point,
            Func<object?, bool>? obstacleFilter = null) => null;

        public ObstacleRayHit? Raycast(Vector3 origin, Vector3 direction, float maxDistance,
            Func<object?, bool>? obstacleFilter = null) => null;
    }

    [Fact]
    public void RoughContactIsRefusedRatherThanGivenAStubCone()
    {
        // Every full-length departure is blocked near the contact (spiky terrain, e.g. teeth).
        // A cone is the whole tip member; there is no short stub to fall back to, so the
        // contact is refused honestly.
        var contact = new Vector3(0, 0, 10);
        var result = new TreeSupportRouter(new NearContactBlockScene(contact),
            GrowthRuleSet.Default).Route(
            new[] { new RoutingTip(contact, Vector3.UnitZ, 0.4f) }, new TreeRoutingOptions());

        var failure = Assert.Single(result.Failures);
        Assert.Equal(RoutingFailureReason.ContactBlocked, failure.Reason);
        Assert.Empty(result.Graph.Segments);
    }

    [Fact]
    public void NeighbouringConesKeepTheirBasesApart()
    {
        // Two contacts on one 45 degree underside whose members run parallel 1.06 mm apart:
        // their necks would clear, but their bases are as wide as the balls they grow from
        // (1.2 mm), so the second cone would overlap the first. It is refused instead of
        // fanning off one ball.
        var outward = Vector3.Normalize(new Vector3(1, 0, -1));
        var result = Route(new[]
        {
            new RoutingTip(new(0, 0, 12), -outward, 0.4f),
            new RoutingTip(new(0.75f, 0, 11.25f), -outward, 0.4f),
        }, new TreeRoutingOptions { UseBaseGrid = true });

        var failure = Assert.Single(result.Failures);
        Assert.Equal(RoutingFailureReason.ContactBlocked, failure.Reason);
        Assert.Single(result.Graph.Segments, s => s.Type == SupportSegmentType.Tip);
    }

    [Fact]
    public void ExistingConeKeepsItsBaseRadiusAgainstLaterPasses()
    {
        // A cone already in the document (from an earlier generation or a manual placement)
        // is as wide as its ball; a later grid-mode pass must not crowd it as if it were only
        // its neck.
        var first = Route(new[] { new RoutingTip(new(0, 0, 10), Vector3.UnitZ, 0.4f) },
            new TreeRoutingOptions { UseBaseGrid = true });
        Assert.Empty(first.Failures);

        var second = new TreeSupportRouter(new LinearCollisionScene(), GrowthRuleSet.Default)
            .Route(new[] { new RoutingTip(new(1.1f, 0, 10), Vector3.UnitZ, 0.4f) },
                new TreeRoutingOptions { UseBaseGrid = true }, first.Graph);

        var failure = Assert.Single(second.Failures);
        Assert.Equal(RoutingFailureReason.ContactBlocked, failure.Reason);
    }

    [Fact]
    public void JunctionNearATrunkAxisLandsTheConeOnTheTrunk()
    {
        // The second tip's junction would sit 0.4 mm beside the first trunk. Instead of a stub
        // branch shorter than its own ball, the cone is re-aimed onto the trunk axis and the
        // trunk is split where it lands.
        var result = Route(new[]
        {
            new RoutingTip(new(0, 0, 14), Vector3.UnitZ, 0.4f),
            new RoutingTip(new(0.4f, 0, 10), Vector3.UnitZ, 0.4f),
        }, new TreeRoutingOptions { BaseGridPitch = 10f });

        Assert.Empty(result.Failures);
        Assert.DoesNotContain(result.Graph.Segments, s => s.Type == SupportSegmentType.Branch);
        Assert.Single(result.BasePositions);
        var tipNode = result.Graph.Nodes.Single(n => n.Type == SupportNodeType.Tip && n.Position.X == 0.4f);
        var member = Assert.Single(result.Graph.SegmentsAt(tipNode.Id));
        var junction = result.Graph.GetNode(member.NodeA == tipNode.Id ? member.NodeB : member.NodeA);
        Assert.Equal(0f, junction.Position.X, 4);
        Assert.Equal(10 - MathF.Sqrt(4 - 0.16f), junction.Position.Z, 3);
        Assert.Contains(result.Graph.SegmentsAt(junction.Id), s => s.Type == SupportSegmentType.Trunk);
        Assert.Equal(2, result.Graph.Segments.Count(s => s.Type == SupportSegmentType.Trunk));
    }

    [Fact]
    public void JunctionNearAGridDropLineSnapsOntoIt()
    {
        // Grid mode, junction 0.4 mm off the lattice point: the cone is re-aimed at the drop
        // line and the trunk falls straight from it, with no stub branch in between.
        var result = Route(new[] { new RoutingTip(new(0.4f, 0, 10), Vector3.UnitZ, 0.4f) },
            new TreeRoutingOptions { BaseGridPitch = 10f });

        Assert.Empty(result.Failures);
        Assert.DoesNotContain(result.Graph.Segments, s => s.Type == SupportSegmentType.Branch);
        var supportBase = Assert.Single(result.Graph.Nodes, n => n.Type == SupportNodeType.Base);
        Assert.Equal(Vector3.Zero, supportBase.Position);
        var junction = Assert.Single(result.Graph.Nodes, n => n.Type == SupportNodeType.Junction);
        Assert.Equal(0f, junction.Position.X, 4);
        Assert.Equal(10 - MathF.Sqrt(4 - 0.16f), junction.Position.Z, 3);
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
    public void EmptyExistingContextIsBitIdenticalToNoContext()
    {
        var tips = new[]
        {
            new RoutingTip(new(0, 0, 10), Vector3.UnitZ, 0.4f),
            new RoutingTip(new(2, 0, 8), Vector3.Normalize(new Vector3(-1, 0, 1)), 0.4f),
            new RoutingTip(new(-4, 3, 6), Vector3.UnitZ, 0.4f),
        };
        var options = new TreeRoutingOptions { Seed = 7 };
        var router = new TreeSupportRouter(new LinearCollisionScene(), GrowthRuleSet.Default);

        var ordinary = router.Route(tips, options);
        var withEmptyContext = router.Route(tips, options, new SupportGraph());

        Assert.Equal(
            ordinary.Graph.Nodes.OrderBy(n => n.Id).Select(n => (n.Id, n.Type, n.Position)),
            withEmptyContext.Graph.Nodes.OrderBy(n => n.Id).Select(n => (n.Id, n.Type, n.Position)));
        Assert.Equal(
            ordinary.Graph.Segments.OrderBy(s => s.Id)
                .Select(s => (s.Id, s.Type, s.NodeA, s.NodeB, s.Diameter)),
            withEmptyContext.Graph.Segments.OrderBy(s => s.Id)
                .Select(s => (s.Id, s.Type, s.NodeA, s.NodeB, s.Diameter)));
        Assert.Equal(ordinary.BasePositions, withEmptyContext.BasePositions);
        Assert.Equal(ordinary.Failures, withEmptyContext.Failures);
    }

    [Fact]
    public void ExistingTrunkBranchCountHonoursTheConfiguredLimit()
    {
        var existing = new SupportGraph();
        var supportBase = new SupportNode
            { Type = SupportNodeType.Base, Position = Vector3.Zero };
        var top = new SupportNode
            { Type = SupportNodeType.Junction, Position = new Vector3(0, 0, 8) };
        existing.AddNode(supportBase);
        existing.AddNode(top);
        existing.AddSegment(new SupportSegment
        {
            Type = SupportSegmentType.Trunk,
            NodeA = supportBase.Id,
            NodeB = top.Id,
        });
        for (var index = 0; index < 6; index++)
        {
            var end = new SupportNode
            {
                Type = SupportNodeType.Junction,
                Position = new Vector3(-index - 1, 0, 7 - index * 0.5f),
            };
            existing.AddNode(end);
            existing.AddSegment(new SupportSegment
            {
                Type = SupportSegmentType.Branch,
                NodeA = top.Id,
                NodeB = end.Id,
            });
        }
        var obstacles = new LinearCollisionScene();
        obstacles.AddSupportGraph(existing);

        var result = new TreeSupportRouter(obstacles, GrowthRuleSet.Default).Route(
            new[] { new RoutingTip(new(4, 0, 10), Vector3.UnitZ, 0.4f) },
            new TreeRoutingOptions { UseBaseGrid = false }, existing);

        Assert.Empty(result.Failures);
        Assert.Single(result.Edit.AddedNodes,
            node => node.Type == SupportNodeType.Base);
        Assert.DoesNotContain(result.Edit.AddedSegments,
            segment => segment.Type == SupportSegmentType.Branch &&
                (segment.NodeA == top.Id || segment.NodeB == top.Id));
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
        var result = Route(tips, new TreeRoutingOptions { BaseGridPitch = 4f });

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

    /// <summary>The largest bend, in degrees, at any joint between a tip member and the members below it.</summary>
    private static float MaxTipJointBend(SupportGraph graph)
    {
        var worst = 0f;
        foreach (var tipSegment in graph.Segments.Where(s => s.Type == SupportSegmentType.Tip))
        {
            var a = graph.GetNode(tipSegment.NodeA);
            var b = graph.GetNode(tipSegment.NodeB);
            var (tip, junction) = a.Type == SupportNodeType.Tip ? (a, b) : (b, a);
            var incoming = junction.Position - tip.Position;
            foreach (var next in graph.SegmentsAt(junction.Id))
            {
                if (next.Id == tipSegment.Id) continue;
                var farId = next.NodeA == junction.Id ? next.NodeB : next.NodeA;
                var outgoing = graph.GetNode(farId).Position - junction.Position;
                if (outgoing.Z > 1e-6f) continue; // the parent trunk passing through upward
                worst = MathF.Max(worst, TreeSupportRouter.BendDegrees(incoming, outgoing));
            }
        }
        return worst;
    }

    [Fact]
    public void FreeModeGivesEveryTipItsOwnSupportBlindToTheOthers()
    {
        // Grid off: two contacts a millimetre apart each get tip, trunk and base of their own,
        // colliding or not, and a later manual pass ignores what is there just the same.
        var result = Route(new[]
        {
            new RoutingTip(new(0, 0, 10), Vector3.UnitZ, 0.4f),
            new RoutingTip(new(1, 0, 10), Vector3.UnitZ, 0.4f),
        }, new TreeRoutingOptions { UseBaseGrid = false });

        Assert.Empty(result.Failures);
        Assert.Equal(2, result.BasePositions.Count);
        Assert.DoesNotContain(result.Graph.Segments, s => s.Type == SupportSegmentType.Branch);

        var later = new TreeSupportRouter(new LinearCollisionScene(), GrowthRuleSet.Default)
            .Route(new[] { new RoutingTip(new(0.5f, 0, 10), Vector3.UnitZ, 0.4f) },
                new TreeRoutingOptions { UseBaseGrid = false }, result.Graph);
        Assert.Empty(later.Failures);
        Assert.Equal(3, later.Graph.Nodes.Count(n => n.Type == SupportNodeType.Base));
    }

    [Fact]
    public void BranchNeverDoublesBackOnTheConeItGrowsFrom()
    {
        // The only existing trunk lies behind a tip that leans the other way: joining it would
        // fold the branch 90° back at the ball, so the tip must take its own support instead.
        var result = Route(new[]
        {
            new RoutingTip(new(-5, 0, 14), Vector3.UnitZ, 0.4f),
            new RoutingTip(new(-2, 0, 10), Vector3.Normalize(new Vector3(-1, 0, 1)), 0.4f),
        }, new TreeRoutingOptions { UseBaseGrid = false });

        Assert.Empty(result.Failures);
        Assert.Equal(2, result.BasePositions.Count);
        Assert.True(MaxTipJointBend(result.Graph) <= 45.01f);
    }

    [Fact]
    public void FreeBranchFanContinuesTheConeAxisWhenTheDropIsBlocked()
    {
        // A shelf under the tip's junction blocks the vertical drop; the branch that simply
        // carries on along the cone's own axis is preferred to any swing around the vertical.
        var scene = new LinearCollisionScene();
        scene.AddTriangle(new(-1.5f, -1.5f, 5), new(1.7f, -1.5f, 5), new(1.7f, 1.5f, 5));
        scene.AddTriangle(new(-1.5f, -1.5f, 5), new(1.7f, 1.5f, 5), new(-1.5f, 1.5f, 5));
        var outward = Vector3.Normalize(new Vector3(1, 0, -1));

        var result = Route(new[] { new RoutingTip(new(0, 0, 10), -outward, 0.4f) },
            new TreeRoutingOptions { UseBaseGrid = false }, scene);

        Assert.Empty(result.Failures);
        var branch = Assert.Single(result.Graph.Segments,
            segment => segment.Type == SupportSegmentType.Branch);
        var a = result.Graph.GetNode(branch.NodeA).Position;
        var b = result.Graph.GetNode(branch.NodeB).Position;
        var (high, low) = a.Z >= b.Z ? (a, b) : (b, a);
        var direction = Vector3.Normalize(low - high);
        Assert.Equal(outward.X, direction.X, 3);
        Assert.Equal(outward.Z, direction.Z, 3);
        Assert.Equal(0f, MaxTipJointBend(result.Graph), 2);
    }

    [Fact]
    public void BlockedTipDirectionFallsBackToVertical()
    {
        // A small obstacle sits exactly where the 45° tip member would end. Rather than swing
        // around the vertical at 45°, the tip goes straight down.
        // The 45° member of a 2 mm tip ends at (1.414, 0, 8.586); the sphere sits just within
        // the member's clearance of that end and clear of the 30° member and its trunk.
        var scene = new LinearCollisionScene();
        scene.AddSphere(new Vector3(2.003f, 0, 9.038f), 0.3f);
        var outward = Vector3.Normalize(new Vector3(1, 0, -1));

        var result = Route(new[] { new RoutingTip(new(0, 0, 10), -outward, 0.4f) },
            new TreeRoutingOptions { UseBaseGrid = false }, scene);

        Assert.Empty(result.Failures);
        var tipNode = Assert.Single(result.Graph.Nodes, n => n.Type == SupportNodeType.Tip);
        var member = Assert.Single(result.Graph.SegmentsAt(tipNode.Id));
        var otherId = member.NodeA == tipNode.Id ? member.NodeB : member.NodeA;
        var delta = result.Graph.GetNode(otherId).Position - tipNode.Position;
        var lean = MathF.Atan2(new Vector2(delta.X, delta.Y).Length(), MathF.Abs(delta.Z))
            * 180 / MathF.PI;
        Assert.Equal(0f, lean, 2);
    }

    [Fact]
    public void ShortTrunkIsRaisedToMeetAMemberAngleBranchWithoutMovingItsBranches()
    {
        // The first tip builds a trunk at the grid origin whose top is at z = 9. The second
        // tip's shallow branch to that top is over the range; raising the trunk lets a 45°
        // branch join at z ≈ 9.4 while the first branch keeps both of its ends.
        var result = Route(new[]
        {
            new RoutingTip(new(3, 0, 14), Vector3.UnitZ, 0.4f),
            new RoutingTip(new(-2.5f, 0, 13.9f), Vector3.UnitZ, 0.4f),
        }, new TreeRoutingOptions { BaseGridPitch = 10f, ExistingTrunkBranchRange = 3.6f });

        Assert.Empty(result.Failures);
        Assert.Single(result.BasePositions);
        var trunks = result.Graph.Segments.Where(s => s.Type == SupportSegmentType.Trunk).ToList();
        Assert.Equal(2, trunks.Count);
        var topZ = trunks.SelectMany(s => new[] { s.NodeA, s.NodeB })
            .Select(id => result.Graph.GetNode(id).Position.Z).Max();
        Assert.Equal(9.399f, topZ, 2);

        var branches = result.Graph.Segments.Where(s => s.Type == SupportSegmentType.Branch)
            .Select(s => (A: result.Graph.GetNode(s.NodeA).Position,
                B: result.Graph.GetNode(s.NodeB).Position))
            .ToList();
        Assert.Equal(2, branches.Count);
        Assert.Contains(branches, branch =>
            (branch.A == new Vector3(3, 0, 12) && branch.B == new Vector3(0, 0, 9)) ||
            (branch.B == new Vector3(3, 0, 12) && branch.A == new Vector3(0, 0, 9)));
        Assert.Contains(branches, branch =>
            MathF.Abs(MathF.Min(branch.A.Z, branch.B.Z) - 9.399f) < 0.01f);
        Assert.True(MaxTipJointBend(result.Graph) <= 45.01f);
    }

    [Fact]
    public void TrunkIsNeverRaisedIntoTheConeTipStandingOnIt()
    {
        // The first trunk's top is the junction of its own cone tip. A second tip that could
        // only join by raising that trunk must not: the raise would run up inside the cone.
        // With no lattice point reachable it gets a base of its own off the grid instead.
        var result = Route(new[]
        {
            new RoutingTip(new(0, 0, 11), Vector3.UnitZ, 0.4f, IsIslandPriority: true),
            new RoutingTip(new(-2.5f, 0, 13.9f), Vector3.UnitZ, 0.4f),
        }, new TreeRoutingOptions { BaseGridPitch = 10f, ExistingTrunkBranchRange = 3.6f });

        Assert.Empty(result.Failures);
        Assert.Equal(2, result.BasePositions.Count);
        Assert.DoesNotContain(result.Graph.Nodes, n => n.Type != SupportNodeType.Tip &&
            n.Position.X == 0 && n.Position.Y == 0 && n.Position.Z > 9.01f);
        Assert.True(MaxTipJointBend(result.Graph) <= 45.01f);
    }
}
