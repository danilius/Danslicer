using System.Security.Cryptography;
using Danslicer.Core.Slicing;

namespace Danslicer.Core.IO;

internal static class ChituWriter
{
    internal static byte[] Rle(byte[] pixels, bool phz = false)
    {
        using var output = new MemoryStream();
        for (int start = 0; start < pixels.Length;)
        {
            int gray = pixels[start] >> 1, end = start + 1;
            while (end < pixels.Length && pixels[end] >> 1 == gray && end - start < 0xfffffff) end++;
            int count = end - start;
            if (phz)
            {
                output.WriteByte((byte)(gray | 128));
                for (int left = count - 1; left > 0;) { int n = Math.Min(left, 125); output.WriteByte((byte)n); left -= n; }
            }
            else
            {
                output.WriteByte((byte)(gray | (count > 1 ? 128 : 0)));
                if (count > 1)
                {
                    int extra = count < 128 ? 0 : count < 16384 ? 1 : count < 2097152 ? 2 : 3;
                    output.WriteByte((byte)((count >> (8 * extra)) | (extra switch { 0 => 0, 1 => 128, 2 => 192, _ => 224 })));
                    for (int k = extra - 1; k >= 0; k--) output.WriteByte((byte)(count >> (8 * k)));
                }
            }
            start = end;
        }
        return output.ToArray();
    }

    internal static long Preview(NativeBinary w, SliceResult r, int width, int height, int headerSize = 32)
    {
        long start = w.Position;
        w.U32(width); w.U32(height); w.U32(start + headerSize); w.U32(width * height * 2); w.Zero(headerSize - 16);
        // RGB555 with bit 5 reserved for the repeat marker; literal pixels need no RLE marker.
        var rgb = NativeBinary.PreviewRgb(r, width, height);
        for (int i = 0; i < rgb.Length; i += 3) w.U16((rgb[i] >> 3) << 11 | (rgb[i + 1] >> 3) << 6 | rgb[i + 2] >> 3);
        return start;
    }

