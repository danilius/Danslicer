using System.Numerics;
using Danslicer.Core.Supports.Routing;

namespace Danslicer.Tests;

public sealed class RoutingGrowthRuleTests
{
    [Fact]
    public void RulesRunInOrderAndDisabledRulesAreSkipped()
    {
        var seen = new List<string>();
        var rules = new GrowthRuleSet(new IGrowthRule[]
        {
            new RecordingRule("first", seen, 2),
            new RecordingRule("disabled", seen, 10) { Enabled = false },
            new RecordingRule("last", seen, 3),
        });
        var context = Context(GrowthOperation.Grow);
        context.Diameter = 1;

        rules.Evaluate(context);

        Assert.Equal(new[] { "first", "last" }, seen);
        Assert.Equal(6, context.Diameter);
    }

    [Fact]
    public void LeanClampsHorizontalTravel()
    {
        var rule = new LeanGrowthRule
        {
            MaxAngleDegrees = 45,
            MaxAngleNearTipDegrees = 30,
            NearTipDistance = 2,
        };
        var context = Context(GrowthOperation.Branch, Vector3.Zero, new(10, 0, 5));
        context.DistanceToTip = 10;

        rule.Evaluate(context);

        Assert.Equal(5, context.End.X, 4);
        Assert.Equal(5, context.End.Z, 4);
    }

    [Fact]
    public void BranchRuleRejectsOutOfReachAndBranchLimit()
    {
        var rule = new BranchGrowthRule { TriggerDistance = 4, MaxBranchesPerTrunk = 2 };
        var distant = Context(GrowthOperation.Branch);
        distant.DistanceToTip = 5;
        rule.Evaluate(distant);
        Assert.False(distant.Allowed);

        var crowded = Context(GrowthOperation.Branch);
        crowded.DistanceToTip = 2;
        crowded.ExistingBranchCount = 2;
        rule.Evaluate(crowded);
        Assert.False(crowded.Allowed);
    }

    [Fact]
    public void TaperShapesOnlyNecks()
    {
        var rule = new TaperGrowthRule { NeckLength = 3, TipToPillarDiameterRatio = 0.5f };
        var context = Context(GrowthOperation.Neck);
        context.Diameter = 0.6f;

        rule.Evaluate(context);

        Assert.Equal(3, context.NeckLength);
        Assert.Equal(0.3f, context.Diameter, 4);
    }

    [Fact]
    public void MergeRequiresClearanceBelowLowestTip()
    {
        var rule = new MergeGrowthRule
        {
            TriggerDistance = 2,
            MinHeightAboveTipsToMerge = 3,
            ResultingTrunkDiameter = 2,
        };
        var allowed = Context(GrowthOperation.Merge, new(0, 0, 5), new(1, 0, 5));
        allowed.LowestTipZ = 8;
        rule.Evaluate(allowed);
        Assert.True(allowed.Allowed);
        Assert.Equal(2, allowed.Diameter);

        var tooClose = Context(GrowthOperation.Merge, new(0, 0, 6), new(1, 0, 6));
        tooClose.LowestTipZ = 8;
        rule.Evaluate(tooClose);
        Assert.False(tooClose.Allowed);
    }

    private static GrowthContext Context(GrowthOperation operation, Vector3 start = default,
        Vector3 end = default) => new()
    {
        Operation = operation,
        Start = start,
        DesiredEnd = end,
        End = end,
        Diameter = 1,
    };

    private sealed class RecordingRule(string name, List<string> seen, float multiplier) : IGrowthRule
    {
        public string Name => name;
        public bool Enabled { get; set; } = true;
        public void Evaluate(GrowthContext context)
        {
            seen.Add(name);
            context.Diameter *= multiplier;
        }
    }
}
