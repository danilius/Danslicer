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

    /// <summary>
    /// Islands smaller than this are ignored when mini supports are disabled. When they are
    /// enabled, <see cref="MiniIslandMaxAreaMm2"/> becomes the mini/regular boundary instead.
    /// 0.1 mm² is ~40 Mono X pixels — small spikes such as teeth are real printable features
    /// and must be supported (user screen test 2026-09-03: the old 0.5 default silently dropped
    /// tooth apexes).
    /// </summary>
    public float MinIslandAreaMm2 { get; init; } = 0.1f;

    /// <summary>When enabled, below-threshold islands are retained as mini-support-only contacts.</summary>
    public bool EnableMiniSupports { get; init; }
    /// <summary>
    /// Upper area bound for mini-island classification. At use it is constrained between the
    /// mini contact footprint and <see cref="MinIslandAreaMm2"/>.
    /// </summary>
    public float MiniIslandMaxAreaMm2 { get; init; } = 0.1f;
    public float MiniSupportTipDiameterMm { get; init; } = 0.25f;
    public float MiniSupportConeLengthMm { get; init; } = 1f;
    /// <summary>Enables density-based conversion of crowded regular contacts.</summary>
    public bool EnableMiniTipClusters { get; init; }
    /// <summary>
    /// Regular contacts linked by distances below this threshold form density-based mini-tip
    /// clusters. The default is half the default placement spacing.
    /// </summary>
    public float MiniSupportClusterDistanceMm { get; init; } = 1.25f;
    /// <summary>Maximum members in one detected cluster; larger groups split deterministically.</summary>
    public int MiniSupportMaxTipsPerCluster { get; init; } = 4;
    /// <summary>
    /// Maximum local feature cross-section which turns an isolated island or local-minimum
    /// contact into a one-member mini cluster. Zero disables the fineness pass.
    /// </summary>
    public float FineFeatureMaxAreaMm2 { get; init; } = 1f;

    /// <summary>
    /// Dedup radius between island tips. Every island physically needs its own support — two
    /// separate islands are disconnected until higher layers join them — so island tips are
    /// exempt from the general spacing rules and only yield to a tip closer than this.
    /// </summary>
    public float IslandSpacingMm { get; init; } = 0.5f;

    /// <summary>Layer height used when slicing for islands. Reuses <see cref="Slicing.MeshSlicer"/>.</summary>
    public float LayerHeightMm { get; init; } = 0.05f;

    public float TipDiameterMm { get; init; } = 0.4f;

    /// <summary>Contact geometry copied onto every generated candidate. Default preserves capsule slicing.</summary>
    public SupportTipShape TipShape { get; init; } = SupportTipShape.Capsule;

    /// <summary>Cone length along the neck, millimetres. Unused when <see cref="TipShape"/> is Capsule.</summary>
    public float ConeLengthMm { get; init; } = 2f;

    /// <summary>Snap-off ball diameter, millimetres. Zero means no ball. Unused when shape is Capsule.</summary>
    public float BallDiameterMm { get; init; } = 0f;

    /// <summary>How far a cone tip continues past the contact along its axis, millimetres.</summary>
    public float PenetrationDepthMm { get; init; } = 0f;

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

    /// <summary>
    /// Which faces are eligible to receive a support contact at all (<see cref="ContactFaceFilter"/>):
    /// maximum angle of a candidate's face normal from straight down (0,0,-1). 0° keeps only
    /// perfectly horizontal undersides; 90° (the default) keeps every downward-facing candidate,
    /// excluding only faces that point sideways or up — today's behaviour for existing users.
    /// 45° is the common choice for excluding side walls (e.g. a chunky CAD part that should only
    /// get supports on its lower faces).
    /// <para>
    /// This is measured from straight down. <see cref="OverhangAngleDegrees"/> measures from
    /// vertical instead (0° = vertical wall, 90° = horizontal underside) and controls a different
    /// thing (which faces get Poisson-sampled as overhangs) — the two angles are complementary
    /// (down-angle = 90° − overhang-angle) and are easy to confuse. Do not conflate them.
    /// </para>
    /// </summary>
    public float MaxContactFaceAngleDegrees { get; init; } = 90f;

    /// <summary>
    /// When true, <see cref="ContactFaceFilter"/> additionally requires an unobstructed
    /// straight-down line of sight from the contact point to <see cref="PlateZ"/>; a candidate the
    /// mesh occludes from the plate is dropped even if it passes
    /// <see cref="MaxContactFaceAngleDegrees"/>. Off by default (today's behaviour). This composes
    /// with the angle setting rather than replacing it: a downward-facing candidate always has a
    /// straight-down ray that geometrically reaches the plate plane unless something else in the
    /// mesh blocks it, so this is not a third exclusive mode.
    /// </summary>
    public bool RequireContactSeesPlate { get; init; }

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
