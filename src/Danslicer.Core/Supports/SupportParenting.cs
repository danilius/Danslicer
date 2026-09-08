using System.Numerics;
using Danslicer.Core.Commands;
using Danslicer.Core.Config;
using Danslicer.Core.Supports.Routing;

namespace Danslicer.Core.Supports;

/// <summary>What one parenting run did, for the status line.</summary>
public sealed record ParentingOutcome(int Operands, int TrunksBefore, int TrunksAfter, int Refused);

/// <summary>
/// What auto-parenting did after a placement, for the status line ("12 placed → 3 trunks"):
/// <paramref name="Trunks"/> is how many trunks now stand under the tips just placed;
/// <paramref name="Refused"/> counts the operand supports the re-route left as they were.
/// </summary>
public sealed record AutoParentingOutcome(int Placed, int Trunks, int Refused);

/// <summary>
/// Parenting (SUPPORT-GEOMETRY-SPEC "Parenting"): generation's routing applied to existing tips.
/// The operand supports are taken down and their tips routed again together with trunk
/// sharing on, so trunks merge, disappear into existing ones, or move onto the grid. A tip the
/// re-route refuses keeps the support it had. Pure planning over a graph: the document turns
/// the plan into one undo step.
/// </summary>
public static class SupportParenting
{
    /// <summary>A planned parenting: what to remove, what the router adds, and the counts.</summary>
    public sealed record ParentingPlan(
        IReadOnlyList<Guid> RemovedNodes, IReadOnlyList<Guid> RemovedSegments, SupportGraphEdit Edit,
        int Refused);

    private sealed record Step(HashSet<Guid> RemovedNodes, HashSet<Guid> RemovedSegments, SupportGraphEdit Edit,
        int Refused, int Bases);

    /// <summary>
    /// Plans a parenting of the supports containing <paramref name="operandTipIds"/> on the
    /// target. Null when there is nothing to parent (fewer than two operand supports).
    /// <paramref name="meshes"/> is the model obstacle scene.
    /// </summary>
    public static (IReadOnlyList<ParentingPlan> Plans, ParentingOutcome Outcome)? Plan(SupportGraph graph, Guid targetId,
        IReadOnlyList<Guid> operandTipIds, SupportConfig settings, ICollisionScene meshes)
    {
        var components = Components(graph, operandTipIds);
        if (components.Count < 2) return null;
        var trunksBefore = BaseCount(graph, targetId);
        var rounds = Math.Max(1, settings.ParentingRounds);

        Step? best = null;
        for (var round = 0; round < rounds; round++)
        {
            var step = Attempt(graph, targetId, components, settings, meshes, seed: round + 1, rangeFactor: 1f);
            if (best is null || step.Bases < best.Bases || (step.Bases == best.Bases && step.Refused < best.Refused))
                best = step;
        }
        var plans = new List<ParentingPlan> { ToPlan(best!) };
        var refused = best!.Refused;
        var bases = best.Bases;

        // Min tips per trunk: a new trunk left nearly alone gets one more try with double range.
        if (settings.ParentingMinTipsPerTrunk > 1)
        {
            var after = Apply(graph, best);
            var lonely = LonelyTrunkTips(after, best.Edit, settings.ParentingMinTipsPerTrunk);
            if (lonely.Count > 0)
            {
                var lonelyComponents = Components(after, lonely);
                if (lonelyComponents.Count >= 1)
                {
                    var second = Attempt(after, targetId, lonelyComponents, settings, meshes, seed: 101, rangeFactor: 2f);
                    if (second.Bases < bases)
                    {
                        plans.Add(ToPlan(second));
                        bases = second.Bases;
                        refused += second.Refused;
                    }
                }
            }
        }
        return (plans, new ParentingOutcome(components.Count, trunksBefore, bases, refused));
    }

    /// <summary>The commands that carry out a plan on <paramref name="graph"/>, in order.</summary>
    public static IReadOnlyList<IDocumentCommand> Commands(SupportGraph graph, IReadOnlyList<ParentingPlan> plans, string name)
    {
        var commands = new List<IDocumentCommand>();
        for (var i = 0; i < plans.Count; i++)
        {
            var plan = plans[i];
            // A later plan removes elements an earlier plan adds, so its removal must be built
            // when it executes, not now (the up-front lookup crashed on 2026-09-08).
            if (plan.RemovedNodes.Count > 0 || plan.RemovedSegments.Count > 0)
                commands.Add(i == 0
                    ? new RemoveSupportElementsCommand(graph, plan.RemovedNodes, plan.RemovedSegments, name)
                    : new DeferredCommand(name, () =>
                        new RemoveSupportElementsCommand(graph, plan.RemovedNodes, plan.RemovedSegments, name)));
            commands.Add(new ApplySupportGraphEditCommand(graph, plan.Edit, name));
        }
        return commands;
    }

