using System;
using System.Collections.Generic;
using System.Numerics;
using System.Linq;
using Xunit;
using Danslicer.Core.Supports;
using Danslicer.Core.Supports.Routing;
using static System.Math;

namespace Danslicer.Tests
{
    public class RoutingTreePropertyTests
    {
        private static readonly Random Random = new Random(7);
        private static readonly TreeRoutingOptions Options = new TreeRoutingOptions { Seed = 7, PlateZ = 0f };
        private static readonly ICollisionScene EmptyScene = new LinearCollisionScene();
        private static readonly ICollisionScene BlockingFloorScene = new LinearCollisionScene();

        static RoutingTreePropertyTests()
        {
            var scene = (LinearCollisionScene)BlockingFloorScene;
            scene.AddTriangle(new Vector3(-100f, -100f, 4f), new Vector3(100f, -100f, 4f), new Vector3(100f, 100f, 4f));
            scene.AddTriangle(new Vector3(-100f, -100f, 4f), new Vector3(100f, 100f, 4f), new Vector3(-100f, 100f, 4f));
        }

        private static IEnumerable<RoutingTip> GenerateRandomTips(int count)
        {
            for (int i = 0; i < count; i++)
            {
                float x = (float)(Random.NextDouble() * 80 - 40);
                float y = (float)(Random.NextDouble() * 80 - 40);
                float z = (float)(Random.NextDouble() * 25 + 5);
                yield return new RoutingTip(new Vector3(x, y, z), Vector3.UnitZ, 1.2f);
            }
        }

        private static float CalculateLean(Vector3 direction)
        {
            direction = Vector3.Normalize(direction);
            float angle = (float)Acos(Vector3.Dot(Vector3.UnitZ, direction)) * (180f / (float)PI);
            return Min(angle, 180f - angle);
        }

        [Fact]
        public void RoutingIsDeterministicAcrossRepeatedRuns()
        {
            var tips = GenerateRandomTips(30).ToList();
            var router = new TreeSupportRouter(EmptyScene, GrowthRuleSet.Default);

            var result1 = router.Route(tips, Options);
            var result2 = router.Route(tips, Options);

            Assert.Equal(result1.Graph.Nodes.Select(n => (n.Id, n.Position)).OrderBy(t => t.Id), result2.Graph.Nodes.Select(n => (n.Id, n.Position)).OrderBy(t => t.Id));
            Assert.Equal(result1.Graph.Segments.Select(s => (s.Id, s.NodeA, s.NodeB)).OrderBy(t => t.Id), result2.Graph.Segments.Select(s => (s.Id, s.NodeA, s.NodeB)).OrderBy(t => t.Id));
        }

        [Fact]
        public void TrunksAreVerticalAndMembersRespectTheAngleLimit()
        {
            var tips = GenerateRandomTips(30).ToList();
            var router = new TreeSupportRouter(EmptyScene, GrowthRuleSet.Default);
            var result = router.Route(tips, Options);

            foreach (var segment in result.Graph.Segments)
            {
                var nodeA = result.Graph.GetNode(segment.NodeA);
                var nodeB = result.Graph.GetNode(segment.NodeB);
                var direction = nodeB.Position - nodeA.Position;
                var angle = CalculateLean(direction);

                if (segment.Type == SupportSegmentType.Trunk)
                {
                    Assert.True(angle < 0.01f, $"Trunk segment {segment.Id} leans too much: {angle} degrees");
                }
                else
                {
                    Assert.True(angle <= 45.02f, $"Segment {segment.Id} leans too much: {angle} degrees");
                }
            }
        }

        [Fact]
        public void EveryBaseSitsExactlyOnThePlate()
        {
            var tips = GenerateRandomTips(30).ToList();
            var router = new TreeSupportRouter(EmptyScene, GrowthRuleSet.Default);
            var result = router.Route(tips, Options);

            foreach (var node in result.Graph.Nodes.Where(n => n.Type == SupportNodeType.Base))
            {
                Assert.True(Abs(node.Position.Z - Options.PlateZ) < 1e-4f, $"Base node {node.Id} is not on the plate: {node.Position.Z}");
            }
        }

        [Fact]
        public void RefusedTipsAddNothingToTheGraph()
        {
            var tips = GenerateRandomTips(30).ToList();
            var router = new TreeSupportRouter(BlockingFloorScene, GrowthRuleSet.Default);
            var result = router.Route(tips, Options);

            Assert.Empty(result.Graph.Nodes);
            Assert.Empty(result.Graph.Segments);
            Assert.Equal(tips.Count, result.UnroutedTips.Count);
        }

