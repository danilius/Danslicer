using System.Numerics;
using Danslicer.Core.Config;

namespace Danslicer.Core.Supports.Routing;

public enum GrowthOperation
{
    Grow,
    Branch,
    Merge,
    Tip,
    Land,
    Reinforce,
}

/// <summary>Mutable proposal passed through enabled growth rules in profile order.</summary>
public sealed class GrowthContext
{
    public required GrowthOperation Operation { get; init; }
    public required Vector3 Start { get; init; }
    public required Vector3 DesiredEnd { get; init; }
    public Vector3 End { get; set; }
    public float Diameter { get; set; }
    public float DistanceToTip { get; set; }
    /// <summary>The lowest tip served by a proposed merge, in world Z.</summary>
    public float LowestTipZ { get; set; }
    public int ExistingBranchCount { get; set; }
    public int BranchLevel { get; set; }
    public float Slenderness { get; set; }
    public bool Allowed { get; set; } = true;
    public bool AllowModelLanding { get; set; }
    public float LandingPadDiameter { get; set; }
    public float TipLength { get; set; }
}

/// <summary>A serializable-style growth policy. Rules are evaluated in list order.</summary>
public interface IGrowthRule
{
    string Name { get; }
    bool Enabled { get; set; }
    void Evaluate(GrowthContext context);
}

public sealed class GrowthRuleSet
{
    private readonly List<IGrowthRule> _rules;
    public IReadOnlyList<IGrowthRule> Rules => _rules;

    public GrowthRuleSet(IEnumerable<IGrowthRule> rules) => _rules = rules.ToList();

    public GrowthContext Evaluate(GrowthContext context)
    {
        foreach (var rule in _rules)
        {
            if (rule.Enabled) rule.Evaluate(context);
            if (!context.Allowed) break;
        }
        return context;
    }

    public T? Find<T>() where T : class, IGrowthRule => _rules.OfType<T>().FirstOrDefault();

    /// <summary>Builds the rule policy consumed by support generation from its saved settings.</summary>
    public static GrowthRuleSet FromConfig(SupportConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        var rules = Default;
        rules.Find<TaperGrowthRule>()!.TipLength = config.TipMemberLength;
        var reinforce = rules.Find<ReinforceGrowthRule>()!;
        reinforce.Enabled = config.ReinforceEnabled;
        reinforce.SeedSelector = config.ReinforceSeedSelector;
        reinforce.Count = config.ReinforceCount;
        reinforce.RingRadius = config.ReinforceRingRadius;
        reinforce.RingDiameterMultiplier = config.ReinforceRingDiameterMultiplier;
        return rules;
    }

    public static GrowthRuleSet Default => new(new IGrowthRule[]
    {
        new LeanGrowthRule(),
        new BranchGrowthRule(),
        new MergeGrowthRule(),
        new ReinforceGrowthRule(),
        new TaperGrowthRule(),
        new ClearanceGrowthRule(),
        new LandGrowthRule(),
    });
}

public enum ReinforceSeedSelector
{
    LowestPointOfObject,
    LowestPointOfRegion,
    CriticalTips,
}

public sealed class ReinforceGrowthRule : IGrowthRule
{
    public string Name => "Reinforce";
    public bool Enabled { get; set; }
    public ReinforceSeedSelector SeedSelector { get; set; } = ReinforceSeedSelector.LowestPointOfObject;
    public int Count { get; set; } = 3;
    public float RingRadius { get; set; } = 2;
    public float RingDiameterMultiplier { get; set; } = 1.25f;

    // Tip expansion is consumed before routing because it creates complete routing inputs rather
    // than changing one segment proposal.
    public void Evaluate(GrowthContext context) { }
}

public sealed class LeanGrowthRule : IGrowthRule
{
    public string Name => "Lean";
    public bool Enabled { get; set; } = true;
    public float MaxAngleDegrees { get; set; } = 45;
    public float MaxAngleNearTipDegrees { get; set; } = 35;
    public float NearTipDistance { get; set; } = 3;

