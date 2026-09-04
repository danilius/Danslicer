using System.Numerics;
using Clipper2Lib;
using Danslicer.Core.Geometry;
using Danslicer.Core.Slicing;

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
/// call. When supports are supplied, their analytic printable sections are sliced immediately
/// below each newborn layer; an intersecting section marks that island as supported.
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
        var supportLayers = supports is null ? null : islands
            .Select(island => island.LayerIndex)
            .Distinct()
            .ToDictionary(index => index, index => SupportSliceGeometry.SectionsAt(
                supports, Math.Max(0, (index - 0.5) * layerHeightMm)));

        return islands
            .Where(island => supportLayers is null ||
                             !IsReachedBySupport(island, supportLayers[island.LayerIndex]))
            .OrderBy(island => island.LayerIndex)
            .ThenBy(island => island.Centroid.X)
            .ThenBy(island => island.Centroid.Y)
            .Select(island => new DetectedIsland(
                island.Centroid, island.AreaMm2, island.LayerIndex))
            .ToList();
    }

    private static bool IsReachedBySupport(Island island, Paths64 supportSections)
    {
        var radius = MathF.Max(0.25f, MathF.Sqrt(island.AreaMm2 / MathF.PI));
        var radiusSquared = radius * radius;
        var centroid = new Point64(
            (long)Math.Round(island.Centroid.X * MeshSlicer.UnitsPerMm),
            (long)Math.Round(island.Centroid.Y * MeshSlicer.UnitsPerMm));
        foreach (var section in supportSections)
        {
            if (Clipper.PointInPolygon(centroid, section) != PointInPolygonResult.IsOutside)
                return true;
            foreach (var point in section)
            {
                var dx = (float)(point.X / MeshSlicer.UnitsPerMm) - island.Centroid.X;
                var dy = (float)(point.Y / MeshSlicer.UnitsPerMm) - island.Centroid.Y;
                if (dx * dx + dy * dy <= radiusSquared) return true;
            }
        }
        return false;
    }
}
