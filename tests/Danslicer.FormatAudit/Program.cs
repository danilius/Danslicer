using System.Security.Cryptography;
using System.Text.Json;
using Danslicer.Core.IO;
using Danslicer.Core.Printers;
using Danslicer.Core.Slicing;

var destination = Path.GetFullPath(args.FirstOrDefault() ?? "artifacts/multi-brand-audit");
Directory.CreateDirectory(destination);
var printers = PrinterCatalog.BuiltIn.AsEnumerable();
if (args.Contains("--representative")) printers = printers.GroupBy(p => (p.NativeFormat, p.FileExtension, p.FormatVersion)).Select(g => g.First());
var manifests = new List<object>();
foreach (var p in printers)
{
    int width = p.ResolutionX, height = p.ResolutionY;
    var layers = new List<SlicedLayer>(); var hashes = new List<string>();
    for (int layer = 0; layer < 3; layer++)
    {
        var pixels = new byte[checked(width * height)];
        if (layer != 1)
        {
            // Asymmetric, isolated pixels, horizontal/vertical runs, grayscale, edge and blank-layer coverage.
            pixels[0] = 255; pixels[^1] = 255; pixels[width - 1] = 17;
            for (int y = height / 2; y < height / 2 + 13; y++) for (int x = width / 2; x < width / 2 + 37; x++)
                pixels[y * width + x] = (byte)(((x + y + layer) % 16) * 17);
            for (int x = 17; x < Math.Min(width, 600); x++) pixels[5 * width + x] = 255;
            for (int y = 20; y < Math.Min(height, 500); y++) pixels[y * width + 9] = 136;
        }
        uint lit = (uint)pixels.Count(v => v != 0);
        layers.Add(new SlicedLayer { Rle = PhotonRle.Encode(pixels), LitPixels = lit, AreaMm2 = lit * p.PixelPitchX * p.PixelPitchY, Z = (layer + 1) * .05f });
        for (int i = 0; i < pixels.Length; i++) pixels[i] = p.NativeFormat switch
        {
            "anet" or "svgx" => pixels[i] >= 128 ? (byte)255 : (byte)0,
            "ctb" or "ctb-encrypted" or "cxdlp-v4" => pixels[i] < 2 ? (byte)0 : (byte)((pixels[i] >> 1) * 2 + 1),
            "phz" => pixels[i] >= 252 ? (byte)255 : (byte)((pixels[i] & 254) | ((pixels[i] >> 1) & 1)),
            _ => pixels[i],
        };
        hashes.Add(Convert.ToHexString(SHA256.HashData(pixels)));
    }
    var result = new SliceResult { Printer = p, Settings = PrintSettings.Default,
        ResinSettings = ResinSettings.Default with { BottomLayers = 1, Exposure = 3, BottomExposure = 30, LightOffDelay = 2, LiftHeight = 6, LiftSpeed = 120, RetractSpeed = 180, BottomLiftHeight = 8, BottomLiftSpeed = 60 },
        Layers = layers, VolumeMl = .123f, Preview = PreviewRenderer.Blank(p.PreviewWidth, p.PreviewHeight), PreviewWidth = p.PreviewWidth, PreviewHeight = p.PreviewHeight,
        MinX = -p.DisplayWidthMm / 2, MinY = -p.DisplayHeightMm / 2, MaxX = p.DisplayWidthMm / 2, MaxY = p.DisplayHeightMm / 2 };
    string filename = p.Id + "." + p.FileExtension;
    NativePrintWriter.Write(result, Path.Combine(destination, filename));
    manifests.Add(new { id = p.Id, filename, format = p.NativeFormat, version = p.FormatVersion, width, height, layers = hashes,
        layerHeight = .05, exposure = 3, bottomExposure = 30, bottomLayers = 1, displayWidth = p.DisplayWidthMm, displayHeight = p.DisplayHeightMm,
        liftHeight = p.FirmwareControlsPeel ? .05f : 6, liftSpeed = p.FirmwareControlsPeel ? .05f : 120,
        bottomLiftHeight = p.FirmwareControlsPeel ? .05f : 8, bottomLiftSpeed = p.FirmwareControlsPeel ? .05f : 60,
        retractSpeed = p.FirmwareControlsPeel ? .05f : 180, firmwareControlsPeel = p.FirmwareControlsPeel });
    Console.WriteLine($"Wrote {filename}");
}
File.WriteAllText(Path.Combine(destination, "expected.json"), JsonSerializer.Serialize(manifests, new JsonSerializerOptions { WriteIndented = true }));