    public void Evaluate(GrowthContext context)
    {
        if (context.Operation is not (GrowthOperation.Grow or GrowthOperation.Branch or GrowthOperation.Tip))
            return;
        var delta = context.DesiredEnd - context.Start;
        var vertical = MathF.Abs(delta.Z);
        var angle = context.DistanceToTip <= NearTipDistance
            ? MaxAngleNearTipDegrees : MaxAngleDegrees;
        var maxHorizontal = vertical * MathF.Tan(angle * MathF.PI / 180);
        var horizontal = new Vector2(delta.X, delta.Y);
        if (horizontal.Length() <= maxHorizontal || horizontal.LengthSquared() <= 1e-12f) return;
        horizontal = Vector2.Normalize(horizontal) * maxHorizontal;
        context.End = context.Start + new Vector3(horizontal, delta.Z);
    }
}

public sealed class BranchGrowthRule : IGrowthRule
{
    public string Name => "Branch";
    public bool Enabled { get; set; } = true;
    public float TriggerDistance { get; set; } = 6;
    public int MaxBranchesPerTrunk { get; set; } = 6;
    public float DiameterStepPerLevel { get; set; } = 0.15f;

    public void Evaluate(GrowthContext context)
    {
        if (context.Operation != GrowthOperation.Branch) return;
        if (context.DistanceToTip > TriggerDistance || context.ExistingBranchCount >= MaxBranchesPerTrunk)
            context.Allowed = false;
        else
            context.Diameter += context.BranchLevel * DiameterStepPerLevel;
    }
}

public sealed class MergeGrowthRule : IGrowthRule
{
    public string Name => "Merge";
    public bool Enabled { get; set; } = true;
    public float TriggerDistance { get; set; } = 2;
    public float ResultingTrunkDiameter { get; set; } = 1.8f;
    public float MinHeightAboveTipsToMerge { get; set; } = 2;

    public void Evaluate(GrowthContext context)
    {
        if (context.Operation != GrowthOperation.Merge) return;
        var distanceBelowTips = context.LowestTipZ - context.Start.Z;
        if (Vector2.Distance(new(context.Start.X, context.Start.Y), new(context.DesiredEnd.X, context.DesiredEnd.Y))
            > TriggerDistance || distanceBelowTips < MinHeightAboveTipsToMerge)
            context.Allowed = false;
        else
            context.Diameter = MathF.Max(context.Diameter, ResultingTrunkDiameter);
    }
}

public sealed class TaperGrowthRule : IGrowthRule
{
    public string Name => "Taper";
    public bool Enabled { get; set; } = true;
    public float TipLength { get; set; } = 2;
    public float TipToPillarDiameterRatio { get; set; } = 0.65f;

    public void Evaluate(GrowthContext context)
    {
        if (context.Operation != GrowthOperation.Tip) return;
        context.TipLength = TipLength;
        context.Diameter *= TipToPillarDiameterRatio;
    }
}

public sealed class ClearanceGrowthRule : IGrowthRule
{
    public string Name => "Clearance";
    public bool Enabled { get; set; } = true;
    public float DistanceFromModel { get; set; } = 0.25f;
    public float DistanceFromKeepCleanFaces { get; set; } = 1;

    // Clearance is consumed by the router as an inflated query radius. Keeping the rule's
    // evaluation side-effect free makes it reusable with other routing strategies.
    public void Evaluate(GrowthContext context) { }
}

public sealed class LandGrowthRule : IGrowthRule
{
    public string Name => "Land";
    public bool Enabled { get; set; }
    public bool AllowLandingOnModel { get; set; }
    public float MinLandingAngleDegrees { get; set; } = 60;
    public float LandingPadDiameter { get; set; } = 2.5f;

    public void Evaluate(GrowthContext context)
    {
        if (context.Operation != GrowthOperation.Land) return;
        context.AllowModelLanding = AllowLandingOnModel;
        context.LandingPadDiameter = LandingPadDiameter;
    }
}
