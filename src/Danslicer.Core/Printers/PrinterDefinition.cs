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

    /// <summary>The Photon Workshop PREVIEW block's size, and the default for every printer.
    /// A definition that does not say otherwise — every one written before this existed —
    /// produces byte-identical output.</summary>
    public const int DefaultPreviewWidth = 224;
    public const int DefaultPreviewHeight = 168;

    /// <summary>
    /// Size of the thumbnail embedded in the print file. Not positional: adding it to the
    /// record's parameter list would rewrite every call site and every persisted definition for
    /// a value almost nobody sets. Other printers want other sizes; this is where that lives.
    /// </summary>
    public int PreviewWidth { get; init; } = DefaultPreviewWidth;
    public int PreviewHeight { get; init; } = DefaultPreviewHeight;

    /// <summary>Centred usable plate area; null retains the full-display legacy behaviour.</summary>
    public float? PrintWidthMm { get; init; }
    public float? PrintHeightMm { get; init; }
    // Defaults deliberately preserve existing Mono X files and embedded user definitions.
    public bool PerLayerSettings { get; init; } = true;
    public uint MachinePropertyFields { get; init; } = 1;
    /// <summary>Explicit container identity where several incompatible formats share a suffix.</summary>
    public string NativeFormat { get; init; } = "photon-workshop";
    /// <summary>Tilt-vat firmware controls peeling; CTB motion fields carry compatibility placeholders.</summary>
    public bool FirmwareControlsPeel { get; init; }

    [JsonIgnore]
    public string CompatibilityNote => Id == PhotonMonoXId
        ? "Mono X: mirror orientation confirmed by a user print."
        : "Experimental profile: physical printing unverified. Calibrate for your firmware and resin.";

    [JsonIgnore]
    public Vector3 BuildVolume => new(PrintWidthMm ?? DisplayWidthMm, PrintHeightMm ?? DisplayHeightMm, ZTravelMm);
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
            IsBuiltIn = normalizedId == PhotonMonoXId || (IsBuiltIn && PrinterCatalog.IsBuiltInId(normalizedId)),
            Name = normalizedName,
            MachineName = normalizedMachine,
            FileExtension = extension,
            DisplayWidthMm = Positive(DisplayWidthMm, fallback.DisplayWidthMm),
            DisplayHeightMm = Positive(DisplayHeightMm, fallback.DisplayHeightMm),
            ZTravelMm = Positive(ZTravelMm, fallback.ZTravelMm),
            ResolutionX = ResolutionX > 0 ? ResolutionX : fallback.ResolutionX,
            ResolutionY = ResolutionY > 0 ? ResolutionY : fallback.ResolutionY,
            FormatVersion = FormatVersion > 0 ? FormatVersion : fallback.FormatVersion,
            PreviewWidth = PreviewWidth > 0 ? PreviewWidth : DefaultPreviewWidth,
            PreviewHeight = PreviewHeight > 0 ? PreviewHeight : DefaultPreviewHeight,
            PrintWidthMm = UsableSize(PrintWidthMm, Positive(DisplayWidthMm, fallback.DisplayWidthMm)),
            PrintHeightMm = UsableSize(PrintHeightMm, Positive(DisplayHeightMm, fallback.DisplayHeightMm)),
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

    private static float? UsableSize(float? value, float display) =>
        value is { } size && float.IsFinite(size) && size > 0 ? Math.Min(size, display) : null;
}
