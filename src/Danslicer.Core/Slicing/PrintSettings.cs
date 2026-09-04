namespace Danslicer.Core.Slicing;

/// <summary>
/// Per-print raster and dimensional choices. Printer facts live in PrinterDefinition and cure/
/// peel behaviour lives in ResinSettings.
/// </summary>
public sealed record PrintSettings
{
    public float LayerHeight { get; init; } = 0.05f;
    /// <summary>Anti-aliased layer edges. The Photon Workshop format stores 16 grey levels.</summary>
    public bool AntiAliasing { get; init; } = true;
    /// <summary>Contour offset applied to every layer, negative shrinks. Compensates for light bleed.</summary>
    public float XyCompensation { get; init; } = 0f;

    public static PrintSettings Default { get; } = new();
}
