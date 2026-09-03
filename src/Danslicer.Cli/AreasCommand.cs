using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Danslicer.Core.IO;
using Danslicer.Core.Supports.Generation;

namespace Danslicer.Cli;

/// <summary>Headless inspection of support-area auto-detection. Does not generate tips.</summary>
internal static class AreasCommand
{
    private static readonly CultureInfo Ci = CultureInfo.InvariantCulture;

    public static int Run(string[] args)
    {
        string? path = null;
        var json = false;
        var seat = false;
        var parameters = SupportAreaParameters.Default;
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--json": json = true; break;
                case "--seat": seat = true; break;
                case "--overhang": parameters = parameters with { OverhangAngleDegrees = F(args[++i]) }; break;
                case "--min-area": parameters = parameters with { MinAreaMm2 = F(args[++i]) }; break;
                case "--layer": parameters = parameters with { LayerHeightMm = F(args[++i]) }; break;
                case "--min-island": parameters = parameters with { MinIslandAreaMm2 = F(args[++i]) }; break;
                case "--sharp-edge": parameters = parameters with { SharpEdgeDegrees = F(args[++i]) }; break;
                default:
                    if (args[i].StartsWith('-'))
                    {
                        Console.Error.WriteLine($"unknown option: {args[i]}");
                        return 1;
                    }
                    path = args[i];
                    break;
            }
        }

        if (path is null)
        {
            Console.Error.WriteLine("Usage:");
            Console.Error.WriteLine("  danslicer areas <file.stl|file.obj> [--json] [--seat] [--overhang 45] [--min-area 0.5]");
            Console.Error.WriteLine("                  [--layer 0.05] [--min-island 0.5] [--sharp-edge 30]");
            return 1;
        }

        var mesh = MeshFile.Read(path);
        float[]? seatOffset = null;
        if (seat)
        {
            var seated = MeshSeat.Apply(mesh);
            mesh = seated.Mesh;
            seatOffset = MeshSeat.Json(seated.Offset);
        }
        var faces = Enumerable.Range(0, mesh.TriangleCount).ToHashSet();
        var areas = SupportAreaDetector.Detect(mesh, faces, parameters);

        if (json)
        {
            var opts = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            };
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                file = path,
                seatOffset,
                count = areas.Count,
                totalAreaMm2 = areas.Sum(a => a.AreaMm2),
                bySeverity = areas.GroupBy(a => a.Severity.ToString()).ToDictionary(g => g.Key, g => g.Count()),
                areas = areas.Select(a => new
                {
                    id = a.Id,
                    faceCount = a.Faces.Count,
                    areaMm2 = a.AreaMm2,
                    centroid = new[] { a.Centroid.X, a.Centroid.Y, a.Centroid.Z },
                    meanNormal = new[] { a.MeanNormal.X, a.MeanNormal.Y, a.MeanNormal.Z },
                    maxOverhangDegrees = a.MaxOverhangDegrees,
                    meanOverhangDegrees = a.MeanOverhangDegrees,
                    containsIsland = a.ContainsIsland,
                    containsLocalMinimum = a.ContainsLocalMinimum,
                    severity = a.Severity.ToString(),
                    patchIds = a.PatchIds,
                    faces = a.Faces,
                }),
            }, opts));
            return 0;
        }

        Console.WriteLine($"File:        {path}");
        Console.WriteLine($"Triangles:   {mesh.TriangleCount.ToString("N0", Ci)}");
        if (seat && seatOffset is { } o)
            Console.WriteLine($"Seat offset:  {Fmt(o[0])}, {Fmt(o[1])}, {Fmt(o[2])}");
        Console.WriteLine($"Areas:       {areas.Count}");
        Console.WriteLine($"Total mm2:   {Fmt(areas.Sum(a => a.AreaMm2))}");
        foreach (var severity in Enum.GetValues<SupportAreaSeverity>())
            Console.WriteLine($"  {severity,-8} {areas.Count(a => a.Severity == severity)}");
        Console.WriteLine("# id  faces  area  severity  island  minimum  maxOverhang  centroid  patches");
        foreach (var a in areas)
        {
            Console.WriteLine(
                $"{a.Id,-4} {a.Faces.Count,-6} {Fmt(a.AreaMm2),-8} {a.Severity,-8} " +
                $"{(a.ContainsIsland ? "island" : "-"),-7} {(a.ContainsLocalMinimum ? "min" : "-"),-7} " +
                $"{Fmt(a.MaxOverhangDegrees),-8} {Fmt(a.Centroid.X)},{Fmt(a.Centroid.Y)},{Fmt(a.Centroid.Z)}  " +
                $"patches {string.Join(',', a.PatchIds)}");
        }
        return 0;
    }

    private static float F(string s) => float.Parse(s, Ci);
    private static string Fmt(float v) => v.ToString("0.###", Ci);
}
