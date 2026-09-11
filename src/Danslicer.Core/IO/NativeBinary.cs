using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using Danslicer.Core.Slicing;

namespace Danslicer.Core.IO;

// Small endian-aware serialization primitives. No reflection or vendor runtime dependency.
internal sealed class NativeBinary(Stream stream, bool bigEndian = false)
{
    public Stream Stream { get; } = stream;
    public bool BigEndian { get; set; } = bigEndian;
    public long Position { get => Stream.Position; set => Stream.Position = value; }
    public void Bytes(ReadOnlySpan<byte> value) => Stream.Write(value);
    public void Byte(byte value) => Stream.WriteByte(value);
    public void U16(int value) { Span<byte> b = stackalloc byte[2]; if (BigEndian) BinaryPrimitives.WriteUInt16BigEndian(b, checked((ushort)value)); else BinaryPrimitives.WriteUInt16LittleEndian(b, checked((ushort)value)); Bytes(b); }
    public void U32(long value) { Span<byte> b = stackalloc byte[4]; if (BigEndian) BinaryPrimitives.WriteUInt32BigEndian(b, checked((uint)value)); else BinaryPrimitives.WriteUInt32LittleEndian(b, checked((uint)value)); Bytes(b); }
    public void F32(float value) => U32(BitConverter.SingleToUInt32Bits(value));
    public void F64(double value) { Span<byte> b = stackalloc byte[8]; if (BigEndian) BinaryPrimitives.WriteUInt64BigEndian(b, BitConverter.DoubleToUInt64Bits(value)); else BinaryPrimitives.WriteUInt64LittleEndian(b, BitConverter.DoubleToUInt64Bits(value)); Bytes(b); }
    public void Zero(int count) { Span<byte> zeros = stackalloc byte[256]; zeros.Clear(); while (count > 0) { int n = Math.Min(count, zeros.Length); Bytes(zeros[..n]); count -= n; } }
    public void Text(string text, int size) { var bytes = Encoding.ASCII.GetBytes(text); Bytes(bytes.AsSpan(0, Math.Min(bytes.Length, size))); Zero(Math.Max(0, size - bytes.Length)); }
    public void Utf16(string text) { var b = Encoding.BigEndianUnicode.GetBytes(text); U32(b.Length); Bytes(b); }
    public void Ascii(string text) { var b = Encoding.ASCII.GetBytes(text); U32(b.Length); Bytes(b); }
    public void LineEnd() => Bytes([13, 10]);
    public void Patch32(long at, long value) { long end = Position; Position = at; U32(value); Position = end; }
    public static byte[] Pixels(SliceResult r, int index) { var p = new byte[checked(r.Printer.ResolutionX * r.Printer.ResolutionY)]; r.Layers[index].Decode(r.Printer.ResolutionX, r.Printer.ResolutionY, p); return p; }

    public static byte[] Preview(SliceResult r, int width, int height, bool bigEndian = false)
    {
        var data = new byte[checked(width * height * 2)];
        for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
        {
            int source = ((y * r.PreviewHeight / height) * r.PreviewWidth + x * r.PreviewWidth / width) * 2;
            int dest = (y * width + x) * 2;
            data[dest] = r.Preview[source + (bigEndian ? 1 : 0)];
            data[dest + 1] = r.Preview[source + (bigEndian ? 0 : 1)];
        }
        return data;
    }

    public static byte[] Png(byte[] pixels, int width, int height, bool rgb = false)
    {
        using var output = new MemoryStream(); var w = new NativeBinary(output, true);
        w.Bytes([137, 80, 78, 71, 13, 10, 26, 10]);
        using var header = new MemoryStream(); var h = new NativeBinary(header, true);
        h.U32(width); h.U32(height); h.Bytes([8, (byte)(rgb ? 2 : 0), 0, 0, 0]);
        Chunk("IHDR", header.ToArray());
        using var packed = new MemoryStream();
        using (var z = new ZLibStream(packed, CompressionLevel.Fastest, true))
            for (int y = 0; y < height; y++) { z.WriteByte(0); z.Write(pixels.AsSpan(y * width * (rgb ? 3 : 1), width * (rgb ? 3 : 1))); }
        Chunk("IDAT", packed.ToArray()); Chunk("IEND", []); return output.ToArray();
        void Chunk(string type, byte[] bytes)
        {
            w.U32(bytes.Length); var tag = Encoding.ASCII.GetBytes(type); w.Bytes(tag); w.Bytes(bytes);
            uint crc = uint.MaxValue;
            foreach (byte b in tag.Concat(bytes)) { crc ^= b; for (int k = 0; k < 8; k++) crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xedb88320u : 0); }
            w.U32(~crc);
        }
    }
    public static byte[] PreviewRgb(SliceResult r, int width, int height)
    {
        var packed = Preview(r, width, height); var rgb = new byte[width * height * 3];
        for (int i = 0; i < width * height; i++) { int v = packed[i * 2] | packed[i * 2 + 1] << 8; rgb[i * 3] = (byte)(((v >> 11) & 31) * 255 / 31); rgb[i * 3 + 1] = (byte)(((v >> 5) & 63) * 255 / 63); rgb[i * 3 + 2] = (byte)((v & 31) * 255 / 31); }
        return rgb;
    }
}
