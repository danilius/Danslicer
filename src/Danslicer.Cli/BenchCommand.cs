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
                Models =
                [
                    RunModel("drogon", options.DrogonPath),
                    RunModel("gripper", options.GripperPath),
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
                "Island", "MiniIsland", "LocalMinimum", "Corner", "Edge", "Overhang",
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
                    ["Tip", "MiniSupport", "Branch", "Trunk", "Bracing"], SegmentName);
                var refusals = OrderedValues(route.RefusalCounts,
                    "ContactBlocked", "NoClearStep", "NoReachableGridPoint",
                    "NoBranchEndInRange", "NoLanding", "BelowPlate");
                text.AppendLine($"| {Escape(model.Key)} | `route` | " +
                    $"`--seat --strategy tree --base-grid {route.BaseGrid} --json` | " +
                    $"{route.WallSeconds:0.000} | {route.ExitCode} | nodes {route.Nodes}, " +
                    $"segs {route.Segments}{Parenthesize(segments)}, **unrouted " +
                    $"{route.UnroutedTips} / {tips.Candidates}**, bases **{route.Bases}**, " +
                    $"max lean {F1(route.MaxLeanAngleDegrees)}°, collisionFree " +
                    $"**{route.CollisionFree.ToString().ToLowerInvariant()}** | " +
                    $"Refusals: {(refusals.Length == 0 ? "none" : refusals)}. |");
            }
        }
        return text.ToString().TrimEnd();
    }

    private static ModelBenchmark RunModel(string key, string path)
    {
        if (!File.Exists(path)) throw new IOException($"model not found: {path}");

        var tipsRun = Capture(() => TipsCommand.Run([path, "--seat", "--json"]));
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
                var routeRun = Capture(() => RouteCommand.Run(
                    [path, "--tips", tipsPath, "--seat", "--strategy", "tree",
                        "--base-grid", mode, "--json"]));
                routes.Add(ParseRoute(mode, routeRun));
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

    private static RouteBenchmark ParseRoute(string mode, CapturedRun run)
    {
        using var document = JsonDocument.Parse(run.Stdout);
        var root = document.RootElement;
        return new RouteBenchmark
        {
            BaseGrid = mode,
            WallSeconds = run.WallSeconds,
            ExitCode = run.ExitCode,
            Nodes = root.GetProperty("nodes").GetInt32(),
            Segments = root.GetProperty("segments").GetInt32(),
            SegmentCounts = ReadIntDictionary(root.GetProperty("segmentCounts")),
            UnroutedTips = root.GetProperty("unroutedTips").GetInt32(),
            RefusalCounts = ReadIntDictionary(root.GetProperty("refusalCounts")),
            Bases = root.GetProperty("bases").GetArrayLength(),
            MaxLeanAngleDegrees = root.GetProperty("maxLeanAngleDegrees").GetSingle(),
            CollisionFree = root.GetProperty("collisionFree").GetBoolean(),
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
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--drogon": drogon = args[++i]; break;
                case "--gripper": gripper = args[++i]; break;
                case "--output": output = args[++i]; break;
                default: throw new ArgumentException($"unknown option '{args[i]}'");
            }
        }
        return new BenchOptions(drogon, gripper, output);
    }

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
        "MiniSupport" => "mini-support",
        "Bracing" => "brace",
        _ => name.ToLowerInvariant(),
    };
    private static string Escape(string value) => value.Replace("|", "\\|");
    private static string F(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);
    private static string F1(float value) => value.ToString("0.0", CultureInfo.InvariantCulture);

    private static void Usage() => Console.Error.WriteLine(
        "usage: danslicer bench [--drogon <path>] [--gripper <path>] [--output <summary.json>]");

    private sealed record BenchOptions(string DrogonPath, string GripperPath, string? OutputPath);
    private sealed record CapturedRun(int ExitCode, double WallSeconds, string Stdout, string Stderr);
}

internal sealed class BenchmarkReport
{
    public DateTimeOffset GeneratedAtUtc { get; init; }
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
    public double WallSeconds { get; init; }
    public int ExitCode { get; init; }
    public int Nodes { get; init; }
    public int Segments { get; init; }
    public required Dictionary<string, int> SegmentCounts { get; init; }
    public int UnroutedTips { get; init; }
    public required Dictionary<string, int> RefusalCounts { get; init; }
    public int Bases { get; init; }
    public float MaxLeanAngleDegrees { get; init; }
    public bool CollisionFree { get; init; }
}
