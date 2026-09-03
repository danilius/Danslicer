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

    public RoutingResult Route(IEnumerable<RoutingTip> tips, TopDownRoutingOptions options)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.StepHeight);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.PillarDiameter);
        ArgumentOutOfRangeException.ThrowIfNegative(options.DetourRings);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.DirectionsPerRing);

        var graph = new SupportGraph();
        var ids = new DeterministicIds(options.Seed);
        var routeNodes = new List<SupportNode>();
        var lowestTipByNode = new Dictionary<Guid, float>();
        var generatedCapsules = new List<GeneratedCapsule>();
        var unrouted = new List<RoutingTip>();
        var maxLean = 0f;
        var angleOffset = new Random(options.Seed).NextSingle() * MathF.Tau;
        var clearanceRule = _rules.Find<ClearanceGrowthRule>();
        var clearance = clearanceRule is { Enabled: true } ? clearanceRule.DistanceFromModel : 0;

        foreach (var item in tips.Select((tip, index) => (Tip: tip, Index: index))
                     .OrderByDescending(item => item.Tip.SurfacePoint.Z).ThenBy(item => item.Index))
        {
            var proposal = Propose(item.Tip, options, routeNodes, lowestTipByNode,
                generatedCapsules, clearance, angleOffset);
            if (proposal is null)
            {
                unrouted.Add(item.Tip);
                continue;
            }

            Emit(graph, item.Tip, proposal, options, ids, routeNodes, lowestTipByNode,
                generatedCapsules, ref maxLean);
        }

        var bases = graph.Nodes.Where(node => node.Type == SupportNodeType.Base)
            .Select(node => node.Position).OrderBy(p => p.X).ThenBy(p => p.Y).ToList();
        return new RoutingResult(graph, unrouted, bases, maxLean);
    }

    private RouteProposal? Propose(RoutingTip tip, TopDownRoutingOptions options,
        IReadOnlyList<SupportNode> routeNodes, IReadOnlyDictionary<Guid, float> lowestTipByNode,
        IReadOnlyList<GeneratedCapsule> generatedCapsules, float clearance, float angleOffset)
    {
        if (tip.SurfacePoint.Z <= options.PlateZ + Epsilon) return null;

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
        var neckDrop = MathF.Min(MathF.Max(0.1f, taper.NeckLength), tip.SurfacePoint.Z - options.PlateZ);
        var first = tip.SurfacePoint - Vector3.UnitZ * neckDrop;
        var neckRadius = neckDiameter * 0.5f + clearance;
        if (!ContactSegmentIsClear(tip.SurfacePoint, first, neckRadius)) return null;

        var points = new List<Vector3> { first };
        SupportNode? mergeTarget = null;
        var current = first;
        var maxSteps = (int)MathF.Ceiling((tip.SurfacePoint.Z - options.PlateZ) / options.StepHeight) + 2;
        for (var step = 0; step < maxSteps && current.Z > options.PlateZ + Epsilon; step++)
        {
            mergeTarget = FindMerge(current, tip.SurfacePoint.Z, options, routeNodes,
                lowestTipByNode, generatedCapsules, clearance);
            if (mergeTarget is not null) break;

            var nextZ = MathF.Max(options.PlateZ, current.Z - options.StepHeight);
            var next = FindClearStep(current, nextZ, tip.SurfacePoint, options,
                generatedCapsules, clearance, angleOffset + step * 0.381966f);
            if (next is null) return null;
            current = next.Value;
            points.Add(current);
        }

        if (mergeTarget is null && current.Z > options.PlateZ + Epsilon) return null;
        return new RouteProposal(points, neckDiameter, mergeTarget);
    }

    private SupportNode? FindMerge(Vector3 current, float tipZ, TopDownRoutingOptions options,
        IReadOnlyList<SupportNode> routeNodes, IReadOnlyDictionary<Guid, float> lowestTipByNode,
        IReadOnlyList<GeneratedCapsule> generatedCapsules, float clearance)
    {
        foreach (var target in routeNodes.Where(node => node.Position.Z < current.Z - Epsilon)
                     .OrderByDescending(node => node.Position.Z)
                     .ThenBy(node => Vector3.DistanceSquared(current, node.Position))
                     .ThenBy(node => node.Id))
        {
            var lowestTip = MathF.Min(tipZ, lowestTipByNode[target.Id]);
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

            var radius = MathF.Max(branch.Diameter, merge.Diameter) * 0.5f + clearance;
            if (_obstacles.IntersectsCapsule(current, target.Position, radius)) continue;
            if (HitsGenerated(current, target.Position, radius, generatedCapsules, target.Id)) continue;
            return target;
        }
        return null;
    }

    private Vector3? FindClearStep(Vector3 current, float nextZ, Vector3 tip,
        TopDownRoutingOptions options, IReadOnlyList<GeneratedCapsule> generatedCapsules,
        float clearance, float angleOffset)
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
            var radius = grow.Diameter * 0.5f + clearance;
            if (_obstacles.IntersectsCapsule(current, end, radius)) continue;
            if (HitsGenerated(current, end, radius, generatedCapsules, null)) continue;
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
        tipNode.SurfaceNormal = -SafeNormal(tip.InwardSurfaceNormal);
        tipNode.TipDiameter = tip.TipDiameter;
        tipNode.ContactObjectId = tip.ContactObjectId;
        graph.AddNode(tipNode);

        SupportNode? previous = tipNode;
        for (var index = 0; index < route.Points.Count; index++)
        {
            var isPlate = index == route.Points.Count - 1 && route.MergeTarget is null;
            var node = Node(ids, isPlate ? SupportNodeType.Base : SupportNodeType.Junction,
                route.Points[index], options.Origin);
            graph.AddNode(node);
            var type = index == 0 ? SupportSegmentType.Neck : SupportSegmentType.Pillar;
            var diameter = index == 0 ? route.NeckDiameter : options.PillarDiameter;
            AddSegment(graph, ids, previous, node, type, diameter, options.Origin,
                generatedCapsules, index != 0, ref maxLean);
            routeNodes.Add(node);
            lowestTipByNode[node.Id] = tip.SurfacePoint.Z;
            previous = node;
        }

        if (route.MergeTarget is not null)
        {
            AddSegment(graph, ids, previous!, route.MergeTarget, SupportSegmentType.Pillar,
                options.PillarDiameter, options.Origin, generatedCapsules, true, ref maxLean);
            PromoteDownstream(graph, route.MergeTarget, tip.SurfacePoint.Z, lowestTipByNode);
        }
    }

    private void PromoteDownstream(SupportGraph graph, SupportNode mergeNode, float newTipZ,
        Dictionary<Guid, float> lowestTipByNode)
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
                stack.Push(other);
            }
        }
        graph.NotifyChanged();
    }

    private static void AddSegment(SupportGraph graph, DeterministicIds ids, SupportNode start,
        SupportNode end, SupportSegmentType type, float diameter, SupportOrigin origin,
        List<GeneratedCapsule> capsules, bool trackCollision, ref float maxLean)
    {
        graph.AddSegment(new SupportSegment
        {
            Id = ids.Next(), Type = type, NodeA = start.Id, NodeB = end.Id,
            Diameter = diameter, Origin = origin,
        });
        if (trackCollision)
            capsules.Add(new GeneratedCapsule(start.Position, end.Position, diameter * 0.5f,
                start.Id, end.Id));
        IncludeLean(start.Position, end.Position, ref maxLean);
    }

    private static SupportNode Node(DeterministicIds ids, SupportNodeType type, Vector3 position,
        SupportOrigin origin) => new() { Id = ids.Next(), Type = type, Position = position, Origin = origin };

    private static Vector3 SafeNormal(Vector3 normal) => normal.LengthSquared() > 1e-12f
        ? Vector3.Normalize(normal) : -Vector3.UnitZ;

    private static void IncludeLean(Vector3 start, Vector3 end, ref float maxLean)
    {
        var delta = end - start;
        var angle = MathF.Atan2(new Vector2(delta.X, delta.Y).Length(), MathF.Abs(delta.Z))
            * 180 / MathF.PI;
        maxLean = MathF.Max(maxLean, angle);
    }

    private sealed record RouteProposal(IReadOnlyList<Vector3> Points, float NeckDiameter,
        SupportNode? MergeTarget);
    private readonly record struct GeneratedCapsule(Vector3 Start, Vector3 End, float Radius,
        Guid NodeA, Guid NodeB);

    private sealed class DeterministicIds
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
}
