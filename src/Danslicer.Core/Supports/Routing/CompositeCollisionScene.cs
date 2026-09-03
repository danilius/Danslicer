using System.Numerics;

namespace Danslicer.Core.Supports.Routing;

/// <summary>
/// Presents several collision scenes as one. Lets a caller pair a cached, expensive-to-build
/// scene (the BVH over scene meshes) with a cheap fresh one (a linear scene holding the current
/// support capsules) without rebuilding the former whenever the latter changes.
/// </summary>
public sealed class CompositeCollisionScene : ICollisionScene
{
    private readonly ICollisionScene[] _children;

    public CompositeCollisionScene(params ICollisionScene[] children) => _children = children;

    public bool IntersectsCapsule(Vector3 start, Vector3 end, float radius)
    {
        foreach (var child in _children)
            if (child.IntersectsCapsule(start, end, radius)) return true;
        return false;
    }

    public ObstacleNearestPoint? NearestObstacle(Vector3 point)
    {
        ObstacleNearestPoint? nearest = null;
        foreach (var child in _children)
        {
            var candidate = child.NearestObstacle(point);
            if (candidate is { } hit && (nearest is null || hit.Distance < nearest.Value.Distance))
                nearest = hit;
        }
        return nearest;
    }
}
