using System.Buffers.Binary;
using System.Globalization;
using System.Numerics;
using System.Text;
using Danslicer.Core.Geometry;

namespace Danslicer.Core.IO;

/// <summary>Reads binary and ASCII STL files into welded meshes.</summary>
public static class StlReader
{
    public static Mesh Read(string path)
    {
        using var stream = File.OpenRead(path);
        return Read(stream);
    }

    public static Mesh Read(Stream stream)
    {
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        var data = ms.GetBuffer().AsSpan(0, (int)ms.Length);

        return IsBinary(data) ? ReadBinary(data) : ReadAscii(data);
    }

    private static bool IsBinary(ReadOnlySpan<byte> data)
    {
        if (data.Length < 84) return false;
        var triangleCount = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(80, 4));
        if (data.Length == 84 + 50L * triangleCount) return true;

        // Some exporters write "solid" in the binary header, so only trust the keyword
        // when the size does not match binary and the content looks like text.
        var head = Encoding.ASCII.GetString(data.Slice(0, Math.Min(data.Length, 512)));
        return !head.TrimStart().StartsWith("solid", StringComparison.OrdinalIgnoreCase);
    }

    private static Mesh ReadBinary(ReadOnlySpan<byte> data)
    {
        var triangleCount = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(80, 4));
        var vertices = new Vector3[triangleCount * 3];
        var offset = 84;
        for (int t = 0; t < triangleCount; t++)
        {
            offset += 12; // normal, ignored; recomputed from winding
            for (int v = 0; v < 3; v++)
            {
                vertices[t * 3 + v] = new Vector3(
                    BinaryPrimitives.ReadSingleLittleEndian(data.Slice(offset, 4)),
                    BinaryPrimitives.ReadSingleLittleEndian(data.Slice(offset + 4, 4)),
                    BinaryPrimitives.ReadSingleLittleEndian(data.Slice(offset + 8, 4)));
                offset += 12;
            }
            offset += 2; // attribute byte count
        }
        return Mesh.FromTriangleSoup(vertices);
    }

    private static Mesh ReadAscii(ReadOnlySpan<byte> data)
    {
        var text = Encoding.ASCII.GetString(data);
        var vertices = new List<Vector3>();
        foreach (var rawLine in text.AsSpan().EnumerateLines())
        {
            var line = rawLine.Trim();
            if (!line.StartsWith("vertex", StringComparison.OrdinalIgnoreCase)) continue;

            var rest = line.Slice(6).Trim();
            var x = ParseNext(ref rest);
            var y = ParseNext(ref rest);
            var z = ParseNext(ref rest);
            vertices.Add(new Vector3(x, y, z));
        }

        if (vertices.Count % 3 != 0)
            throw new InvalidDataException("ASCII STL vertex count is not a multiple of three.");

        return Mesh.FromTriangleSoup(vertices.ToArray());
    }

    private static float ParseNext(ref ReadOnlySpan<char> span)
    {
        span = span.TrimStart();
        var end = span.IndexOfAny(' ', '\t');
        var token = end < 0 ? span : span.Slice(0, end);
        span = end < 0 ? ReadOnlySpan<char>.Empty : span.Slice(end);
        return float.Parse(token, NumberStyles.Float, CultureInfo.InvariantCulture);
    }
}