    /// <summary>The whole supports (bracing excluded) containing the given tips, each once.</summary>
    private static List<(HashSet<Guid> Nodes, HashSet<Guid> Segments)> Components(SupportGraph graph, IEnumerable<Guid> tipIds)
    {
        var result = new List<(HashSet<Guid>, HashSet<Guid>)>();
        var seen = new HashSet<Guid>();
        foreach (var id in tipIds)
        {
            if (seen.Contains(id) || !graph.TryGetNode(id, out _)) continue;
            var component = graph.Component(id);
            seen.UnionWith(component.Nodes);
            result.Add(component);
        }
        return result;
    }

    private static Step Attempt(SupportGraph graph, Guid targetId,
        List<(HashSet<Guid> Nodes, HashSet<Guid> Segments)> components, SupportConfig settings,
        ICollisionScene meshes, int seed, float rangeFactor)
    {
        var kept = new HashSet<int>();
        while (true)
        {
            // The graph as it would be with the non-kept operand supports taken down.
            var working = Clone(graph);
            for (var i = 0; i < components.Count; i++)
            {
                if (kept.Contains(i)) continue;
                foreach (var s in components[i].Segments) if (working.TryGetSegment(s, out _)) working.RemoveSegment(s);
                foreach (var n in components[i].Nodes) if (working.TryGetNode(n, out _)) working.RemoveNode(n);
            }
            var tips = new List<(RoutingTip Tip, int Component)>();
            for (var i = 0; i < components.Count; i++)
            {
                if (kept.Contains(i)) continue;
                foreach (var id in components[i].Nodes)
                {
                    var node = graph.GetNode(id);
                    if (node.Type != SupportNodeType.Tip) continue;
                    tips.Add((new RoutingTip(node.Position, -node.SurfaceNormal, node.TipDiameter,
                        node.ContactObjectId ?? targetId, TipShape: node.TipShape, ConeLength: node.ConeLength,
                        BallDiameter: node.BallDiameter, PenetrationDepth: node.PenetrationDepth,
                        TipNormalLeadIn: node.TipNormalLeadIn), i));
                }
            }
            if (tips.Count == 0)
                return new Step([], [], new SupportGraphEdit([], [], []), components.Count, BaseCount(graph, targetId));

            // Sharing is the point of parenting, so it is on in free mode too (ShareTrunks), and
            // a route that shares trunks must see the other supports as obstacles.
            ICollisionScene obstacles = new CompositeCollisionScene(meshes, SupportScene(working));

            if (settings.ParentingHierarchical)
            {
                // Tips pair into junctions, junctions pair again, one trunk carries the lot.
                // The supports left standing offer their trunks: a junction joins one within
                // range before dropping a trunk of its own.
                var (edit, refusedTips) = HierarchicalParenting.Build(tips.Select(t => t.Tip).ToList(),
                    settings, obstacles, SupportOrigin.ManualFor(targetId), seed, rangeFactor,
                    HierarchicalParenting.ExistingTrunks(working, targetId));
                if (refusedTips.Count > 0)
                {
                    var before = kept.Count;
                    foreach (var unrouted in refusedTips)
                        foreach (var (tip, component) in tips)
                            if (tip.SurfacePoint == unrouted.SurfacePoint) kept.Add(component);
                    if (kept.Count > before && kept.Count < components.Count) continue;
                    if (kept.Count == components.Count)
                        return new Step([], [], new SupportGraphEdit([], [], []), components.Count, BaseCount(graph, targetId));
                }
                return Finish(edit);
            }
            var rules = GrowthRuleSet.FromConfig(settings);
            // Aggressive parenting (user screen test 2026-09-08): a trunk may carry more branches
            // than the growth rule's default when the user asks for it.
            if (settings.ParentingMaxBranchesPerTrunk > 0 && rules.Find<BranchGrowthRule>() is { } branchRule)
                branchRule.MaxBranchesPerTrunk = settings.ParentingMaxBranchesPerTrunk;
            var router = new TreeSupportRouter(obstacles, rules);
            var options = new TreeRoutingOptions
            {
                TrunkDiameter = settings.TrunkDiameter,
                BranchDiameter = settings.BranchDiameter,
                MaxMemberAngleDegrees = settings.ParentingMaxBranchAngle > 0 ? settings.ParentingMaxBranchAngle : settings.MemberAngleDegrees,
                TipMemberLength = settings.TipMemberLength,
                MaxBranchLength = (settings.ParentingMaxBranchLength > 0 ? settings.ParentingMaxBranchLength : settings.MaxBranchLength) * rangeFactor,
                PreferExistingTrunks = true,
                ExistingTrunkBranchRange = (settings.ParentingTrunkRange > 0 ? settings.ParentingTrunkRange : settings.ExistingTrunkBranchRange) * rangeFactor,
                IgnoreExistingSupports = false,
                ShareTrunks = true,
                MaxConeBendDegrees = settings.ParentingMaxConeBend,
                MinMemberSeparationMm = settings.MinMemberSeparationMm,
                UseBaseGrid = settings.UseBaseGrid,
                BaseGridPitch = settings.BaseGridPitch,
                BaseShape = settings.BaseShape,
                BaseDiameter = settings.BaseDiameter,
                BaseHeight = settings.BaseHeight,
                BaseConeHeight = settings.BaseConeHeight,
                Seed = seed,
                Origin = SupportOrigin.ManualFor(targetId),
            };
            var result = router.Route(tips.Select(t => t.Tip).ToList(), options, working);

            if (result.UnroutedTips.Count > 0)
            {
                // A refused tip keeps its support: put its whole support back and route the rest.
                var before = kept.Count;
                foreach (var unrouted in result.UnroutedTips)
                    foreach (var (tip, component) in tips)
                        if (tip.SurfacePoint == unrouted.SurfacePoint) kept.Add(component);
                if (kept.Count > before && kept.Count < components.Count) continue;
                if (kept.Count == components.Count)
                    return new Step([], [], new SupportGraphEdit([], [], []), components.Count, BaseCount(graph, targetId));
            }

            return Finish(result.Edit);
        }

        Step Finish(SupportGraphEdit edit)
        {
            var removedNodes = new HashSet<Guid>();
            var removedSegments = new HashSet<Guid>();
            for (var i = 0; i < components.Count; i++)
            {
                if (kept.Contains(i)) continue;
                removedNodes.UnionWith(components[i].Nodes);
                removedSegments.UnionWith(components[i].Segments);
            }
            var bases = BaseCount(graph, targetId)
                - removedNodes.Count(id => graph.GetNode(id).Type == SupportNodeType.Base)
                + edit.AddedNodes.Count(n => n.Type == SupportNodeType.Base);
            return new Step(removedNodes, removedSegments, edit, kept.Count, bases);
        }
    }

