using Danslicer.Core.Supports;

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
