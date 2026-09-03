using System.Numerics;

namespace Danslicer.Core.Supports;

/// <summary>Pure screen-space hit logic for support marquee selection.</summary>
public static class SupportMarqueeSelection
{
    public static IReadOnlyList<Guid> ElementsInside(SupportGraph graph,
        Func<Vector3, Vector2?> project, Vector2 cornerA, Vector2 cornerB,
        Func<Vector3, bool>? isVisible = null)
    {
        var min = Vector2.Min(cornerA, cornerB);
        var max = Vector2.Max(cornerA, cornerB);
        bool Inside(Vector3 point) => project(point) is { } p &&
            p.X >= min.X && p.X <= max.X && p.Y >= min.Y && p.Y <= max.Y &&
            (isVisible?.Invoke(point) ?? true);

        var ids = new List<Guid>();
        foreach (var node in graph.Nodes.OrderBy(node => node.Id))
            if (!node.Hidden && Inside(node.Position)) ids.Add(node.Id);
        foreach (var segment in graph.Segments.OrderBy(segment => segment.Id))
        {
            if (segment.Hidden) continue;
            var a = graph.GetNode(segment.NodeA);
            var b = graph.GetNode(segment.NodeB);
            if (a.Hidden || b.Hidden) continue;
            if (Inside((a.Position + b.Position) * 0.5f)) ids.Add(segment.Id);
        }
        return ids;
    }
}