        [Fact]
        public void TipMemberLengthIsRespected()
        {
            var tips = GenerateRandomTips(30).Select(tip => new RoutingTip(tip.SurfacePoint, tip.InwardSurfaceNormal, tip.TipDiameter) { SurfacePoint = new Vector3(tip.SurfacePoint.X, tip.SurfacePoint.Y, tip.SurfacePoint.Z + 10f) }).ToList();
            var router = new TreeSupportRouter(EmptyScene, GrowthRuleSet.Default);
            var result = router.Route(tips, Options);

            foreach (var segment in result.Graph.Segments.Where(s => s.Type == SupportSegmentType.Tip))
            {
                var tipNode = result.Graph.GetNode(segment.NodeA);
                var junctionNode = result.Graph.GetNode(segment.NodeB);
                var distance = Vector3.Distance(tipNode.Position, junctionNode.Position);
                Assert.True(Abs(distance - Options.TipMemberLength) < 1e-3f, $"Tip segment {segment.Id} does not respect the tip member length: {distance}");
            }
        }

        [Fact]
        public void ZeroMaxBranchLengthStillRoutesStraightDrops()
        {
            var options = new TreeRoutingOptions { Seed = 7, PlateZ = 0f, MaxBranchLength = 0.001f, UseBaseGrid = false, PreferExistingTrunks = false };
            var tips = new List<RoutingTip>();
            for (int i = 0; i < 5; i++)
            {
                for (int j = 0; j < 4; j++)
                {
                    float x = i * 9 + 0.7f;
                    float y = j * 9 + 0.7f;
                    tips.Add(new RoutingTip(new Vector3(x, y, 10f), -Vector3.UnitZ, 1.2f));
                }
            }
            var router = new TreeSupportRouter(EmptyScene, GrowthRuleSet.Default);
            var result = router.Route(tips, options);

            Assert.Equal(tips.Count, result.Graph.Nodes.Count(n => n.Type == SupportNodeType.Tip));
            Assert.Empty(result.Graph.Segments.Where(s => s.Type == SupportSegmentType.Branch));
            Assert.All(result.Graph.Segments.Where(s => s.Type == SupportSegmentType.Tip), segment =>
            {
                var tipNode = result.Graph.GetNode(segment.NodeA);
                var junctionNode = result.Graph.GetNode(segment.NodeB);
                var direction = junctionNode.Position - tipNode.Position;
                Assert.True(direction.Z < 0, "Tip segment does not route straight down");
            });
        }

        [Fact]
        public void SharedTrunkAttachmentsRespectMaxBranchesPerTrunk()
        {
            var options = new TreeRoutingOptions { Seed = 7, PlateZ = 0f };
            var tips = new List<RoutingTip>();
            float centerX = 0f;
            float centerY = 0f;
            float radius = 5f;
            for (int i = 0; i < 100; i++)
            {
                float angle = (float)(Random.NextDouble() * 2 * PI);
                float x = centerX + radius * (float)Cos(angle);
                float y = centerY + radius * (float)Sin(angle);
                tips.Add(new RoutingTip(new Vector3(x, y, 10f), -Vector3.UnitZ, 1.2f));
            }
            var router = new TreeSupportRouter(EmptyScene, GrowthRuleSet.Default);
            var result = router.Route(tips, options);

            var baseNodes = result.Graph.Nodes.Where(n => n.Type == SupportNodeType.Base).ToList();
            foreach (var baseNode in baseNodes)
            {
                var trunkSegments = result.Graph.Segments.Where(s => s.NodeB == baseNode.Id).ToList();
                foreach (var trunkSegment in trunkSegments)
                {
                    var trunkNode = result.Graph.GetNode(trunkSegment.NodeA);
                    var branchCount = result.Graph.Segments.Count(s => s.Type == SupportSegmentType.Branch && s.NodeA == trunkNode.Id);
                    Assert.True(branchCount <= 6, $"Trunk at {trunkNode.Position} has more than 6 branches: {branchCount}");
                }
            }
        }

        [Fact]
        public void GridModeRefusesOffLatticeStraightDropsWhenBranchesDisabled()
        {
            var options = new TreeRoutingOptions { Seed = 7, PlateZ = 0f, MaxBranchLength = 0.001f, UseBaseGrid = true, BaseGridPitch = 20f };
            var tips = new List<RoutingTip>();
            for (int i = 0; i < 20; i++)
            {
                float x = (float)(Random.NextDouble() * 80 - 40) + 7.3f;
                float y = (float)(Random.NextDouble() * 80 - 40) + 7.3f;
                tips.Add(new RoutingTip(new Vector3(x, y, 10f), -Vector3.UnitZ, 1.2f));
            }
            var router = new TreeSupportRouter(EmptyScene, GrowthRuleSet.Default);
            var result = router.Route(tips, options);

            Assert.Equal(tips.Count, result.UnroutedTips.Count);
            Assert.All(result.Failures, failure => Assert.Equal(RoutingFailureReason.NoReachableGridPoint, failure.Reason));
        }
    }
}
