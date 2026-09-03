using System.Numerics;
using Danslicer.Core.Supports;
using Danslicer.Core.Supports.Routing;

namespace Danslicer.Tests;

public sealed class RoutingCollisionTests
{
    [Fact]
    public void DownwardRaycastReturnsNearestTriangleAndIgnoresSupportCapsules()
    {
        var linear = new LinearCollisionScene();
        var bvh = new BvhCollisionScene();
        foreach (var scene in new Action<Vector3, Vector3, Vector3, object?>[]
                 { linear.AddTriangle, bvh.AddTriangle })
            scene(new(-2, -2, 4), new(2, -2, 4), new(2, 2, 4), "landing");
        linear.AddCapsule(new(0, 0, 6), new(0, 0, 8), 1, "support");
        bvh.AddCapsule(new(0, 0, 6), new(0, 0, 8), 1, "support");

        var expected = Assert.IsType<ObstacleRayHit>(
            linear.Raycast(new(0, 0, 10), -Vector3.UnitZ, 10));
        var actual = Assert.IsType<ObstacleRayHit>(
            bvh.Raycast(new(0, 0, 10), -Vector3.UnitZ, 10));

        Assert.Equal("landing", expected.Tag);
        Assert.Equal(6, expected.Distance, 4);
        Assert.Equal(Vector3.UnitZ, expected.SurfaceNormal);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void CapsuleDetectsTriangleFaceAndMissesOutsideRadius()
    {
        var scene = new LinearCollisionScene();
        scene.AddTriangle(new(-2, -2, 1), new(2, -2, 1), new(0, 2, 1));

        Assert.True(scene.IntersectsCapsule(new(0, 0, 0), new(0, 0, 2), 0.1f));
        Assert.False(scene.IntersectsCapsule(new(3, 0, 0), new(3, 0, 2), 0.5f));
    }

    [Fact]
    public void CapsuleDetectsTriangleEdgeWithoutAxisIntersection()
    {
        var scene = new LinearCollisionScene();
        scene.AddTriangle(new(0, 0, 0), new(2, 0, 0), new(0, 2, 0));

        Assert.True(scene.IntersectsCapsule(new(1, -0.2f, 1), new(1, -0.2f, -1), 0.25f));
        Assert.False(scene.IntersectsCapsule(new(1, -0.4f, 1), new(1, -0.4f, -1), 0.25f));
    }

    [Fact]
    public void SupportCapsulesAreObstacles()
    {
        var scene = new LinearCollisionScene();
        scene.AddCapsule(new(0, 0, 0), new(0, 0, 5), 0.5f, "pillar");

        Assert.True(scene.IntersectsCapsule(new(-2, 0, 2), new(2, 0, 2), 0.25f));
        Assert.False(scene.IntersectsCapsule(new(-2, 1, 2), new(2, 1, 2), 0.25f));
    }

    [Fact]
    public void NearestObstacleReturnsSurfacePointAndTag()
    {
        var scene = new LinearCollisionScene();
        scene.AddTriangle(new(-1, -1, 0), new(1, -1, 0), new(0, 1, 0), "plate");
        scene.AddCapsule(new(5, 0, 0), new(5, 0, 2), 0.5f, "support");

        var hit = Assert.IsType<ObstacleNearestPoint>(scene.NearestObstacle(new(0, 0, 2)));
        Assert.Equal("plate", hit.Tag);
        Assert.Equal(2, hit.Distance, 4);
        Assert.Equal(Vector3.Zero, hit.Point);
    }

    [Fact]
    public void BvhAgreesWithLinearSceneOnFixedSeedRandomizedQueries()
    {
        var random = new Random(20260903);
        var linear = new LinearCollisionScene();
        var bvh = new BvhCollisionScene();
        for (var i = 0; i < 120; i++)
        {
            var a = Point(random);
            var b = a + Direction(random);
            var c = a + Direction(random);
            linear.AddTriangle(a, b, c, $"triangle-{i}");
            bvh.AddTriangle(a, b, c, $"triangle-{i}");
        }
        for (var i = 0; i < 40; i++)
        {
            var start = Point(random);
            var end = start + Direction(random) * 2;
            var radius = 0.05f + random.NextSingle() * 0.7f;
            linear.AddCapsule(start, end, radius, $"capsule-{i}");
            bvh.AddCapsule(start, end, radius, $"capsule-{i}");
        }

        for (var i = 0; i < 500; i++)
        {
            var start = Point(random);
            var end = start + Direction(random) * 4;
            var radius = random.NextSingle();
            Assert.Equal(linear.IntersectsCapsule(start, end, radius),
                bvh.IntersectsCapsule(start, end, radius));

            var point = Point(random);
            var expected = Assert.IsType<ObstacleNearestPoint>(linear.NearestObstacle(point));
            var actual = Assert.IsType<ObstacleNearestPoint>(bvh.NearestObstacle(point));
            Assert.Equal(expected.Tag, actual.Tag);
            Assert.Equal(expected.Distance, actual.Distance, 4);
            Assert.InRange(Vector3.Distance(expected.Point, actual.Point), 0, 0.0001f);
        }
    }

    [Fact]
    public void OversizedContactBallIsASphereObstacle()
    {
        var graph = new SupportGraph();
        var tip = new SupportNode
        {
            Type = SupportNodeType.Tip, Position = new Vector3(0, 0, 10),
            SurfaceNormal = -Vector3.UnitZ, TipDiameter = 0.4f,
            TipShape = SupportTipShape.Cone, ConeLength = 2f, BallDiameter = 3f,
            PenetrationDepth = 0.2f,
        };
        var junction = new SupportNode { Type = SupportNodeType.Junction, Position = Vector3.Zero };
        graph.AddNode(tip);
        graph.AddNode(junction);
        graph.AddSegment(new SupportSegment
        {
            Type = SupportSegmentType.Neck, NodeA = tip.Id, NodeB = junction.Id, Diameter = 1.2f,
        });

        var linear = new LinearCollisionScene();
        var bvh = new BvhCollisionScene();
        linear.AddSupportGraph(graph);
        bvh.AddSupportGraph(graph);

        Assert.Equal(1, linear.SphereCount);
        Assert.Equal(1, bvh.SphereCount);

        // A query that misses the 0.6 mm neck radius but hits the 1.5 mm ball.
        var start = new Vector3(1.2f, 0, 10.2f);
        var end = new Vector3(1.2f, 0, 10.2f) + Vector3.UnitY;
        Assert.True(linear.IntersectsCapsule(start, end, 0.05f));
        Assert.True(bvh.IntersectsCapsule(start, end, 0.05f));
        Assert.False(linear.IntersectsCapsule(new Vector3(4, 0, 10), new Vector3(4, 0, 11), 0.05f));
    }

    [Fact]
    public void BallSmallerThanNeckIsNotAnExtraObstacle()
    {
        var graph = new SupportGraph();
        var tip = new SupportNode
        {
            Type = SupportNodeType.Tip, Position = new Vector3(0, 0, 10),
            SurfaceNormal = -Vector3.UnitZ, TipShape = SupportTipShape.Cone,
            BallDiameter = 0.8f,
        };
        var junction = new SupportNode { Type = SupportNodeType.Junction, Position = Vector3.Zero };
        graph.AddNode(tip);
        graph.AddNode(junction);
        graph.AddSegment(new SupportSegment
        {
            Type = SupportSegmentType.Neck, NodeA = tip.Id, NodeB = junction.Id, Diameter = 1.2f,
        });

        var scene = new LinearCollisionScene();
        scene.AddSupportGraph(graph);
        Assert.Equal(0, scene.SphereCount);
        Assert.Equal(1, scene.CapsuleCount);
    }

    [Fact]
    public void BvhAgreesWithLinearOnSpheres()
    {
        var linear = new LinearCollisionScene();
        var bvh = new BvhCollisionScene();
        linear.AddSphere(new Vector3(0, 0, 2), 1f, "ball");
        bvh.AddSphere(new Vector3(0, 0, 2), 1f, "ball");

        Assert.True(linear.IntersectsCapsule(new Vector3(0, 0, 0), new Vector3(0, 0, 4), 0.1f));
        Assert.Equal(
            linear.IntersectsCapsule(new Vector3(3, 0, 2), new Vector3(4, 0, 2), 0.1f),
            bvh.IntersectsCapsule(new Vector3(3, 0, 2), new Vector3(4, 0, 2), 0.1f));

        var expected = Assert.IsType<ObstacleNearestPoint>(linear.NearestObstacle(new Vector3(0, 0, 5)));
        var actual = Assert.IsType<ObstacleNearestPoint>(bvh.NearestObstacle(new Vector3(0, 0, 5)));
        Assert.Equal("ball", expected.Tag);
        Assert.Equal(expected.Tag, actual.Tag);
        Assert.Equal(expected.Distance, actual.Distance, 4);
    }

    [Fact]
    public void BvhRebuildsAfterObstacleIsAdded()
    {
        var scene = new BvhCollisionScene();
        Assert.False(scene.IntersectsCapsule(Vector3.Zero, Vector3.UnitZ, 0.1f));

        scene.AddTriangle(new(-1, -1, 0.5f), new(1, -1, 0.5f), new(0, 1, 0.5f));

        Assert.True(scene.IntersectsCapsule(Vector3.Zero, Vector3.UnitZ, 0.1f));
    }

    private static Vector3 Point(Random random) => new(
        random.NextSingle() * 20 - 10,
        random.NextSingle() * 20 - 10,
        random.NextSingle() * 20 - 10);

    private static Vector3 Direction(Random random) => new(
        random.NextSingle() * 2 - 1,
        random.NextSingle() * 2 - 1,
        random.NextSingle() * 2 - 1);
}
