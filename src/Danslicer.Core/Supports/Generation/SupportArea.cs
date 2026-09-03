using System.Numerics;
using Danslicer.Core.Geometry;

namespace Danslicer.Core.Supports.Generation;

/// <summary>
/// How badly an area needs support. Derived from max overhang and whether the area
/// contains an island or a local minimum — the "must support" seeds.
/// </summary>
public enum SupportAreaSeverity
{
    Low,
    Medium,
    High,
}

/// <summary>
/// One connected region of faces that need support. Stable <see cref="Id"/> is assigned
/// after sorting by descending area, then centroid, then min face index — same inputs
/// always produce the same ids.
/// </summary>
public sealed record SupportArea(
    int Id,
    IReadOnlyList<int> Faces,
    float AreaMm2,
    Vector3 Centroid,
    Vector3 MeanNormal,
    float MaxOverhangDegrees,
    float MeanOverhangDegrees,
    bool ContainsIsland,
    bool ContainsLocalMinimum,
    SupportAreaSeverity Severity,
    IReadOnlyList<int> PatchIds);

/// <summary>Parameters for <see cref="SupportAreaDetector.Detect"/>.</summary>
public sealed record SupportAreaParameters
{
    public float OverhangAngleDegrees { get; init; } = 45f;
    public float MinAreaMm2 { get; init; } = 0.5f;
    public float LayerHeightMm { get; init; } = 0.05f;
    public float MinIslandAreaMm2 { get; init; } = 0.5f;
    public float PlateZ { get; init; } = 0f;
    public float SharpEdgeDegrees { get; init; } = MeshAnalysis.DefaultSharpEdgeDegrees;

    public static SupportAreaParameters Default { get; } = new();
}
