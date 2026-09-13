using System.Numerics;
using Danslicer.Core.Slicing;

namespace Danslicer.Core.IO;

internal static class LegacyResinWriters
{
    public static void Longer(SliceResult r, Stream stream)
    {
        var w = new NativeBinary(stream); var p = r.Printer; var s = r.ResinSettings;
        int model = p.FileExtension switch { "lgs" => 10, "lgs30" => 30, "lgs120" => 120, _ => 4500 };
        w.Text("Longer3D", 8); w.U32(1); w.U32(1); w.U32(model); w.U32(0); w.U32(34);
        w.F32(1 / p.PixelPitchX); w.F32(1 / p.PixelPitchY); w.F32(p.ResolutionX); w.F32(p.ResolutionY);
        w.F32(r.Settings.LayerHeight); w.F32(s.Exposure * 1000); w.F32(s.BottomExposure * 1000); w.F32(10);
        w.F32(s.LightOffDelay * 1000); w.F32(s.LightOffDelay * 1000); w.F32(s.BottomLayers * r.Settings.LayerHeight); w.F32(.6f);
        w.F32(s.BottomLiftHeight); w.F32(s.LiftHeight); w.F32(s.LiftSpeed); w.F32(s.LiftSpeed); w.F32(s.BottomLiftSpeed); w.F32(s.BottomLiftSpeed);
        foreach (float v in new float[] { 5, 60, 10, 600, 600, 2, .2f, 60, 1, 6, 150, 1001 }) w.F32(v);
        w.F32(p.ZTravelMm); w.Zero(12); w.U32(r.LayerCount); w.U32(4); w.U32(120); w.U32(150);
        w.Bytes(NativeBinary.Preview(r, 120, 150, true));
        if (model == 120) { var png = NativeBinary.Png(NativeBinary.PreviewRgb(r, 1200, 1600), 1200, 1600, true); w.U32(png.Length); w.Bytes(png); w.U16(0); }
        for (int i = 0; i < r.LayerCount; i++)
        {
            var pixels = NativeBinary.Pixels(r, i);
            if (model == 4500)
            {
                var rotated = new byte[pixels.Length];
                for (int y = 0; y < p.ResolutionY; y++) for (int x = 0; x < p.ResolutionX; x++)
                    rotated[x * p.ResolutionY + p.ResolutionY - 1 - y] = pixels[y * p.ResolutionX + x];
                pixels = rotated;
            }
            using var encoded = new MemoryStream();
            for (int pos = 0; pos < pixels.Length;)
            {
                int shade = pixels[pos] >> 4, end = pos + 1;
                while (end < pixels.Length && pixels[end] >> 4 == shade) end++;
                int count = end - pos, shift = 28;
                while (shift > 0 && (count >> shift) == 0) shift -= 4;
                for (; shift >= 0; shift -= 4) encoded.WriteByte((byte)(shade << 4 | (count >> shift) & 15));
                pos = end;
            }
            w.U32(encoded.Length); w.Bytes(encoded.ToArray());
        }
    }

    public static void Anet(SliceResult r, Stream stream)
    {
        var w = new NativeBinary(stream, true); var p = r.Printer; var s = r.ResinSettings;
        w.Utf16("3"); w.Utf16("Danslicer"); w.Utf16(""); w.F64(p.PixelPitchX); w.F64(r.Settings.LayerHeight); w.U32(0); w.U32(0);
        w.U32((long)Math.Ceiling(s.Exposure)); w.U32((long)Math.Ceiling(s.BottomExposure)); w.U32(s.BottomLayers);
        w.U32((long)Math.Ceiling(s.LiftSpeed / 60)); w.U32((long)Math.Ceiling(s.LiftHeight));
        w.U32(260); w.U32(140); w.U32(66 + 260 * 140 * 2);
        // BITMAPINFOHEADER with 16-bit bitfields. Anet stores blue in the high five bits.
        w.BigEndian = false; w.Text("BM", 2); w.U32(72866); w.U32(0); w.U32(66); w.U32(40); w.U32(260); w.U32(140); w.U16(1); w.U16(16);
        w.U32(3); w.U32(72800); w.U32(0); w.U32(0); w.U32(0); w.U32(0); w.U32(31); w.U32(2016); w.U32(63488);
        var preview = NativeBinary.Preview(r, 260, 140);
        for (int j = 0; j < preview.Length; j += 2) { int v = preview[j] | preview[j + 1] << 8; w.U16((v & 31) << 11 | v & 2016 | v >> 11); }
        w.BigEndian = true; w.F64(r.VolumeMl * 1000); w.U32((long)r.EstimatedSeconds); w.U32(r.LayerCount);
        for (int i = 0; i < r.LayerCount; i++)
        {
            var pixels = NativeBinary.Pixels(r, i); var bits = new List<byte>(); int bitPosition = 0;
            void Bits(uint value, int count)
            {
                for (int k = count - 1; k >= 0; k--, bitPosition++)
                { if (bitPosition / 8 == bits.Count) bits.Add(0); if ((value & (1u << k)) != 0) bits[bitPosition / 8] |= (byte)(1 << (bitPosition % 8)); }
            }
            Bits((uint)p.ResolutionX, 16); Bits((uint)p.ResolutionY, 16); Bits(pixels[0] > 127 ? 1u : 0u, 1);
            int lit = 0, minX = p.ResolutionX, minY = p.ResolutionY, maxX = 0, maxY = 0;
            for (int pos = 0; pos < pixels.Length;)
            {
                bool white = pixels[pos] > 127; int end = pos + 1;
                while (end < pixels.Length && (pixels[end] > 127) == white) end++;
                uint count = (uint)(end - pos); int size = BitOperations.Log2(count);
                Bits((uint)size, 5); Bits(count, size + 1);
                if (white) for (int j = pos; j < end; j++) { lit++; int x = j % p.ResolutionX, y = j / p.ResolutionX; minX = Math.Min(minX, x); maxX = Math.Max(maxX, x); minY = Math.Min(minY, y); maxY = Math.Max(maxY, y); }
                pos = end;
            }
            w.U32(lit); w.U32(lit == 0 ? 0 : minX); w.U32(lit == 0 ? 0 : minY); w.U32(maxX); w.U32(maxY); w.U32(bitPosition); w.Bytes(bits.ToArray());
        }
        w.U32(0);
    }
}
