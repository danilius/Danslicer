using System.Numerics;

namespace Danslicer.Core.Supports.Routing;

public readonly record struct RoutingTip(Vector3 SurfacePoint, Vector3 InwardSurfaceNormal,
    float TipDiameter, Guid? ContactObjectId = null, bool IsCritical = false,
    bool IsObjectLowest = false, bool IsRegionLowest = false,
    SupportTipShape TipShape = SupportTipShape.Capsule,
    float ConeLength = 2f, float BallDiameter = 0f, float PenetrationDepth = 0f,
    /// <summary>Preserves island provenance for diagnostics even when priority is benchmark-disabled.</summary>
    bool IsIslandOrigin = false,
    /// <summary>Routes print-critical island contacts before all ordinary strategies.</summary>
    bool IsIslandPriority = false,
    float TipNormalLeadIn = 0f);

public enum BaseLatticeType
{
    Square,
    Hexagonal,
}

public sealed record GridRoutingOptions
{
    public BaseLatticeType Lattice { get; init; } = BaseLatticeType.Square;
    public float Spacing { get; init; } = 5;
    public Vector2 Offset { get; init; }
    public float RotationDegrees { get; init; }
    public float SnapTolerance { get; init; } = 0.25f;
    public float PillarDiameter { get; init; } = 1.2f;
    public float PlateZ { get; init; }
    public int CandidateRingCount { get; init; } = 3;
    public int Seed { get; init; } = 1;
    public SupportOrigin Origin { get; init; } = SupportOrigin.Manual;
    public bool AttachToExisting { get; init; }
    public IReadOnlySet<object>? KeepCleanObstacleTags { get; init; }
}

public enum RoutingFailureReason
{
    ContactBlocked,
    MemberCrossing,
    NoClearStep,
    NoReachableGridPoint,
    NoLanding,
    BelowPlate,
}

public readonly record struct RoutingFailure(RoutingTip Tip, RoutingFailureReason Reason);

/// <summary>
/// The graph mutation emitted by a route. Existing-context routes can replace a trunk segment
/// while joining it, so additions and removals travel together as one undoable edit.
/// </summary>
public sealed record SupportGraphEdit(
    IReadOnlyList<SupportNode> AddedNodes,
    IReadOnlyList<SupportSegment> AddedSegments,
    IReadOnlyList<SupportSegment> RemovedSegments);

public sealed record RoutingResult(SupportGraph Graph, IReadOnlyList<RoutingTip> UnroutedTips,
    IReadOnlyList<Vector3> BasePositions, float MaxLeanAngleDegrees,
    IReadOnlyList<RoutingFailure> Failures)
{
    /// <summary>
    /// Elements emitted by this route. For ordinary fresh-graph routes this is the whole graph;
    /// an existing-context route excludes unchanged context and records split segments removed
    /// from that context.
    /// </summary>
    public SupportGraphEdit Edit { get; init; } = new(
        Graph.Nodes.ToList(), Graph.Segments.ToList(), Array.Empty<SupportSegment>());
}

/// <summary>Deterministic grid-bottom-up routing into a new support graph.</summary>
public sealed class GridSupportRouter
{
    private readonly ICollisionScene _obstacles;
    private readonly GrowthRuleSet _rules;

    public GridSupportRouter(ICollisionScene obstacles, GrowthRuleSet rules)
    {
        _obstacles = obstacles;
        _rules = rules;
    }

