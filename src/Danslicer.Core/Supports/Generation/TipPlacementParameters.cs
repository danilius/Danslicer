using System.Numerics;
using Danslicer.Core.Supports.Routing;

namespace Danslicer.Core.Supports.Generation;

/// <summary>
/// Profile-like parameters for stage-1 tip placement. Lengths in millimetres, angles in degrees.
/// Placement is a pure function of (mesh, region faces, these parameters, existing graph, seed).
/// </summary>
public sealed record TipPlacementParameters
{
    /// <summary>
    /// Faces whose angle from vertical exceeds this are overhangs. 0° is a vertical wall,
    /// 90° is a horizontal underside. Default 45°, matching the common resin threshold.
    /// </summary>
    public float OverhangAngleDegrees { get; init; } = 45f;

    /// <summary>Poisson-disk spacing between newly placed tips.</summary>
    public float SpacingMm { get; init; } = 2.5f;

    /// <summary>
    /// Minimum distance to an existing graph tip (any pass or manual) and between required
    /// (island / local-minimum) tips. Defaults to the same value as <see cref="SpacingMm"/>.
    /// </summary>
    public float MinSpacingMm { get; init; } = 2.5f;

    /// <summary>Islands smaller than this are ignored.</summary>
    public float MinIslandAreaMm2 { get; init; } = 0.5f;

    /// <summary>Layer height used when slicing for islands. Reuses <see cref="Slicing.MeshSlicer"/>.</summary>
    public float LayerHeightMm { get; init; } = 0.05f;

    public float TipDiameterMm { get; init; } = 0.4f;

    /// <summary>
    /// Sharp-feature bias, 0 to 1. Zero ignores edges; 1 strongly prefers ridges and corners
    /// in scoring and extra edge samples. Interior faces still receive tips unless
    /// <see cref="ForceEdgePlacement"/> is set.
    /// </summary>
    public float EdgePreference { get; init; } = 0f;

    /// <summary>
    /// When true, the interior of faces never receives a tip (DESIGN.md §8.4 "forced").
    /// Islands and local minima are still placed; they must be supported regardless of density.
    /// </summary>
    public bool ForceEdgePlacement { get; init; } = false;

    /// <summary>Dihedral angle above which an edge is sharp. DESIGN.md §5.2 default is 30°.</summary>
    public float SharpEdgeDegrees { get; init; } = 30f;

    /// <summary>Z at or below this (plus one layer) is the plate; no tip is placed there.</summary>
    public float PlateZ { get; init; } = 0f;

    /// <summary>
    /// When set, Poisson overhang sampling is replaced by grid projection: lattice verticals
    /// from <see cref="GridRoutingOptions"/> (square/hex, spacing, offset, rotation) are cast
    /// +Z and emit a tip where they hit a downward region face. Islands and minima still run.
    /// Reuses the routing option shape so tips line up with the bases the grid router chooses.
    /// </summary>
    public GridRoutingOptions? Grid { get; init; }

    /// <summary>
    /// Minimum Euclidean distance to any keep-clean face. Zero (default) is membership only:
    /// a candidate on an allowed face is kept even if it shares an edge with a keep-clean face.
    /// Positive values drop candidates whose closest point on the keep-clean subset is nearer
    /// than this, using the mesh BVH.
    /// </summary>
    public float KeepCleanDistanceMm { get; init; } = 0f;

    public static TipPlacementParameters Default { get; } = new();

    /// <summary>
    /// Overhang angle of an outward face normal: 0° vertical, 90° horizontal down, 0° if facing up.
    /// </summary>
    public static float OverhangDegrees(Vector3 outwardNormal)
    {
        var down = -outwardNormal.Z;
        if (down <= 0f) return 0f;
        return MathF.Asin(MathF.Min(down, 1f)) * (180f / MathF.PI);
    }

    public bool IsOverhang(Vector3 outwardNormal) =>
        OverhangDegrees(outwardNormal) > OverhangAngleDegrees;
}
