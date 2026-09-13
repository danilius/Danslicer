using Danslicer.Cli;

namespace Danslicer.Tests;

public class BenchCommandTests
{
    [Fact]
    public void MarkdownUsesBenchmarkHouseStyleAndIncludesRequiredMetrics()
    {
        var report = new BenchmarkReport
        {
            MinMemberSeparationMm = 0.5f,
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
                            ["Corner"] = 0,
                            ["Overhang"] = 10,
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
                            IslandRefusals = 3,
                            Bases = 1,
                            MaxLeanAngleDegrees = 44.56f,
                            CollisionFree = true,
                            CrossingPairs = 4,
                            CrossingPairsBelowHalfMm = 2,
                            IntersectionPairs = 1,
                        },
                    ],
                },
            ],
        };

        var markdown = BenchCommand.BuildMarkdown(report);

        Assert.Contains("| Model | Command | Flags | Wall s | Exit | Counts | Notes |", markdown);
        Assert.Contains("| model | `tips` | `--seat --json` | 1.235 | 0 | **12** candidates " +
                        "(Island 2, Overhang 10)", markdown);
        Assert.Contains("`--seat --strategy tree --base-grid on " +
                        "--min-member-separation 0.5 --reinforce on --json` | " +
                        "2.346 | 2", markdown);
        Assert.Contains("segs 7 (tip 2, branch 2, trunk 2)", markdown);
        Assert.Contains("**unrouted 9 / 12**, bases **1**, max lean 44.6°, " +
                        "collisionFree **true**, crossing pairs <0.5 / <1 mm **2 / 4**, " +
                        "intersections **1**", markdown);
        Assert.Contains("Refusals: ContactBlocked 1, NoClearStep 8; island-origin **3**.", markdown);
        Assert.DoesNotContain("Corner 0", markdown);
        Assert.DoesNotContain("brace 0", markdown);
    }
}
