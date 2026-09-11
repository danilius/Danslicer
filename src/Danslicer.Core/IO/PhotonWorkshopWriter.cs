using System.Text;
using Danslicer.Core.Slicing;

namespace Danslicer.Core.IO;

/// <summary>
/// Writes Anycubic Photon Workshop files using the selected printer's compatible format version.
/// Implements pw0Img versions 1, 515, 516, 517 and 518. Tables and file-mark layout vary by
/// version. Addresses are absolute byte offsets. Legacy Mono X 516 bytes remain unchanged.
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
        PhotonWorkshopFormat.Validate(result);
        if (!string.Equals(Path.GetExtension(path).TrimStart('.'), result.Printer.FileExtension.TrimStart('.'),
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Export filename must end in .{result.Printer.FileExtension.TrimStart('.')} for this printer.");
        using var stream = File.Create(path);
        Write(result, stream);
    }

    public static void Write(SliceResult result, Stream stream)
    {
        PhotonWorkshopFormat.Validate(result);
        if (!stream.CanWrite || !stream.CanSeek)
            throw new ArgumentException("Print output must be writable and seekable.", nameof(stream));
        var w = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
        var s = result.Settings;
        var resin = result.ResinSettings;
        var p = result.Printer;
        var layerCount = result.LayerCount;
        var version = p.FormatVersion;
        var aaLevels = s.AntiAliasing ? 16u : 1u;

        // Reserve the file mark; it is written last once all addresses are known.
        var headerAddress = version switch { <= 515 => 48u, 516 => FileMarkSize, 517 => 56u, _ => 64u };
        stream.Position = headerAddress;

        // HEADER
        WriteTableName(w, "HEADER");
        w.Write(version switch { <= 515 => 80u, 516 => HeaderTableLength, 517 => 92u, _ => 96u });
        w.Write(Math.Max(p.PixelPitchX, p.PixelPitchY) * 1000f); // legacy scalar pitch; per-axis geometry is in MACHINE
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
        w.Write(p.PerLayerSettings ? 1u : 0u);
        w.Write((uint)Math.Round(result.EstimatedSeconds));
        w.Write(0u);                                    // transition layer count
        w.Write(0u);                                    // transition layer type
        if (version >= 516) w.Write(0u);                // advanced mode (TSMC) off
        if (version >= 517)
        {
            w.Write((ushort)0);                         // grayscale parameter; image AA is already baked
            w.Write((ushort)0);                         // no additional blur
            w.Write(0u);                                // unspecified resin type
        }
        if (version >= 518) w.Write(0u);                // no intelligent/automatic exposure

        // PREVIEW
        var previewAddress = (uint)stream.Position;
        WriteTableName(w, "PREVIEW");
        w.Write((uint)(MarkSize + 4 + 12 + result.Preview.Length));
        w.Write((uint)result.PreviewWidth);
        w.Write((byte)'x'); w.Write((byte)0); w.Write((byte)0); w.Write((byte)0);
        w.Write((uint)result.PreviewHeight);
        w.Write(result.Preview);

        // Grey level table (no table name)
        uint colorTableAddress = 0;
        if (version >= 515)
        {
            colorTableAddress = (uint)stream.Position;
            w.Write(0u);                                    // use full greyscale: no
            w.Write(16u);                                   // grey count
            for (int i = 0; i < 16; i++)
                w.Write((byte)Math.Min((i + 1) * 255f / aaLevels, 255f));
            w.Write(0u);
        }

        // LAYERDEF table, filled in after the images are written.
        var layerDefAddress = (uint)stream.Position;
        var layerDefTableLength = (uint)(4 + LayerDefSize * layerCount);
        stream.Position = layerDefAddress + MarkSize + 4 + layerDefTableLength;

        // EXTRA
        uint extraAddress = 0, machineAddress = 0, softwareAddress = 0, modelAddress = 0;
        uint subLayerAddress = 0, preview2Address = 0;
        if (version >= 516)
        {
            extraAddress = (uint)stream.Position;
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
            machineAddress = (uint)stream.Position;
            WriteTableName(w, "MACHINE");
            w.Write(version >= 518 ? 224u : MachineTableLength);
            WriteFixedString(w, p.MachineName, 96);
            WriteFixedString(w, "pw0Img", 16);
            w.Write(16u);                                   // max anti-aliasing level
            w.Write(p.MachinePropertyFields);
            w.Write(p.BuildVolume.X);
            w.Write(p.BuildVolume.Y);
            w.Write(p.BuildVolume.Z);
            w.Write(p.FormatVersion);                       // max file version
            w.Write(6506241u);                              // machine background colour
            if (version >= 518)
            {
                w.Write(p.PixelPitchX * 1000f);
                w.Write(p.PixelPitchY * 1000f);
                w.Write(new byte[32]);
                w.Write(1u);                                // one display
                w.Write(0u);
                w.Write((ushort)p.ResolutionX);
                w.Write((ushort)p.ResolutionY);
                w.Write(new byte[16]);
            }
        }

        if (version >= 517)
        {
            softwareAddress = (uint)stream.Position;
            WriteFixedString(w, "Danslicer", 32);
            w.Write(164u);
            WriteFixedString(w, "1.0", 32);
            WriteFixedString(w, System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier, 64);
            WriteFixedString(w, "", 32);                 // software metadata, no GPU dependency

            modelAddress = (uint)stream.Position;
            WriteTableName(w, "MODEL");
            w.Write(48u);                               // inclusive table size
            w.Write(result.MinX); w.Write(result.MinY); w.Write(0f);
            w.Write(result.MaxX); w.Write(result.MaxY); w.Write(result.PrintHeight);
            w.Write(0u); w.Write(0f);                    // no separate support metadata; supports are in layers
        }

        if (version >= 518)
        {
            subLayerAddress = (uint)stream.Position;
            stream.Position += 24L + 44L * layerCount;
            preview2Address = (uint)stream.Position;
            WriteTableName(w, "PREVIEW2");
            w.Write(28u + 330u * 190u * 2u);
            w.Write(330u); w.Write(0u); w.Write(190u);
            for (int y = 0; y < 190; y++)
                for (int x = 0; x < 330; x++)
                {
                    int pixel = ((y * result.PreviewHeight / 190) * result.PreviewWidth
                        + x * result.PreviewWidth / 330) * 2;
                    w.Write(result.Preview[pixel]); w.Write(result.Preview[pixel + 1]);
                }
        }

        // Layer images
        var layerImageAddress = (uint)stream.Position;
        if (version == 515) extraAddress = layerImageAddress;
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

        if (version >= 518)
        {
            stream.Position = subLayerAddress;
            WriteTableName(w, "SUBIMGS");
            w.Write((uint)(24L + 44L * layerCount));
            w.Write((uint)layerCount); w.Write(1u);
            for (int i = 0; i < layerCount; i++)
            {
                w.Write(addresses[i]);
                w.Write((uint)result.Layers[i].Rle.Length);
                w.Write(result.Layers[i].LitPixels);
                w.Write(new byte[32]);
            }
        }

        // File mark
        stream.Position = 0;
        WriteFixedString(w, "ANYCUBIC", MarkSize);
        w.Write(p.FormatVersion);
        w.Write(version switch { 1 => 4u, 515 => 5u, 516 => 8u, 517 => 9u, _ => 11u });
        w.Write(headerAddress);
        w.Write(softwareAddress);
        w.Write(previewAddress);
        w.Write(colorTableAddress);
        w.Write(layerDefAddress);
        w.Write(extraAddress);
        if (version >= 516) w.Write(machineAddress);
        w.Write(layerImageAddress);
        if (version >= 517) w.Write(modelAddress);
        if (version >= 518) { w.Write(subLayerAddress); w.Write(preview2Address); }

        stream.Position = end;
        stream.SetLength(end);
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
