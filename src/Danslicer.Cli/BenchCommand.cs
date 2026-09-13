using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Danslicer.Cli;

/// <summary>
/// Runs the seated canonical support benchmark pair. Standard output stays machine-readable
/// unless --output is supplied; the Markdown table uses the other stream in that case.
/// </summary>
internal static class BenchCommand
{
    internal const string DefaultDrogonPath =
        @"F:\Git Repos\Danslicer\test files\Drogon_flat_surface.stl";
    internal const string DefaultGripperPath =
        @"F:\Git Repos\Danslicer\test files\roof gripper T2.obj";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static int Run(string[] args)
    {
        try
        {
            var options = Parse(args);
            var report = new BenchmarkReport
            {
                GeneratedAtUtc = DateTimeOffset.UtcNow,
                IslandFirst = options.IslandFirst,
                MinMemberSeparationMm = options.MinMemberSeparationMm,
                Models =
                [
                    RunModel("drogon", options.DrogonPath, options.Reinforce,
                        options.IslandFirst, options.MinMemberSeparationMm),
                    RunModel("gripper", options.GripperPath, options.Reinforce,
                        options.IslandFirst, options.MinMemberSeparationMm),
                ],
            };
            report.Markdown = BuildMarkdown(report);

            var json = JsonSerializer.Serialize(report, JsonOptions);
            if (options.OutputPath is null)
            {
                Console.WriteLine(json);
                Console.Error.WriteLine(report.Markdown);
            }
            else
            {
                File.WriteAllText(options.OutputPath, json + Environment.NewLine);
                Console.WriteLine(report.Markdown);
                Console.Error.WriteLine($"JSON summary: {Path.GetFullPath(options.OutputPath)}");
            }
            return 0;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or JsonException
                                   or InvalidOperationException or IndexOutOfRangeException)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            Usage();
            return 2;
        }
    }

    internal static string BuildMarkdown(BenchmarkReport report)
    {
        var text = new StringBuilder();
        text.AppendLine("### Results");
        text.AppendLine();
        text.AppendLine("| Model | Command | Flags | Wall s | Exit | Counts | Notes |");
        text.AppendLine("| --- | --- | ---: | ---: | ---: | --- | --- |");
        foreach (var model in report.Models)
        {
            var tips = model.Tips;
            var strategies = OrderedValues(tips.ByStrategy,
                "Island", "LocalMinimum", "Corner", "Edge", "Overhang",
                "GridProjection");
            var spacing = tips.Spacing is null
                ? "Spacing n/a."
                : $"Spacing min {F(tips.Spacing.Min)} / median {F(tips.Spacing.Median)} / " +
                  $"mean {F(tips.Spacing.Mean)}.";
            text.AppendLine($"| {Escape(model.Key)} | `tips` | `--seat --json` | " +
                $"{tips.WallSeconds:0.000} | {tips.ExitCode} | **{tips.Candidates}** candidates" +
                $"{Parenthesize(strategies)} | {spacing} |");

            foreach (var route in model.Routes)
            {
                var segments = OrderedValues(route.SegmentCounts,
                    ["Tip", "Branch", "Trunk", "Bracing"], SegmentName);
                var refusals = OrderedValues(route.RefusalCounts,
                    "ContactBlocked", "MemberCrossing", "NoClearStep", "NoReachableGridPoint",
                    "NoLanding", "BelowPlate");
                text.AppendLine($"| {Escape(model.Key)} | `route` | " +
                    $"`--seat --strategy tree --base-grid {route.BaseGrid} " +
                    $"--min-member-separation {F(report.MinMemberSeparationMm)} " +
                    $"--reinforce {(route.Reinforce ? "on" : "off")} --json` | " +
                    $"{route.WallSeconds:0.000} | {route.ExitCode} | nodes {route.Nodes}, " +
                    $"segs {route.Segments}{Parenthesize(segments)}, **unrouted " +
                    $"{route.UnroutedTips} / {tips.Candidates}**, bases **{route.Bases}**, " +
                    $"max lean {F1(route.MaxLeanAngleDegrees)}°, collisionFree " +
                    $"**{route.CollisionFree.ToString().ToLowerInvariant()}**, crossing pairs " +
                    $"<0.5 / <1 mm **{route.CrossingPairsBelowHalfMm} / {route.CrossingPairs}**, " +
                    $"intersections **{route.IntersectionPairs}** | " +
                    $"Refusals: {(refusals.Length == 0 ? "none" : refusals)}; " +
                    $"island-origin **{route.IslandRefusals}**. |");
            }
        }
        return text.ToString().TrimEnd();
    }

    private static ModelBenchmark RunModel(string key, string path, bool reinforce,
        bool islandFirst, float minMemberSeparationMm)
    {
        if (!File.Exists(path)) throw new IOException($"model not found: {path}");

        var tipsArgs = new List<string> { path, "--seat", "--json" };
        var tipsRun = Capture(() => TipsCommand.Run([.. tipsArgs]));
        if (tipsRun.ExitCode != 0)
            throw new InvalidOperationException($"{key} tips exited {tipsRun.ExitCode}: {tipsRun.Stderr.Trim()}");
        var tips = ParseTips(tipsRun);

        var tipsPath = Path.Combine(Path.GetTempPath(), $"danslicer-bench-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(tipsPath, tipsRun.Stdout);
            var routes = new List<RouteBenchmark>();
            foreach (var mode in new[] { "on", "off" })
            {
                var routeArgs = new List<string>
                {
                    path, "--tips", tipsPath, "--seat", "--strategy", "tree",
                    "--base-grid", mode,
                    "--min-member-separation", F(minMemberSeparationMm),
                    "--reinforce", reinforce ? "on" : "off", "--json",
                };
                if (!islandFirst) routeArgs.AddRange(["--island-first", "off"]);
                var routeRun = Capture(() => RouteCommand.Run([.. routeArgs]));
                routes.Add(ParseRoute(mode, reinforce, routeRun));
            }
            return new ModelBenchmark { Key = key, Path = path, Tips = tips, Routes = routes };
        }
        finally
        {
            if (File.Exists(tipsPath)) File.Delete(tipsPath);
        }
    }

    private static TipsBenchmark ParseTips(CapturedRun run)
    {
        using var document = JsonDocument.Parse(run.Stdout);
        var root = document.RootElement;
        return new TipsBenchmark
        {
            WallSeconds = run.WallSeconds,
            ExitCode = run.ExitCode,
            Candidates = root.GetProperty("count").GetInt32(),
            ByStrategy = ReadIntDictionary(root.GetProperty("byStrategy")),
            Spacing = root.TryGetProperty("spacing", out var spacing) &&
                      spacing.ValueKind != JsonValueKind.Null
                ? new SpacingBenchmark
                {
                    Min = spacing.GetProperty("min").GetSingle(),
                    Median = spacing.GetProperty("median").GetSingle(),
                    Mean = spacing.GetProperty("mean").GetSingle(),
                }
                : null,
        };
    }

    private static RouteBenchmark ParseRoute(string mode, bool reinforce, CapturedRun run)
    {
        using var document = JsonDocument.Parse(run.Stdout);
        var root = document.RootElement;
        return new RouteBenchmark
        {
            BaseGrid = mode,
            Reinforce = reinforce,
            WallSeconds = run.WallSeconds,
            ExitCode = run.ExitCode,
            Nodes = root.GetProperty("nodes").GetInt32(),
            Segments = root.GetProperty("segments").GetInt32(),
            SegmentCounts = ReadIntDictionary(root.GetProperty("segmentCounts")),
            UnroutedTips = root.GetProperty("unroutedTips").GetInt32(),
            RefusalCounts = ReadIntDictionary(root.GetProperty("refusalCounts")),
            IslandRefusals = root.GetProperty("refusals").EnumerateArray().Count(item =>
                item.TryGetProperty("isIslandOrigin", out var island) && island.GetBoolean()),
            Bases = root.GetProperty("bases").GetArrayLength(),
            MaxLeanAngleDegrees = root.GetProperty("maxLeanAngleDegrees").GetSingle(),
            CollisionFree = root.GetProperty("collisionFree").GetBoolean(),
            CrossingPairs = root.GetProperty("crossingPairs").GetInt32(),
            CrossingPairsBelowHalfMm = root.GetProperty("crossingPairsBelowHalfMm").GetInt32(),
            CrossingPairCounts = ReadIntDictionary(root.GetProperty("crossingPairCounts")),
            IntersectionPairs = root.GetProperty("intersectionPairs").GetInt32(),
        };
    }

    private static Dictionary<string, int> ReadIntDictionary(JsonElement element) =>
        element.EnumerateObject().ToDictionary(property => property.Name,
            property => property.Value.GetInt32(), StringComparer.Ordinal);

    private static CapturedRun Capture(Func<int> command)
    {
        var originalOut = Console.Out;
        var originalError = Console.Error;
        using var stdout = new StringWriter(CultureInfo.InvariantCulture);
        using var stderr = new StringWriter(CultureInfo.InvariantCulture);
        var timer = Stopwatch.StartNew();
        int exitCode;
        try
        {
            Console.SetOut(stdout);
            Console.SetError(stderr);
            exitCode = command();
        }
        finally
        {
            timer.Stop();
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }
        return new CapturedRun(exitCode, timer.Elapsed.TotalSeconds, stdout.ToString(),
            stderr.ToString());
    }

    private static BenchOptions Parse(string[] args)
    {
        var drogon = DefaultDrogonPath;
        var gripper = DefaultGripperPath;
        string? output = null;
        var reinforce = false;
        var islandFirst = true;
        var minMemberSeparationMm = 0f;
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--drogon": drogon = args[++i]; break;
                case "--gripper": gripper = args[++i]; break;
                case "--output": output = args[++i]; break;
                case "--reinforce": reinforce = ParseToggle(args[++i], "reinforce"); break;
                case "--island-first": islandFirst = ParseToggle(args[++i], "island-first"); break;
                case "--min-member-separation":
                    minMemberSeparationMm = float.Parse(args[++i], CultureInfo.InvariantCulture);
                    if (!float.IsFinite(minMemberSeparationMm) || minMemberSeparationMm < 0)
                        throw new ArgumentException(
                            "min-member-separation must be a non-negative number");
                    break;
                default: throw new ArgumentException($"unknown option '{args[i]}'");
            }
        }
        return new BenchOptions(drogon, gripper, output, reinforce, islandFirst,
            minMemberSeparationMm);
    }

    private static bool ParseToggle(string value, string name) => value.ToLowerInvariant() switch
    {
        "on" or "true" => true,
        "off" or "false" => false,
        _ => throw new ArgumentException($"{name} must be 'on' or 'off'"),
    };

    private static string OrderedValues(IReadOnlyDictionary<string, int> values,
        params string[] order) => OrderedValues(values, order, valueName: null);

    private static string OrderedValues(IReadOnlyDictionary<string, int> values,
        string[] order, Func<string, string>? valueName)
    {
        return string.Join(", ", order
            .Where(name => values.GetValueOrDefault(name) > 0)
            .Select(name => $"{valueName?.Invoke(name) ?? name} {values[name]}"));
    }

    private static string Parenthesize(string value) => value.Length == 0 ? string.Empty : $" ({value})";
    private static string SegmentName(string name) => name switch
    {
        "Bracing" => "brace",
        _ => name.ToLowerInvariant(),
    };
    private static string Escape(string value) => value.Replace("|", "\\|");
    private static string F(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);
    private static string F1(float value) => value.ToString("0.0", CultureInfo.InvariantCulture);

    private static void Usage() => Console.Error.WriteLine(
        "usage: danslicer bench [--drogon <path>] [--gripper <path>] [--reinforce on|off] [--island-first on|off] [--min-member-separation <mm>] [--output <summary.json>]");

    private sealed record BenchOptions(string DrogonPath, string GripperPath, string? OutputPath,
        bool Reinforce, bool IslandFirst, float MinMemberSeparationMm);
    private sealed record CapturedRun(int ExitCode, double WallSeconds, string Stdout, string Stderr);
}

