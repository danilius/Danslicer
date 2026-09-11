using System.Globalization;
using Danslicer.Core.Slicing;

namespace Danslicer.Core.IO;

internal static class CrealityWriter
{
    public static void Write(SliceResult r, Stream stream)
    {
        var w = new NativeBinary(stream, true); var p = r.Printer; var s = r.ResinSettings;
        w.Ascii("CXSW3DV2\0"); w.U16(3); w.Ascii(p.MachineName + "\0"); w.U16(r.LayerCount); w.U16(p.ResolutionX); w.U16(p.ResolutionY); w.Zero(64);
        w.Bytes(NativeBinary.Preview(r, 116, 116, true)); w.LineEnd();
        for (int j = 0; j < 2; j++) { w.Bytes(NativeBinary.Preview(r, 290, 290, true)); w.LineEnd(); }
        w.Utf16(p.DisplayWidthMm.ToString(CultureInfo.InvariantCulture)); w.Utf16(p.DisplayHeightMm.ToString(CultureInfo.InvariantCulture)); w.Utf16(r.Settings.LayerHeight.ToString(CultureInfo.InvariantCulture));
        foreach (float value in new float[] { s.Exposure * 10, Math.Max(1, s.LightOffDelay), s.BottomExposure, s.BottomLayers, s.BottomLiftHeight, s.BottomLiftSpeed / 60, s.LiftHeight, s.LiftSpeed / 60, s.RetractSpeed / 60, 255, 255 }) w.U16((int)Math.Ceiling(value));
        long areas = w.Position; w.Zero(r.LayerCount * 4); w.LineEnd();
        w.Ascii("Danslicer\0"); w.Ascii("User resin\0"); w.Byte(0); w.U32(0); w.U32(0); w.Byte(0); w.U16(0); w.Byte(0); w.U16(0);
        w.Byte(r.Settings.AntiAliasing ? (byte)1 : (byte)0); w.Byte(0); w.Byte(255); w.Byte(0); w.Byte(0); w.LineEnd();
        for (int i = 0; i < r.LayerCount; i++)
        {
            var pixels = NativeBinary.Pixels(r, i); long start = w.Position; uint count = 0;
            w.U32((long)Math.Ceiling(r.Layers[i].AreaMm2)); w.U32(0);
            for (int x = 0; x < p.ResolutionX; x++) for (int y = 0; y < p.ResolutionY;)
            {
                byte color = pixels[y * p.ResolutionX + x]; int end = y + 1;
                while (end < p.ResolutionY && pixels[end * p.ResolutionX + x] == color) end++;
                if (color != 0)
                {
                    ulong packed = (ulong)y << 27 | (ulong)(end - 1) << 14 | (uint)x;
                    for (int shift = 32; shift >= 0; shift -= 8) w.Byte((byte)(packed >> shift));
                    w.Byte(color); count++;
                }
                y = end;
            }
            w.LineEnd(); w.Patch32(start + 4, count); w.Patch32(areas + i * 4, (long)Math.Ceiling(r.Layers[i].AreaMm2));
        }
        w.Ascii("CXSW3DV2\0"); ChituWriter.AppendCrc(w);
    }
}
