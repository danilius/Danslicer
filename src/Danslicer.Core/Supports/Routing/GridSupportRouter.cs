using System.Numerics;

namespace Danslicer.Core.Supports.Routing;

public readonly record struct RoutingTip(Vector3 SurfacePoint, Vector3 InwardSurfaceNormal,
    float TipDiameter, Guid? ContactObjectId = null);

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
}

public sealed record RoutingResult(SupportGraph Graph, IReadOnlyList<RoutingTip> UnroutedTips,
    IReadOnlyList<Vector3> BasePositions, float MaxLeanAngleDegrees);

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

    public RoutingResult Route(IEnumerable<RoutingTip> tips, GridRoutingOptions options)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.Spacing);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.PillarDiameter);
        ArgumentOutOfRangeException.ThrowIfNegative(options.SnapTolerance);
        ArgumentOutOfRangeException.ThrowIfNegative(options.CandidateRingCount);

        var orderedTips = tips.Select((tip, index) => (Tip: tip, Index: index)).ToList();
        var candidates = orderedTips.Select(item =>
            (item.Tip, item.Index, Bases: CandidateBases(item.Tip.SurfacePoint, options).ToList())).ToList();
        var assignments = new List<(RoutingTip Tip, int Index, Vector3 Base, Vector3 Junction, float NeckDiameter)>();
        var unrouted = new List<RoutingTip>();
        var branchCounts = new Dictionary<Vector3, int>();
        var clearance = _rules.Find<ClearanceGrowthRule>();
        var clearanceDistance = clearance is { Enabled: true } ? clearance.DistanceFromModel : 0;

        foreach (var candidate in candidates)
        {
            var routed = false;
            foreach (var basePosition in candidate.Bases)
            {
                branchCounts.TryGetValue(basePosition, out var branchCount);
                if (!TryProposal(candidate.Tip, basePosition, branchCount, options, clearanceDistance,
                    out var junction, out var neckDiameter)) continue;
                assignments.Add((candidate.Tip, candidate.Index, basePosition, junction, neckDiameter));
                branchCounts[basePosition] = branchCount + 1;
                routed = true;
                break;
            }
            if (!routed) unrouted.Add(candidate.Tip);
        }

        var graph = new SupportGraph();
        var ids = new DeterministicIds(options.Seed);
        var bases = new List<Vector3>();
        var maxLean = 0f;
        foreach (var group in assignments.GroupBy(a => a.Base).OrderBy(g => g.Key.X).ThenBy(g => g.Key.Y))
        {
            EmitGroup(graph, group.OrderBy(a => a.Junction.Z).ThenBy(a => a.Index).ToList(),
                options, ids, bases, ref maxLean);
        }
        return new RoutingResult(graph, unrouted, bases, maxLean);
    }

    private bool TryProposal(RoutingTip tip, Vector3 basePosition, int existingBranchCount,
        GridRoutingOptions options,
        float clearance, out Vector3 junction, out float neckDiameter)
    {
        var taper = new GrowthContext
        {
            Operation = GrowthOperation.Neck,
            Start = tip.SurfacePoint,
            DesiredEnd = tip.SurfacePoint,
            End = tip.SurfacePoint,
            Diameter = tip.TipDiameter,
        };
        _rules.Evaluate(taper);
        neckDiameter = MathF.Max(0.05f, taper.Diameter);
        var neckLength = MathF.Max(0.1f, taper.NeckLength);
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

        var queryRadius = MathF.Max(options.PillarDiameter, neckDiameter) * 0.5f + clearance;
        if (_obstacles.IntersectsCapsule(basePosition, junction, queryRadius)) return false;

        // The last part of a neck intentionally enters the contacted surface. Query only the
        // portion that should remain clear of unrelated geometry.
        var neckVector = tip.SurfacePoint - junction;
        var neckLengthActual = neckVector.Length();
        if (neckLengthActual > queryRadius * 2 + 0.01f)
        {
            var clearEnd = tip.SurfacePoint - neckVector / neckLengthActual * (queryRadius * 2 + 0.01f);
            if (_obstacles.IntersectsCapsule(junction, clearEnd, queryRadius)) return false;
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
                Start = basePosition,
                DesiredEnd = basePosition,
                End = basePosition,
                Diameter = trunkDiameter,
            };
            _rules.Evaluate(merge);
            if (merge.Allowed) trunkDiameter = merge.Diameter;
        }

        foreach (var route in group)
        {
            SupportNode junction;
            if (Vector3.DistanceSquared(previous.Position, route.Junction) < 1e-8f)
            {
                junction = previous;
            }
            else
            {
                junction = Node(ids, SupportNodeType.Junction, route.Junction, options.Origin);
                graph.AddNode(junction);
                graph.AddSegment(Segment(ids, shared ? SupportSegmentType.Trunk : SupportSegmentType.Pillar,
                    previous.Id, junction.Id, shared ? trunkDiameter : options.PillarDiameter, options.Origin));
                IncludeLean(previous.Position, junction.Position, ref maxLean);
                previous = junction;
            }

            var tipNode = Node(ids, SupportNodeType.Tip, route.Tip.SurfacePoint, options.Origin);
            // Convention boundary: RoutingTip carries the INWARD (penetration) normal, but
            // SupportNode.SurfaceNormal is OUTWARD everywhere else (viewport picking, tip move).
            tipNode.SurfaceNormal = SafeNormal(-route.Tip.InwardSurfaceNormal);
            tipNode.TipDiameter = route.Tip.TipDiameter;
            tipNode.ContactObjectId = route.Tip.ContactObjectId;
            graph.AddNode(tipNode);
            graph.AddSegment(Segment(ids, SupportSegmentType.Neck, junction.Id, tipNode.Id,
                route.NeckDiameter, options.Origin));
            IncludeLean(junction.Position, tipNode.Position, ref maxLean);
        }
    }

    private static IEnumerable<Vector3> CandidateBases(Vector3 tip, GridRoutingOptions options)
    {
        var radians = -options.RotationDegrees * MathF.PI / 180;
        var local = Rotate(new Vector2(tip.X, tip.Y) - options.Offset, radians);
        var seeds = options.Lattice == BaseLatticeType.Square
            ? SquareCoordinates(local, options.Spacing, options.CandidateRingCount)
            : HexCoordinates(local, options.Spacing, options.CandidateRingCount);
        var forward = -radians;
        return seeds.Select(p => Rotate(p, forward) + options.Offset)
            .Select(p => new Vector3(p, options.PlateZ))
            .OrderBy(p => Vector2.DistanceSquared(new(p.X, p.Y), new(tip.X, tip.Y)))
            .ThenBy(p => p.X).ThenBy(p => p.Y);
    }

    private static IEnumerable<Vector2> SquareCoordinates(Vector2 point, float spacing, int rings)
    {
        var x = (int)MathF.Round(point.X / spacing);
        var y = (int)MathF.Round(point.Y / spacing);
        for (var ring = 0; ring <= rings; ring++)
            for (var iy = y - ring; iy <= y + ring; iy++)
                for (var ix = x - ring; ix <= x + ring; ix++)
                    if (ring == 0 || Math.Max(Math.Abs(ix - x), Math.Abs(iy - y)) == ring)
                        yield return new Vector2(ix * spacing, iy * spacing);
    }

    private static IEnumerable<Vector2> HexCoordinates(Vector2 point, float spacing, int rings)
    {
        var rowHeight = spacing * MathF.Sqrt(3) * 0.5f;
        var row = (int)MathF.Round(point.Y / rowHeight);
        var column = (int)MathF.Round(point.X / spacing - (row & 1) * 0.5f);
        for (var ring = 0; ring <= rings; ring++)
            for (var r = row - ring; r <= row + ring; r++)
                for (var q = column - ring; q <= column + ring; q++)
                    if (ring == 0 || Math.Max(Math.Abs(q - column), Math.Abs(r - row)) == ring)
                        yield return new Vector2((q + (r & 1) * 0.5f) * spacing, r * rowHeight);
    }

    private static Vector2 Rotate(Vector2 p, float radians)
    {
        var c = MathF.Cos(radians);
        var s = MathF.Sin(radians);
        return new Vector2(p.X * c - p.Y * s, p.X * s + p.Y * c);
    }

    private static SupportNode Node(DeterministicIds ids, SupportNodeType type, Vector3 position,
        SupportOrigin origin) => new() { Id = ids.Next(), Type = type, Position = position, Origin = origin };

    private static SupportSegment Segment(DeterministicIds ids, SupportSegmentType type, Guid a,
        Guid b, float diameter, SupportOrigin origin) => new()
        { Id = ids.Next(), Type = type, NodeA = a, NodeB = b, Diameter = diameter, Origin = origin };

    private static Vector3 SafeNormal(Vector3 normal) => normal.LengthSquared() > 1e-12f
        ? Vector3.Normalize(normal) : Vector3.UnitZ;

    private static void IncludeLean(Vector3 start, Vector3 end, ref float maxLean)
    {
        var delta = end - start;
        var angle = MathF.Atan2(new Vector2(delta.X, delta.Y).Length(), MathF.Abs(delta.Z))
            * 180 / MathF.PI;
        maxLean = MathF.Max(maxLean, angle);
    }

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