internal sealed class BenchmarkReport
{
    public DateTimeOffset GeneratedAtUtc { get; init; }
    public bool IslandFirst { get; init; } = true;
    public float MinMemberSeparationMm { get; init; }
    public required List<ModelBenchmark> Models { get; init; }
    public string Markdown { get; set; } = string.Empty;
}

internal sealed class ModelBenchmark
{
    public required string Key { get; init; }
    public required string Path { get; init; }
    public required TipsBenchmark Tips { get; init; }
    public required List<RouteBenchmark> Routes { get; init; }
}

internal sealed class TipsBenchmark
{
    public double WallSeconds { get; init; }
    public int ExitCode { get; init; }
    public int Candidates { get; init; }
    public required Dictionary<string, int> ByStrategy { get; init; }
    public SpacingBenchmark? Spacing { get; init; }
}

internal sealed class SpacingBenchmark
{
    public float Min { get; init; }
    public float Median { get; init; }
    public float Mean { get; init; }
}

internal sealed class RouteBenchmark
{
    public required string BaseGrid { get; init; }
    public bool Reinforce { get; init; }
    public double WallSeconds { get; init; }
    public int ExitCode { get; init; }
    public int Nodes { get; init; }
    public int Segments { get; init; }
    public required Dictionary<string, int> SegmentCounts { get; init; }
    public int UnroutedTips { get; init; }
    public required Dictionary<string, int> RefusalCounts { get; init; }
    public int IslandRefusals { get; init; }
    public int Bases { get; init; }
    public float MaxLeanAngleDegrees { get; init; }
    public bool CollisionFree { get; init; }
    public int CrossingPairs { get; init; }
    public int CrossingPairsBelowHalfMm { get; init; }
    public Dictionary<string, int> CrossingPairCounts { get; init; } = [];
    public int IntersectionPairs { get; init; }
}
