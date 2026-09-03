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
    public void BranchEnvelopeWithoutAGridPointReportsItsOwnRefusalReason()
    {
        var result = Route(new[] { new RoutingTip(new(10, 10, 10), Vector3.UnitZ, 0.4f) },
            new TreeRoutingOptions { BaseGridPitch = 20f, MaxBranchLength = 8f });

        var failure = Assert.Single(result.Failures);
        Assert.Equal(RoutingFailureReason.NoReachableGridPoint, failure.Reason);
        Assert.Empty(result.Graph.Nodes);
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
    public void MiniSupportsFanFromBranchEndWithConfiguredGeometryAndLimits()
    {
        var regular = new[]
        {
            new RoutingTip(new(0, 0, 14), Vector3.UnitZ, 0.4f),
            new RoutingTip(new(4, 0, 12), Vector3.UnitZ, 0.4f),
        };
        var mini = new[]
        {
            new RoutingTip(new(4, 1, 11), Vector3.UnitZ, 0.4f, MiniSupportOnly: true),
            new RoutingTip(new(4, -1, 11), Vector3.UnitZ, 0.4f, MiniSupportOnly: true),
            new RoutingTip(new(5, 0, 11), Vector3.UnitZ, 0.4f, MiniSupportOnly: true),
            new RoutingTip(new(3, 0, 11), Vector3.UnitZ, 0.4f, MiniSupportOnly: true),
            new RoutingTip(new(4, 0, 14), Vector3.UnitZ, 0.4f, MiniSupportOnly: true),
            new RoutingTip(new(20, 0, 11), Vector3.UnitZ, 0.4f, MiniSupportOnly: true),
        };
        var result = Route(regular.Concat(mini), new TreeRoutingOptions
        {
            MiniSupportDiameter = 0.7f,
            MiniSupportTipDiameter = 0.3f,
            MiniSupportConeLength = 1.2f,
            MiniSupportMaxLength = 5f,
            MiniSupportMaxFanPerBranchEnd = 4,
        });

        Assert.Equal(2, result.Failures.Count); // fifth fan contact and out-of-range contact
        var miniSegments = result.Graph.Segments
            .Where(segment => segment.Type == SupportSegmentType.MiniSupport).ToList();
        Assert.Equal(4, miniSegments.Count);
        Assert.All(miniSegments, segment => Assert.Equal(0.7f, segment.Diameter));
        var miniTips = miniSegments.Select(segment =>
                result.Graph.GetNode(segment.NodeA).Type == SupportNodeType.Tip
                    ? result.Graph.GetNode(segment.NodeA)
                    : result.Graph.GetNode(segment.NodeB))
            .ToList();
        Assert.All(miniTips, tip =>
        {
            Assert.Equal(0.3f, tip.TipDiameter);
            Assert.Equal(SupportTipShape.Cone, tip.TipShape);
            Assert.Equal(1.2f, tip.ConeLength);
        });
        var branchEnds = miniSegments.Select(segment =>
                result.Graph.GetNode(segment.NodeA).Type == SupportNodeType.Junction
                    ? segment.NodeA : segment.NodeB)
            .Distinct().ToList();
        Assert.Single(branchEnds);
    }

    [Fact]
    public void MiniSupportWithoutAReachableBranchEndReportsItsOwnRefusalReason()
    {
        var result = Route(new[]
        {
            new RoutingTip(new(0, 0, 10), Vector3.UnitZ, 0.4f, MiniSupportOnly: true),
        });

        var failure = Assert.Single(result.Failures);
        Assert.Equal(RoutingFailureReason.NoBranchEndInRange, failure.Reason);
        Assert.Empty(result.Graph.Nodes);
    }

    [Fact]
    public void RefusedRegularTipsOnlyFallBackToMiniWhenExplicitlyEnabled()
    {
        var tips = new[]
        {
            // Creates a reachable grid trunk and a branch end at (5, 0, 12).
            new RoutingTip(new(5, 0, 14), Vector3.UnitZ, 0.4f),
            // Its own junction cannot reach the 20 mm grid, but its contact can reach that end.
            new RoutingTip(new(9, 0, 12), Vector3.UnitZ, 0.4f),
        };
        var options = new TreeRoutingOptions
        {
            BaseGridPitch = 20f,
            MaxBranchLength = 8f,
            PreferExistingTrunks = false,
        };

        var honest = Route(tips, options);
        var downgraded = Route(tips, options with { RefusedTipsFallBackToMini = true });

        var failure = Assert.Single(honest.Failures);
        Assert.Equal(RoutingFailureReason.NoReachableGridPoint, failure.Reason);
        Assert.DoesNotContain(honest.Graph.Segments,
            segment => segment.Type == SupportSegmentType.MiniSupport);
        Assert.Empty(downgraded.Failures);
        Assert.Single(downgraded.Graph.Segments,
            segment => segment.Type == SupportSegmentType.MiniSupport);
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
}
