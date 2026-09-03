using System.Numerics;
using Danslicer.Core;
using Danslicer.Core.Commands;
using Danslicer.Core.Supports;
using Danslicer.Core.Supports.Generation;

namespace Danslicer.Tests;

public sealed class SupportGenerationBatchTests
{
    [Fact]
    public void BatchesCommitIncrementallyThenCollapseToOneUndoStep()
    {
        var graph = new SupportGraph();
        var history = new UndoStack();
        var prepared = Prepared(3);
        var batch = new SupportGenerationBatch(graph, history, prepared, batchSize: 2);

        Assert.True(batch.CommitNextBatch());
        Assert.Equal(2, graph.NodeCount);
        Assert.False(history.CanUndo);
        while (!batch.IsFinished) batch.CommitNextBatch();
        batch.Complete();

        Assert.Equal("Generate supports", history.UndoName);
        Assert.True(history.Undo());
        Assert.Equal(0, graph.NodeCount);
        Assert.Equal(0, graph.SegmentCount);
        Assert.True(history.Redo());
        Assert.Equal(3, graph.NodeCount);
        Assert.Equal(2, graph.SegmentCount);
    }

    [Fact]
    public void CancelRestoresTheExactPreRunGraphWithoutHistory()
    {
        var graph = new SupportGraph();
        var existing = new SupportNode { Type = SupportNodeType.Base, Position = Vector3.Zero };
        graph.AddNode(existing);
        var history = new UndoStack();
        var batch = new SupportGenerationBatch(graph, history, Prepared(4), batchSize: 3);

        batch.CommitNextBatch();
        batch.CommitNextBatch();
        batch.Cancel();

        Assert.Single(graph.Nodes);
        Assert.Same(existing, graph.Nodes.Single());
        Assert.Empty(graph.Segments);
        Assert.False(history.CanUndo);
    }

    [Fact]
    public void BatchSizeDoesNotChangeThePreparedDeterministicResult()
    {
        var prepared = Prepared(6);
        var batchedGraph = new SupportGraph();
        var oneShotGraph = new SupportGraph();
        var batched = new SupportGenerationBatch(batchedGraph, new UndoStack(), prepared, 1);
        var oneShot = new SupportGenerationBatch(oneShotGraph, new UndoStack(), prepared, int.MaxValue);

        while (!batched.IsFinished) batched.CommitNextBatch();
        oneShot.CommitNextBatch();

        Assert.Equal(oneShotGraph.Nodes.OrderBy(n => n.Id).Select(n => (n.Id, n.Position)),
            batchedGraph.Nodes.OrderBy(n => n.Id).Select(n => (n.Id, n.Position)));
        Assert.Equal(oneShotGraph.Segments.OrderBy(s => s.Id).Select(s => (s.Id, s.NodeA, s.NodeB)),
            batchedGraph.Segments.OrderBy(s => s.Id).Select(s => (s.Id, s.NodeA, s.NodeB)));
    }

    private static PreparedSupportGeneration Prepared(int count)
    {
        var nodes = Enumerable.Range(0, count).Select(i => new SupportNode
        {
            Type = i == 0 ? SupportNodeType.Tip : i == count - 1 ? SupportNodeType.Base : SupportNodeType.Junction,
            Position = new Vector3(0, 0, count - i),
        }).ToList();
        var segments = Enumerable.Range(0, count - 1).Select(i => new SupportSegment
        {
            Type = i == 0 ? SupportSegmentType.Tip : SupportSegmentType.Branch,
            NodeA = nodes[i].Id,
            NodeB = nodes[i + 1].Id,
        }).ToList();
        return new PreparedSupportGeneration(nodes, segments,
            new SupportGenerationSummary(1, 1, 0));
    }
}
