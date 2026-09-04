using Danslicer.Cli;

namespace Danslicer.Tests;

public class BenchCommandTests
{
    [Fact]
    public void MarkdownUsesBenchmarkHouseStyleAndIncludesRequiredMetrics()
    {
        var report = new BenchmarkReport
        {
            FineFeatureMaxAreaMm2 = 1f,
            Models =
            [
                new ModelBenchmark
                {
                    Key = "model",
                    Path = "model.stl",
                    Tips = new TipsBenchmark
                    {
                        WallSeconds = 1.2345,
                        ExitCode = 0,
                        Candidates = 12,
                        ByStrategy = new Dictionary<string, int>
                        {
                            ["Island"] = 2,
                            ["MiniIsland"] = 0,
                            ["MiniCluster"] = 6,
                            ["Overhang"] = 10,
                        },
                        MiniClusters = 2,
                        FineFeatureMinis = 2,
                        MiniClusterMembersBySourceStrategy = new Dictionary<string, int>
                        {
                            ["Island"] = 3,
                            ["Overhang"] = 3,
                        },
                        Spacing = new SpacingBenchmark { Min = 1.1f, Median = 2.2f, Mean = 3.3f },
                    },
                    Routes =
                    [
                        new RouteBenchmark
                        {
                            BaseGrid = "on",
                            Reinforce = true,
                            WallSeconds = 2.3456,
                            ExitCode = 2,
                            Nodes = 8,
                            Segments = 7,
                            SegmentCounts = new Dictionary<string, int>
                            {
                                ["Tip"] = 2,
                                ["MiniSupport"] = 1,
                                ["Branch"] = 2,
                                ["Trunk"] = 2,
                                ["Bracing"] = 0,
                            },
                            UnroutedTips = 9,
                            RefusalCounts = new Dictionary<string, int>
                            {
                                ["ContactBlocked"] = 1,
                                ["NoClearStep"] = 8,
                            },
                            Bases = 1,
                            MaxLeanAngleDegrees = 44.56f,
                            CollisionFree = true,
                        },
                    ],
                },
            ],
        };

        var markdown = BenchCommand.BuildMarkdown(report);

        Assert.Contains("| Model | Command | Flags | Wall s | Exit | Counts | Notes |", markdown);
        Assert.Contains("| model | `tips` | `--seat --json --fine-feature-max 1` | 1.235 | 0 | **12** candidates " +
                        "(Island 2, MiniCluster 6, Overhang 10) across **2** mini clusters " +
                        "(**3 island / 3 regular members**), **2 fine-feature singles**", markdown);
        Assert.Contains("`--seat --strategy tree --base-grid on --reinforce on --json` | " +
                        "2.346 | 2", markdown);
        Assert.Contains("segs 7 (tip 2, mini-support 1, branch 2, trunk 2)", markdown);
        Assert.Contains("**unrouted 9 / 12**, bases **1**, max lean 44.6°, " +
                        "collisionFree **true**", markdown);
        Assert.Contains("Refusals: ContactBlocked 1, NoClearStep 8.", markdown);
        Assert.DoesNotContain("MiniIsland 0", markdown);
        Assert.DoesNotContain("brace 0", markdown);
    }
}
