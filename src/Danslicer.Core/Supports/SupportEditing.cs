using System.Numerics;

namespace Danslicer.Core.Supports;

/// <summary>Editing helpers over the support graph shared by interactive tools and commands.</summary>
public static class SupportEditing
{
    /// <summary>
    /// The nodes a vertical tip move drags along: the tip itself and, when the tip hangs on the
    /// simple manual pattern (one neck to a junction whose other member is a pillar to a base),
    /// that junction and base. Anything more connected moves only the tip.
    /// </summary>
    public static List<SupportNode> AffectedByTipMove(SupportGraph graph, Guid tipId)
    {
        var tip = graph.GetNode(tipId);
        var affected = new List<SupportNode> { tip };
        if (tip.Type != SupportNodeType.Tip) return affected;

        var tipSegments = graph.SegmentsAt(tipId);
        if (tipSegments.Count != 1) return affected;
        var junction = graph.GetNode(OtherEnd(tipSegments[0], tipId));
        if (junction.Type != SupportNodeType.Junction) return affected;

        var junctionSegments = graph.SegmentsAt(junction.Id);
        if (junctionSegments.Count != 2) return affected;
        var down = junctionSegments[0].Id == tipSegments[0].Id ? junctionSegments[1] : junctionSegments[0];
        var baseNode = graph.GetNode(OtherEnd(down, junction.Id));
        if (baseNode.Type != SupportNodeType.Base) return affected;

        affected.Add(junction);
        affected.Add(baseNode);
        return affected;
    }

    /// <summary>
    /// Moves a tip to a new contact and re-drops its simple vertical tree: the junction stays a
    /// neck-length below the contact and the base follows in XY on the plate. Mutates positions in
    /// place; the caller owns undo (capture positions before, commit after).
    /// </summary>
    public static void MoveTipVertical(SupportGraph graph, Guid tipId, Vector3 contact, Vector3 surfaceNormal)
    {
        const float neckLength = 2f;
        var affected = AffectedByTipMove(graph, tipId);
        var tip = affected[0];
        tip.Position = contact;
        tip.SurfaceNormal = surfaceNormal;
        if (affected.Count == 3)
        {
            affected[1].Position = contact with { Z = MathF.Max(contact.Z - neckLength, 0.1f) };
            affected[2].Position = contact with { Z = 0 };
        }
        graph.NotifyChanged();
    }

    // ----- Edit mode handles (user, 2026-09-08: click a support, Space, move base / trunk / tip) -----

    /// <summary>
    /// The grab points of one support in edit mode: every node of the support except brace
    /// ends, and one <see cref="SupportHandleKind.Trunk"/> handle at the middle of each trunk
    /// segment. Bracing is excluded the way whole-support selection excludes it; the seed may
    /// be any node or segment of the support.
    /// </summary>
    public static List<SupportHandle> HandlesOf(SupportGraph graph, Guid elementId)
    {
        var handles = new List<SupportHandle>();
        Guid seed;
        if (graph.TryGetNode(elementId, out var seedNode)) seed = seedNode.Id;
        else if (graph.TryGetSegment(elementId, out var seedSegment)) seed = seedSegment.NodeA;
        else return handles;

        var (nodes, segments) = graph.Component(seed);
        foreach (var id in nodes)
        {
            var node = graph.GetNode(id);
            var kind = node.Type switch
            {
                SupportNodeType.Base => SupportHandleKind.Base,
                SupportNodeType.Junction => SupportHandleKind.Junction,
                SupportNodeType.Tip => SupportHandleKind.Tip,
                _ => (SupportHandleKind?)null,
            };
            if (kind is { } k) handles.Add(new SupportHandle(k, id, node.Position));
        }
        foreach (var id in segments)
        {
            var segment = graph.GetSegment(id);
            if (segment.Type != SupportSegmentType.Trunk) continue;
            var a = graph.GetNode(segment.NodeA).Position;
            var b = graph.GetNode(segment.NodeB).Position;
            handles.Add(new SupportHandle(SupportHandleKind.Trunk, id, (a + b) * 0.5f));
        }
        return handles;
    }

    /// <summary>
    /// The nodes a handle drags: a node handle moves that node alone (the members meeting it
    /// re-aim); a trunk handle moves the whole column — every node reachable from the segment
    /// through trunk segments, base included — together with the brace ends those trunk
    /// segments carry, so the column stays vertical and its braces stay attached.
    /// </summary>
    public static List<SupportNode> AffectedByHandle(SupportGraph graph, SupportHandle handle)
    {
        if (handle.Kind != SupportHandleKind.Trunk)
            return graph.TryGetNode(handle.ElementId, out var node) ? [node] : [];
        if (!graph.TryGetSegment(handle.ElementId, out var seed)) return [];

        var columnNodes = new HashSet<Guid>();
        var columnSegments = new HashSet<Guid> { seed.Id };
        var pending = new Stack<Guid>();
        pending.Push(seed.NodeA);
        pending.Push(seed.NodeB);
        while (pending.Count > 0)
        {
            var id = pending.Pop();
            if (!columnNodes.Add(id)) continue;
            foreach (var segment in graph.SegmentsAt(id))
            {
                if (segment.Type != SupportSegmentType.Trunk) continue;
                columnSegments.Add(segment.Id);
                pending.Push(OtherEnd(segment, id));
            }
        }

        var moved = columnNodes.Select(graph.GetNode).ToList();
        foreach (var node in graph.Nodes)
        {
            if (node.Type != SupportNodeType.BraceEnd) continue;
            if (SupportBracing.CarrierOf(graph, node, columnSegments.Contains) is not null) moved.Add(node);
        }
        return moved;
    }

    /// <summary>
    /// Slides every node to its recorded origin plus a horizontal offset. Mutates positions in
    /// place; the caller owns undo (capture positions before, commit after).
    /// </summary>
    public static void TranslateXY(SupportGraph graph,
        IReadOnlyList<(SupportNode Node, Vector3 Origin)> nodes, Vector2 delta)
    {
        foreach (var (node, origin) in nodes)
            node.Position = origin + new Vector3(delta.X, delta.Y, 0f);
        graph.NotifyChanged();
    }

    /// <summary>The nearest point of the plate-origin-aligned base grid.</summary>
    public static Vector2 SnapToBaseGrid(Vector2 xy, float pitch) =>
        pitch <= 0f ? xy : new Vector2(MathF.Round(xy.X / pitch) * pitch, MathF.Round(xy.Y / pitch) * pitch);

    private static Guid OtherEnd(SupportSegment segment, Guid from) =>
        segment.NodeA == from ? segment.NodeB : segment.NodeA;
}

/// <summary>What a support edit handle stands on.</summary>
public enum SupportHandleKind
{
    /// <summary>A base node: drags in XY on the plate.</summary>
    Base,
    /// <summary>A junction: drags in XY at its own height.</summary>
    Junction,
    /// <summary>A tip: drags across the model surface.</summary>
    Tip,
    /// <summary>A trunk segment: drags the whole column in XY.</summary>
    Trunk,
}

/// <summary>A grab point of a support in edit mode; <see cref="ElementId"/> is a node or, for a trunk, a segment.</summary>
public readonly record struct SupportHandle(SupportHandleKind Kind, Guid ElementId, Vector3 Position);
