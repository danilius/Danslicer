using System.Numerics;

namespace Danslicer.Core.Supports.Routing;

internal static class RoutingUtilities
{
    // A degenerate routing normal most commonly belongs to an underside contact. Treating its
    // inward direction as +Z makes the graph's negated, outward contact normal point downward.
    public static Vector3 SafeInwardNormal(Vector3 normal) => normal.LengthSquared() > 1e-12f
        ? Vector3.Normalize(normal) : Vector3.UnitZ;

    public static IReadOnlyList<RoutingTip> AddReinforcementTips(IEnumerable<RoutingTip> tips,
        GrowthRuleSet rules, ICollisionScene obstacles, int seed)
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
                var unprojected = routingSeed.SurfacePoint + offset;
                var inward = SafeInwardNormal(routingSeed.InwardSurfaceNormal);
                var hit = ProjectToSurface(unprojected, inward, rule.RingRadius, obstacles);
                if (hit is null) continue;
                var projectedInward = -hit.Value.SurfaceNormal;
                if (Vector3.Dot(projectedInward, inward) < 0) projectedInward = -projectedInward;
                result.Add(routingSeed with
                {
                    SurfacePoint = hit.Value.Point,
                    InwardSurfaceNormal = projectedInward,
                    TipDiameter = routingSeed.TipDiameter * rule.RingDiameterMultiplier,
                    IsCritical = false,
                    IsObjectLowest = false,
                    IsRegionLowest = false,
                });
            }
        }
        return result;
    }

    /// <summary>
    /// Projects a horizontal Reinforce-ring sample back onto triangle geometry. Raycast deliberately
    /// ignores support capsules, so an existing support can never steal a model contact. Trying both
    /// directions also handles samples which begin just inside a sloping surface.
    /// </summary>
    private static ObstacleRayHit? ProjectToSurface(Vector3 point, Vector3 inward,
        float maxDistance, ICollisionScene obstacles)
    {
        const float epsilon = 1e-3f;
        var into = obstacles.Raycast(point - inward * epsilon, inward, maxDistance + epsilon);
        var outward = obstacles.Raycast(point + inward * epsilon, -inward, maxDistance + epsilon);
        if (into is null) return outward;
        if (outward is null) return into;
        return Vector3.DistanceSquared(point, into.Value.Point) <=
               Vector3.DistanceSquared(point, outward.Value.Point) ? into : outward;
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
