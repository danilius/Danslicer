using Danslicer.Core.Supports.Routing;

namespace Danslicer.Core.Supports;

/// <summary>
/// Turns a routing edit into a small graph of its own for drawing before it is applied: the
/// placement ghost. An edit that branches onto an existing trunk carries a segment whose far
/// end is a node of the document's graph, not of the edit, so that node is borrowed (cloned
/// with its id) and the branch draws to where it will really join. A segment whose end is in
/// neither graph is left out rather than thrown on (the crash of 2026-09-09).
/// </summary>
public static class SupportEditPreview
{
    public static SupportGraph ToGraph(this SupportGraphEdit edit, SupportGraph? existing)
    {
        var ghost = new SupportGraph();
        foreach (var node in edit.AddedNodes) ghost.AddNode(node);
        foreach (var segment in edit.AddedSegments)
        {
            Borrow(segment.NodeA);
            Borrow(segment.NodeB);
            if (ghost.TryGetNode(segment.NodeA, out _) && ghost.TryGetNode(segment.NodeB, out _))
                ghost.AddSegment(segment);
        }
        return ghost;

        void Borrow(Guid id)
        {
            if (ghost.TryGetNode(id, out _) || existing is null || !existing.TryGetNode(id, out var node)) return;
            ghost.AddNode(node.Clone(node.Id, node.Origin));
        }
    }
}
