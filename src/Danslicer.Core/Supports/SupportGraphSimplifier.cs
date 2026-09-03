using System.Numerics;

namespace Danslicer.Core.Supports;

public static class SupportGraphSimplifier
{
    /// <summary>
    /// Collapses runs of collinear, same-type, same-diameter segments through two-segment
    /// junctions into single segments. Step-based routing emits a junction every step even on a
    /// straight descent; collapsing restores the minimal tip-junction-base shape that editing
    /// tools (tip drag, element selection) expect. Necks never merge into pillars because the
    /// segment types differ.
    /// </summary>
    public static void CollapseCollinearJunctions(SupportGraph graph)
    {
        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var node in graph.Nodes.Where(n => n.Type == SupportNodeType.Junction).ToList())
            {
                var segments = graph.SegmentsAt(node.Id);
                if (segments.Count != 2) continue;
                var (first, second) = (segments[0], segments[1]);
                if (first.Type != second.Type ||
                    MathF.Abs(first.Diameter - second.Diameter) > 1e-5f ||
                    first.Pinned != second.Pinned ||
                    first.Hidden != second.Hidden ||
                    first.Disabled != second.Disabled) continue;

                var aId = first.NodeA == node.Id ? first.NodeB : first.NodeA;
                var bId = second.NodeA == node.Id ? second.NodeB : second.NodeA;
                if (aId == bId) continue;
                var into = graph.GetNode(node.Id).Position;
                var d1 = into - graph.GetNode(aId).Position;
                var d2 = graph.GetNode(bId).Position - into;
                if (d1.LengthSquared() < 1e-12f || d2.LengthSquared() < 1e-12f) continue;
                if (Vector3.Dot(Vector3.Normalize(d1), Vector3.Normalize(d2)) < 1f - 1e-4f) continue;

                graph.RemoveNode(node.Id); // takes both segments with it
                graph.AddSegment(new SupportSegment
                {
                    Type = first.Type,
                    NodeA = aId,
                    NodeB = bId,
                    Diameter = first.Diameter,
                    Origin = first.Origin,
                    Pinned = first.Pinned,
                    Hidden = first.Hidden,
                    Disabled = first.Disabled,
                });
                changed = true;
            }
        }
    }
}
