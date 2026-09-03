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

    private static Guid OtherEnd(SupportSegment segment, Guid from) =>
        segment.NodeA == from ? segment.NodeB : segment.NodeA;
}
