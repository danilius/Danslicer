using System.Globalization;
using Danslicer.Core.IO;

if (args.Length < 2 || args[0] != "info")
{
    Console.Error.WriteLine("Usage: danslicer info <file.stl>");
    return 1;
}

var mesh = StlReader.Read(args[1]);
var b = mesh.Bounds;
var size = b.Size;
var ci = CultureInfo.InvariantCulture;

Console.WriteLine($"File:      {args[1]}");
Console.WriteLine($"Triangles: {mesh.TriangleCount.ToString("N0", ci)}");
Console.WriteLine($"Vertices:  {mesh.VertexCount.ToString("N0", ci)}");
Console.WriteLine($"Min:       {b.Min.X.ToString("0.###", ci)}, {b.Min.Y.ToString("0.###", ci)}, {b.Min.Z.ToString("0.###", ci)}");
Console.WriteLine($"Max:       {b.Max.X.ToString("0.###", ci)}, {b.Max.Y.ToString("0.###", ci)}, {b.Max.Z.ToString("0.###", ci)}");
Console.WriteLine($"Size:      {size.X.ToString("0.###", ci)} x {size.Y.ToString("0.###", ci)} x {size.Z.ToString("0.###", ci)} mm");
return 0;
