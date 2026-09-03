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
            scene.AddTriangle(new Vector3(-100, -100, 4), new Vector3(100, -100, 4), new Vector3(100, 100, 4));
            scene.AddTriangle(new Vector3(-100, -100, 4), new Vector3(100, 100, 4), new Vector3(-100, 100, 4));
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
                var angle = Acos(Vector3.Dot(Vector3.UnitZ, Vector3.Normalize(direction))) * (180 / PI);

                if (segment.Type == SupportSegmentType.Trunk)
                {
                    Assert.True(angle < 0.01, $"Trunk segment {segment.Id} leans too much: {angle} degrees");
                }
                else
                {
                    Assert.True(angle <= 45.02, $"Segment {segment.Id} leans too much: {angle} degrees");
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
                Assert.True(Abs(node.Position.Z - Options.PlateZ) < 1e-4, $"Base node {node.Id} is not on the plate: {node.Position.Z}");
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
    }
}
