using Danslicer.Core.Config;

namespace Danslicer.Core.Supports;

/// <summary>
/// Single viewport visibility policy shared by drawing and selection. Graph Hidden state remains
/// authoritative; this policy adds only the temporary display-mode filter.
/// </summary>
public static class SupportDisplayPolicy
{
    /// <summary>
    /// The display config as a given workspace should actually see it. In Layout a model and its
    /// supports are one object being arranged, so supports are ALWAYS drawn in full there: every
    /// reduced mode (hidden, tips only, contact points, lines, transparent) is a Support-mode
    /// working aid and is demoted to Full, and the per-part toggles come back on with it.
    /// Switching to Layout must never leave a supported model looking bare, whatever the user
    /// last set while working on its supports.
    ///
    /// <para>Both render paths and the picking code must be handed the SAME value from this
    /// method rather than each deciding for itself — that is the whole point of routing it
    /// through here, and it is why transparency-driven draw ordering and hit testing cannot
    /// disagree about what the user is looking at.</para>
    /// </summary>
    public static SupportDisplayConfig ForWorkspace(SupportDisplayConfig display, bool isLayoutView) =>
        !isLayoutView
            ? display
            : display with
            {
                Mode = SupportDisplayMode.Full,
                ShowHiddenElements = true,
                ShowTips = true,
                ShowBranches = true,
                ShowTrunks = true,
                ShowBases = true,
                ShowBracing = true,
                ShowRafts = true,
            };

    /// <summary>
    /// Whether an element's own Hidden flag currently hides it. Layout answers no to everything
    /// (see <see cref="ForWorkspace"/>); Support mode answers with the flag itself.
    /// </summary>
    public static bool IsHiddenBy(bool hidden, SupportDisplayConfig display) =>
        hidden && !display.ShowHiddenElements;

    /// <summary>Rafts draw with the meshes of the Full and Transparent modes, under their own switch.</summary>
    public static bool ShowsRafts(SupportDisplayConfig display) =>
        display.ShowRafts && display.Mode is SupportDisplayMode.Full or SupportDisplayMode.Transparent;

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
            SupportDisplayMode.Tips => type is SupportSegmentType.Tip,
            SupportDisplayMode.Full or SupportDisplayMode.Transparent => type switch
            {
                SupportSegmentType.Tip => display.ShowTips,
                SupportSegmentType.Branch => display.ShowBranches,
                SupportSegmentType.Trunk => display.ShowTrunks,
                SupportSegmentType.Bracing => display.ShowBracing,
                _ => false,
            },
            _ => false,
        };

    public static bool IsNodeDisplayed(SupportGraph graph, SupportNode node,
        SupportDisplayConfig display, ViewportClipRange clip = default) => clip.Contains(node.Position) &&
        node.Type switch
        {
            SupportNodeType.Tip => IsTipMarkerDisplayed(graph, node, display),
            SupportNodeType.Base => display.ShowBases &&
                (display.Mode is SupportDisplayMode.Full or SupportDisplayMode.Transparent),
            SupportNodeType.Junction => graph.SegmentsAt(node.Id)
                .Any(segment => !IsHiddenBy(segment.Hidden, display) &&
                    IsSegmentDisplayed(segment.Type, display) &&
                    !IsHiddenBy(graph.GetNode(segment.NodeA == node.Id ? segment.NodeB : segment.NodeA)
                        .Hidden, display)),
            _ => false,
        };

    public static bool IsSegmentDisplayed(SupportGraph graph, SupportSegment segment,
        SupportDisplayConfig display, ViewportClipRange clip = default) =>
        IsSegmentDisplayed(segment.Type, display) &&
        clip.TryClipSegment(graph.GetNode(segment.NodeA).Position,
            graph.GetNode(segment.NodeB).Position, out _, out _);

    private static bool IsTipMarkerDisplayed(SupportGraph graph, SupportNode node,
        SupportDisplayConfig display)
    {
        if (!ShowsContactMarkers(display)) return false;
        if (display.Mode is not (SupportDisplayMode.Full or SupportDisplayMode.Transparent))
            return true;
        var incident = graph.SegmentsAt(node.Id);
        // A bare tip retains regular-tip visibility.
        return incident.Count == 0 ? display.ShowTips :
            incident.Any(segment => IsSegmentDisplayed(segment.Type, display));
    }

    public static bool IsElementDisplayed(SupportGraph graph, Guid id,
        SupportDisplayConfig display, ViewportClipRange clip = default)
    {
        if (graph.TryGetNode(id, out var node))
            return !IsHiddenBy(node.Hidden, display) && IsNodeDisplayed(graph, node, display, clip);
        if (!graph.TryGetSegment(id, out var segment) || IsHiddenBy(segment.Hidden, display) ||
            !IsSegmentDisplayed(graph, segment, display, clip)) return false;
        return !IsHiddenBy(graph.GetNode(segment.NodeA).Hidden, display) &&
               !IsHiddenBy(graph.GetNode(segment.NodeB).Hidden, display);
    }

    public static IEnumerable<Guid> DisplayedElementIds(SupportGraph graph,
        SupportDisplayConfig display, ViewportClipRange clip = default)
    {
        foreach (var node in graph.Nodes)
            if (IsElementDisplayed(graph, node.Id, display, clip)) yield return node.Id;
        foreach (var segment in graph.Segments)
            if (IsElementDisplayed(graph, segment.Id, display, clip)) yield return segment.Id;
    }
}
