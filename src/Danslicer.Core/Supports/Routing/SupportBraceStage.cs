using System.Numerics;

namespace Danslicer.Core.Supports.Routing;

public sealed record BraceStageOptions
{
    public float Diameter { get; init; } = 0.8f;
    public int Seed { get; init; } = 1;
    public SupportOrigin Origin { get; init; } = SupportOrigin.Manual;
}

/// <summary>Adds rule-approved cross-members between neighbouring support trees.</summary>
public sealed class SupportBraceStage
{
    private readonly ICollisionScene _obstacles;
    private readonly GrowthRuleSet _rules;

    public SupportBraceStage(ICollisionScene obstacles, GrowthRuleSet rules)
    {
        _obstacles = obstacles;
        _rules = rules;
    }

    /// <summary>
    /// Adds at most one best-scoring brace per pair of trees. Existing endpoint pairs are retained,
    /// so invoking the stage again without graph changes is idempotent.
    /// </summary>
    public int Apply(SupportGraph graph, BraceStageOptions options)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.Diameter);
        var rule = _rules.Find<BraceGrowthRule>();
        if (rule is not { Enabled: true }) return 0;

        var components = graph.Supports().Select((component, index) => (component, index)).ToList();
        var componentByNode = components.SelectMany(item => item.component.Nodes
                .Select(nodeId => (nodeId, item.index)))
            .ToDictionary(item => item.nodeId, item => item.index);
        var candidates = new List<Candidate>();
        var nodes = graph.Nodes.Where(node => node.Type != SupportNodeType.Tip)
            .OrderBy(node => node.Position.X).ThenBy(node => node.Position.Y)
            .ThenBy(node => node.Position.Z).ThenBy(node => node.Id).ToList();

        for (var i = 0; i < nodes.Count; i++)
        for (var j = i + 1; j < nodes.Count; j++)
        {
            var a = nodes[i];
            var b = nodes[j];
            if (componentByNode[a.Id] == componentByNode[b.Id] || HasBrace(graph, a.Id, b.Id)) continue;
            var slenderness = MathF.Min(NodeSlenderness(graph, a), NodeSlenderness(graph, b));
            var context = new GrowthContext
            {
                Operation = GrowthOperation.Brace,
                Start = a.Position,
                DesiredEnd = b.Position,
                End = b.Position,
                Diameter = options.Diameter,
                Slenderness = slenderness,
            };
            _rules.Evaluate(context);
            if (!context.Allowed) continue;
            var radius = context.Diameter * 0.5f;
            if (_obstacles.IntersectsCapsule(a.Position, b.Position, radius)) continue;
            var delta = b.Position - a.Position;
            var angle = MathF.Atan2(MathF.Abs(delta.Z),
                MathF.Max(1e-6f, new Vector2(delta.X, delta.Y).Length())) * 180 / MathF.PI;
            candidates.Add(new Candidate(a, b, context.Diameter,
                MathF.Abs(angle - rule.PreferredAngleDegrees), delta.Length()));
        }

        var random = new Random(options.Seed);
        var usedPairs = new HashSet<(int, int)>();
        var added = 0;
        foreach (var candidate in candidates.OrderBy(candidate => candidate.AngleError)
                     .ThenBy(candidate => candidate.Length).ThenBy(candidate => candidate.A.Id)
                     .ThenBy(candidate => candidate.B.Id))
        {
            var pair = OrderedPair(componentByNode[candidate.A.Id], componentByNode[candidate.B.Id]);
            if (!usedPairs.Add(pair)) continue;
            var bytes = new byte[16];
            random.NextBytes(bytes);
            graph.AddSegment(new SupportSegment
            {
                Id = new Guid(bytes),
                Type = SupportSegmentType.Bracing,
                NodeA = candidate.A.Id,
                NodeB = candidate.B.Id,
                Diameter = candidate.Diameter,
                Origin = options.Origin,
            });
            added++;
        }
        return added;
    }

    private static bool HasBrace(SupportGraph graph, Guid a, Guid b) =>
        graph.SegmentsAt(a).Any(segment => segment.Type == SupportSegmentType.Bracing &&
            (segment.NodeA == b || segment.NodeB == b));

    private static float NodeSlenderness(SupportGraph graph, SupportNode node)
    {
        var members = graph.SegmentsAt(node.Id)
            .Where(segment => segment.Type is SupportSegmentType.Pillar or SupportSegmentType.Trunk)
            .ToList();
        if (members.Count == 0) return 0;
        return members.Max(segment =>
        {
            var otherId = segment.NodeA == node.Id ? segment.NodeB : segment.NodeA;
            var length = Vector3.Distance(node.Position, graph.GetNode(otherId).Position);
            return length / MathF.Max(0.01f, segment.Diameter);
        });
    }

    private static (int, int) OrderedPair(int a, int b) => a < b ? (a, b) : (b, a);
    private sealed record Candidate(SupportNode A, SupportNode B, float Diameter,
        float AngleError, float Length);
}
