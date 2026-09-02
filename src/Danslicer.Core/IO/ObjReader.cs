using System.Globalization;
using System.Numerics;
using System.Text;
using Danslicer.Core.Geometry;

namespace Danslicer.Core.IO;

/// <summary>
/// Reads Wavefront OBJ files into meshes. Only geometry is used: <c>v</c> lines for positions and
/// <c>f</c> lines for faces, which may be polygons and are fan-triangulated. Texture and normal
/// indices (<c>v/vt/vn</c>) and negative (relative) indices are accepted; materials, groups and
/// normals are ignored. Coordinates are taken as-is, millimetres and Z-up, matching STL handling.
/// </summary>
public static class ObjReader
{
    public static Mesh Read(string path)
    {
        using var stream = File.OpenRead(path);
        return Read(stream);
    }

    public static Mesh Read(Stream stream)
    {
        var positions = new List<Vector3>();
        var indices = new List<int>();
        var face = new List<int>();

        using var reader = new StreamReader(stream, Encoding.UTF8);
        string? rawLine;
        var lineNumber = 0;
        while ((rawLine = reader.ReadLine()) is not null)
        {
            lineNumber++;
            var line = rawLine.AsSpan().Trim();
            if (line.Length < 2) continue;

            if (line[0] == 'v' && (line[1] == ' ' || line[1] == '\t'))
            {
                var rest = line.Slice(2);
                var x = ParseNext(ref rest, lineNumber);
                var y = ParseNext(ref rest, lineNumber);
                var z = ParseNext(ref rest, lineNumber);
                positions.Add(new Vector3(x, y, z));
            }
            else if (line[0] == 'f' && (line[1] == ' ' || line[1] == '\t'))
            {
                face.Clear();
                var rest = line.Slice(2);
                while (true)
                {
                    rest = rest.TrimStart();
                    if (rest.IsEmpty) break;
                    var end = rest.IndexOfAny(' ', '\t');
                    var token = end < 0 ? rest : rest.Slice(0, end);
                    rest = end < 0 ? ReadOnlySpan<char>.Empty : rest.Slice(end);

                    // A vertex reference is "v", "v/vt", "v/vt/vn" or "v//vn"; only v is used.
                    var slash = token.IndexOf('/');
                    if (slash >= 0) token = token.Slice(0, slash);
                    if (!int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index) || index == 0)
                        throw new InvalidDataException($"OBJ line {lineNumber}: bad face vertex index '{token}'.");
                    var resolved = index > 0 ? index - 1 : positions.Count + index;
                    if (resolved < 0 || resolved >= positions.Count)
                        throw new InvalidDataException($"OBJ line {lineNumber}: face vertex index {index} out of range.");
                    face.Add(resolved);
                }

                for (int i = 2; i < face.Count; i++)
                {
                    int a = face[0], b = face[i - 1], c = face[i];
                    if (a == b || b == c || c == a) continue;
                    indices.Add(a);
                    indices.Add(b);
                    indices.Add(c);
                }
            }
        }

        if (indices.Count == 0)
            throw new InvalidDataException("OBJ file contains no faces.");

        return new Mesh(positions.ToArray(), indices.ToArray());
    }

    private static float ParseNext(ref ReadOnlySpan<char> span, int lineNumber)
    {
        span = span.TrimStart();
        var end = span.IndexOfAny(' ', '\t');
        var token = end < 0 ? span : span.Slice(0, end);
        span = end < 0 ? ReadOnlySpan<char>.Empty : span.Slice(end);
        if (!float.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            throw new InvalidDataException($"OBJ line {lineNumber}: bad coordinate '{token}'.");
        return value;
    }
}