    public static void Write(SliceResult r, Stream stream)
    {
        if (r.Printer.NativeFormat == "ctb-encrypted") { Encrypted(r, stream); return; }
        var w = new NativeBinary(stream); var p = r.Printer; var s = r.ResinSettings;
        bool phz = p.NativeFormat == "phz", creality = p.NativeFormat == "cxdlp-v4";
        long headerBase = 0;
        if (creality) { w.BigEndian = true; w.Ascii("CXSW3DV2\0"); w.U16(4); w.Ascii(p.MachineName + "\0"); w.BigEndian = false; headerBase = w.Position; w.Zero(92); }
        else w.Zero(phz ? 216 : 112);
        long large = Preview(w, r, 400, 300), small = Preview(w, r, 200, 125);
        long name = w.Position; w.Text(p.MachineName, p.MachineName.Length);
        long parameters = w.Position;
        w.F32(s.BottomLiftHeight); w.F32(s.BottomLiftSpeed); w.F32(s.LiftHeight); w.F32(s.LiftSpeed); w.F32(s.RetractSpeed);
        w.F32(r.VolumeMl); w.F32(0); w.F32(0); w.F32(s.LightOffDelay); w.F32(s.LightOffDelay); w.U32(s.BottomLayers);
        if (creality) { w.F32(s.Exposure); w.F32(s.BottomExposure); }
        w.Zero(16);
        long slicer = w.Position; w.Zero(76);
        if (phz) w.Position = name + p.MachineName.Length;
        if (!creality && !phz) { w.Patch32(slicer + 28, name); w.Patch32(slicer + 32, p.MachineName.Length); w.Patch32(slicer + 36, 15); w.Patch32(slicer + 44, 1); }
        long table = w.Position; int entrySize = creality ? 40 : 36; w.Zero(checked(entrySize * r.LayerCount));
        for (int i = 0; i < r.LayerCount; i++)
        {
            long dataStart = w.Position;
            if (creality)
            {
                w.F32(s.LiftHeightForLayer(i)); w.F32(s.LiftSpeedForLayer(i)); w.F32(0); w.F32(0); w.F32(s.RetractSpeed); w.F32(0); w.F32(0);
                w.F32(0); w.F32(0); w.F32(0); w.F32(255);
            }
            var encoded = Rle(NativeBinary.Pixels(r, i), phz); w.Bytes(encoded); long end = w.Position;
            w.Position = table + i * entrySize; w.F32(r.Layers[i].Z); w.F32(i < s.BottomLayers ? s.BottomExposure : s.Exposure); w.F32(s.LightOffDelay);
            w.U32(dataStart); w.U32(end - dataStart); w.Zero(entrySize - 20); w.Position = end;
        }
        long fileEnd = w.Position; w.Position = headerBase;
        if (creality)
        {
            w.U16(p.ResolutionX); w.U16(p.ResolutionY); w.F32(p.DisplayWidthMm); w.F32(p.DisplayHeightMm); w.F32(p.ZTravelMm);
            w.F32(r.PrintHeight); w.F32(r.Settings.LayerHeight); w.U32(s.BottomLayers); w.U32(small); w.U32(table); w.U32(r.LayerCount); w.U32(large);
            w.U32((long)r.EstimatedSeconds); w.U32(1); w.U32(parameters); w.U32(68); w.U32(1); w.U16(255); w.U16(255); w.U32(0); w.U32(slicer); w.U32(76);
        }
        else if (phz)
        {
            w.U32(0x9fda83ae); w.U32(2); w.F32(r.Settings.LayerHeight); w.F32(s.Exposure); w.F32(s.BottomExposure); w.U32(s.BottomLayers);
            w.U32(p.ResolutionX); w.U32(p.ResolutionY); w.U32(large); w.U32(table); w.U32(r.LayerCount); w.U32(small);
            w.U32((long)r.EstimatedSeconds); w.U32(1); w.U32(1); w.U16(255); w.U16(255); w.Zero(8);
            w.F32(r.PrintHeight); w.F32(p.DisplayWidthMm); w.F32(p.DisplayHeightMm); w.F32(p.ZTravelMm); w.U32(0);
            w.F32(s.LightOffDelay); w.F32(s.LightOffDelay); w.U32(s.BottomLayers); w.U32(0);
            w.F32(s.BottomLiftHeight); w.F32(s.BottomLiftSpeed); w.F32(s.LiftHeight); w.F32(s.LiftSpeed); w.F32(s.RetractSpeed); w.F32(r.VolumeMl); w.F32(0); w.F32(0);
            w.U32(0); w.U32(name); w.U32(p.MachineName.Length);
        }
        else
        {
            w.U32(0x12fd0086); w.U32(2); w.F32(p.DisplayWidthMm); w.F32(p.DisplayHeightMm); w.F32(p.ZTravelMm); w.Zero(8);
            w.F32(r.PrintHeight); w.F32(r.Settings.LayerHeight); w.F32(s.Exposure); w.F32(s.BottomExposure); w.F32(s.LightOffDelay); w.U32(s.BottomLayers);
            w.U32(p.ResolutionX); w.U32(p.ResolutionY); w.U32(large); w.U32(table); w.U32(r.LayerCount); w.U32(small); w.U32((long)r.EstimatedSeconds);
            w.U32(1); w.U32(parameters); w.U32(60); w.U32(1); w.U16(255); w.U16(255); w.U32(0); w.U32(slicer); w.U32(76);
        }
        w.Position = fileEnd;
        if (creality) AppendCrc(w);
    }

    internal static void AppendCrc(NativeBinary w)
    {
        long end = w.Position; w.Stream.SetLength(end); w.Position = 0;
        uint crc = 0; var buffer = new byte[65536];
        while (w.Position < end) { int n = w.Stream.Read(buffer, 0, (int)Math.Min(buffer.Length, end - w.Position)); if (n == 0) throw new EndOfStreamException(); for (int i = 0; i < n; i++) { crc ^= buffer[i]; for (int k = 0; k < 8; k++) crc = crc >> 1 ^ ((crc & 1) == 0 ? 0 : 0xedb88320u); } }
        bool endian = w.BigEndian; w.BigEndian = true; w.U32(crc); w.BigEndian = endian;
    }

