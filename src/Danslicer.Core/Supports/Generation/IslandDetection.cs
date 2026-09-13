using System.Numerics;
using Clipper2Lib;
using Danslicer.Core.Geometry;
using Danslicer.Core.Slicing;

namespace Danslicer.Core.Supports.Generation;

/// <summary>An island that remains unsupported at the time of analysis.</summary>
public readonly record struct DetectedIsland(Vector3 Position, float AreaMm2, int LayerIndex)
{
    internal Paths64? Footprint { get; init; }
    internal double SupportPlaneZ { get; init; }
    public float X => Position.X;
    public float Y => Position.Y;
    public float Z => Position.Z;

    /// <summary>Viewport marker radius: area-relative, clamped to remain usable.</summary>
    public float MarkerRadiusMm => Math.Clamp(MathF.Sqrt(AreaMm2 / MathF.PI) * 0.35f, 0.35f, 2f);
}

/// <summary>
/// Deterministic disconnected-component analysis shared by the app and CLI. Connected overhang
/// growth is handled by support generation, not reported as a new island on every layer.
/// When supports are supplied, their analytic printable sections at the preceding layer's
/// mid-height must overlap the actual solid island footprint, excluding holes.
/// </summary>
public static class IslandDetection
{
    /// <summary>Finds components with no positive-area overlap with the preceding model layer.
    /// The minimum area applies at birth. The angle parameter is retained for API compatibility;
    /// disconnected islands must not be hidden by an overhang allowance.</summary>
    public static IReadOnlyList<DetectedIsland> FindUnsupported(
        Mesh worldMesh, SupportGraph? supports, float layerHeightMm,
        float minIslandAreaMm2, float plateZ, float overhangAngleDegrees)
    {
        ArgumentNullException.ThrowIfNull(worldMesh);
        var layers = LayerStack.Slice(worldMesh, layerHeightMm);
        var islands = IslandFinder.Find(layers, worldMesh.Bounds.Min.Z, layerHeightMm,
            minIslandAreaMm2, plateZ, overhangAngleDegrees, includeOverhangs: false);
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
                island.Centroid, island.AreaMm2, island.LayerIndex)
            {
                Footprint = island.Footprint,
                SupportPlaneZ = Math.Max(0, (island.LayerIndex - 0.5) * layerHeightMm)
            })
            .ToList();
    }

    /// <summary>Updates cached markers after support edits without slicing the model again.</summary>
    public static IReadOnlyList<DetectedIsland> FilterUnsupported(
        IReadOnlyList<DetectedIsland> islands, SupportGraph supports)
    {
        var sections = islands.Where(i => i.Footprint is not null)
            .Select(i => i.SupportPlaneZ).Distinct()
            .ToDictionary(z => z, z => SupportSliceGeometry.SectionsAt(supports, z));
        return islands.Where(i => i.Footprint is null || MeshSlicer.AreaMm2(
            Clipper.Intersect(i.Footprint, sections[i.SupportPlaneZ], FillRule.NonZero)) <= 0).ToList();
    }

    internal static bool IsReachedBySupport(Island island, Paths64 supportSections) =>
        MeshSlicer.AreaMm2(Clipper.Intersect(island.Footprint, supportSections, FillRule.NonZero)) > 0;
}
