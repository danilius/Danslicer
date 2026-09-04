using System.Numerics;
using Danslicer.Core.Config;
using Danslicer.Core.Supports;
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
    public void RuleSetBuildsReinforcePolicyFromSupportConfig()
    {
        var config = new SupportConfig
        {
            TipMemberLength = 3.5f,
            ReinforceEnabled = true,
            ReinforceSeedSelector = ReinforceSeedSelector.CriticalTips,
            ReinforceCount = 5,
            ReinforceRingRadius = 4.25f,
            ReinforceRingDiameterMultiplier = 1.6f,
        };

        var rules = GrowthRuleSet.FromConfig(config);

        Assert.Equal(3.5f, rules.Find<TaperGrowthRule>()!.TipLength);
        var reinforce = rules.Find<ReinforceGrowthRule>()!;
        Assert.True(reinforce.Enabled);
        Assert.Equal(ReinforceSeedSelector.CriticalTips, reinforce.SeedSelector);
        Assert.Equal(5, reinforce.Count);
        Assert.Equal(4.25f, reinforce.RingRadius);
        Assert.Equal(1.6f, reinforce.RingDiameterMultiplier);
        Assert.False(GrowthRuleSet.FromConfig(new SupportConfig())
            .Find<ReinforceGrowthRule>()!.Enabled);
    }

    [Fact]
    public void DefaultSupportConfigRoutesBitIdenticallyToThePreviousDefaultRules()
    {
        var tips = new[] { new RoutingTip(new(0, 0, 10), Vector3.UnitZ, 0.4f) };
        var options = new TreeRoutingOptions { Seed = 19 };

        var before = new TreeSupportRouter(new LinearCollisionScene(), GrowthRuleSet.Default)
            .Route(tips, options).Graph;
        var after = new TreeSupportRouter(new LinearCollisionScene(),
                GrowthRuleSet.FromConfig(new SupportConfig()))
            .Route(tips, options).Graph;

        Assert.Equal(
            before.Nodes.Select(node => (node.Id, node.Type, node.Position, node.TipDiameter)),
            after.Nodes.Select(node => (node.Id, node.Type, node.Position, node.TipDiameter)));
        Assert.Equal(
            before.Segments.Select(segment =>
                (segment.Id, segment.Type, segment.NodeA, segment.NodeB, segment.Diameter)),
            after.Segments.Select(segment =>
                (segment.Id, segment.Type, segment.NodeA, segment.NodeB, segment.Diameter)));
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
        var rule = new TaperGrowthRule { TipLength = 3, TipToPillarDiameterRatio = 0.5f };
        var context = Context(GrowthOperation.Tip);
        context.Diameter = 0.6f;

        rule.Evaluate(context);

        Assert.Equal(3, context.TipLength);
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
