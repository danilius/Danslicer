using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Danslicer.Core.IO;
using Danslicer.Core.Supports.Checks;

namespace Danslicer.Cli;

/// <summary>Headless print checks (DESIGN.md §8.10). Does not modify geometry.</summary>
internal static class ChecksCommand
{
    private static readonly CultureInfo Ci = CultureInfo.InvariantCulture;

    public static int Run(string[] args)
    {
        var inputs = new List<string>();
        var json = false;
        var seat = false;
        var parameters = PrintCheckParameters.Default;
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--json": json = true; break;
                case "--seat": seat = true; break;
                case "--layer": parameters = parameters with { LayerHeightMm = F(args[++i]) }; break;
                case "--min-island": parameters = parameters with { MinIslandAreaMm2 = F(args[++i]) }; break;
                case "--overhang": parameters = parameters with { OverhangAngleDegrees = F(args[++i]) }; break;
                case "--min-suction": parameters = parameters with { MinSuctionVolumeMm3 = F(args[++i]) }; break;
                case "--drain": parameters = parameters with { DrainOpeningMm = F(args[++i]) }; break;
                case "--support-spacing": parameters = parameters with { SupportSupportThresholdMm = F(args[++i]) }; break;
                case "--model-clearance": parameters = parameters with { SupportModelThresholdMm = F(args[++i]) }; break;
                case "--object-spacing": parameters = parameters with { ObjectObjectThresholdMm = F(args[++i]) }; break;
                default:
                    if (args[i].StartsWith('-'))
                    {
                        Console.Error.WriteLine($"unknown option: {args[i]}");
                        return 1;
                    }
                    inputs.Add(args[i]);
                    break;
            }
        }

        if (inputs.Count == 0)
        {
            Console.Error.WriteLine("Usage:");
            Console.Error.WriteLine("  danslicer checks <file.stl|file.obj>... [--json] [--seat] [--layer 0.05] [--min-island 0.5]");
            Console.Error.WriteLine("                   [--overhang 45] [--min-suction 5] [--drain 0.8]");
            Console.Error.WriteLine("                   [--support-spacing 1] [--model-clearance 0.5] [--object-spacing 1]");
            return 1;
        }

        var meshes = new List<Danslicer.Core.Geometry.Mesh>();
        var seatOffsets = new List<float[]?>();
        foreach (var path in inputs)
        {
            var mesh = MeshFile.Read(path);
            if (seat)
            {
                var seated = MeshSeat.Apply(mesh);
                meshes.Add(seated.Mesh);
                seatOffsets.Add(MeshSeat.Json(seated.Offset));
            }
            else
            {
                meshes.Add(mesh);
                seatOffsets.Add(null);
            }
        }
        var findings = PrintChecker.Check(meshes, supports: null, parameters);

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
                files = inputs,
                seatOffsets = seat ? seatOffsets : null,
                count = findings.Count,
                byKind = findings.GroupBy(f => f.Kind.ToString()).ToDictionary(g => g.Key, g => g.Count()),
                findings = findings.Select(f => new
                {
                    kind = f.Kind.ToString(),
                    severity = f.Severity.ToString(),
                    objectIndex = f.ObjectIndex,
                    objectIndexB = f.ObjectIndexB,
                    elementIdA = f.ElementIdA,
                    elementIdB = f.ElementIdB,
                    layerFrom = f.LayerFrom,
                    layerTo = f.LayerTo,
                    point = Xyz(f.Point),
                    pointA = Xyz(f.PointA),
                    pointB = Xyz(f.PointB),
                    distanceMm = f.DistanceMm,
                    volumeMm3 = f.VolumeMm3,
                    areaMm2 = f.AreaMm2,
                }),
            }, opts));
            return 0;
        }

        Console.WriteLine($"Files:     {string.Join(", ", inputs)}");
        if (seat)
        {
            for (int i = 0; i < inputs.Count; i++)
            {
                var o = seatOffsets[i]!;
                Console.WriteLine($"Seat offset:  {inputs[i]}  {Fmt(o[0])}, {Fmt(o[1])}, {Fmt(o[2])}");
            }
        }
        Console.WriteLine($"Findings:  {findings.Count}");
        foreach (var kind in Enum.GetValues<CheckKind>())
        {
            var n = findings.Count(f => f.Kind == kind);
            Console.WriteLine($"  {kind,-22} {n}");
        }
        Console.WriteLine("# kind  severity  object  layers  volume/area/dist  point");
        foreach (var f in findings)
        {
            var layers = f.LayerFrom is { } lo ? $"{lo}..{f.LayerTo}" : "-";
            var metric = f.VolumeMm3 is { } v ? $"vol {Fmt(v)} mm3"
                : f.AreaMm2 is { } a ? $"area {Fmt(a)} mm2"
                : f.DistanceMm is { } d ? $"dist {Fmt(d)} mm"
                : "-";
            var pt = f.Point ?? f.PointA;
            var pts = pt is { } p ? $"{Fmt(p.X)},{Fmt(p.Y)},{Fmt(p.Z)}" : "-";
            Console.WriteLine($"{f.Kind,-22} {f.Severity,-8} {f.ObjectIndex?.ToString(Ci) ?? "-"}  {layers,-8} {metric,-18} {pts}");
        }
        return 0;
    }

    private static float F(string s) => float.Parse(s, Ci);
    private static string Fmt(float v) => v.ToString("0.###", Ci);
    private static float[]? Xyz(System.Numerics.Vector3? p) =>
        p is { } v ? [v.X, v.Y, v.Z] : null;
}