    private static void Encrypted(SliceResult r, Stream stream)
    {
        var w = new NativeBinary(stream); var p = r.Printer; var s = r.ResinSettings;
        if (p.FirmwareControlsPeel) s = s with { LiftHeight = .05f, BottomLiftHeight = .05f, LiftSpeed = .05f, BottomLiftSpeed = .05f, RetractSpeed = .05f };
        w.Zero(48 + 304); // Current settings record is 304 bytes, AES block aligned.
        long large = Preview(w, r, 400, 300, 16), small = Preview(w, r, 200, 125, 16);
        long name = w.Position; w.Text(p.MachineName, p.MachineName.Length);
        long table = w.Position; w.Zero(r.LayerCount * 16);
        for (int i = 0; i < r.LayerCount; i++)
        {
            long layer = w.Position; byte[] data = Rle(NativeBinary.Pixels(r, i));
            // Layer XOR is a separate part of this container's contract from AES settings.
            const uint seed = 0xefbeadde; uint step = unchecked(seed * 0x2d83cdac + 0xd8a83423);
            uint initial = unchecked(((uint)i * 0x1e1530cd + 0xec3d47cd) * step);
            for (int j = 0; j < data.Length; j++) data[j] ^= (byte)(unchecked(initial + (uint)(j / 4) * step) >> (8 * (j % 4)));
            w.U32(88); w.F32(r.Layers[i].Z); w.F32(i < s.BottomLayers ? s.BottomExposure : s.Exposure); w.F32(s.LightOffDelay);
            w.U32(layer + 88); w.U32(0); w.U32(data.Length); w.Zero(12);
            w.F32(s.LiftHeightForLayer(i)); w.F32(s.LiftSpeedForLayer(i)); w.F32(0); w.F32(0); w.F32(s.RetractSpeed); w.F32(0); w.F32(0); w.F32(0); w.F32(0); w.F32(0); w.F32(255); w.U32(0);
            w.Bytes(data); long end = w.Position;
            w.Position = table + 16 * i; w.U32(layer); w.U32(0); w.U32(88); w.U32(0); w.Position = end;
        }
        w.U32(1109414650); w.U32(0); long signature = w.Position;
        using var settings = new MemoryStream(new byte[304], true); var h = new NativeBinary(settings);
        h.U32(0xcafebabe); h.U32(0); h.U32(table); h.F32(p.DisplayWidthMm); h.F32(p.DisplayHeightMm); h.F32(p.ZTravelMm); h.Zero(8);
        h.F32(r.PrintHeight); h.F32(r.Settings.LayerHeight); h.F32(s.Exposure); h.F32(s.BottomExposure); h.F32(s.LightOffDelay); h.U32(s.BottomLayers);
        h.U32(p.ResolutionX); h.U32(p.ResolutionY); h.U32(r.LayerCount); h.U32(large); h.U32(small); h.U32((long)r.EstimatedSeconds); h.U32(1);
        h.F32(s.BottomLiftHeight); h.F32(s.BottomLiftSpeed); h.F32(s.LiftHeight); h.F32(s.LiftSpeed); h.F32(s.RetractSpeed); h.F32(r.VolumeMl); h.F32(0); h.F32(0); h.F32(s.LightOffDelay);
        h.U32(1); h.U16(255); h.U16(255); h.U32(0xefbeadde); h.Zero(28); h.U32(name); h.U32(p.MachineName.Length); h.Byte(15); h.U16(0); h.Byte(p.FirmwareControlsPeel ? (byte)0 : (byte)0x40);
        h.U32(0); h.U32(1); h.F32(0); h.F32(0); h.U32(0); h.F32(s.RetractSpeed); h.F32(0); h.U32(0); h.F32(4); h.U32(0); h.F32(4);
        h.Zero(24); h.U32(4); h.U32(r.LayerCount - 1);
        var settingsBytes = settings.ToArray();
        w.Bytes(Aes(SHA256.HashData(settingsBytes.AsSpan(0, 8)))); w.U32(1833054899); long fileEnd = w.Position;
        w.Position = 48; w.Bytes(Aes(settingsBytes)); w.Position = 0;
        w.U32(0x12fd0107); w.U32(304); w.U32(48); w.U32(0); w.U32(5); w.U32(32); w.U32(signature); w.U32(0); w.U16(1); w.U16(1); w.U32(0); w.U32(42); w.U32(0);
        w.Position = fileEnd;
    }

    private static byte[] Aes(byte[] data)
    {
        // Fixed interoperability parameters of the CTB encrypted container (not application secrets).
        using var aes = System.Security.Cryptography.Aes.Create();
        aes.Key = Convert.FromHexString("D05B8E3371DE3D1AE54F22DDDF5BFD94AB5D643A9D7EBFAF4203F310D8522AEA");
        return aes.EncryptCbc(data, Convert.FromHexString("0F010A05050B060708060A0C0C0D090F"), PaddingMode.None);
    }
}
