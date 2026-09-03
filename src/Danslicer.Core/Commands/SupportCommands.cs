using Danslicer.Core.Supports;
using Danslicer.Core.Supports.Routing;

namespace Danslicer.Core.Commands;

/// <summary>Adds a set of support nodes and segments as one undoable step.</summary>
public sealed class AddSupportElementsCommand : IDocumentCommand
{
    private readonly SupportGraph _graph;
    private readonly IReadOnlyList<SupportNode> _nodes;
    private readonly IReadOnlyList<SupportSegment> _segments;

    public AddSupportElementsCommand(SupportGraph graph, IReadOnlyList<SupportNode> nodes,
        IReadOnlyList<SupportSegment> segments, string name = "Add support")
    {
        _graph = graph;
        _nodes = nodes;
        _segments = segments;
        Name = name;
    }

    public string Name { get; }

    public void Execute()
    {
        foreach (var node in _nodes) _graph.AddNode(node);
        foreach (var segment in _segments) _graph.AddSegment(segment);
    }

    public void Undo()
    {
        foreach (var segment in _segments) _graph.RemoveSegment(segment.Id);
        foreach (var node in _nodes) _graph.RemoveNode(node.Id);
    }
}

/// <summary>Sets support node positions (and tip normals) as one undoable step, e.g. a tip move.</summary>
public sealed class SetSupportPositionsCommand : IDocumentCommand
{
    public readonly record struct Entry(SupportNode Node,
        System.Numerics.Vector3 BeforePosition, System.Numerics.Vector3 BeforeNormal,
        System.Numerics.Vector3 AfterPosition, System.Numerics.Vector3 AfterNormal);

    private readonly SupportGraph _graph;
    private readonly IReadOnlyList<Entry> _entries;

    public SetSupportPositionsCommand(SupportGraph graph, IReadOnlyList<Entry> entries, string name = "Move support")
    {
        _graph = graph;
        _entries = entries;
        Name = name;
    }

    public string Name { get; }

    public void Execute()
    {
        foreach (var e in _entries)
        {
            e.Node.Position = e.AfterPosition;
            e.Node.SurfaceNormal = e.AfterNormal;
        }
        _graph.NotifyChanged();
    }

    public void Undo()
    {
        foreach (var e in _entries)
        {
            e.Node.Position = e.BeforePosition;
            e.Node.SurfaceNormal = e.BeforeNormal;
        }
        _graph.NotifyChanged();
    }
}

/// <summary>
/// Applies a routed graph edit as one undoable step. Removals support splitting an existing trunk
/// at a new branch attachment; undo restores the exact original segment and topology.
/// </summary>
public sealed class ApplySupportGraphEditCommand : IDocumentCommand
{
    private readonly SupportGraph _graph;
    private readonly SupportGraphEdit _edit;

    public ApplySupportGraphEditCommand(SupportGraph graph, SupportGraphEdit edit,
        string name = "Add support")
    {
        _graph = graph;
        _edit = edit;
        Name = name;
    }

    public string Name { get; }

    public void Execute()
    {
        foreach (var segment in _edit.RemovedSegments) _graph.RemoveSegment(segment.Id);
        foreach (var node in _edit.AddedNodes) _graph.AddNode(node);
        foreach (var segment in _edit.AddedSegments) _graph.AddSegment(segment);
    }

    public void Undo()
    {
        foreach (var segment in _edit.AddedSegments) _graph.RemoveSegment(segment.Id);
        foreach (var node in _edit.AddedNodes) _graph.RemoveNode(node.Id);
        foreach (var segment in _edit.RemovedSegments) _graph.AddSegment(segment);
    }
}

/// <summary>Changes support visibility without changing whether the elements slice.</summary>
public sealed class SetSupportHiddenCommand : IDocumentCommand
{
    public readonly record struct Entry(Action<bool> SetHidden, bool Before, bool After);

    private readonly SupportGraph _graph;
    private readonly IReadOnlyList<Entry> _entries;

    public SetSupportHiddenCommand(SupportGraph graph, IReadOnlyList<Entry> entries,
        string name = "Hide supports")
    {
        _graph = graph;
        _entries = entries;
        Name = name;
    }

    public string Name { get; }

    public void Execute() => Apply(after: true);
    public void Undo() => Apply(after: false);

    private void Apply(bool after)
    {
        foreach (var entry in _entries) entry.SetHidden(after ? entry.After : entry.Before);
        _graph.NotifyChanged();
    }
}

/// <summary>
/// Removes support nodes and segments as one undoable step. Segments attached to a removed node are
/// captured and removed too, so undo restores the exact structure.
/// </summary>
public sealed class RemoveSupportElementsCommand : IDocumentCommand
{
    private readonly SupportGraph _graph;
    private readonly List<SupportNode> _nodes;
    private readonly List<SupportSegment> _segments;

    public RemoveSupportElementsCommand(SupportGraph graph, IEnumerable<Guid> nodeIds, IEnumerable<Guid> segmentIds,
        string name = "Delete support")
    {
        _graph = graph;
        _nodes = nodeIds.Select(graph.GetNode).ToList();
        var segments = new Dictionary<Guid, SupportSegment>();
        foreach (var id in segmentIds) segments[id] = graph.GetSegment(id);
        foreach (var node in _nodes)
            foreach (var attached in graph.SegmentsAt(node.Id))
                segments[attached.Id] = attached;
        _segments = segments.Values.ToList();
        Name = name;
    }

    public string Name { get; }

    public void Execute()
    {
        foreach (var segment in _segments) _graph.RemoveSegment(segment.Id);
        foreach (var node in _nodes) _graph.RemoveNode(node.Id);
    }

    public void Undo()
    {
        foreach (var node in _nodes) _graph.AddNode(node);
        foreach (var segment in _segments) _graph.AddSegment(segment);
    }
}
