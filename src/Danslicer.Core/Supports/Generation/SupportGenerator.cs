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
        int seed = 0)
    {
        var candidates = TipPlacer.Place(mesh, regionFaces, placement, existingGraph, keepCleanFaces, seed);

        // Both sides of this mapping speak the inward (penetration) normal, so it passes through;
        // the router flips to the graph's outward convention when it creates the tip node.
        var tips = candidates.Select(c => new RoutingTip(c.Point, c.InwardNormal, c.TipDiameter));

        var router = new GridSupportRouter(obstacles, rules);
        var result = router.Route(tips, routing with { Seed = seed }, existingGraph);
        return new GenerationResult(candidates, result);
    }
}
