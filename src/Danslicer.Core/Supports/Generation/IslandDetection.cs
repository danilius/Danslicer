using System.Numerics;
using Danslicer.Core.Geometry;

namespace Danslicer.Core.Supports.Generation;

/// <summary>An island that remains unsupported at the time of analysis.</summary>
public readonly record struct DetectedIsland(Vector3 Position, float AreaMm2, int LayerIndex)
{
    public float X => Position.X;
    public float Y => Position.Y;
    public float Z => Position.Z;

    /// <summary>Viewport marker radius: area-relative, clamped to remain usable.</summary>
    public float MarkerRadiusMm => Math.Clamp(MathF.Sqrt(AreaMm2 / MathF.PI) * 0.35f, 0.35f, 2f);
}

/// <summary>
/// Deterministic island analysis shared by the app and CLI. The model is freshly sliced on every
/// call. When supports are supplied, an island is supported when an active tip contact reaches
/// its newborn layer and falls inside the island's area-equivalent footprint.
/// </summary>
public static class IslandDetection
{
    public static IReadOnlyList<DetectedIsland> FindUnsupported(
        Mesh worldMesh, SupportGraph? supports, float layerHeightMm,
        float minIslandAreaMm2, float plateZ, float overhangAngleDegrees)
    {
        ArgumentNullException.ThrowIfNull(worldMesh);
        var layers = LayerStack.Slice(worldMesh, layerHeightMm);
        var islands = IslandFinder.Find(layers, worldMesh.Bounds.Min.Z, layerHeightMm,
            minIslandAreaMm2, plateZ, overhangAngleDegrees);
        var activeTips = supports?.Nodes.Where(node =>
            node.Type == SupportNodeType.Tip && !node.Disabled).ToList() ?? [];

        return islands
            .Where(island => !IsReachedBySupport(island, activeTips, layerHeightMm))
            .OrderBy(island => island.LayerIndex)
            .ThenBy(island => island.Centroid.X)
            .ThenBy(island => island.Centroid.Y)
            .Select(island => new DetectedIsland(
                island.Centroid, island.AreaMm2, island.LayerIndex))
            .ToList();
    }

    private static bool IsReachedBySupport(Island island, IReadOnlyList<SupportNode> tips,
        float layerHeightMm)
    {
        var radius = MathF.Max(0.25f, MathF.Sqrt(island.AreaMm2 / MathF.PI));
        var radiusSquared = radius * radius;
        foreach (var tip in tips)
        {
            if (MathF.Abs(tip.Position.Z - island.Z) > MathF.Max(layerHeightMm * 2f, 0.1f))
                continue;
            var delta = new Vector2(tip.Position.X - island.Centroid.X,
                tip.Position.Y - island.Centroid.Y);
            if (delta.LengthSquared() <= radiusSquared) return true;
        }
        return false;
    }
}
