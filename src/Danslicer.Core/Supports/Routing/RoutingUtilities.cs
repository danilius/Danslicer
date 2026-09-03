using System.Numerics;

namespace Danslicer.Core.Supports.Routing;

internal static class RoutingUtilities
{
    // A degenerate routing normal most commonly belongs to an underside contact. Treating its
    // inward direction as +Z makes the graph's negated, outward contact normal point downward.
    public static Vector3 SafeInwardNormal(Vector3 normal) => normal.LengthSquared() > 1e-12f
        ? Vector3.Normalize(normal) : Vector3.UnitZ;

    public static IReadOnlyList<RoutingTip> AddReinforcementTips(IEnumerable<RoutingTip> tips,
        GrowthRuleSet rules, int seed)
    {
        var result = tips.ToList();
        var rule = rules.Find<ReinforceGrowthRule>();
        if (rule is not { Enabled: true } || rule.Count == 0 || result.Count == 0) return result;
        ArgumentOutOfRangeException.ThrowIfNegative(rule.Count);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rule.RingRadius);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rule.RingDiameterMultiplier);

        var selected = SelectSeeds(result, rule.SeedSelector);
        var random = new Random(seed);
        foreach (var routingSeed in selected)
        {
            var angleOffset = random.NextSingle() * MathF.Tau;
            for (var index = 0; index < rule.Count; index++)
            {
                var angle = angleOffset + index * MathF.Tau / rule.Count;
                var offset = new Vector3(MathF.Cos(angle), MathF.Sin(angle), 0) * rule.RingRadius;
                result.Add(routingSeed with
                {
                    SurfacePoint = routingSeed.SurfacePoint + offset,
                    TipDiameter = routingSeed.TipDiameter * rule.RingDiameterMultiplier,
                    IsCritical = false,
                    IsObjectLowest = false,
                    IsRegionLowest = false,
                });
            }
        }
        return result;
    }

    private static IReadOnlyList<RoutingTip> SelectSeeds(IReadOnlyList<RoutingTip> tips,
        ReinforceSeedSelector selector)
    {
        if (selector == ReinforceSeedSelector.CriticalTips)
            return tips.Where(tip => tip.IsCritical).ToList();

        var marked = selector == ReinforceSeedSelector.LowestPointOfObject
            ? tips.Where(tip => tip.IsObjectLowest).ToList()
            : tips.Where(tip => tip.IsRegionLowest).ToList();
        if (marked.Count > 0) return new[] { Lowest(marked) };
        return new[] { Lowest(tips) };
    }

    private static RoutingTip Lowest(IEnumerable<RoutingTip> tips) => tips
        .OrderBy(tip => tip.SurfacePoint.Z)
        .ThenBy(tip => tip.SurfacePoint.X)
        .ThenBy(tip => tip.SurfacePoint.Y)
        .First();
}

internal sealed class DeterministicIds
{
    private readonly Random _random;

    public DeterministicIds(int seed) => _random = new Random(seed);

    public Guid Next()
    {
        Span<byte> bytes = stackalloc byte[16];
        _random.NextBytes(bytes);
        return new Guid(bytes);
    }
}

internal readonly record struct RoutingClearance(
    float ModelDistance,
    float KeepCleanDistance,
    IReadOnlySet<object>? KeepCleanTags)
{
    public static RoutingClearance From(GrowthRuleSet rules, IReadOnlySet<object>? keepCleanTags)
    {
        var rule = rules.Find<ClearanceGrowthRule>();
        return rule is { Enabled: true }
            ? new RoutingClearance(rule.DistanceFromModel, rule.DistanceFromKeepCleanFaces,
                keepCleanTags)
            : new RoutingClearance(0, 0, keepCleanTags);
    }

    public bool PillarIsClear(ICollisionScene scene, Vector3 start, Vector3 end,
        float physicalRadius, Func<object?, bool>? obstacleFilter = null)
    {
        if (scene.IntersectsCapsule(start, end, physicalRadius + ModelDistance, obstacleFilter))
            return false;
        if (KeepCleanTags is null || KeepCleanTags.Count == 0 ||
            KeepCleanDistance <= ModelDistance) return true;

        var keepCleanTags = KeepCleanTags;
        return !scene.IntersectsCapsule(start, end, physicalRadius + KeepCleanDistance,
            tag => tag is not null && keepCleanTags.Contains(tag) &&
                (obstacleFilter is null || obstacleFilter(tag)));
    }
}
