using System.Numerics;

namespace Danslicer.Core.Supports;

public enum SupportNodeType
{
    /// <summary>Contact with the model: carries contact point, surface normal and tip shape.</summary>
    Tip,
    /// <summary>Branch or merge point.</summary>
    Junction,
    /// <summary>Contact with the plate, or with the model when landing on the model is allowed.</summary>
    Base,
}

/// <summary>How a tip meets the model. Defaults to a capsule end so existing graphs slice unchanged.</summary>
public enum SupportTipShape
{
    /// <summary>Neck is a capsule; the contact is the hemispherical cap. Today's behaviour.</summary>
    Capsule = 0,
    /// <summary>
    /// Cone from the neck diameter down to <see cref="SupportNode.TipDiameter"/> over
    /// <see cref="SupportNode.ConeLength"/>, optionally with a snap-off ball of
    /// <see cref="SupportNode.BallDiameter"/>.
    /// </summary>
    Cone = 1,
}

/// <summary>
/// Canonical member vocabulary (user decision 2026-09-03): base / trunk / branch / tip + brace.
/// The old neck and pillar fold into tip and branch respectively.
/// </summary>
public enum SupportSegmentType
{
    /// <summary>Contact member from the tip node to the first junction. Thin, tapered, carries the cone.</summary>
    Tip,
    /// <summary>Angled member spanning from a trunk or junction toward a tip.</summary>
    Branch,
    /// <summary>Vertical (or merged main) member rising from the base.</summary>
    Trunk,
    /// <summary>Cross-member between two trunks or branches. Never part of a whole-support selection.</summary>
    Bracing,
}

/// <summary>
/// Where an element came from: the region and generation pass that made it, or manual placement.
/// Regeneration of a region deletes that region's unpinned elements by matching this tag.
/// </summary>
public readonly record struct SupportOrigin(Guid RegionId, int Pass)
{
    public static SupportOrigin Manual => default;
    public bool IsManual => RegionId == Guid.Empty;
}

/// <summary>A point of the support graph. Mutable; all mutation goes through commands.</summary>
public sealed class SupportNode
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required SupportNodeType Type { get; set; }
    public required Vector3 Position { get; set; }
    public SupportOrigin Origin { get; set; } = SupportOrigin.Manual;

    /// <summary>Pinned elements survive regeneration. Editing a generated element pins it.</summary>
    public bool Pinned { get; set; }
    /// <summary>Hidden elements are not drawn but still slice.</summary>
    public bool Hidden { get; set; }
    /// <summary>Disabled elements are excluded from slicing without being deleted.</summary>
    public bool Disabled { get; set; }

    // Tip-only shape parameters; ignored on junctions and bases.
    public Vector3 SurfaceNormal { get; set; } = Vector3.UnitZ;
    public float TipDiameter { get; set; } = 0.4f;
    private float _penetrationDepth;
    /// <summary>
    /// How far the tip embeds past its surface contact, millimetres. For cone tips this extends
    /// the frustum into the model along the tip axis; it also offsets the optional contact ball.
    /// </summary>
    public float PenetrationDepth
    {
        get => _penetrationDepth;
        set => _penetrationDepth = Math.Max(value, 0f);
    }
    /// <summary>The scene object a tip contacts (or a base lands on when landing on the model).</summary>
    public Guid? ContactObjectId { get; set; }

    /// <summary>Contact geometry. Default <see cref="SupportTipShape.Capsule"/> slices as before.</summary>
    public SupportTipShape TipShape { get; set; } = SupportTipShape.Capsule;
    /// <summary>
    /// Length of the cone along the neck, millimetres. Unused when <see cref="TipShape"/> is Capsule.
    /// Matches the default taper neck length so a cone-shaped tip has a printable run.
    /// </summary>
    public float ConeLength { get; set; } = 2f;
    /// <summary>
    /// Snap-off ball diameter at the contact, millimetres. Zero means no ball. Unused when
    /// <see cref="TipShape"/> is Capsule. May exceed the neck diameter (the extra sphere is then
    /// a collision obstacle).
    /// </summary>
    public float BallDiameter { get; set; } = 0f;

    /// <summary>
    /// Centre of the optional contact ball: the surface point pushed into the model along the
    /// inward normal by <see cref="PenetrationDepth"/>.
    /// </summary>
    public Vector3 ContactBallCenter
    {
        get
        {
            var n = SurfaceNormal;
            var lenSq = n.LengthSquared();
            if (lenSq < 1e-12f) return Position;
            return Position - n * (PenetrationDepth / MathF.Sqrt(lenSq));
        }
    }
}

/// <summary>A member joining two nodes. Mutable; all mutation goes through commands.</summary>
public sealed class SupportSegment
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required SupportSegmentType Type { get; set; }
    public required Guid NodeA { get; init; }
    public required Guid NodeB { get; init; }
    public float Diameter { get; set; } = 1.2f;
    public SupportOrigin Origin { get; set; } = SupportOrigin.Manual;
    public bool Pinned { get; set; }
    public bool Hidden { get; set; }
    public bool Disabled { get; set; }
}

/// <summary>
/// The single source of truth for supports: nodes and segments with referential integrity. Render
/// meshes and slice geometry are derived on demand and never stored. A "support" in the user's
/// sense is a connected component of the graph with bracing segments removed.
/// </summary>
public sealed class SupportGraph
{
    private readonly Dictionary<Guid, SupportNode> _nodes = new();
    private readonly Dictionary<Guid, SupportSegment> _segments = new();
    private readonly Dictionary<Guid, List<SupportSegment>> _segmentsByNode = new();

    /// <summary>Raised after any structural or property change committed through the graph.</summary>
    public event Action? Changed;