    public RoutingResult Route(IEnumerable<RoutingTip> tips, GridRoutingOptions options,
        SupportGraph? existingGraph = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.Spacing);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.PillarDiameter);
        ArgumentOutOfRangeException.ThrowIfNegative(options.SnapTolerance);
        ArgumentOutOfRangeException.ThrowIfNegative(options.CandidateRingCount);

        var expandedTips = RoutingUtilities.AddReinforcementTips(tips, _rules, _obstacles, options.Seed);
        var orderedTips = expandedTips.Select((tip, index) => (Tip: tip, Index: index)).ToList();
        var candidates = orderedTips.Select(item =>
            (item.Tip, item.Index, Bases: CandidateBases(item.Tip.SurfacePoint, options).ToList())).ToList();
        var assignments = new List<(RoutingTip Tip, int Index, Vector3 Base, Vector3 Junction, float NeckDiameter)>();
        var attachments = new List<ExistingAssignment>();
        var unrouted = new List<RoutingTip>();
        var branchCounts = new Dictionary<Vector3, int>();
        var clearance = RoutingClearance.From(_rules, options.KeepCleanObstacleTags);
        var attachTargets = options.AttachToExisting
            ? ExistingSupportTargets.From(existingGraph)
            : Array.Empty<ExistingSupportTarget>();

        foreach (var candidate in candidates)
        {
            SupportGenerationMonitor.Check();
            var routed = false;
            foreach (var target in attachTargets
                         .Where(target => target.Node.Position.Z < candidate.Tip.SurfacePoint.Z)
                         .OrderBy(target => Vector3.DistanceSquared(candidate.Tip.SurfacePoint,
                             target.Node.Position))
                         .ThenBy(target => target.Node.Id))
            {
                SupportGenerationMonitor.Check();
                if (!TryExistingProposal(candidate.Tip, target, options, clearance,
                        out var junction, out var neckDiameter)) continue;
                attachments.Add(new ExistingAssignment(candidate.Tip, candidate.Index, junction,
                    neckDiameter, target));
                routed = true;
                break;
            }
            if (routed) continue;

            foreach (var basePosition in candidate.Bases)
            {
                SupportGenerationMonitor.Check();
                branchCounts.TryGetValue(basePosition, out var branchCount);
                if (!TryProposal(candidate.Tip, basePosition, branchCount, options, clearance,
                    out var junction, out var neckDiameter)) continue;
                assignments.Add((candidate.Tip, candidate.Index, basePosition, junction, neckDiameter));
                branchCounts[basePosition] = branchCount + 1;
                routed = true;
                break;
            }
            if (!routed) unrouted.Add(candidate.Tip);
        }

        var graph = options.AttachToExisting && existingGraph is not null
            ? existingGraph
            : new SupportGraph();
        var ids = new DeterministicIds(options.Seed);
        var bases = new List<Vector3>();
        var maxLean = 0f;
        foreach (var attachment in attachments.OrderBy(item => item.Tip.SurfacePoint.Z)
                     .ThenBy(item => item.Index))
            EmitAttachment(graph, attachment, options, ids, ref maxLean);
        foreach (var group in assignments.GroupBy(a => a.Base).OrderBy(g => g.Key.X).ThenBy(g => g.Key.Y))
        {
            SupportGenerationMonitor.Check();
            EmitGroup(graph, group.OrderBy(a => a.Junction.Z).ThenBy(a => a.Index).ToList(),
                options, ids, bases, ref maxLean);
        }
        return new RoutingResult(graph, unrouted, bases, maxLean,
            unrouted.Select(tip => new RoutingFailure(tip, RoutingFailureReason.NoClearStep)).ToList());
    }

    private bool TryExistingProposal(RoutingTip tip, ExistingSupportTarget target,
        GridRoutingOptions options, RoutingClearance clearance, out Vector3 junction,
        out float neckDiameter)
    {
        var taper = new GrowthContext
        {
            Operation = GrowthOperation.Tip,
            Start = tip.SurfacePoint,
            DesiredEnd = tip.SurfacePoint,
            End = tip.SurfacePoint,
            Diameter = options.PillarDiameter,
        };
        _rules.Evaluate(taper);
        neckDiameter = MathF.Max(0.05f, taper.Diameter);
        var neckDrop = MathF.Max(0.1f, taper.TipLength);
        junction = tip.SurfacePoint - Vector3.UnitZ * neckDrop;
        if (target.Node.Position.Z >= junction.Z - 1e-5f) return false;

        var merge = new GrowthContext
        {
            Operation = GrowthOperation.Merge,
            Start = target.Node.Position,
            DesiredEnd = junction,
            End = junction,
            Diameter = options.PillarDiameter,
            LowestTipZ = MathF.Min(tip.SurfacePoint.Z, target.LowestTipZ),
        };
        _rules.Evaluate(merge);
        if (!merge.Allowed) return false;

        var horizontal = Vector2.Distance(new(junction.X, junction.Y),
            new(target.Node.Position.X, target.Node.Position.Y));
        var branch = new GrowthContext
        {
            Operation = GrowthOperation.Branch,
            Start = junction,
            DesiredEnd = target.Node.Position,
            End = target.Node.Position,
            Diameter = options.PillarDiameter,
            DistanceToTip = horizontal,
        };
        _rules.Evaluate(branch);
        if (!branch.Allowed || Vector3.DistanceSquared(branch.End, target.Node.Position) > 1e-6f)
            return false;

        var neckRadius = neckDiameter * 0.5f + clearance.ModelDistance;
        if (!ContactSegmentIsClear(tip.SurfacePoint, junction, neckRadius)) return false;
        return clearance.PillarIsClear(_obstacles, junction, target.Node.Position,
            branch.Diameter * 0.5f, ExistingSupportTargets.ExcludingIncidentSegments(target));
    }

    private bool ContactSegmentIsClear(Vector3 tip, Vector3 junction, float radius)
    {
        var delta = tip - junction;
        var length = delta.Length();
        if (length <= radius * 2 + 0.01f) return true;
        var clearEnd = tip - delta / length * (radius * 2 + 0.01f);
        return !_obstacles.IntersectsCapsule(junction, clearEnd, radius);
    }

    private static void EmitAttachment(SupportGraph graph, ExistingAssignment attachment,
        GridRoutingOptions options, DeterministicIds ids, ref float maxLean)
    {
        var tipNode = Node(ids, SupportNodeType.Tip, attachment.Tip.SurfacePoint, options.Origin);
        RoutingUtilities.ApplyContact(tipNode, attachment.Tip);
        var junction = Node(ids, SupportNodeType.Junction, attachment.Junction, options.Origin);
        graph.AddNode(tipNode);
        graph.AddNode(junction);
        graph.AddSegment(Segment(ids, SupportSegmentType.Tip, tipNode.Id, junction.Id,
            attachment.NeckDiameter, options.Origin));
        graph.AddSegment(Segment(ids, SupportSegmentType.Branch, junction.Id,
            attachment.Target.Node.Id, options.PillarDiameter, options.Origin));
        IncludeLean(tipNode.Position, junction.Position, ref maxLean);
        IncludeLean(junction.Position, attachment.Target.Node.Position, ref maxLean);
    }

    private bool TryProposal(RoutingTip tip, Vector3 basePosition, int existingBranchCount,
        GridRoutingOptions options,
        RoutingClearance clearance, out Vector3 junction, out float neckDiameter)
    {
        var taper = new GrowthContext
        {
            Operation = GrowthOperation.Tip,
            Start = tip.SurfacePoint,
            DesiredEnd = tip.SurfacePoint,
            End = tip.SurfacePoint,
            Diameter = options.PillarDiameter,
        };
        _rules.Evaluate(taper);
        neckDiameter = MathF.Max(0.05f, taper.Diameter);
        var neckLength = MathF.Max(0.1f, taper.TipLength);
        var horizontal = Vector2.Distance(new(basePosition.X, basePosition.Y),
            new(tip.SurfacePoint.X, tip.SurfacePoint.Y));
        var junctionXy = horizontal <= options.SnapTolerance
            ? new Vector2(tip.SurfacePoint.X, tip.SurfacePoint.Y)
            : new Vector2(basePosition.X, basePosition.Y);
        var branchHorizontal = Vector2.Distance(junctionXy,
            new Vector2(tip.SurfacePoint.X, tip.SurfacePoint.Y));
        var lean = _rules.Find<LeanGrowthRule>();
        var angle = lean is { Enabled: true } ? lean.MaxAngleNearTipDegrees : 89;
        var requiredDrop = branchHorizontal / MathF.Max(0.01f, MathF.Tan(angle * MathF.PI / 180));
        var junctionZ = tip.SurfacePoint.Z - MathF.Max(neckLength, requiredDrop);
        if (junctionZ <= options.PlateZ + 0.05f)
        {
            junction = default;
            return false;
        }
        junction = new Vector3(junctionXy, junctionZ);

        var pillar = new GrowthContext
        {
            Operation = GrowthOperation.Grow,
            Start = basePosition,
            DesiredEnd = junction,
            End = junction,
            Diameter = options.PillarDiameter,
            DistanceToTip = Vector3.Distance(basePosition, tip.SurfacePoint),
        };
        _rules.Evaluate(pillar);
        if (!pillar.Allowed || Vector3.DistanceSquared(pillar.End, junction) > 1e-6f) return false;

        var branch = new GrowthContext
        {
            Operation = GrowthOperation.Branch,
            Start = junction,
            DesiredEnd = tip.SurfacePoint,
            End = tip.SurfacePoint,
            Diameter = options.PillarDiameter,
            DistanceToTip = branchHorizontal,
            ExistingBranchCount = existingBranchCount,
        };
        _rules.Evaluate(branch);
        if (!branch.Allowed || Vector3.DistanceSquared(branch.End, tip.SurfacePoint) > 1e-6f) return false;

        var physicalRadius = MathF.Max(options.PillarDiameter, neckDiameter) * 0.5f;
        if (!clearance.PillarIsClear(_obstacles, basePosition, junction, physicalRadius)) return false;

        // The last part of a neck intentionally enters the contacted surface. Query only the
        // portion that should remain clear of unrelated geometry.
        var neckVector = tip.SurfacePoint - junction;
        var neckLengthActual = neckVector.Length();
        var neckRadius = neckDiameter * 0.5f + clearance.ModelDistance;
        if (neckLengthActual > neckRadius * 2 + 0.01f)
        {
            var clearEnd = tip.SurfacePoint - neckVector / neckLengthActual * (neckRadius * 2 + 0.01f);
            if (_obstacles.IntersectsCapsule(junction, clearEnd, neckRadius)) return false;
        }
        return true;
    }

    private void EmitGroup(SupportGraph graph,
        IReadOnlyList<(RoutingTip Tip, int Index, Vector3 Base, Vector3 Junction, float NeckDiameter)> group,
        GridRoutingOptions options, DeterministicIds ids, List<Vector3> bases, ref float maxLean)
    {
        var basePosition = group[0].Base;
        var baseNode = Node(ids, SupportNodeType.Base, basePosition, options.Origin);
        graph.AddNode(baseNode);
        bases.Add(basePosition);
        var previous = baseNode;
        var shared = group.Count > 1;
        var trunkDiameter = options.PillarDiameter;
        if (shared)
        {
            var merge = new GrowthContext
            {
                Operation = GrowthOperation.Merge,
                Start = group[0].Junction,
                DesiredEnd = group[0].Junction,
                End = group[0].Junction,
                Diameter = trunkDiameter,
                LowestTipZ = group.Min(route => route.Tip.SurfacePoint.Z),
            };
            _rules.Evaluate(merge);
            if (merge.Allowed) trunkDiameter = merge.Diameter;
        }

        foreach (var route in group)
        {
            SupportGenerationMonitor.Check();
            SupportNode junction;
            if (Vector3.DistanceSquared(previous.Position, route.Junction) < 1e-8f)
            {
                junction = previous;
            }
            else
            {
                junction = Node(ids, SupportNodeType.Junction, route.Junction, options.Origin);
                graph.AddNode(junction);
                graph.AddSegment(Segment(ids, shared ? SupportSegmentType.Trunk : SupportSegmentType.Branch,
                    previous.Id, junction.Id, shared ? trunkDiameter : options.PillarDiameter, options.Origin));
                IncludeLean(previous.Position, junction.Position, ref maxLean);
                previous = junction;
            }

            var tipNode = Node(ids, SupportNodeType.Tip, route.Tip.SurfacePoint, options.Origin);
            // Routing input normals point into the model; graph contact normals point outward.
            RoutingUtilities.ApplyContact(tipNode, route.Tip);
            graph.AddNode(tipNode);
            graph.AddSegment(Segment(ids, SupportSegmentType.Tip, junction.Id, tipNode.Id,
                route.NeckDiameter, options.Origin));
            IncludeLean(junction.Position, tipNode.Position, ref maxLean);
        }
    }

    private static IEnumerable<Vector3> CandidateBases(Vector3 tip, GridRoutingOptions options)
    {
        var local = BaseLattice.WorldToLocal(new Vector2(tip.X, tip.Y), options);
        var seeds = options.Lattice == BaseLatticeType.Square
            ? BaseLattice.SquareRing(local, options.Spacing, options.CandidateRingCount)
            : BaseLattice.HexRing(local, options.Spacing, options.CandidateRingCount);
        return seeds.Select(p => BaseLattice.LocalToWorld(p, options))
            .Select(p => new Vector3(p, options.PlateZ))
            .OrderBy(p => Vector2.DistanceSquared(new(p.X, p.Y), new(tip.X, tip.Y)))
            .ThenBy(p => p.X).ThenBy(p => p.Y);
    }

    private static SupportNode Node(DeterministicIds ids, SupportNodeType type, Vector3 position,
        SupportOrigin origin) => new() { Id = ids.Next(), Type = type, Position = position, Origin = origin };

    private static SupportSegment Segment(DeterministicIds ids, SupportSegmentType type, Guid a,
        Guid b, float diameter, SupportOrigin origin) => new()
        { Id = ids.Next(), Type = type, NodeA = a, NodeB = b, Diameter = diameter, Origin = origin };

    private static void IncludeLean(Vector3 start, Vector3 end, ref float maxLean)
    {
        var delta = end - start;
        var angle = MathF.Atan2(new Vector2(delta.X, delta.Y).Length(), MathF.Abs(delta.Z))
            * 180 / MathF.PI;
        maxLean = MathF.Max(maxLean, angle);
    }

    private sealed record ExistingAssignment(RoutingTip Tip, int Index, Vector3 Junction,
        float NeckDiameter, ExistingSupportTarget Target);

}
