using System.Numerics;
using Danslicer.Core.Printers;

namespace Danslicer.Core.Supports.Checks;

public enum CheckKind
{
    SuctionCup,
    SupportProximity,
    SupportModelProximity,
    ObjectProximity,
    Island,
    BelowPlate,
    OutsideVolume,
}

public enum CheckSeverity
{
    Info,
    Warning,
    Error,
}

/// <summary>
/// One print-check result. Unused fields stay null; the CLI and UI format, this type does not.
/// Positions are in world millimetres. Layer indices match the slicer (layer 0 at mid first layer).
/// </summary>
public readonly record struct CheckFinding
{
    public CheckKind Kind { get; init; }
    public CheckSeverity Severity { get; init; }

    /// <summary>Object index in the list passed to <see cref="PrintChecker.Check"/>.</summary>
    public int? ObjectIndex { get; init; }
    public int? ObjectIndexB { get; init; }

    public Guid? ElementIdA { get; init; }
    public Guid? ElementIdB { get; init; }

    public int? LayerFrom { get; init; }
    public int? LayerTo { get; init; }

    public Vector3? Point { get; init; }
    public Vector3? PointA { get; init; }
    public Vector3? PointB { get; init; }

    public float? DistanceMm { get; init; }
    public float? VolumeMm3 { get; init; }
    public float? AreaMm2 { get; init; }
}

/// <summary>Thresholds and printer bounds for print checks. Lengths in millimetres.</summary>
public sealed record PrintCheckParameters
{
    public float LayerHeightMm { get; init; } = 0.05f;
    public float MinIslandAreaMm2 { get; init; } = 0.5f;
    public float OverhangAngleDegrees { get; init; } = 45f;
    public float PlateZ { get; init; } = 0f;

    /// <summary>Support capsules closer than this, and not in the same connected component, are reported.</summary>
    public float SupportSupportThresholdMm { get; init; } = 1.0f;

    /// <summary>Support-to-model clearance for surfaces the support does not touch.</summary>
    public float SupportModelThresholdMm { get; init; } = 0.5f;

    public float ObjectObjectThresholdMm { get; init; } = 1.0f;

    /// <summary>Suction cups smaller than this are ignored (dimples, quantisation).</summary>
    public float MinSuctionVolumeMm3 { get; init; } = 5f;

    /// <summary>
    /// Side openings narrower than this are treated as closed (nearly-closed cups).
    /// A real drain hole must be at least this wide.
    /// </summary>
    public float DrainOpeningMm { get; init; } = 0.8f;

    /// <summary>Build volume with the plate centred on the origin in X/Y, Z up from <see cref="PlateZ"/>.</summary>
    public Vector3 BuildVolume { get; init; } = PrinterDefinition.PhotonMonoX.BuildVolume;

    public static PrintCheckParameters Default { get; } = new();
}
