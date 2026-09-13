using Danslicer.Core.Printers;
using Danslicer.Core.Slicing;

namespace Danslicer.Core.IO;

/// <summary>Native export dispatch. UVtools is never required to generate print files.</summary>
public static class NativePrintWriter
{
    public static string Limitations(PrinterDefinition p) => p.FirmwareControlsPeel
        ? "Tilt-vat firmware controls peeling; lift settings are not applied. Layer grayscale uses 7-bit precision."
        : p.NativeFormat switch
        {
            "anet" => "Binary layers; exposure and lift height round up to whole units, lift speed to whole mm/s. One lift setting applies to all layers. Retract and delay are controlled by firmware.",
            "svgx" => "Binary vector layers. Peel motion and delays are controlled by firmware.",
            "cxdlp" => "Exposure rounds up to 0.1 s; bottom exposure, lift height and delay to whole units; speeds to whole mm/s. Minimum delay 1 s.",
            "ctb" or "ctb-encrypted" or "phz" or "cxdlp-v4" => "Layer grayscale is quantized to the format's 7-bit precision.",
            "sl1" => "Prusa firmware controls tilt motion and interprets the bottom count as exposure fade layers.",
            "lgs" => "16-level grayscale; retract motion is controlled by firmware.",
            _ => "",
        };

    public static void ValidatePrinter(PrinterDefinition p)
    {
        if (p.NativeFormat == "photon-workshop") { PhotonWorkshopFormat.ValidatePrinter(p); return; }
        PhotonWorkshopFormat.ValidatePrinter(p with { FileExtension = "pwmx", FormatVersion = 516 });
        bool supported = p.NativeFormat switch
        {
            "goo" => p.FileExtension == "goo" && p.FormatVersion == 3,
            "ctb" => p.FileExtension == "ctb" && p.FormatVersion == 2,
            "ctb-encrypted" => p.FileExtension == "ctb" && p.FormatVersion == 5,
            "phz" => p.FileExtension == "phz" && p.FormatVersion == 2,
            "cxdlp" => p.FileExtension == "cxdlp" && p.FormatVersion == 3,
            "cxdlp-v4" => p.FileExtension == "cxdlpv4" && p.FormatVersion == 4,
            "sl1" => p.FileExtension is "sl1" or "sl1s" && p.FormatVersion == 1,
            "cws" or "cws-rgb" => p.FileExtension == "cws" && p.FormatVersion == 1,
            "chitu-zip" => p.FileExtension == "zip" && p.FormatVersion == 1,
            "lgs" => p.FileExtension is "lgs" or "lgs30" or "lgs120" or "lgs4k" && p.FormatVersion == 1,
            "anet" => p.FileExtension is "n4" or "n7" && p.FormatVersion == 3,
            "svgx" => p.FileExtension == "svgx" && p.FormatVersion == 1,
            _ => false,
        };
        if (!supported) throw new NotSupportedException($"No native writer for {p.NativeFormat} .{p.FileExtension} version {p.FormatVersion}.");
        if (p.ResolutionX > ushort.MaxValue || p.ResolutionY > ushort.MaxValue)
            throw new InvalidOperationException("Printer resolution exceeds this format's 16-bit dimension limit.");
        if (p.NativeFormat == "cxdlp" && (p.ResolutionX >= 16384 || p.ResolutionY >= 8192))
            throw new InvalidOperationException("CXDLP line coordinates exceed the format limit.");
    }

    public static void Write(SliceResult result, string path)
    {
        ValidatePrinter(result.Printer); PhotonWorkshopFormat.ValidateContent(result);
        ValidateJobLimits(result);
        if (!path.EndsWith("." + result.Printer.FileExtension.TrimStart('.'), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Export filename must end in .{result.Printer.FileExtension}.");
        string destination = Path.GetFullPath(path);
        string temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.ReadWrite)) Write(result, stream);
            File.Move(temporary, destination, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public static void Write(SliceResult result, Stream stream)
    {
        ValidatePrinter(result.Printer); PhotonWorkshopFormat.ValidateContent(result);
        ValidateJobLimits(result);
        if (!stream.CanWrite || !stream.CanSeek) throw new ArgumentException("Native output must be writable and seekable.", nameof(stream));
        if (result.Printer.NativeFormat is "cxdlp" or "cxdlp-v4" && !stream.CanRead)
            throw new ArgumentException("Creality output must also be readable for its checksum.", nameof(stream));
        stream.Position = 0;
        switch (result.Printer.NativeFormat)
        {
            case "photon-workshop": PhotonWorkshopWriter.Write(result, stream); return;
            case "goo": GooWriter.Write(result, stream); break;
            case "ctb": case "ctb-encrypted": case "phz": case "cxdlp-v4": ChituWriter.Write(result, stream); break;
            case "cxdlp": CrealityWriter.Write(result, stream); break;
            case "sl1": case "cws": case "cws-rgb": case "chitu-zip": ResinArchiveWriter.Write(result, stream); break;
            case "lgs": LegacyResinWriters.Longer(result, stream); break;
            case "anet": LegacyResinWriters.Anet(result, stream); break;
            case "svgx": FlashforgeWriter.Write(result, stream); break;
            default: throw new NotSupportedException(result.Printer.NativeFormat);
        }
        stream.SetLength(stream.Position);
    }

    private static void ValidateJobLimits(SliceResult result)
    {
        var s = result.ResinSettings;
        if (result.Printer.NativeFormat == "cxdlp" && (result.LayerCount > ushort.MaxValue || s.Exposure * 10 > ushort.MaxValue
            || s.BottomExposure > ushort.MaxValue || s.LightOffDelay > ushort.MaxValue || s.LiftHeight > ushort.MaxValue
            || s.BottomLiftHeight > ushort.MaxValue || s.BottomLiftSpeed / 60 > ushort.MaxValue
            || s.LiftSpeed / 60 > ushort.MaxValue || s.RetractSpeed / 60 > ushort.MaxValue))
            throw new InvalidOperationException("Print settings exceed the CXDLP field limits.");
    }
}
