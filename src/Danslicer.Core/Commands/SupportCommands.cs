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