    public IReadOnlyCollection<SupportNode> Nodes => _nodes.Values;
    public IReadOnlyCollection<SupportSegment> Segments => _segments.Values;
    public int NodeCount => _nodes.Count;
    public int SegmentCount => _segments.Count;

    public SupportNode GetNode(Guid id) => _nodes[id];
    public SupportSegment GetSegment(Guid id) => _segments[id];
    public bool TryGetNode(Guid id, out SupportNode node) => _nodes.TryGetValue(id, out node!);
    public bool TryGetSegment(Guid id, out SupportSegment segment) => _segments.TryGetValue(id, out segment!);

    public void AddNode(SupportNode node)
    {
        if (!_nodes.TryAdd(node.Id, node))
            throw new InvalidOperationException($"Node {node.Id} is already in the graph.");
        _segmentsByNode[node.Id] = new List<SupportSegment>();
        Changed?.Invoke();
    }

    /// <summary>Removing a node also removes every segment attached to it.</summary>
    public void RemoveNode(Guid id)
    {
        if (!_nodes.Remove(id)) throw new InvalidOperationException($"Node {id} is not in the graph.");
        foreach (var segment in _segmentsByNode[id].ToList())
            RemoveSegmentInternal(segment.Id);
        _segmentsByNode.Remove(id);
        Changed?.Invoke();
    }

    public void AddSegment(SupportSegment segment)
    {
        if (segment.NodeA == segment.NodeB)
            throw new InvalidOperationException("A segment cannot join a node to itself.");
        if (!_nodes.ContainsKey(segment.NodeA) || !_nodes.ContainsKey(segment.NodeB))
            throw new InvalidOperationException("Both segment endpoints must be in the graph.");
        if (!_segments.TryAdd(segment.Id, segment))
            throw new InvalidOperationException($"Segment {segment.Id} is already in the graph.");
        _segmentsByNode[segment.NodeA].Add(segment);
        _segmentsByNode[segment.NodeB].Add(segment);
        Changed?.Invoke();
    }

    public void RemoveSegment(Guid id)
    {
        RemoveSegmentInternal(id);
        Changed?.Invoke();
    }

    private void RemoveSegmentInternal(Guid id)
    {
        if (!_segments.Remove(id, out var segment))
            throw new InvalidOperationException($"Segment {id} is not in the graph.");
        _segmentsByNode[segment.NodeA].Remove(segment);
        _segmentsByNode[segment.NodeB].Remove(segment);
    }

    /// <summary>Notify listeners after mutating element properties in place (via commands).</summary>
    public void NotifyChanged() => Changed?.Invoke();

    public IReadOnlyList<SupportSegment> SegmentsAt(Guid nodeId) => _segmentsByNode[nodeId];

    /// <summary>
    /// The connected component containing the given node. With <paramref name="includeBracing"/>
    /// false this is one "support": a tree from its base to its tips, bracing excluded.
    /// </summary>
    public (HashSet<Guid> Nodes, HashSet<Guid> Segments) Component(Guid nodeId, bool includeBracing = false)
    {
        if (!_nodes.ContainsKey(nodeId)) throw new InvalidOperationException($"Node {nodeId} is not in the graph.");
        var nodes = new HashSet<Guid>();
        var segments = new HashSet<Guid>();
        var stack = new Stack<Guid>();
        stack.Push(nodeId);
        nodes.Add(nodeId);
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            foreach (var segment in _segmentsByNode[current])
            {
                if (!includeBracing && segment.Type == SupportSegmentType.Bracing) continue;
                if (!segments.Add(segment.Id)) continue;
                var next = segment.NodeA == current ? segment.NodeB : segment.NodeA;
                if (nodes.Add(next)) stack.Push(next);
            }
        }
        return (nodes, segments);
    }

    /// <summary>Enumerates every support (component without bracing) once, as sets of element ids.</summary>
    public IEnumerable<(HashSet<Guid> Nodes, HashSet<Guid> Segments)> Supports()
    {
        var visited = new HashSet<Guid>();
        foreach (var id in _nodes.Keys)
        {
            if (visited.Contains(id)) continue;
            var component = Component(id);
            visited.UnionWith(component.Nodes);
            yield return component;
        }
    }

    /// <summary>Every element created by the given region that is not pinned, for regeneration.</summary>
    public (List<SupportNode> Nodes, List<SupportSegment> Segments) UnpinnedElementsOf(Guid regionId)
    {
        var nodes = _nodes.Values.Where(n => !n.Pinned && !n.Origin.IsManual && n.Origin.RegionId == regionId).ToList();
        var segments = _segments.Values.Where(s => !s.Pinned && !s.Origin.IsManual && s.Origin.RegionId == regionId).ToList();
        return (nodes, segments);
    }

    /// <summary>
    /// Contact balls whose diameter exceeds every incident segment (the neck). Smaller balls sit
    /// inside the neck capsule, so collision can ignore them; these must be extra sphere obstacles.
    /// </summary>
    public IEnumerable<(Vector3 Centre, float Radius, Guid Id)> ContactBallsExceedingNeck()
    {
        foreach (var node in _nodes.Values)
        {
            if (node.Disabled || node.Type != SupportNodeType.Tip) continue;
            if (node.TipShape != SupportTipShape.Cone || node.BallDiameter <= 0) continue;
            var neck = 0f;
            foreach (var segment in _segmentsByNode[node.Id])
                if (segment.Diameter > neck) neck = segment.Diameter;
            if (node.BallDiameter <= neck) continue;
            yield return (node.ContactBallCenter, node.BallDiameter * 0.5f, node.Id);
        }
    }
}
