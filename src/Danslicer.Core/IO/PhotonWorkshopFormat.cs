using Danslicer.Core.Printers;
using Danslicer.Core.Slicing;

namespace Danslicer.Core.IO;

/// <summary>Implemented pw0Img containers, not a firmware compatibility certificate.</summary>
public static class PhotonWorkshopFormat
{
    public static bool Supports(string extension, uint version) => extension.TrimStart('.').ToLowerInvariant() switch
    {
        "pw0" or "pwx" => version == 1,
        "pwmx" or "pwmo" or "pwms" or "pmsq" or "dlp" => version is 1 or 515 or 516,
        "pwma" or "pm3" or "pm3m" => version is 515 or 516,
        "pwmb" or "dl2p" or "pmx2" or "pm3r" => version is 515 or 516 or 517,
        "pm3n" => version is 516 or 517, // Official TEST.pm3n is 516; current profiles use 517.
        "pm5" or "px6s" => version == 517,
        "pm5s" or "m5sp" => version == 518,
        _ => false,
    };

    public static void ValidatePrinter(PrinterDefinition p)
    {
        if (string.IsNullOrWhiteSpace(p.FileExtension) || !Supports(p.FileExtension, p.FormatVersion))
            throw new NotSupportedException($"No native writer for .{p.FileExtension} version {p.FormatVersion}.");
        if (p.ResolutionX <= 0 || p.ResolutionY <= 0 || (long)p.ResolutionX * p.ResolutionY > int.MaxValue
            || (p.FormatVersion == 518 && (p.ResolutionX > ushort.MaxValue || p.ResolutionY > ushort.MaxValue)))
            throw new InvalidOperationException("Printer resolution exceeds the native format's limits.");
        if (!Positive(p.DisplayWidthMm) || !Positive(p.DisplayHeightMm) || !Positive(p.ZTravelMm)
            || !Positive(p.BuildVolume.X) || !Positive(p.BuildVolume.Y)
            || !Positive(p.PixelPitchX * 1000) || !Positive(p.PixelPitchY * 1000)
            || p.BuildVolume.X > p.DisplayWidthMm || p.BuildVolume.Y > p.DisplayHeightMm)
            throw new InvalidOperationException("Usable print dimensions must fit inside the full display.");
        if (string.IsNullOrWhiteSpace(p.MachineName) || p.MachineName.Length > 95
            || p.MachineName.Any(c => c < 32 || c > 126))
            throw new InvalidOperationException("Machine name must contain 1–95 printable ASCII characters.");
    }

    public static void Validate(SliceResult result)
    {
        ValidatePrinter(result.Printer);
        ValidateContent(result);
    }

    internal static void ValidateContent(SliceResult result)
    {
        if (result.LayerCount == 0 || !Positive(result.Settings.LayerHeight))
            throw new InvalidOperationException("A print needs layers with a positive layer height.");
        if (!float.IsFinite(result.MinX) || !float.IsFinite(result.MinY) || !float.IsFinite(result.MaxX)
            || !float.IsFinite(result.MaxY) || !float.IsFinite(result.PrintHeight))
            throw new InvalidOperationException("Print bounds are invalid.");
        var resin = result.ResinSettings;
        if (resin != resin.Normalize() || !float.IsFinite(result.VolumeMl) || result.VolumeMl < 0
            || !double.IsFinite(result.EstimatedSeconds) || result.EstimatedSeconds > uint.MaxValue)
            throw new InvalidOperationException("Print exposure, motion or material values are invalid.");
        if (result.PreviewWidth <= 0 || result.PreviewHeight <= 0
            || (long)result.PreviewWidth * result.PreviewHeight * 2 != result.Preview.Length)
            throw new InvalidOperationException("Preview dimensions do not match its RGB565 data.");
        // Include all tables, both previews, and maximum per-layer entry sizes before any write.
        long size = 2048L + result.Preview.Length + 330 * 190 * 2 + result.LayerCount * 76L;
        foreach (var layer in result.Layers)
        {
            if (layer.Rle.Length == 0 || layer.LitPixels > (long)result.Printer.ResolutionX * result.Printer.ResolutionY)
                throw new InvalidOperationException("Invalid encoded layer.");
            size += layer.Rle.Length;
        }
        if (size > uint.MaxValue)
            throw new InvalidOperationException("Print exceeds the format's 32-bit file-address limit.");
    }

    private static bool Positive(float n) => float.IsFinite(n) && n > 0;
}
