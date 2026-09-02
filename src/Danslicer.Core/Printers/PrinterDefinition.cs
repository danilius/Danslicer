using System.Numerics;

namespace Danslicer.Core.Printers;

/// <summary>Physical description of a printer. Dimensions in millimetres.</summary>
public sealed record PrinterDefinition(
    string Name,
    Vector3 BuildVolume,
    int ResolutionX,
    int ResolutionY)
{
    public float PixelPitchX => BuildVolume.X / ResolutionX;
    public float PixelPitchY => BuildVolume.Y / ResolutionY;

    public static PrinterDefinition PhotonMonoX { get; } = new(
        "Anycubic Photon Mono X",
        new Vector3(192f, 120f, 245f),
        3840,
        2400);
}
