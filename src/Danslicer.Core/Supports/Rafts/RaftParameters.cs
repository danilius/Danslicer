namespace Danslicer.Core.Supports.Rafts;

/// <summary>Plate: one filled silhouette of the feet. Web: a disc per foot joined by flat bars.</summary>
public enum RaftType
{
    Plate = 0,
    Web = 1,
}

/// <summary>
/// The raft settings an object takes when a raft is added to it (SUPPORT-GEOMETRY-SPEC "Rafts").
/// Lengths in millimetres, angles in degrees. Immutable so an object's snapshot cannot drift
/// from under the raft it describes.
/// </summary>
public sealed record RaftParameters
{
    public RaftType Type { get; init; } = RaftType.Plate;
    /// <summary>Height of the raft on the plate.</summary>
    public float Thickness { get; init; } = 1f;
    /// <summary>
    /// Angle from vertical of the scraper lip on the raft's outside: the top overhangs the plate
    /// footprint by Thickness / tan(angle), so the bottom is narrower than the top (user,
    /// 2026-09-09). 90 = no lip.
    /// </summary>
    public float EdgeAngleDegrees { get; init; } = 45f;
    /// <summary>Disc under each foot (Web), and each foot's footprint for the Plate silhouette.</summary>
    public float DiscDiameter { get; init; } = 5f;
    /// <summary>Width of the flat bars between neighbouring feet (Web).</summary>
    public float BarWidth { get; init; } = 4f;
    /// <summary>Bars longer than this are not laid; 0 = no limit (Web).</summary>
    public float MaxBarLength { get; init; } = 15f;
    /// <summary>How far the plate extends beyond the outermost feet (Plate).</summary>
    public float Margin { get; init; } = 2f;
    /// <summary>The widest gap between feet that the plate fills in (Plate).</summary>
    public float BridgingDistance { get; init; } = 8f;

    public static RaftParameters Default { get; } = new();

    /// <summary>Clamps every value into the range the builder can honour.</summary>
    public RaftParameters Normalize() => this with
    {
        Thickness = Sane(Thickness, 0.05f, 1f),
        EdgeAngleDegrees = float.IsFinite(EdgeAngleDegrees) ? Math.Clamp(EdgeAngleDegrees, 5f, 90f) : 45f,
        DiscDiameter = Sane(DiscDiameter, 0.1f, 5f),
        BarWidth = Sane(BarWidth, 0.1f, 4f),
        MaxBarLength = float.IsFinite(MaxBarLength) ? MathF.Max(0f, MaxBarLength) : 15f,
        Margin = float.IsFinite(Margin) ? MathF.Max(0f, Margin) : 2f,
        BridgingDistance = float.IsFinite(BridgingDistance) ? MathF.Max(0f, BridgingDistance) : 8f,
    };

    private static float Sane(float value, float min, float fallback) =>
        float.IsFinite(value) && value >= min ? value : fallback;
}
