using Danslicer.Core.Slicing;

namespace Danslicer.Core.IO;

internal static class GooWriter
{
    public static void Write(SliceResult r, Stream stream)
    {
        var w = new NativeBinary(stream, true); var p = r.Printer; var s = r.ResinSettings;
        byte[] magic = [7, 0, 0, 0, 68, 76, 80, 0];
        w.Text("V3.0", 4); w.Bytes(magic); w.Text("Danslicer", 32); w.Text("1.0", 24);
        w.Text(DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"), 24); w.Text(p.MachineName, 32);
        w.Text("LCD", 32); w.Text(p.Name, 32); w.U16(r.Settings.AntiAliasing ? 16 : 1); w.U16(0); w.U16(0);
        w.Bytes(NativeBinary.Preview(r, 116, 116, true)); w.LineEnd(); w.Bytes(NativeBinary.Preview(r, 290, 290, true)); w.LineEnd();
        w.U32(r.LayerCount); w.U16(p.ResolutionX); w.U16(p.ResolutionY); w.Byte(0); w.Byte(0); // Mirroring is already baked into pixels.
        w.F32(p.DisplayWidthMm); w.F32(p.DisplayHeightMm); w.F32(p.ZTravelMm); w.F32(r.Settings.LayerHeight); w.F32(s.Exposure);
        w.Byte(0); w.F32(s.LightOffDelay); for (int j = 0; j < 6; j++) w.F32(0);
        w.F32(s.BottomExposure); w.U32(s.BottomLayers);
        w.F32(s.BottomLiftHeight); w.F32(s.BottomLiftSpeed); w.F32(s.LiftHeight); w.F32(s.LiftSpeed);
        w.F32(s.BottomLiftHeight); w.F32(s.RetractSpeed); w.F32(s.LiftHeight); w.F32(s.RetractSpeed);
        for (int j = 0; j < 8; j++) w.F32(0);
        w.U16(255); w.U16(255); w.Byte(0); w.U32((long)r.EstimatedSeconds); w.F32(r.VolumeMl * 1000); w.F32(0); w.F32(0); w.Text("$", 8);
        w.U32(w.Position + 7); w.Byte(1); w.U16(0);
        for (int i = 0; i < r.LayerCount; i++)
        {
            w.U16(0); w.F32(0); w.F32(r.Layers[i].Z); w.F32(i < s.BottomLayers ? s.BottomExposure : s.Exposure); w.F32(s.LightOffDelay);
            w.F32(0); w.F32(0); w.F32(0); w.F32(s.LiftHeightForLayer(i)); w.F32(s.LiftSpeedForLayer(i)); w.F32(0); w.F32(0);
            w.F32(s.LiftHeightForLayer(i)); w.F32(s.RetractSpeed); w.F32(0); w.F32(0); w.U16(255); w.LineEnd();
            var encoded = Encode(NativeBinary.Pixels(r, i)); w.U32(encoded.Length); w.Bytes(encoded); w.LineEnd();
        }
        w.Zero(3); w.Bytes(magic);
    }

    internal static byte[] Encode(byte[] pixels)
    {
        using var output = new MemoryStream(); output.WriteByte(0x55);
        for (int i = 0; i < pixels.Length;)
        {
            byte color = pixels[i]; int end = i + 1;
            while (end < pixels.Length && pixels[end] == color && end - i < 0xfffffff) end++;
            int count = end - i; int extra = count < 16 ? 0 : count < 4096 ? 1 : count < 1048576 ? 2 : 3;
            output.WriteByte((byte)((color == 0 ? 0 : color == 255 ? 192 : 64) | extra << 4 | count & 15));
            if (color is > 0 and < 255) output.WriteByte(color);
            for (int k = extra - 1; k >= 0; k--) output.WriteByte((byte)(count >> (4 + k * 8)));
            i = end;
        }
        var body = output.ToArray(); int sum = 0; for (int i = 1; i < body.Length; i++) sum += body[i];
        output.WriteByte(unchecked((byte)~sum)); return output.ToArray();
    }
}
