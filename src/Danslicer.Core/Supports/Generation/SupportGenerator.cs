using Danslicer.Core.Geometry;
using Danslicer.Core.Supports.Routing;

namespace Danslicer.Core.Supports.Generation;

/// <summary>The outcome of one generation pass: what was placed and how routing fared.</summary>
public sealed record GenerationResult(
    IReadOnlyList<TipCandidate> Candidates,
    RoutingResult Routing);

/// <summary>
/// Joins the two generation stages (DESIGN.md §8.4): tip placement produces candidates on the
/// region's faces, grid routing connects them to the plate. A pure function of its inputs and
/// the seed (§8.7); the caller owns transforms (the mesh is world space) and the collision
/// scene's contents (every scene object plus the existing graph, per §8.6).
/// </summary>
public static class SupportGenerator
{
    public static GenerationResult Generate(
        Mesh mesh,
        IReadOnlySet<int> regionFaces,
        TipPlacementParameters placement,
        GridRoutingOptions routing,
        GrowthRuleSet rules,
        ICollisionScene obstacles,
        SupportGraph? existingGraph = null,
        IReadOnlySet<int>? keepCleanFaces = null,
        int seed = 0,
        IProgress<SupportGenerationProgress>? progress = null)
    {
        // Grid routing expects tips on lattice verticals. When the caller has not already
        // opted into (or out of) grid projection, pass the lattice into placement so Poisson
        // overhang sampling is replaced by grid hits. Islands and minima still run.
        var effectivePlacement = placement.Grid is null
            ? placement with { Grid = routing }
            : placement;
        var candidates = TipPlacer.Place(mesh, regionFaces, effectivePlacement, existingGraph, keepCleanFaces, seed);
        progress?.Report(new SupportGenerationProgress(0.5, "Tips placed", candidates.Count, candidates.Count));

        // Both sides of this mapping speak the inward (penetration) normal, so it passes through;
        // the router flips to the graph's outward convention when it creates the tip node.
        var lowestRegion = candidates.OrderBy(candidate => candidate.Point.Z)
            .ThenBy(candidate => candidate.Point.X).ThenBy(candidate => candidate.Point.Y)
            .Select(candidate => (TipCandidate?)candidate).FirstOrDefault();
        var tips = candidates.Select(c => new RoutingTip(c.Point, c.InwardNormal, c.TipDiameter,
            IsObjectLowest: lowestRegion is { } lowestObject &&
                MathF.Abs(lowestObject.Point.Z - mesh.Bounds.Min.Z) <= 1e-4f && c.Equals(lowestObject),
            IsRegionLowest: lowestRegion is { } lowest && c.Equals(lowest),
            TipShape: c.TipShape, ConeLength: c.ConeLength, BallDiameter: c.BallDiameter,
            PenetrationDepth: c.PenetrationDepth));

        var router = new GridSupportRouter(obstacles, rules);
        var result = router.Route(tips, routing with { Seed = seed }, existingGraph);
        progress?.Report(new SupportGenerationProgress(1, "Tips routed",
            candidates.Count - result.UnroutedTips.Count, candidates.Count));
        return new GenerationResult(candidates, result);
    }

    /// <summary>
    /// Spec-shaped generation (docs/SUPPORT-GEOMETRY-SPEC.md): Poisson/island/minimum tip
    /// placement (no lattice projection) routed by <see cref="TreeSupportRouter"/> into
    /// base/trunk/branch/tip trees. The default path behind Generate Supports.
    /// </summary>
    public static GenerationResult GenerateTree(
        Mesh mesh,
        IReadOnlySet<int> regionFaces,
        TipPlacementParameters placement,
        TreeRoutingOptions routing,
        GrowthRuleSet rules,
        ICollisionScene obstacles,
        SupportGraph? existingGraph = null,
        IReadOnlySet<int>? keepCleanFaces = null,
        int seed = 0,
        IProgress<SupportGenerationProgress>? progress = null)
    {
        var candidates = TipPlacer.Place(mesh, regionFaces, placement, existingGraph, keepCleanFaces, seed);
        progress?.Report(new SupportGenerationProgress(0.5, "Tips placed", candidates.Count, candidates.Count));

        var lowestRegion = candidates.OrderBy(candidate => candidate.Point.Z)
            .ThenBy(candidate => candidate.Point.X).ThenBy(candidate => candidate.Point.Y)
            .Select(candidate => (TipCandidate?)candidate).FirstOrDefault();
        var tips = candidates.Select(c => new RoutingTip(c.Point, c.InwardNormal, c.TipDiameter,
            IsObjectLowest: lowestRegion is { } lowestObject &&
                MathF.Abs(lowestObject.Point.Z - mesh.Bounds.Min.Z) <= 1e-4f && c.Equals(lowestObject),
            IsRegionLowest: lowestRegion is { } lowest && c.Equals(lowest),
            TipShape: c.TipShape, ConeLength: c.ConeLength, BallDiameter: c.BallDiameter,
            PenetrationDepth: c.PenetrationDepth,
            MiniSupportOnly: c.Strategy == TipStrategy.MiniIsland));

        var router = new TreeSupportRouter(obstacles, rules);
        var result = router.Route(tips, routing with { Seed = seed });
        progress?.Report(new SupportGenerationProgress(1, "Tips routed",
            candidates.Count - result.UnroutedTips.Count, candidates.Count));
        return new GenerationResult(candidates, result);
    }
}

public readonly record struct SupportGenerationProgress(double Fraction, string Stage,
    int Completed, int Total);
