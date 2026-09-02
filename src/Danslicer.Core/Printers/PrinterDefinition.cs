using System.Numerics;

namespace Danslicer.Core.Printers;

/// <summary>Physical description of a printer. Dimensions in millimetres.</summary>
public sealed record PrinterDefinition(
    string Name,
    string MachineName,
    string FileExtension,
    Vector3 BuildVolume,
    int ResolutionX,
    int ResolutionY,
    bool MirrorX,
    bool MirrorY)
{
    public float PixelPitchX => BuildVolume.X / ResolutionX;
    public float PixelPitchY => BuildVolume.Y / ResolutionY;

    /// <summary>
    /// Anycubic Photon Mono X. Layer images are mirrored in X, matching the PrusaSlicer and UVtools
    /// printer profiles; to be confirmed with an asymmetric test print.
    /// </summary>
    public static PrinterDefinition PhotonMonoX { get; } = new(
        "Anycubic Photon Mono X",
        "Photon Mono X",
        "pwmx",
        new Vector3(192f, 120f, 245f),
        3840,
        2400,
        MirrorX: true,
        MirrorY: false);
}
