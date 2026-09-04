using System.Numerics;

namespace Danslicer.Core.Supports.Routing;

internal static class RoutingUtilities
{
    // A degenerate routing normal most commonly belongs to an underside contact. Treating its
    // inward direction as +Z makes the graph's negated, outward contact normal point downward.
    public static Vector3 SafeInwardNormal(Vector3 normal) => normal.LengthSquared() > 1e-12f
        ? Vector3.Normalize(normal) : Vector3.UnitZ;

    public static void ApplyContact(SupportNode node, RoutingTip tip)
    {
        node.SurfaceNormal = -SafeInwardNormal(tip.InwardSurfaceNormal);
        node.TipDiameter = tip.TipDiameter;
        node.TipNormalLeadIn = MathF.Max(0f, tip.TipNormalLeadIn);
        node.ContactObjectId = tip.ContactObjectId;
        node.TipShape = tip.TipShape;
        node.ConeLength = tip.ConeLength;
        node.BallDiameter = tip.BallDiameter;
        node.PenetrationDepth = tip.PenetrationDepth;
    }

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
        var reinforced = new List<RoutingTip>();
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
                reinforced.Add(routingSeed with
                {
                    SurfacePoint = hit.Value.Point,
                    InwardSurfaceNormal = projectedInward,
                    TipDiameter = routingSeed.TipDiameter * rule.RingDiameterMultiplier,
                    IsCritical = false,
                    IsObjectLowest = false,
                    IsRegionLowest = false,
                    // A reinforcement ring is its own set of ordinary structural contacts, not
                    // additional members of the seed's density cluster.
                    MiniSupportOnly = false,
                    MiniClusterId = null,
                    MiniClusterCenter = null,
                });
            }
        }
        if (reinforced.Count == 0) return result;
        var seedSet = selected.ToHashSet();
        return selected.Concat(reinforced).Concat(result.Where(tip => !seedSet.Contains(tip)))
            .ToList();
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
        // Density clusters already replace one structural contact with a fine fan. Keep
        // reinforcement independent by choosing an ordinary contact whenever one exists.
        var eligible = tips.Where(tip => tip.MiniClusterId is null).ToList();
        if (eligible.Count == 0) eligible = tips.ToList();
        if (selector == ReinforceSeedSelector.CriticalTips)
            return eligible.Where(tip => tip.IsCritical).ToList();

        var marked = selector == ReinforceSeedSelector.LowestPointOfObject
            ? eligible.Where(tip => tip.IsObjectLowest).ToList()
            : eligible.Where(tip => tip.IsRegionLowest).ToList();
        if (marked.Count > 0)
        {
            // A flat underside can have many equally-low contacts while generation marks one
            // deterministic extreme. Choose the contact nearest the tied layer's centroid so the
            // ring stays on the surface instead of falling mostly beyond an outside edge.
            var markedZ = marked.Min(tip => tip.SurfacePoint.Z);
            var tied = eligible.Where(tip =>
                MathF.Abs(tip.SurfacePoint.Z - markedZ) <= 1e-4f).ToList();
            return new[] { Lowest(tied) };
        }
        return new[] { Lowest(eligible) };
    }

    private static RoutingTip Lowest(IEnumerable<RoutingTip> tips)
    {
        var ordered = tips.OrderBy(tip => tip.SurfacePoint.Z)
            .ThenBy(tip => tip.SurfacePoint.X)
            .ThenBy(tip => tip.SurfacePoint.Y).ToList();
        var minZ = ordered[0].SurfacePoint.Z;
        var tied = ordered.Where(tip => MathF.Abs(tip.SurfacePoint.Z - minZ) <= 1e-4f).ToList();
        var centroid = tied.Aggregate(Vector2.Zero,
            (sum, tip) => sum + new Vector2(tip.SurfacePoint.X, tip.SurfacePoint.Y)) / tied.Count;
        return tied.OrderBy(tip => Vector2.DistanceSquared(
                new Vector2(tip.SurfacePoint.X, tip.SurfacePoint.Y), centroid))
            .ThenBy(tip => tip.SurfacePoint.X)
            .ThenBy(tip => tip.SurfacePoint.Y)
            .First();
    }
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
