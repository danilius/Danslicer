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

public enum SupportSegmentType
{
    /// <summary>Tip to first junction. Thin, tapered.</summary>
    Neck,
    /// <summary>Ordinary vertical or leaning member.</summary>
    Pillar,
    /// <summary>Merged pillar of larger diameter.</summary>
    Trunk,
    /// <summary>Cross-member between two pillars or trunks. Never part of a whole-support selection.</summary>
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
    public float PenetrationDepth { get; set; } = 0.2f;
    /// <summary>The scene object a tip contacts (or a base lands on when landing on the model).</summary>
    public Guid? ContactObjectId { get; set; }
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
}
