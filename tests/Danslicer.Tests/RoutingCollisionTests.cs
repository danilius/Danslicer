using System.Numerics;
using Danslicer.Core.Supports.Routing;

namespace Danslicer.Tests;

public sealed class RoutingCollisionTests
{
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
}
