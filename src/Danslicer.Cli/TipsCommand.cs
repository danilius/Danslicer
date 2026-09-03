using System.Globalization;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Danslicer.Core.IO;
using Danslicer.Core.Supports;
using Danslicer.Core.Supports.Generation;
using Danslicer.Core.Supports.Routing;

namespace Danslicer.Cli;

/// <summary>Headless inspection of stage-1 tip placement. Does not route or write a graph.</summary>
internal static class TipsCommand
{
    private static readonly CultureInfo Ci = CultureInfo.InvariantCulture;

    public static int Run(string[] args)
    {
        string? path = null;
        var json = false;
        var seat = false;
        // Mirrors the app's tree-generation default: preserve below-threshold islands for the
        // mini-support pass. Other direct TipPlacer callers remain opt-in.
        var parameters = TipPlacementParameters.Default with { EnableMiniSupports = true };
        var seed = 0;
        BaseLatticeType? gridLattice = null;
        float? gridSpacing = null;
        var gridOffsetX = 0f;
        var gridOffsetY = 0f;
        var gridRotation = 0f;
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--json": json = true; break;
                case "--seat": seat = true; break;
                case "--overhang": parameters = parameters with { OverhangAngleDegrees = F(args[++i]) }; break;
                case "--spacing": parameters = parameters with { SpacingMm = F(args[++i]) }; break;
                case "--min-spacing": parameters = parameters with { MinSpacingMm = F(args[++i]) }; break;
                case "--min-island": parameters = parameters with { MinIslandAreaMm2 = F(args[++i]) }; break;
                case "--layer": parameters = parameters with { LayerHeightMm = F(args[++i]) }; break;
                case "--tip": parameters = parameters with { TipDiameterMm = F(args[++i]) }; break;
                case "--tip-shape":
                {
                    var name = args[++i].ToLowerInvariant();
                    if (name == "capsule") parameters = parameters with { TipShape = SupportTipShape.Capsule };
                    else if (name == "cone") parameters = parameters with { TipShape = SupportTipShape.Cone };
                    else
                    {
                        Console.Error.WriteLine("tip-shape must be 'capsule' or 'cone'");
                        return 1;
                    }
                    break;
                }
                case "--cone-length": parameters = parameters with { ConeLengthMm = F(args[++i]) }; break;
                case "--ball-diameter": parameters = parameters with { BallDiameterMm = F(args[++i]) }; break;
                case "--penetration-depth": parameters = parameters with { PenetrationDepthMm = Math.Max(F(args[++i]), 0f) }; break;
                case "--edge": parameters = parameters with { EdgePreference = F(args[++i]) }; break;
                case "--force-edges": parameters = parameters with { ForceEdgePlacement = true }; break;
                case "--sharp-edge": parameters = parameters with { SharpEdgeDegrees = F(args[++i]) }; break;
                case "--keep-clean-distance": parameters = parameters with { KeepCleanDistanceMm = F(args[++i]) }; break;
                case "--grid":
                {
                    var name = args[++i].ToLowerInvariant();
                    if (name == "square") gridLattice = BaseLatticeType.Square;
                    else if (name is "hex" or "hexagonal") gridLattice = BaseLatticeType.Hexagonal;
                    else
                    {
                        Console.Error.WriteLine($"unknown lattice: {name}");
                        return 1;
                    }
                    break;
                }
                case "--grid-spacing": gridSpacing = F(args[++i]); break;
                case "--grid-offset-x": gridOffsetX = F(args[++i]); break;
                case "--grid-offset-y": gridOffsetY = F(args[++i]); break;
                case "--grid-rotation": gridRotation = F(args[++i]); break;
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
            Console.Error.WriteLine("  danslicer tips <file.stl|file.obj> [--json] [--seat] [--spacing 2.5] [--min-spacing 2.5]");
            Console.Error.WriteLine("                 [--overhang 45] [--min-island 0.5] [--layer 0.05] [--tip 0.4]");
            Console.Error.WriteLine("                 [--tip-shape capsule|cone] [--cone-length 2] [--ball-diameter 0] [--penetration-depth 0]");
            Console.Error.WriteLine("                 [--edge 0] [--force-edges] [--sharp-edge 30] [--seed 0]");
            Console.Error.WriteLine("                 [--grid square|hex] [--grid-spacing 5] [--grid-offset-x 0] [--grid-offset-y 0]");
            Console.Error.WriteLine("                 [--grid-rotation 0] [--keep-clean-distance 0]");
            return 1;
        }

        if (gridLattice is { } lattice)
        {
            parameters = parameters with
            {
                Grid = new GridRoutingOptions
                {
                    Lattice = lattice,
                    Spacing = gridSpacing ?? parameters.SpacingMm,
                    Offset = new Vector2(gridOffsetX, gridOffsetY),
                    RotationDegrees = gridRotation,
                    PlateZ = parameters.PlateZ,
                },
            };
        }

        var mesh = MeshFile.Read(path);
        Vector3? seatOffset = null;
        if (seat)
        {
            var seated = MeshSeat.Apply(mesh);
            mesh = seated.Mesh;
            seatOffset = seated.Offset;
        }
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
                SeatOffset = seatOffset is { } o ? MeshSeat.Json(o) : null,
                TipShape = parameters.TipShape.ToString(),
                ConeLength = parameters.ConeLengthMm,
                BallDiameter = parameters.BallDiameterMm,
                PenetrationDepth = parameters.PenetrationDepthMm,
                Candidates = tips.Select(t => new CandidateDto
                {
                    Strategy = t.Strategy.ToString(),
                    Point = [t.Point.X, t.Point.Y, t.Point.Z],
                    InwardNormal = [t.InwardNormal.X, t.InwardNormal.Y, t.InwardNormal.Z],
                    Diameter = t.TipDiameter,
                    Score = t.Score,
                    FaceIndex = t.FaceIndex,
                    TipShape = t.TipShape.ToString(),
                    ConeLength = t.ConeLength,
                    BallDiameter = t.BallDiameter,
                    PenetrationDepth = t.PenetrationDepth,
                }).ToList(),
            };
            var opts = new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
            Console.WriteLine(JsonSerializer.Serialize(dto, opts));
            return 0;
        }

        Console.WriteLine($"File:        {path}");
        Console.WriteLine($"Triangles:   {mesh.TriangleCount.ToString("N0", Ci)}");
        if (seatOffset is { } offset) MeshSeat.WriteText(offset);
        Console.WriteLine($"Tip shape:   {parameters.TipShape}  cone {Fmt(parameters.ConeLengthMm)}  ball {Fmt(parameters.BallDiameterMm)}  penetration {Fmt(parameters.PenetrationDepthMm)}");
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
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public float[]? SeatOffset { get; init; }
        public required string TipShape { get; init; }
        public required float ConeLength { get; init; }
        public required float BallDiameter { get; init; }
        public required float PenetrationDepth { get; init; }
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
        public required string TipShape { get; init; }
        public required float ConeLength { get; init; }
        public required float BallDiameter { get; init; }
        public required float PenetrationDepth { get; init; }
    }

    private sealed class SpacingDto
    {
        [JsonPropertyName("min")] public float Min { get; init; }
        [JsonPropertyName("median")] public float Median { get; init; }
        [JsonPropertyName("mean")] public float Mean { get; init; }
    }
}
