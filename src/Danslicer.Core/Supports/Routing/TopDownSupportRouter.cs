using System.Numerics;

namespace Danslicer.Core.Supports.Routing;

public sealed record TopDownRoutingOptions
{
    public float StepHeight { get; init; } = 2;
    public float PillarDiameter { get; init; } = 1.2f;
    public float PlateZ { get; init; }
    public int DetourRings { get; init; } = 3;
    public int DirectionsPerRing { get; init; } = 12;
    public int Seed { get; init; } = 1;
    public SupportOrigin Origin { get; init; } = SupportOrigin.Manual;
    public bool AttachToExisting { get; init; }
    public IReadOnlySet<object>? KeepCleanObstacleTags { get; init; }
}

/// <summary>
/// Deterministic classic tree routing. Tips descend in bounded, lean-limited steps, detour around
/// obstacles, and join an earlier route when the merge and branch rules permit.
/// </summary>
public sealed class TopDownSupportRouter
{
    private const float Epsilon = 1e-5f;
    private readonly ICollisionScene _obstacles;
    private readonly GrowthRuleSet _rules;

    public TopDownSupportRouter(ICollisionScene obstacles, GrowthRuleSet rules)
    {
        _obstacles = obstacles;
        _rules = rules;
    }

    public RoutingResult Route(IEnumerable<RoutingTip> tips, TopDownRoutingOptions options,
        SupportGraph? existingGraph = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.StepHeight);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.PillarDiameter);
        ArgumentOutOfRangeException.ThrowIfNegative(options.DetourRings);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.DirectionsPerRing);

        var attachTargets = options.AttachToExisting
            ? ExistingSupportTargets.From(existingGraph)
            : Array.Empty<ExistingSupportTarget>();
        var graph = options.AttachToExisting && existingGraph is not null
            ? existingGraph
            : new SupportGraph();
        var originalNodeIds = graph.Nodes.Select(node => node.Id).ToHashSet();
        var ids = new DeterministicIds(options.Seed);
        var routeNodes = new List<SupportNode>();
        var lowestTipByNode = new Dictionary<Guid, float>();
        var generatedCapsules = new List<GeneratedCapsule>();
        var unrouted = new List<RoutingTip>();
        var failures = new List<RoutingFailure>();
        var maxLean = 0f;
        var angleOffset = new Random(options.Seed).NextSingle() * MathF.Tau;
        var clearance = RoutingClearance.From(_rules, options.KeepCleanObstacleTags);

        var expandedTips = RoutingUtilities.AddReinforcementTips(tips, _rules, _obstacles, options.Seed);
        foreach (var item in expandedTips.Select((tip, index) => (Tip: tip, Index: index))
                     .OrderByDescending(item => item.Tip.SurfacePoint.Z).ThenBy(item => item.Index))
        {
            var proposal = Propose(item.Tip, options, routeNodes, lowestTipByNode,
                generatedCapsules, attachTargets, clearance, angleOffset, out var failureReason);
            if (proposal is null)
            {
                unrouted.Add(item.Tip);
                failures.Add(new RoutingFailure(item.Tip, failureReason));
                continue;
            }

            Emit(graph, item.Tip, proposal, options, ids, routeNodes, lowestTipByNode,
                generatedCapsules, ref maxLean);
        }

        var bases = graph.Nodes.Where(node => node.Type == SupportNodeType.Base)
            .Where(node => !originalNodeIds.Contains(node.Id))
            .Select(node => node.Position).OrderBy(p => p.X).ThenBy(p => p.Y).ToList();
        return new RoutingResult(graph, unrouted, bases, maxLean, failures);
    }

    private RouteProposal? Propose(RoutingTip tip, TopDownRoutingOptions options,
        IReadOnlyList<SupportNode> routeNodes, IReadOnlyDictionary<Guid, float> lowestTipByNode,
        IReadOnlyList<GeneratedCapsule> generatedCapsules,
        IReadOnlyList<ExistingSupportTarget> attachTargets, RoutingClearance clearance,
        float angleOffset, out RoutingFailureReason failureReason)
    {
        failureReason = RoutingFailureReason.NoClearStep;
        if (tip.SurfacePoint.Z <= options.PlateZ + Epsilon)
        {
            failureReason = RoutingFailureReason.BelowPlate;
            return null;
        }

        var taper = new GrowthContext
        {
            Operation = GrowthOperation.Neck,
            Start = tip.SurfacePoint,
            DesiredEnd = tip.SurfacePoint,
            End = tip.SurfacePoint,
            Diameter = options.PillarDiameter,
        };
        _rules.Evaluate(taper);
        var neckDiameter = MathF.Max(0.05f, taper.Diameter);
        var neckLength = MathF.Max(0.1f, taper.NeckLength);
        var outward = -RoutingUtilities.SafeInwardNormal(tip.InwardSurfaceNormal);
        // Down-facing steep contacts must leave along the surface normal before turning toward
        // the plate. A vertical departure embeds the neck capsule in the contact face. Up-facing
        // contacts retain the vertical proposal so they cannot escape through the top of a solid.
        var departure = outward.Z < -Epsilon ? outward : -Vector3.UnitZ;
        if (departure.Z < -Epsilon)
            neckLength = MathF.Min(neckLength,
                (tip.SurfacePoint.Z - options.PlateZ) / -departure.Z);
        var first = tip.SurfacePoint + departure * neckLength;
        var neckRadius = neckDiameter * 0.5f + clearance.ModelDistance;
        if (!ContactSegmentIsClear(tip.SurfacePoint, first, neckRadius) ||
            HitsGenerated(tip.SurfacePoint, first, neckRadius, generatedCapsules, null))
        {
            failureReason = RoutingFailureReason.ContactBlocked;
            return null;
        }

        var points = new List<Vector3> { first };
        MergeTarget? mergeTarget = null;
        var current = first;
        var maxSteps = (int)MathF.Ceiling((tip.SurfacePoint.Z - options.PlateZ) / options.StepHeight) + 2;
        for (var step = 0; step < maxSteps && current.Z > options.PlateZ + Epsilon; step++)
        {
            mergeTarget = FindMerge(current, tip.SurfacePoint.Z, options, routeNodes,
                lowestTipByNode, generatedCapsules, attachTargets, clearance);
            if (mergeTarget is not null) break;

            var landing = FindLanding(current, options, generatedCapsules, clearance,
                out var landingRejected);
            if (landing is not null)
            {
                points.Add(landing.Hit.Point);
                return new RouteProposal(points, neckDiameter, null, landing);
            }

            var nextZ = MathF.Max(options.PlateZ, current.Z - options.StepHeight);
            var next = FindClearStep(current, nextZ, tip.SurfacePoint, options,
                generatedCapsules, clearance, angleOffset + step * 0.381966f);
            if (next is null)
            {
                failureReason = landingRejected
                    ? RoutingFailureReason.NoLanding
                    : RoutingFailureReason.NoClearStep;
                return null;
            }
            current = next.Value;
            points.Add(current);
        }

        if (mergeTarget is null && current.Z > options.PlateZ + Epsilon) return null;
        return new RouteProposal(points, neckDiameter, mergeTarget, null);
    }

    private ModelLanding? FindLanding(Vector3 current, TopDownRoutingOptions options,
        IReadOnlyList<GeneratedCapsule> generatedCapsules, RoutingClearance clearance,
        out bool rejected)
    {
        rejected = false;
        var landRule = _rules.Find<LandGrowthRule>();
        if (landRule is not { Enabled: true, AllowLandingOnModel: true }) return null;
        var maxDistance = MathF.Min(options.StepHeight, current.Z - options.PlateZ);
        var hit = _obstacles.Raycast(current, -Vector3.UnitZ, maxDistance);
        if (hit is null || hit.Value.SurfaceNormal.Z <= 0) return null;
        rejected = true;
        if (clearance.KeepCleanTags is not null && hit.Value.Tag is not null &&
            clearance.KeepCleanTags.Contains(hit.Value.Tag)) return null;

        var landingAngle = MathF.Asin(Math.Clamp(hit.Value.SurfaceNormal.Z, 0, 1))
            * 180 / MathF.PI;
        var context = new GrowthContext
        {
            Operation = GrowthOperation.Land,
            Start = current,
            DesiredEnd = hit.Value.Point,
            End = hit.Value.Point,
            Diameter = options.PillarDiameter,
        };
        _rules.Evaluate(context);
        if (!context.Allowed || !context.AllowModelLanding) return null;

        var angleShortfall = MathF.Max(0, landRule.MinLandingAngleDegrees - landingAngle);
        var steepnessScale = 1 + angleShortfall / MathF.Max(1, landRule.MinLandingAngleDegrees);
        var padDiameter = MathF.Max(options.PillarDiameter,
            context.LandingPadDiameter * steepnessScale);
        var physicalRadius = padDiameter * 0.5f;
        var delta = hit.Value.Point - current;
        var length = delta.Length();
        var terminalAllowance = physicalRadius * 2 + clearance.ModelDistance + 0.01f;
        if (length > terminalAllowance)
        {
            var clearEnd = hit.Value.Point - delta / length * terminalAllowance;
            if (!clearance.PillarIsClear(_obstacles, current, clearEnd, physicalRadius)) return null;
        }
        if (HitsGenerated(current, hit.Value.Point,
                physicalRadius + clearance.ModelDistance, generatedCapsules, null)) return null;
        rejected = false;
        return new ModelLanding(hit.Value, padDiameter);
    }

    private MergeTarget? FindMerge(Vector3 current, float tipZ, TopDownRoutingOptions options,
        IReadOnlyList<SupportNode> routeNodes, IReadOnlyDictionary<Guid, float> lowestTipByNode,
        IReadOnlyList<GeneratedCapsule> generatedCapsules,
        IReadOnlyList<ExistingSupportTarget> attachTargets, RoutingClearance clearance)
    {
        var candidates = routeNodes.Select(node => new MergeCandidate(node,
                lowestTipByNode[node.Id], false, null))
            .Concat(attachTargets.Select(target => new MergeCandidate(target.Node,
                target.LowestTipZ, true, target)))
            .Where(candidate => candidate.Node.Position.Z < current.Z - Epsilon)
            .OrderByDescending(candidate => candidate.Node.Position.Z)
            .ThenBy(candidate => Vector3.DistanceSquared(current, candidate.Node.Position))
            .ThenBy(candidate => candidate.Node.Id);
        foreach (var candidate in candidates)
        {
            var target = candidate.Node;
            var lowestTip = MathF.Min(tipZ, candidate.LowestTipZ);
            var merge = new GrowthContext
            {
                Operation = GrowthOperation.Merge,
                Start = target.Position,
                DesiredEnd = current,
                End = current,
                Diameter = options.PillarDiameter,
                LowestTipZ = lowestTip,
            };
            _rules.Evaluate(merge);
            if (!merge.Allowed) continue;

            var branch = new GrowthContext
            {
                Operation = GrowthOperation.Branch,
                Start = current,
                DesiredEnd = target.Position,
                End = target.Position,
                Diameter = options.PillarDiameter,
                DistanceToTip = Vector2.Distance(new(current.X, current.Y),
                    new(target.Position.X, target.Position.Y)),
                ExistingBranchCount = 0,
            };
            _rules.Evaluate(branch);
            if (!branch.Allowed || Vector3.DistanceSquared(branch.End, target.Position) > 1e-6f) continue;

            // The incoming branch remains pillar-sized; only the shared downstream path is trunk-sized.
            var physicalRadius = branch.Diameter * 0.5f;
            var queryRadius = physicalRadius + clearance.ModelDistance;
            var filter = candidate.Existing
                ? ExistingSupportTargets.ExcludingIncidentSegments(candidate.Attachment!)
                : null;
            if (!clearance.PillarIsClear(_obstacles, current, target.Position, physicalRadius, filter))
                continue;
            if (HitsGenerated(current, target.Position, queryRadius, generatedCapsules, target.Id)) continue;
            return new MergeTarget(target, candidate.Existing);
        }
        return null;
    }

    private Vector3? FindClearStep(Vector3 current, float nextZ, Vector3 tip,
        TopDownRoutingOptions options, IReadOnlyList<GeneratedCapsule> generatedCapsules,
        RoutingClearance clearance, float angleOffset)
    {
        var drop = current.Z - nextZ;
        var lean = _rules.Find<LeanGrowthRule>();
        var maxAngle = lean is { Enabled: true }
            ? (Vector3.Distance(current, tip) <= lean.NearTipDistance
                ? lean.MaxAngleNearTipDegrees : lean.MaxAngleDegrees)
            : 89;
        var maxHorizontal = drop * MathF.Tan(maxAngle * MathF.PI / 180);
        foreach (var end in StepCandidates(current, nextZ, maxHorizontal, options, angleOffset))
        {
            var grow = new GrowthContext
            {
                Operation = GrowthOperation.Grow,
                Start = current,
                DesiredEnd = end,
                End = end,
                Diameter = options.PillarDiameter,
                DistanceToTip = Vector3.Distance(current, tip),
            };
            _rules.Evaluate(grow);
            if (!grow.Allowed || Vector3.DistanceSquared(grow.End, end) > 1e-6f) continue;
            var physicalRadius = grow.Diameter * 0.5f;
            var queryRadius = physicalRadius + clearance.ModelDistance;
            if (!clearance.PillarIsClear(_obstacles, current, end, physicalRadius)) continue;
            if (HitsGenerated(current, end, queryRadius, generatedCapsules, null)) continue;
            return end;
        }
        return null;
    }

    private static IEnumerable<Vector3> StepCandidates(Vector3 current, float nextZ,
        float maxHorizontal, TopDownRoutingOptions options, float angleOffset)
    {
        yield return new Vector3(current.X, current.Y, nextZ);
        for (var ring = 1; ring <= options.DetourRings; ring++)
        {
            var radius = maxHorizontal * ring / options.DetourRings;
            for (var direction = 0; direction < options.DirectionsPerRing; direction++)
            {
                var angle = angleOffset + direction * MathF.Tau / options.DirectionsPerRing;
                yield return new Vector3(current.X + MathF.Cos(angle) * radius,
                    current.Y + MathF.Sin(angle) * radius, nextZ);
            }
        }
    }

    private bool ContactSegmentIsClear(Vector3 tip, Vector3 junction, float radius)
    {
        var delta = tip - junction;
        var length = delta.Length();
        if (length <= radius * 2 + 0.01f) return true;
        var clearEnd = tip - delta / length * (radius * 2 + 0.01f);
        return !_obstacles.IntersectsCapsule(junction, clearEnd, radius);
    }

    private static bool HitsGenerated(Vector3 start, Vector3 end, float radius,
        IReadOnlyList<GeneratedCapsule> capsules, Guid? allowedEndpoint)
    {
        foreach (var capsule in capsules)
        {
            if (allowedEndpoint.HasValue &&
                (capsule.NodeA == allowedEndpoint || capsule.NodeB == allowedEndpoint)) continue;
            var sum = radius + capsule.Radius;
            if (GeometryDistance.SegmentSegmentSquared(start, end, capsule.Start, capsule.End)
                <= sum * sum) return true;
        }
        return false;
    }

    private void Emit(SupportGraph graph, RoutingTip tip, RouteProposal route,
        TopDownRoutingOptions options, DeterministicIds ids, List<SupportNode> routeNodes,
        Dictionary<Guid, float> lowestTipByNode, List<GeneratedCapsule> generatedCapsules,
        ref float maxLean)
    {
        var tipNode = Node(ids, SupportNodeType.Tip, tip.SurfacePoint, options.Origin);
        tipNode.SurfaceNormal = -RoutingUtilities.SafeInwardNormal(tip.InwardSurfaceNormal);
        tipNode.TipDiameter = tip.TipDiameter;
        tipNode.ContactObjectId = tip.ContactObjectId;
        graph.AddNode(tipNode);

        SupportNode? previous = tipNode;
        for (var index = 0; index < route.Points.Count; index++)
        {
            var isPlate = index == route.Points.Count - 1 && route.MergeTarget is null;
            var node = Node(ids, isPlate ? SupportNodeType.Base : SupportNodeType.Junction,
                route.Points[index], options.Origin);
            if (isPlate && route.Landing is not null && route.Landing.Hit.Tag is Guid objectId)
                node.ContactObjectId = objectId;
            graph.AddNode(node);
            var type = index == 0 ? SupportSegmentType.Neck : SupportSegmentType.Pillar;
            var diameter = index == 0
                ? route.NeckDiameter
                : index == route.Points.Count - 1 && route.Landing is not null
                    ? route.Landing.PadDiameter
                    : options.PillarDiameter;
            AddSegment(graph, ids, previous, node, type, diameter, options.Origin,
                generatedCapsules, true, ref maxLean);
            routeNodes.Add(node);
            lowestTipByNode[node.Id] = tip.SurfacePoint.Z;
            previous = node;
        }

        if (route.MergeTarget is not null)
        {
            AddSegment(graph, ids, previous!, route.MergeTarget.Node, SupportSegmentType.Pillar,
                options.PillarDiameter, options.Origin, generatedCapsules, true, ref maxLean);
            if (!route.MergeTarget.Existing)
                PromoteDownstream(graph, route.MergeTarget.Node, tip.SurfacePoint.Z, lowestTipByNode,
                    generatedCapsules);
        }
    }

    private void PromoteDownstream(SupportGraph graph, SupportNode mergeNode, float newTipZ,
        Dictionary<Guid, float> lowestTipByNode, List<GeneratedCapsule> generatedCapsules)
    {
        var mergeRule = _rules.Find<MergeGrowthRule>();
        var diameter = mergeRule is { Enabled: true }
            ? mergeRule.ResultingTrunkDiameter : 1.2f;
        var stack = new Stack<SupportNode>();
        var visited = new HashSet<Guid> { mergeNode.Id };
        stack.Push(mergeNode);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            lowestTipByNode[node.Id] = MathF.Min(lowestTipByNode[node.Id], newTipZ);
            foreach (var segment in graph.SegmentsAt(node.Id)
                         .Where(segment => segment.Type is SupportSegmentType.Pillar or SupportSegmentType.Trunk))
            {
                var otherId = segment.NodeA == node.Id ? segment.NodeB : segment.NodeA;
                var other = graph.GetNode(otherId);
                if (other.Position.Z > node.Position.Z + Epsilon || !visited.Add(other.Id)) continue;
                segment.Type = SupportSegmentType.Trunk;
                segment.Diameter = MathF.Max(segment.Diameter, diameter);
                var capsuleIndex = generatedCapsules.FindIndex(capsule => capsule.SegmentId == segment.Id);
                if (capsuleIndex >= 0)
                    generatedCapsules[capsuleIndex] = generatedCapsules[capsuleIndex] with
                        { Radius = segment.Diameter * 0.5f };
                stack.Push(other);
            }
        }
        graph.NotifyChanged();
    }

    private static void AddSegment(SupportGraph graph, DeterministicIds ids, SupportNode start,
        SupportNode end, SupportSegmentType type, float diameter, SupportOrigin origin,
        List<GeneratedCapsule> capsules, bool trackCollision, ref float maxLean)
    {
        var segment = new SupportSegment
        {
            Id = ids.Next(), Type = type, NodeA = start.Id, NodeB = end.Id,
            Diameter = diameter, Origin = origin,
        };
        graph.AddSegment(segment);
        if (trackCollision)
            capsules.Add(new GeneratedCapsule(start.Position, end.Position, diameter * 0.5f,
                start.Id, end.Id, segment.Id));
        IncludeLean(start.Position, end.Position, ref maxLean);
    }

    private static SupportNode Node(DeterministicIds ids, SupportNodeType type, Vector3 position,
        SupportOrigin origin) => new() { Id = ids.Next(), Type = type, Position = position, Origin = origin };

    private static void IncludeLean(Vector3 start, Vector3 end, ref float maxLean)
    {
        var delta = end - start;
        var angle = MathF.Atan2(new Vector2(delta.X, delta.Y).Length(), MathF.Abs(delta.Z))
            * 180 / MathF.PI;
        maxLean = MathF.Max(maxLean, angle);
    }

    private sealed record RouteProposal(IReadOnlyList<Vector3> Points, float NeckDiameter,
        MergeTarget? MergeTarget, ModelLanding? Landing);
    private sealed record ModelLanding(ObstacleRayHit Hit, float PadDiameter);
    private sealed record MergeTarget(SupportNode Node, bool Existing);
    private sealed record MergeCandidate(SupportNode Node, float LowestTipZ, bool Existing,
        ExistingSupportTarget? Attachment);
    private readonly record struct GeneratedCapsule(Vector3 Start, Vector3 End, float Radius,
        Guid NodeA, Guid NodeB, Guid SegmentId);
}
