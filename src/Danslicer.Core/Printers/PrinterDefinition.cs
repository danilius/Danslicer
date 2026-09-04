using System.Numerics;
using System.Text.Json.Serialization;

namespace Danslicer.Core.Printers;

/// <summary>Persistable physical and file-format description of a printer.</summary>
public sealed record PrinterDefinition(
    string Id,
    bool IsBuiltIn,
    string Name,
    string MachineName,
    string FileExtension,
    float DisplayWidthMm,
    float DisplayHeightMm,
    float ZTravelMm,
    int ResolutionX,
    int ResolutionY,
    bool MirrorX,
    bool MirrorY,
    uint FormatVersion)
{
    public const string PhotonMonoXId = "anycubic-photon-mono-x";

    [JsonIgnore]
    public Vector3 BuildVolume => new(DisplayWidthMm, DisplayHeightMm, ZTravelMm);
    [JsonIgnore]
    public float PixelPitchX => DisplayWidthMm / ResolutionX;
    [JsonIgnore]
    public float PixelPitchY => DisplayHeightMm / ResolutionY;

    /// <summary>
    /// Anycubic Photon Mono X. Layer images are mirrored in X, matching the PrusaSlicer and UVtools
    /// printer profiles; to be confirmed with an asymmetric test print.
    /// </summary>
    public static PrinterDefinition PhotonMonoX => new(
        PhotonMonoXId,
        IsBuiltIn: true,
        "Anycubic Photon Mono X",
        "Photon Mono X",
        "pwmx",
        192f,
        120f,
        245f,
        3840,
        2400,
        MirrorX: true,
        MirrorY: false,
        FormatVersion: 516);

    /// <summary>Returns a safe persisted definition, repairing only unusable values.</summary>
    public PrinterDefinition Normalize()
    {
        var fallback = PhotonMonoX;
        var normalizedId = string.IsNullOrWhiteSpace(Id) ? Guid.NewGuid().ToString("N") : Id.Trim();
        var normalizedName = string.IsNullOrWhiteSpace(Name) ? "Unnamed printer" : Name.Trim();
        var normalizedMachine = string.IsNullOrWhiteSpace(MachineName) ? normalizedName : MachineName.Trim();
        var extension = (FileExtension ?? "").Trim().TrimStart('.');
        if (extension.Length == 0) extension = fallback.FileExtension;
        return this with
        {
            Id = normalizedId,
            IsBuiltIn = normalizedId == PhotonMonoXId,
            Name = normalizedName,
            MachineName = normalizedMachine,
            FileExtension = extension,
            DisplayWidthMm = Positive(DisplayWidthMm, fallback.DisplayWidthMm),
            DisplayHeightMm = Positive(DisplayHeightMm, fallback.DisplayHeightMm),
            ZTravelMm = Positive(ZTravelMm, fallback.ZTravelMm),
            ResolutionX = ResolutionX > 0 ? ResolutionX : fallback.ResolutionX,
            ResolutionY = ResolutionY > 0 ? ResolutionY : fallback.ResolutionY,
            FormatVersion = FormatVersion > 0 ? FormatVersion : fallback.FormatVersion,
        };
    }

    public PrinterDefinition CreateUserCopy(string name) => this with
    {
        Id = Guid.NewGuid().ToString("N"),
        IsBuiltIn = false,
        Name = name.Trim(),
    };

    private static float Positive(float value, float fallback) =>
        float.IsFinite(value) && value > 0 ? value : fallback;
}
