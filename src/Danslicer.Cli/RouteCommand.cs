using System.Globalization;
using System.Numerics;
using System.Text.Json;
using Danslicer.Core.IO;
using Danslicer.Core.Supports;
using Danslicer.Core.Supports.Routing;

internal static class RouteCommand
{
    private static readonly CultureInfo Ci = CultureInfo.InvariantCulture;

    public static int Run(string[] args)
    {
        if (args.Length == 0) return UsageError("a mesh path is required");
        try
        {
            var meshPath = args[0];
            string? tipsPath = null;
            var options = new GridRoutingOptions();
            var strategy = "grid";
            var stepHeight = 2f;
            var json = false;
            for (var i = 1; i < args.Length; i++)
            {
                options = args[i] switch
                {
                    "--spacing" => options with { Spacing = Parse(args[++i]) },
                    "--lattice" => options with { Lattice = ParseLattice(args[++i]) },
                    "--offset-x" => options with { Offset = options.Offset with { X = Parse(args[++i]) } },
                    "--offset-y" => options with { Offset = options.Offset with { Y = Parse(args[++i]) } },
                    "--rotation" => options with { RotationDegrees = Parse(args[++i]) },
                    "--snap" => options with { SnapTolerance = Parse(args[++i]) },
                    "--seed" => options with { Seed = int.Parse(args[++i], Ci) },
                    "--strategy" => SetStrategy(options, args[++i], out strategy),
                    "--step-height" => SetStepHeight(options, args[++i], out stepHeight),
                    "--tips" => SetTips(options, args[++i], out tipsPath),
                    "--json" => SetJson(options, out json),
                    _ => throw new ArgumentException($"unknown option '{args[i]}'"),
                };
            }
            if (tipsPath is null) return UsageError("--tips <tips.json> is required");

            var mesh = MeshFile.Read(meshPath);
            var obstacles = new BvhCollisionScene();
            obstacles.AddMesh(mesh, Matrix4x4.Identity, Path.GetFileName(meshPath));
            var tips = ReadTips(tipsPath);
            var result = strategy == "topdown"
                ? new TopDownSupportRouter(obstacles, GrowthRuleSet.Default).Route(tips,
                    new TopDownRoutingOptions
                    {
                        StepHeight = stepHeight,
                        PillarDiameter = options.PillarDiameter,
                        PlateZ = options.PlateZ,
                        Seed = options.Seed,
                        Origin = options.Origin,
                    })
                : new GridSupportRouter(obstacles, GrowthRuleSet.Default).Route(tips, options);
            var collisionFree = IsCollisionFree(result.Graph, obstacles);
            if (json) WriteJson(result, collisionFree);
            else WriteText(meshPath, tips.Count, result, collisionFree);
            return result.UnroutedTips.Count == 0 && collisionFree ? 0 : 2;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or JsonException
                                   or InvalidOperationException or IndexOutOfRangeException)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            return 2;
        }
    }

    private static GridRoutingOptions SetTips(GridRoutingOptions options, string path, out string tipsPath)
    {
        tipsPath = path;
        return options;
    }

    private static GridRoutingOptions SetJson(GridRoutingOptions options, out bool json)
    {
        json = true;
        return options;
    }

    private static GridRoutingOptions SetStrategy(GridRoutingOptions options, string value,
        out string strategy)
    {
        strategy = value.ToLowerInvariant();
        if (strategy is not ("grid" or "topdown"))
            throw new ArgumentException("strategy must be 'grid' or 'topdown'");
        return options;
    }

    private static GridRoutingOptions SetStepHeight(GridRoutingOptions options, string value,
        out float stepHeight)
    {
        stepHeight = Parse(value);
        return options;
    }

    private static List<RoutingTip> ReadTips(string path)
    {
        var records = JsonSerializer.Deserialize<List<TipJson>>(File.ReadAllText(path),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new JsonException("tips file must contain a JSON array");
        return records.Select((tip, index) =>
        {
            if (tip.SurfacePoint is not { Length: 3 })
                throw new JsonException($"tip {index}: surfacePoint must contain three numbers");
            if (tip.InwardSurfaceNormal is not { Length: 3 })
                throw new JsonException($"tip {index}: inwardSurfaceNormal must contain three numbers");
            if (tip.TipDiameter <= 0)
                throw new JsonException($"tip {index}: tipDiameter must be positive");
            return new RoutingTip(ToVector(tip.SurfacePoint), ToVector(tip.InwardSurfaceNormal),
                tip.TipDiameter, tip.ContactObjectId);
        }).ToList();
    }

    private static bool IsCollisionFree(SupportGraph graph, ICollisionScene obstacles)
    {
        foreach (var segment in graph.Segments)
        {
            var start = graph.GetNode(segment.NodeA).Position;
            var end = graph.GetNode(segment.NodeB).Position;
            var radius = segment.Diameter * 0.5f;
            if (segment.Type == SupportSegmentType.Neck)
            {
                var tipAtA = graph.GetNode(segment.NodeA).Type == SupportNodeType.Tip;
                var tip = tipAtA ? start : end;
                var other = tipAtA ? end : start;
                var delta = tip - other;
                var length = delta.Length();
                if (length <= radius * 2 + 0.01f) continue;
                tip -= delta / length * (radius * 2 + 0.01f);
                if (tipAtA) start = tip; else end = tip;
            }
            if (obstacles.IntersectsCapsule(start, end, radius)) return false;
        }
        return true;
    }

    private static void WriteText(string meshPath, int tipCount, RoutingResult result, bool collisionFree)
    {
        Console.WriteLine($"Mesh:           {meshPath}");
        Console.WriteLine($"Tips:           {tipCount} ({result.UnroutedTips.Count} unrouted)");
        foreach (var reason in Enum.GetValues<RoutingFailureReason>())
            Console.WriteLine($"  {reason,-14} {result.Failures.Count(failure => failure.Reason == reason)}");
        Console.WriteLine($"Nodes:          {result.Graph.NodeCount}");
        Console.WriteLine($"Segments:       {result.Graph.SegmentCount}");
        foreach (var type in Enum.GetValues<SupportSegmentType>())
            Console.WriteLine($"  {type,-12} {result.Graph.Segments.Count(s => s.Type == type)}");
        Console.WriteLine($"Bases:          {result.BasePositions.Count}");
        foreach (var position in result.BasePositions)
            Console.WriteLine($"  {Format(position.X)}, {Format(position.Y)}, {Format(position.Z)}");
        Console.WriteLine($"Max lean:       {Format(result.MaxLeanAngleDegrees)} degrees");
        Console.WriteLine($"Collision-free: {(collisionFree ? "yes" : "no")}");
    }

    private static void WriteJson(RoutingResult result, bool collisionFree)
    {
        var summary = new
        {
            nodes = result.Graph.NodeCount,
            segments = result.Graph.SegmentCount,
            segmentCounts = Enum.GetValues<SupportSegmentType>().ToDictionary(type => type.ToString(),
                type => result.Graph.Segments.Count(segment => segment.Type == type)),
            unroutedTips = result.UnroutedTips.Count,
            refusalCounts = Enum.GetValues<RoutingFailureReason>().ToDictionary(reason => reason.ToString(),
                reason => result.Failures.Count(failure => failure.Reason == reason)),
            bases = result.BasePositions.Select(p => new[] { p.X, p.Y, p.Z }),
            maxLeanAngleDegrees = result.MaxLeanAngleDegrees,
            collisionFree,
        };
        Console.WriteLine(JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static BaseLatticeType ParseLattice(string value) => value.ToLowerInvariant() switch
    {
        "square" => BaseLatticeType.Square,
        "hex" or "hexagonal" => BaseLatticeType.Hexagonal,
        _ => throw new ArgumentException("lattice must be 'square' or 'hex'"),
    };

    private static float Parse(string value) => float.Parse(value, Ci);
    private static Vector3 ToVector(float[] values) => new(values[0], values[1], values[2]);
    private static string Format(float value) => value.ToString("0.###", Ci);

    private static int UsageError(string message)
    {
        Console.Error.WriteLine($"error: {message}");
        Console.Error.WriteLine("usage: danslicer route <mesh.stl|mesh.obj> --tips <tips.json> [--strategy grid|topdown] [--step-height 2] [--spacing 5] [--lattice square|hex] [--offset-x 0] [--offset-y 0] [--rotation 0] [--snap 0.25] [--seed 1] [--json]");
        return 1;
    }

    private sealed class TipJson
    {
        public float[]? SurfacePoint { get; set; }
        public float[]? InwardSurfaceNormal { get; set; }
        public float TipDiameter { get; set; }
        public Guid? ContactObjectId { get; set; }
    }
}
