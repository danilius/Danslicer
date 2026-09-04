using Danslicer.Core.Commands;

namespace Danslicer.Core.Supports.Generation;

/// <summary>A fully computed generation pass, ready for UI-thread insertion.</summary>
public sealed record PreparedSupportGeneration(
    IReadOnlyList<SupportNode> Nodes,
    IReadOnlyList<SupportSegment> Segments,
    SupportGenerationSummary Summary,
    string UndoName = "Generate supports");

/// <summary>
/// UI-thread batching state machine. It applies a prepared deterministic result incrementally,
/// records exactly one undo command on completion, and removes every applied element on cancel.
/// </summary>
public sealed class SupportGenerationBatch
{
    private readonly SupportGraph _graph;
    private readonly UndoStack _history;
    private readonly PreparedSupportGeneration _prepared;
    private readonly int _batchSize;
    private int _nodeIndex;
    private int _segmentIndex;

    public SupportGenerationBatch(SupportGraph graph, UndoStack history,
        PreparedSupportGeneration prepared, int batchSize = 32)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batchSize);
        _graph = graph;
        _history = history;
        _prepared = prepared;
        _batchSize = batchSize;
    }

    public int TotalElements => _prepared.Nodes.Count + _prepared.Segments.Count;
    public int CommittedElements => _nodeIndex + _segmentIndex;
    public double Progress => TotalElements == 0 ? 1 : (double)CommittedElements / TotalElements;
    public bool IsFinished => CommittedElements == TotalElements;

    public bool CommitNextBatch()
    {
        if (IsFinished) return false;
        var remaining = _batchSize;
        while (remaining > 0 && _nodeIndex < _prepared.Nodes.Count)
        {
            _graph.AddNode(_prepared.Nodes[_nodeIndex++]);
            remaining--;
        }
        while (remaining > 0 && _segmentIndex < _prepared.Segments.Count)
        {
            _graph.AddSegment(_prepared.Segments[_segmentIndex++]);
            remaining--;
        }
        return true;
    }

    public void Complete()
    {
        if (!IsFinished) throw new InvalidOperationException("Every batch must be committed before completion.");
        if (TotalElements == 0) return;
        _history.RecordExecuted(new AddSupportElementsCommand(_graph, _prepared.Nodes,
            _prepared.Segments, _prepared.UndoName));
    }

    public void Cancel()
    {
        for (var i = _segmentIndex - 1; i >= 0; i--)
            if (_graph.TryGetSegment(_prepared.Segments[i].Id, out _))
                _graph.RemoveSegment(_prepared.Segments[i].Id);
        for (var i = _nodeIndex - 1; i >= 0; i--)
            if (_graph.TryGetNode(_prepared.Nodes[i].Id, out _))
                _graph.RemoveNode(_prepared.Nodes[i].Id);
        _nodeIndex = 0;
        _segmentIndex = 0;
    }
}
