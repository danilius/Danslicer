using System.Globalization;
using System.Numerics;
using System.Text.Json;
using Danslicer.Core.Config;
using Danslicer.Core.IO;
using Danslicer.Core.Supports;
using Danslicer.Core.Supports.Generation;
using Danslicer.Core.Supports.Routing;
using Danslicer.Cli;

internal static class RouteCommand
{
    private static readonly CultureInfo Ci = CultureInfo.InvariantCulture;
    private const float HalfMillimetreCrossingThreshold = 0.5f;
    private const float CrossingReportThresholdMm = 1f;

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
            var seat = false;
            var useBaseGrid = true;
            var reinforce = false;
            var islandFirst = true;
            var fineFeatureFallback = true;
            var minMemberSeparation = 0f;
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
                    "--seat" => SetSeat(options, out seat),
                    "--base-grid" => SetUseBaseGrid(options, args[++i], out useBaseGrid),
                    "--reinforce" => SetReinforce(options, args[++i], out reinforce),
                    "--island-first" => SetIslandFirst(options, args[++i], out islandFirst),
                    "--fine-feature-fallback" => SetFineFeatureFallback(options, args[++i],
                        out fineFeatureFallback),
                    "--min-member-separation" => SetMinMemberSeparation(options, args[++i],
                        out minMemberSeparation),
                    _ => throw new ArgumentException($"unknown option '{args[i]}'"),
                };
            }
            if (tipsPath is null) return UsageError("--tips <tips.json> is required");

            var mesh = MeshFile.Read(meshPath);
            Vector3? seatOffset = null;
            if (seat)
            {
                var seated = MeshSeat.Apply(mesh);
                mesh = seated.Mesh;
                seatOffset = seated.Offset;
            }
            var obstacles = new BvhCollisionScene();
            obstacles.AddMesh(mesh, Matrix4x4.Identity, Path.GetFileName(meshPath));
            var tips = ReadTips(tipsPath, islandFirst);
            var rules = GrowthRuleSet.FromConfig(new SupportConfig { ReinforceEnabled = reinforce });
            RoutingResult result;
            if (strategy == "topdown")
            {
                result = new TopDownSupportRouter(obstacles, rules).Route(tips,
                    new TopDownRoutingOptions
                    {
                        StepHeight = stepHeight,
                        PillarDiameter = options.PillarDiameter,
                        PlateZ = options.PlateZ,
                        Seed = options.Seed,
                        Origin = options.Origin,
                    });
            }
            else if (strategy == "tree")
            {
                result = new TreeSupportRouter(obstacles, rules).Route(tips,
                    new TreeRoutingOptions
                    {
                        TrunkDiameter = options.PillarDiameter,
                        BranchDiameter = options.PillarDiameter,
                        UseBaseGrid = useBaseGrid,
                        FineFeatureMinisFallBackToRegular = fineFeatureFallback,
                        MinMemberSeparationMm = minMemberSeparation,
                        PlateZ = options.PlateZ,
                        Seed = options.Seed,
                        Origin = options.Origin,
                    });
            }
            else
            {
                result = new GridSupportRouter(obstacles, rules).Route(tips, options);
            }
            var collisionFree = IsCollisionFree(result.Graph, obstacles);
            var crossingPairs = MemberSeparation.CountPairs(result.Graph,
                CrossingReportThresholdMm);
            var crossingPairsBelowHalfMm = MemberSeparation.CountPairs(result.Graph,
                HalfMillimetreCrossingThreshold);
            var crossingPairCounts = MemberSeparation.CountPairsByType(result.Graph,
                CrossingReportThresholdMm);
            var intersectionPairs = MemberSeparation.CountIntersections(result.Graph);
            if (json) WriteJson(result, collisionFree, crossingPairs,
                crossingPairsBelowHalfMm, crossingPairCounts, intersectionPairs,
                minMemberSeparation, seatOffset);
            else WriteText(meshPath, tips.Count, result, collisionFree, crossingPairs,
                crossingPairsBelowHalfMm, intersectionPairs, minMemberSeparation, seatOffset);
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

    private static GridRoutingOptions SetSeat(GridRoutingOptions options, out bool seat)
    {
        seat = true;
        return options;
    }

    private static GridRoutingOptions SetUseBaseGrid(GridRoutingOptions options, string value,
        out bool useBaseGrid)
    {
        useBaseGrid = value.ToLowerInvariant() switch
        {
            "on" or "true" => true,
            "off" or "false" => false,
            _ => throw new ArgumentException("base-grid must be 'on' or 'off'"),
        };
        return options;
    }

    private static GridRoutingOptions SetReinforce(GridRoutingOptions options, string value,
        out bool reinforce)
    {
        reinforce = value.ToLowerInvariant() switch
        {
            "on" or "true" => true,
            "off" or "false" => false,
            _ => throw new ArgumentException("reinforce must be 'on' or 'off'"),
        };
        return options;
    }

    private static GridRoutingOptions SetIslandFirst(GridRoutingOptions options, string value,
        out bool islandFirst)
    {
        islandFirst = value.ToLowerInvariant() switch
        {
            "on" or "true" => true,
            "off" or "false" => false,
            _ => throw new ArgumentException("island-first must be 'on' or 'off'"),
        };
        return options;
    }

    private static GridRoutingOptions SetFineFeatureFallback(GridRoutingOptions options,
        string value, out bool fineFeatureFallback)
    {
        fineFeatureFallback = value.ToLowerInvariant() switch
        {
            "on" or "true" => true,
            "off" or "false" => false,
            _ => throw new ArgumentException("fine-feature-fallback must be 'on' or 'off'"),
        };
        return options;
    }

    private static GridRoutingOptions SetMinMemberSeparation(GridRoutingOptions options,
        string value, out float minMemberSeparation)
    {
        minMemberSeparation = Parse(value);
        if (!float.IsFinite(minMemberSeparation) || minMemberSeparation < 0)
            throw new ArgumentException("min-member-separation must be a non-negative number");
        return options;
    }

    private static GridRoutingOptions SetStrategy(GridRoutingOptions options, string value,
        out string strategy)
    {
        strategy = value.ToLowerInvariant();
        if (strategy is not ("grid" or "topdown" or "tree"))
            throw new ArgumentException("strategy must be 'grid', 'topdown' or 'tree'");
        return options;
    }

    private static GridRoutingOptions SetStepHeight(GridRoutingOptions options, string value,
        out float stepHeight)
    {
        stepHeight = Parse(value);
        return options;
    }

    private static List<RoutingTip> ReadTips(string path, bool islandFirst)
    {
        var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var text = File.ReadAllText(path);
        using var doc = JsonDocument.Parse(text);
        var records = doc.RootElement.ValueKind == JsonValueKind.Array
            ? JsonSerializer.Deserialize<List<TipJson>>(text, opts)
            : JsonSerializer.Deserialize<TipsFileJson>(text, opts)?.Candidates;
        if (records is null)
            throw new JsonException("tips file must contain a JSON array or a tips --json object with 'candidates'");
        return records.Select((tip, index) =>
        {
            var point = tip.SurfacePoint ?? tip.Point;
            var normal = tip.InwardSurfaceNormal ?? tip.InwardNormal;
            var diameter = tip.TipDiameter > 0 ? tip.TipDiameter : tip.Diameter;
            if (point is not { Length: 3 })
                throw new JsonException($"tip {index}: surfacePoint/point must contain three numbers");
            if (normal is not { Length: 3 })
                throw new JsonException($"tip {index}: inwardSurfaceNormal/inwardNormal must contain three numbers");
            if (diameter <= 0)
                throw new JsonException($"tip {index}: tipDiameter/diameter must be positive");
            var islandOrigin = IsIsland(tip.Strategy) || IsIsland(tip.MiniClusterSourceStrategy);
            return new RoutingTip(ToVector(point), ToVector(normal), diameter, tip.ContactObjectId,
                TipShape: ParseShape(tip.TipShape),
                ConeLength: tip.ConeLength > 0 ? tip.ConeLength : 2f,
                BallDiameter: tip.BallDiameter,
                PenetrationDepth: Math.Max(tip.PenetrationDepth, 0f),
                MiniSupportOnly: string.Equals(tip.Strategy, nameof(TipStrategy.MiniIsland),
                                     StringComparison.OrdinalIgnoreCase) ||
                                 string.Equals(tip.Strategy, nameof(TipStrategy.MiniCluster),
                                     StringComparison.OrdinalIgnoreCase),
                MiniClusterId: tip.MiniClusterId,
                MiniClusterCenter: tip.MiniClusterCenter is { Length: 3 }
                    ? ToVector(tip.MiniClusterCenter)
                    : null,
                IsIslandOrigin: islandOrigin,
                IsIslandPriority: islandFirst && islandOrigin,
                IsFineFeatureMini: tip.IsFineFeatureMini,
                FallbackTipDiameter: tip.FallbackTipDiameter,
                FallbackTipShape: ParseOptionalShape(tip.FallbackTipShape),
                FallbackConeLength: tip.FallbackConeLength,
                FallbackBallDiameter: tip.FallbackBallDiameter,
                TipNormalLeadIn: Math.Max(tip.TipNormalLeadIn, 0f));
        }).ToList();
    }

    private static bool IsIsland(string? strategy) =>
        string.Equals(strategy, nameof(TipStrategy.Island), StringComparison.OrdinalIgnoreCase) ||
        string.Equals(strategy, nameof(TipStrategy.MiniIsland), StringComparison.OrdinalIgnoreCase);

    private static bool IsCollisionFree(SupportGraph graph, ICollisionScene obstacles)
    {
        foreach (var segment in graph.Segments)
        {
            var start = graph.GetNode(segment.NodeA).Position;
            var end = graph.GetNode(segment.NodeB).Position;
            var radius = segment.Diameter * 0.5f;
            if (segment.Type is SupportSegmentType.Tip or SupportSegmentType.MiniSupport)
            {
                var nodeA = graph.GetNode(segment.NodeA);
                var nodeB = graph.GetNode(segment.NodeB);
                var tipAtA = nodeA.Type == SupportNodeType.Tip;
                var tipNode = tipAtA ? nodeA : nodeB;
                var other = tipAtA ? end : start;
                radius = MathF.Max(0.025f, tipNode.TipDiameter * 0.5f);
                var contactAllowance = (radius + 0.25f) * 2 + 0.01f;
                if (TipPathIntersects(obstacles, tipNode, other, radius, contactAllowance))
                    return false;
                continue;
            }
            if (obstacles.IntersectsCapsule(start, end, radius)) return false;
        }
        return true;
    }

    private static bool TipPathIntersects(ICollisionScene obstacles, SupportNode tip,
        Vector3 other, float radius, float contactAllowance)
    {
        var points = TipBodyGeometry.Centerline(tip.Position, tip.SurfaceNormal, other,
            tip.TipNormalLeadIn);
        var remainingTrim = contactAllowance;
        for (var i = 1; i < points.Count; i++)
        {
            var start = points[i - 1];
            var end = points[i];
            var length = Vector3.Distance(start, end);
            if (remainingTrim >= length)
            {
                remainingTrim -= length;
                continue;
            }
            if (remainingTrim > 0)
            {
                start = Vector3.Lerp(start, end, remainingTrim / length);
                remainingTrim = 0;
            }
            if (obstacles.IntersectsCapsule(start, end, radius)) return true;
        }
        return false;
    }

    private static void WriteText(string meshPath, int tipCount, RoutingResult result,
        bool collisionFree, int crossingPairs, int crossingPairsBelowHalfMm,
        int intersectionPairs, float minMemberSeparation, Vector3? seatOffset)
    {
        Console.WriteLine($"Mesh:           {meshPath}");
        if (seatOffset is { } offset) MeshSeat.WriteText(offset);
        Console.WriteLine($"Tips:           {tipCount} ({result.UnroutedTips.Count} unrouted)");
        foreach (var reason in Enum.GetValues<RoutingFailureReason>())
            Console.WriteLine($"  {reason,-14} {result.Failures.Count(failure => failure.Reason == reason)}");
        foreach (var failure in result.Failures)
            Console.WriteLine($"  refused {Format(failure.Tip.SurfacePoint.X)}, " +
                              $"{Format(failure.Tip.SurfacePoint.Y)}, " +
                              $"{Format(failure.Tip.SurfacePoint.Z)}: {failure.Reason}");
        Console.WriteLine($"Nodes:          {result.Graph.NodeCount}");
        Console.WriteLine($"Segments:       {result.Graph.SegmentCount}");
        foreach (var type in Enum.GetValues<SupportSegmentType>())
            Console.WriteLine($"  {type,-12} {result.Graph.Segments.Count(s => s.Type == type)}");
        Console.WriteLine($"Bases:          {result.BasePositions.Count}");
        foreach (var position in result.BasePositions)
            Console.WriteLine($"  {Format(position.X)}, {Format(position.Y)}, {Format(position.Z)}");
        Console.WriteLine($"Max lean:       {Format(result.MaxLeanAngleDegrees)} degrees");
        Console.WriteLine($"Crossing pairs: {crossingPairsBelowHalfMm} below 0.5 mm; " +
                          $"{crossingPairs} below {Format(CrossingReportThresholdMm)} mm");
        Console.WriteLine($"Intersections:  {intersectionPairs} overlapping member pairs");
        Console.WriteLine($"Collision-free: {(collisionFree ? "yes" : "no")}");
    }

    private static void WriteJson(RoutingResult result, bool collisionFree, int crossingPairs,
        int crossingPairsBelowHalfMm, IReadOnlyDictionary<string, int> crossingPairCounts,
        int intersectionPairs, float minMemberSeparation, Vector3? seatOffset)
    {
        var summary = new Dictionary<string, object?>
        {
            ["nodes"] = result.Graph.NodeCount,
            ["segments"] = result.Graph.SegmentCount,
            ["segmentCounts"] = Enum.GetValues<SupportSegmentType>().ToDictionary(type => type.ToString(),
                type => result.Graph.Segments.Count(segment => segment.Type == type)),
            ["unroutedTips"] = result.UnroutedTips.Count,
            ["refusalCounts"] = Enum.GetValues<RoutingFailureReason>().ToDictionary(reason => reason.ToString(),
                reason => result.Failures.Count(failure => failure.Reason == reason)),
            ["refusals"] = result.Failures.Select(failure => new
            {
                point = new[] { failure.Tip.SurfacePoint.X, failure.Tip.SurfacePoint.Y,
                    failure.Tip.SurfacePoint.Z },
                reason = failure.Reason.ToString(),
                isIslandOrigin = failure.Tip.IsIslandOrigin,
            }).ToList(),
            ["bases"] = result.BasePositions.Select(p => new[] { p.X, p.Y, p.Z }).ToList(),
            ["maxLeanAngleDegrees"] = result.MaxLeanAngleDegrees,
            ["minMemberSeparationMm"] = minMemberSeparation,
            ["crossingThresholdMm"] = CrossingReportThresholdMm,
            ["crossingPairsBelowHalfMm"] = crossingPairsBelowHalfMm,
            ["crossingPairs"] = crossingPairs,
            ["crossingPairCounts"] = crossingPairCounts,
            ["intersectionPairs"] = intersectionPairs,
            ["collisionFree"] = collisionFree,
        };
        if (seatOffset is { } offset) summary["seatOffset"] = MeshSeat.Json(offset);
        Console.WriteLine(JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static SupportTipShape ParseShape(string? value)
    {
        if (string.IsNullOrEmpty(value)) return SupportTipShape.Capsule;
        return value.ToLowerInvariant() switch
        {
            "capsule" => SupportTipShape.Capsule,
            "cone" => SupportTipShape.Cone,
            _ => throw new JsonException($"unknown tip shape '{value}'"),
        };
    }

    private static SupportTipShape? ParseOptionalShape(string? value) =>
        string.IsNullOrEmpty(value) ? null : ParseShape(value);

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
        Console.Error.WriteLine("usage: danslicer route <mesh.stl|mesh.obj> --tips <tips.json> [--seat] [--strategy grid|topdown|tree] [--base-grid on|off] [--island-first on|off] [--fine-feature-fallback on|off] [--min-member-separation <mm>] [--reinforce on|off] [--step-height 2] [--spacing 5] [--lattice square|hex] [--offset-x 0] [--offset-y 0] [--rotation 0] [--snap 0.25] [--seed 1] [--json]");
        return 1;
    }

    private sealed class TipsFileJson
    {
        public List<TipJson>? Candidates { get; set; }
    }

    private sealed class TipJson
    {
        public float[]? SurfacePoint { get; set; }
        public float[]? Point { get; set; }
        public float[]? InwardSurfaceNormal { get; set; }
        public float[]? InwardNormal { get; set; }
        public float TipDiameter { get; set; }
        public float Diameter { get; set; }
        public Guid? ContactObjectId { get; set; }
        public string? TipShape { get; set; }
        public float ConeLength { get; set; }
        public float BallDiameter { get; set; }
        public float PenetrationDepth { get; set; }
        public float TipNormalLeadIn { get; set; }
        public string? Strategy { get; set; }
        public string? MiniClusterSourceStrategy { get; set; }
        public int? MiniClusterId { get; set; }
        public float[]? MiniClusterCenter { get; set; }
        public bool IsFineFeatureMini { get; set; }
        public float? FallbackTipDiameter { get; set; }
        public string? FallbackTipShape { get; set; }
        public float? FallbackConeLength { get; set; }
        public float? FallbackBallDiameter { get; set; }
    }
}
