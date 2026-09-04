using System.Text;
using Danslicer.Core.Slicing;

namespace Danslicer.Core.IO;

/// <summary>
/// Writes Anycubic Photon Workshop files using the selected printer's compatible format version.
/// Layout: file mark, HEADER, PREVIEW, grey table, LAYERDEF table, EXTRA, MACHINE, then the
/// run-length encoded layer images. Addresses in the file mark are absolute byte offsets.
/// Written from the published structure description; contains no third-party code.
/// </summary>
public static class PhotonWorkshopWriter
{
    public const uint Version = 516;
    private const int MarkSize = 12;
    private const int LayerDefSize = 32;
    private const uint HeaderTableLength = 84;
    private const uint MachineTableLength = 156;
    private const uint ExtraTableLength = 24; // quirk of the format: 56 bytes follow, but the length field says 24
    private const uint FileMarkSize = MarkSize + 4 * 10;
    private const float MmPerMinToMmPerSec = 1f / 60f;

    public static void Write(SliceResult result, string path)
    {
        using var stream = File.Create(path);
        Write(result, stream);
    }

    public static void Write(SliceResult result, Stream stream)
    {
        var w = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
        var s = result.Settings;
        var resin = result.ResinSettings;
        var p = result.Printer;
        var layerCount = result.LayerCount;
        var aaLevels = s.AntiAliasing ? 16u : 1u;

        // Reserve the file mark; it is written last once all addresses are known.
        var headerAddress = FileMarkSize;
        stream.Position = headerAddress;

        // HEADER
        WriteTableName(w, "HEADER");
        w.Write(HeaderTableLength);
        w.Write(p.PixelPitchX * 1000f);                 // pixel size µm
        w.Write(s.LayerHeight);
        w.Write(resin.Exposure);
        w.Write(resin.LightOffDelay);
        w.Write(resin.BottomExposure);
        w.Write((float)resin.BottomLayers);
        w.Write(resin.LiftHeight);
        w.Write(resin.LiftSpeed * MmPerMinToMmPerSec);
        w.Write(resin.RetractSpeed * MmPerMinToMmPerSec);
        w.Write(result.VolumeMl);
        w.Write(aaLevels);
        w.Write((uint)p.ResolutionX);
        w.Write((uint)p.ResolutionY);
        w.Write(result.VolumeMl * 1.1f);                // weight g, resin density approximation
        w.Write(0f);                                    // price
        w.Write((byte)'$'); w.Write((byte)0); w.Write((byte)0); w.Write((byte)0);
        w.Write(1u);                                    // per-layer settings present
        w.Write((uint)Math.Round(result.EstimatedSeconds));
        w.Write(0u);                                    // transition layer count
        w.Write(0u);                                    // transition layer type
        w.Write(0u);                                    // advanced mode (TSMC) off

        // PREVIEW
        var previewAddress = (uint)stream.Position;
        WriteTableName(w, "PREVIEW");
        w.Write((uint)(MarkSize + 4 + 12 + result.Preview.Length));
        w.Write((uint)Slicer.PreviewWidth);
        w.Write((byte)'x'); w.Write((byte)0); w.Write((byte)0); w.Write((byte)0);
        w.Write((uint)Slicer.PreviewHeight);
        w.Write(result.Preview);

        // Grey level table (no table name)
        var colorTableAddress = (uint)stream.Position;
        w.Write(0u);                                    // use full greyscale: no
        w.Write(16u);                                   // grey count
        for (int i = 0; i < 16; i++)
            w.Write((byte)Math.Min((i + 1) * 255f / aaLevels, 255f));
        w.Write(0u);

        // LAYERDEF table, filled in after the images are written.
        var layerDefAddress = (uint)stream.Position;
        var layerDefTableLength = (uint)(4 + LayerDefSize * layerCount);
        stream.Position = layerDefAddress + MarkSize + 4 + layerDefTableLength;

        // EXTRA
        var extraAddress = (uint)stream.Position;
        WriteTableName(w, "EXTRA");
        w.Write(ExtraTableLength);
        w.Write(2u);                                    // bottom lift stages
        w.Write(resin.BottomLiftHeight);
        w.Write(resin.BottomLiftSpeed * MmPerMinToMmPerSec);
        w.Write(resin.RetractSpeed * MmPerMinToMmPerSec);   // bottom retract speed 2
        w.Write(0f);                                    // bottom lift height 2
        w.Write(resin.BottomLiftSpeed * MmPerMinToMmPerSec);
        w.Write(resin.RetractSpeed * MmPerMinToMmPerSec);   // bottom retract speed 1
        w.Write(2u);                                    // normal lift stages
        w.Write(resin.LiftHeight);
        w.Write(resin.LiftSpeed * MmPerMinToMmPerSec);
        w.Write(resin.RetractSpeed * MmPerMinToMmPerSec);
        w.Write(0f);                                    // lift height 2
        w.Write(resin.LiftSpeed * MmPerMinToMmPerSec);
        w.Write(resin.RetractSpeed * MmPerMinToMmPerSec);

        // MACHINE
        var machineAddress = (uint)stream.Position;
        WriteTableName(w, "MACHINE");
        w.Write(MachineTableLength);
        WriteFixedString(w, p.MachineName, 96);
        WriteFixedString(w, "pw0Img", 16);
        w.Write(16u);                                   // max anti-aliasing level
        w.Write(1u);                                    // property fields
        w.Write(p.BuildVolume.X);
        w.Write(p.BuildVolume.Y);
        w.Write(p.BuildVolume.Z);
        w.Write(p.FormatVersion);                       // max file version
        w.Write(6506241u);                              // machine background colour

        // Layer images
        var layerImageAddress = (uint)stream.Position;
        var addresses = new uint[layerCount];
        for (int i = 0; i < layerCount; i++)
        {
            addresses[i] = (uint)stream.Position;
            w.Write(result.Layers[i].Rle);
        }
        var end = stream.Position;

        // LAYERDEF
        stream.Position = layerDefAddress;
        WriteTableName(w, "LAYERDEF");
        w.Write(layerDefTableLength);
        w.Write((uint)layerCount);
        for (int i = 0; i < layerCount; i++)
        {
            var layer = result.Layers[i];
            w.Write(addresses[i]);
            w.Write((uint)layer.Rle.Length);
            w.Write(resin.LiftHeightForLayer(i));
            w.Write(resin.LiftSpeedForLayer(i) * MmPerMinToMmPerSec);
            w.Write(resin.ExposureForLayer(i));
            w.Write(s.LayerHeight);
            w.Write(layer.LitPixels);
            w.Write(0u);
        }

        // File mark
        stream.Position = 0;
        WriteFixedString(w, "ANYCUBIC", MarkSize);
        w.Write(p.FormatVersion);
        w.Write(8u);                                    // number of tables
        w.Write(headerAddress);
        w.Write(0u);                                    // software table (not written for 516)
        w.Write(previewAddress);
        w.Write(colorTableAddress);
        w.Write(layerDefAddress);
        w.Write(extraAddress);
        w.Write(machineAddress);
        w.Write(layerImageAddress);

        stream.Position = end;
        w.Flush();
    }

    private static void WriteTableName(BinaryWriter w, string name) => WriteFixedString(w, name, MarkSize);

    private static void WriteFixedString(BinaryWriter w, string value, int length)
    {
        var bytes = Encoding.ASCII.GetBytes(value);
        var n = Math.Min(bytes.Length, length - 1);
        w.Write(bytes, 0, n);
        for (int i = n; i < length; i++) w.Write((byte)0);
    }
}
