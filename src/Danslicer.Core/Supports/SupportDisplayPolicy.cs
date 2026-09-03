using Danslicer.Core.Config;

namespace Danslicer.Core.Supports;

/// <summary>
/// Single viewport visibility policy shared by drawing and selection. Graph Hidden state remains
/// authoritative; this policy adds only the temporary display-mode filter.
/// </summary>
public static class SupportDisplayPolicy
{
    public static bool ShowsMeshes(SupportDisplayConfig display) =>
        display.Mode is SupportDisplayMode.Full or SupportDisplayMode.Tips or
            SupportDisplayMode.Transparent;

    public static bool ShowsLines(SupportDisplayConfig display) =>
        display.Mode == SupportDisplayMode.Lines;

    public static bool ShowsContactMarkers(SupportDisplayConfig display) => display.Mode switch
    {
        SupportDisplayMode.Full or SupportDisplayMode.ContactPoints or
            SupportDisplayMode.Lines or SupportDisplayMode.Tips => true,
        SupportDisplayMode.Transparent => display.ShowContactPointsInTransparent,
        _ => false,
    };

    public static bool IsSegmentDisplayed(SupportSegmentType type, SupportDisplayConfig display) =>
        display.Mode switch
        {
            SupportDisplayMode.ContactPoints => false,
            SupportDisplayMode.Lines => true,
            SupportDisplayMode.Tips => type is SupportSegmentType.Tip or SupportSegmentType.MiniSupport,
            SupportDisplayMode.Full or SupportDisplayMode.Transparent => type switch
            {
                SupportSegmentType.Tip => display.ShowTips,
                SupportSegmentType.MiniSupport => display.ShowMiniSupports,
                SupportSegmentType.Branch => display.ShowBranches,
                SupportSegmentType.Trunk => display.ShowTrunks,
                SupportSegmentType.Bracing => display.ShowBracing,
                _ => false,
            },
            _ => false,
        };

    public static bool IsNodeDisplayed(SupportGraph graph, SupportNode node,
        SupportDisplayConfig display) => node.Type switch
    {
        SupportNodeType.Tip => IsTipMarkerDisplayed(graph, node, display),
        SupportNodeType.Base => display.ShowBases &&
            (display.Mode is SupportDisplayMode.Full or SupportDisplayMode.Transparent),
        SupportNodeType.Junction => graph.SegmentsAt(node.Id)
            .Any(segment => !segment.Hidden && IsSegmentDisplayed(segment.Type, display) &&
                !graph.GetNode(segment.NodeA == node.Id ? segment.NodeB : segment.NodeA).Hidden),
        _ => false,
    };

    private static bool IsTipMarkerDisplayed(SupportGraph graph, SupportNode node,
        SupportDisplayConfig display)
    {
        if (!ShowsContactMarkers(display)) return false;
        if (display.Mode is not (SupportDisplayMode.Full or SupportDisplayMode.Transparent))
            return true;
        var incident = graph.SegmentsAt(node.Id);
        // A bare tip predates the mini taxonomy and retains regular-tip visibility.
        return incident.Count == 0 ? display.ShowTips :
            incident.Any(segment => IsSegmentDisplayed(segment.Type, display));
    }

    public static bool IsElementDisplayed(SupportGraph graph, Guid id,
        SupportDisplayConfig display)
    {
        if (graph.TryGetNode(id, out var node))
            return !node.Hidden && IsNodeDisplayed(graph, node, display);
        if (!graph.TryGetSegment(id, out var segment) || segment.Hidden ||
            !IsSegmentDisplayed(segment.Type, display)) return false;
        return !graph.GetNode(segment.NodeA).Hidden && !graph.GetNode(segment.NodeB).Hidden;
    }

    public static IEnumerable<Guid> DisplayedElementIds(SupportGraph graph,
        SupportDisplayConfig display)
    {
        foreach (var node in graph.Nodes)
            if (IsElementDisplayed(graph, node.Id, display)) yield return node.Id;
        foreach (var segment in graph.Segments)
            if (IsElementDisplayed(graph, segment.Id, display)) yield return segment.Id;
    }
}
