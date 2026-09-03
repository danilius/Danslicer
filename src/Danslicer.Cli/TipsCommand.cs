using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Danslicer.Core.IO;
using Danslicer.Core.Supports.Generation;

namespace Danslicer.Cli;

/// <summary>Headless inspection of stage-1 tip placement. Does not route or write a graph.</summary>
internal static class TipsCommand
{
    private static readonly CultureInfo Ci = CultureInfo.InvariantCulture;

    public static int Run(string[] args)
    {
        string? path = null;
        var json = false;
        var parameters = TipPlacementParameters.Default;
        var seed = 0;
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--json": json = true; break;
                case "--overhang": parameters = parameters with { OverhangAngleDegrees = F(args[++i]) }; break;
                case "--spacing": parameters = parameters with { SpacingMm = F(args[++i]) }; break;
                case "--min-spacing": parameters = parameters with { MinSpacingMm = F(args[++i]) }; break;
                case "--min-island": parameters = parameters with { MinIslandAreaMm2 = F(args[++i]) }; break;
                case "--layer": parameters = parameters with { LayerHeightMm = F(args[++i]) }; break;
                case "--tip": parameters = parameters with { TipDiameterMm = F(args[++i]) }; break;
                case "--edge": parameters = parameters with { EdgePreference = F(args[++i]) }; break;
                case "--force-edges": parameters = parameters with { ForceEdgePlacement = true }; break;
                case "--sharp-edge": parameters = parameters with { SharpEdgeDegrees = F(args[++i]) }; break;
                case "--seed": seed = int.Parse(args[++i], Ci); break;
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
            Console.Error.WriteLine("  danslicer tips <file.stl|file.obj> [--json] [--spacing 2.5] [--min-spacing 2.5]");
            Console.Error.WriteLine("                 [--overhang 45] [--min-island 0.5] [--layer 0.05] [--tip 0.4]");
            Console.Error.WriteLine("                 [--edge 0] [--force-edges] [--sharp-edge 30] [--seed 0]");
            return 1;
        }

        var mesh = MeshFile.Read(path);
        var faces = Enumerable.Range(0, mesh.TriangleCount).ToHashSet();
        var tips = TipPlacer.Place(mesh, faces, parameters, existingGraph: null, keepCleanFaces: null, seed);

        var byStrategy = tips.GroupBy(t => t.Strategy)
            .ToDictionary(g => g.Key.ToString(), g => g.Count());
        var spacing = SpacingStats(tips);

        if (json)
        {
            var dto = new ResultDto
            {
                File = path,
                Count = tips.Count,
                ByStrategy = byStrategy,
                Spacing = spacing,
                Candidates = tips.Select(t => new CandidateDto
                {
                    Strategy = t.Strategy.ToString(),
                    Point = [t.Point.X, t.Point.Y, t.Point.Z],
                    InwardNormal = [t.InwardNormal.X, t.InwardNormal.Y, t.InwardNormal.Z],
                    Diameter = t.TipDiameter,
                    Score = t.Score,
                    FaceIndex = t.FaceIndex,
                }).ToList(),
            };
            var opts = new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
            Console.WriteLine(JsonSerializer.Serialize(dto, opts));
            return 0;
        }

        Console.WriteLine($"File:        {path}");
        Console.WriteLine($"Triangles:   {mesh.TriangleCount.ToString("N0", Ci)}");
        Console.WriteLine($"Candidates:  {tips.Count}");
        foreach (var strategy in Enum.GetValues<TipStrategy>())
        {
            byStrategy.TryGetValue(strategy.ToString(), out var n);
            Console.WriteLine($"  {strategy,-14} {n}");
        }
        if (spacing is { } s)
        {
            Console.WriteLine($"Spacing mm:  min {Fmt(s.Min)}  median {Fmt(s.Median)}  mean {Fmt(s.Mean)}");
        }
        else
        {
            Console.WriteLine("Spacing mm:  n/a (fewer than 2 candidates)");
        }
        Console.WriteLine("# strategy  x  y  z  nx  ny  nz  diameter  score  face");
        foreach (var t in tips)
        {
            Console.WriteLine(
                $"{t.Strategy,-14} {Fmt(t.Point.X)} {Fmt(t.Point.Y)} {Fmt(t.Point.Z)}  " +
                $"{Fmt(t.InwardNormal.X)} {Fmt(t.InwardNormal.Y)} {Fmt(t.InwardNormal.Z)}  " +
                $"{Fmt(t.TipDiameter)} {Fmt(t.Score)}  {t.FaceIndex}");
        }
        return 0;
    }

    private static SpacingDto? SpacingStats(IReadOnlyList<TipCandidate> tips)
    {
        if (tips.Count < 2) return null;
        var nearest = new float[tips.Count];
        for (int i = 0; i < tips.Count; i++)
        {
            var best = float.PositiveInfinity;
            for (int j = 0; j < tips.Count; j++)
            {
                if (i == j) continue;
                var d = System.Numerics.Vector3.Distance(tips[i].Point, tips[j].Point);
                if (d < best) best = d;
            }
            nearest[i] = best;
        }
        Array.Sort(nearest);
        var mean = nearest.Average();
        var median = nearest.Length % 2 == 1
            ? nearest[nearest.Length / 2]
            : 0.5f * (nearest[nearest.Length / 2 - 1] + nearest[nearest.Length / 2]);
        return new SpacingDto { Min = nearest[0], Median = median, Mean = mean };
    }

    private static float F(string s) => float.Parse(s, Ci);
    private static string Fmt(float v) => v.ToString("0.###", Ci);

    private sealed class ResultDto
    {
        public required string File { get; init; }
        public required int Count { get; init; }
        public required Dictionary<string, int> ByStrategy { get; init; }
        public SpacingDto? Spacing { get; init; }
        public required List<CandidateDto> Candidates { get; init; }
    }

    private sealed class CandidateDto
    {
        public required string Strategy { get; init; }
        public required float[] Point { get; init; }
        public required float[] InwardNormal { get; init; }
        public required float Diameter { get; init; }
        public required float Score { get; init; }
        public required int FaceIndex { get; init; }
    }

    private sealed class SpacingDto
    {
        [JsonPropertyName("min")] public float Min { get; init; }
        [JsonPropertyName("median")] public float Median { get; init; }
        [JsonPropertyName("mean")] public float Mean { get; init; }
    }
}