    private static ParentingPlan ToPlan(Step step) =>
        new(step.RemovedNodes.ToList(), step.RemovedSegments.ToList(), step.Edit, step.Refused);

    /// <summary>A clone of <paramref name="graph"/> with <paramref name="step"/> applied.</summary>
    private static SupportGraph Apply(SupportGraph graph, Step step)
    {
        var after = Clone(graph);
        foreach (var s in step.RemovedSegments) if (after.TryGetSegment(s, out _)) after.RemoveSegment(s);
        foreach (var n in step.RemovedNodes) if (after.TryGetNode(n, out _)) after.RemoveNode(n);
        foreach (var s in step.Edit.RemovedSegments) if (after.TryGetSegment(s.Id, out _)) after.RemoveSegment(s.Id);
        foreach (var n in step.Edit.AddedNodes) after.AddNode(n.Clone());
        foreach (var s in step.Edit.AddedSegments) after.AddSegment(s.Clone());
        return after;
    }

    /// <summary>Tips of trunks this parenting added that carry fewer than <paramref name="minTips"/>.</summary>
    private static List<Guid> LonelyTrunkTips(SupportGraph after, SupportGraphEdit edit, int minTips)
    {
        var result = new List<Guid>();
        foreach (var added in edit.AddedNodes)
        {
            if (added.Type != SupportNodeType.Base || !after.TryGetNode(added.Id, out _)) continue;
            var component = after.Component(added.Id);
            var tips = component.Nodes.Where(id => after.GetNode(id).Type == SupportNodeType.Tip).ToList();
            if (tips.Count < minTips) result.AddRange(tips);
        }
        return result;
    }

    private static int BaseCount(SupportGraph graph, Guid targetId) =>
        graph.Nodes.Count(n => n.Type == SupportNodeType.Base &&
            (graph.OwningObjectId(n.Id) is not { } owner || owner == targetId));

    private static LinearCollisionScene SupportScene(SupportGraph graph)
    {
        var scene = new LinearCollisionScene();
        scene.AddSupportGraph(graph);
        return scene;
    }

    private static SupportGraph Clone(SupportGraph source)
    {
        var clone = new SupportGraph();
        foreach (var node in source.Nodes) clone.AddNode(node.Clone());
        foreach (var segment in source.Segments) clone.AddSegment(segment.Clone());
        return clone;
    }
}
