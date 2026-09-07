using System.Numerics;
using Danslicer.Core.Config;
using Danslicer.Core.Supports;
using Danslicer.Core;

namespace Danslicer.Tests;

public sealed class SupportDisplayPolicyTests
{
    [Fact]
    public void FullDefaultsShowEveryElementTypeAndContactMarkers()
    {
        var display = new SupportDisplayConfig();

        Assert.True(SupportDisplayPolicy.ShowsMeshes(display));
        Assert.False(SupportDisplayPolicy.ShowsLines(display));
        Assert.True(SupportDisplayPolicy.ShowsContactMarkers(display));
        Assert.All(Enum.GetValues<SupportSegmentType>(),
            type => Assert.True(SupportDisplayPolicy.IsSegmentDisplayed(type, display)));
    }

    [Theory]
    [InlineData(SupportDisplayMode.ContactPoints, false, false, false, true)]
    [InlineData(SupportDisplayMode.Lines, false, true, true, true)]
    [InlineData(SupportDisplayMode.Tips, true, false, false, true)]
    [InlineData(SupportDisplayMode.Transparent, true, false, true, true)]
    public void FocusedModesHaveFixedScope(SupportDisplayMode mode, bool meshes, bool lines,
        bool branch, bool markers)
    {
        var display = new SupportDisplayConfig { Mode = mode };

        Assert.Equal(meshes, SupportDisplayPolicy.ShowsMeshes(display));
        Assert.Equal(lines, SupportDisplayPolicy.ShowsLines(display));
        Assert.Equal(branch,
            SupportDisplayPolicy.IsSegmentDisplayed(SupportSegmentType.Branch, display));
        Assert.Equal(markers, SupportDisplayPolicy.ShowsContactMarkers(display));
        Assert.Equal(mode is SupportDisplayMode.Lines or SupportDisplayMode.Tips or
            SupportDisplayMode.Transparent,
            SupportDisplayPolicy.IsSegmentDisplayed(SupportSegmentType.Tip, display));
    }

    [Theory]
    [InlineData(SupportDisplayMode.Full)]
    [InlineData(SupportDisplayMode.Transparent)]
    public void ElementSwitchesFilterFullAndTransparent(SupportDisplayMode mode)
    {
        var display = new SupportDisplayConfig
        {
            Mode = mode,
            ShowTips = false,
            ShowBranches = false,
            ShowTrunks = false,
            ShowBases = false,
            ShowBracing = false,
        };
        var (graph, nodes, segments) = CompleteGraph();

        Assert.All(segments, segment =>
            Assert.False(SupportDisplayPolicy.IsElementDisplayed(graph, segment.Id, display)));
        Assert.All(nodes, node =>
            Assert.False(SupportDisplayPolicy.IsElementDisplayed(graph, node.Id, display)));
    }

    [Fact]
    public void FocusedModesIgnoreFullAndTransparentElementSwitches()
    {
        var hiddenBySwitches = new SupportDisplayConfig
        {
            Mode = SupportDisplayMode.Lines,
            ShowTips = false,
            ShowBranches = false,
            ShowTrunks = false,
            ShowBases = false,
            ShowBracing = false,
        };

        Assert.All(Enum.GetValues<SupportSegmentType>(), type =>
            Assert.True(SupportDisplayPolicy.IsSegmentDisplayed(type, hiddenBySwitches)));
    }

    [Fact]
    public void TransparentContactMarkerToggleControlsTipNodeVisibility()
    {
        var (graph, nodes, _) = CompleteGraph();
        var display = new SupportDisplayConfig
        {
            Mode = SupportDisplayMode.Transparent,
            ShowContactPointsInTransparent = false,
        };

        Assert.False(SupportDisplayPolicy.ShowsContactMarkers(display));
        Assert.All(nodes.Where(node => node.Type == SupportNodeType.Tip), node =>
            Assert.False(SupportDisplayPolicy.IsElementDisplayed(graph, node.Id, display)));
        Assert.True(SupportDisplayPolicy.IsSegmentDisplayed(SupportSegmentType.Tip, display));
    }

    [Fact]
    public void ContactPointModeSelectsOnlyTipMarkers()
    {
        var display = new SupportDisplayConfig { Mode = SupportDisplayMode.ContactPoints };
        var (graph, nodes, _) = CompleteGraph();

        var ids = SupportMarqueeSelection.ElementsInside(graph, point => new(point.X, point.Y),
            new Vector2(-10), new Vector2(10), includeNode: node =>
                SupportDisplayPolicy.IsNodeDisplayed(graph, node, display),
            includeSegment: segment =>
                SupportDisplayPolicy.IsSegmentDisplayed(segment.Type, display));

        Assert.Equal(nodes.Where(node => node.Type == SupportNodeType.Tip)
            .Select(node => node.Id).ToHashSet(), ids.ToHashSet());
    }

    [Fact]
    public void JunctionWithoutAnyDrawnIncidentMemberIsNotSelectable()
    {
        var (graph, nodes, segments) = CompleteGraph();
        foreach (var segment in segments) segment.Hidden = true;

        Assert.False(SupportDisplayPolicy.IsElementDisplayed(
            graph, nodes[2].Id, new SupportDisplayConfig()));
    }

    [Fact]
    public void ClipRangeFiltersNodesAndOnlyWhollyOutsideSegments()
    {
        var (graph, nodes, segments) = CompleteGraph();
        var clip = new ViewportClipRange(0, 5, 3.5f, 4.5f, Active: true);
        var display = new SupportDisplayConfig();

        Assert.False(SupportDisplayPolicy.IsElementDisplayed(graph, nodes[0].Id, display, clip));
        Assert.True(SupportDisplayPolicy.IsElementDisplayed(graph, nodes[2].Id, display, clip));
        Assert.True(SupportDisplayPolicy.IsElementDisplayed(graph, segments[0].Id, display, clip));
        Assert.False(SupportDisplayPolicy.IsElementDisplayed(graph, segments[3].Id, display, clip));
    }

    private static (SupportGraph Graph, SupportNode[] Nodes, SupportSegment[] Segments) CompleteGraph()
    {
        var graph = new SupportGraph();
        var nodes = new[]
        {
            new SupportNode { Type = SupportNodeType.Tip, Position = new(-3, 0, 5) },
            new SupportNode { Type = SupportNodeType.Tip, Position = new(-2, 0, 5) },
            new SupportNode { Type = SupportNodeType.Junction, Position = new(-1, 0, 4) },
            new SupportNode { Type = SupportNodeType.Junction, Position = new(0, 0, 3) },
            new SupportNode { Type = SupportNodeType.Junction, Position = new(1, 0, 2) },
            new SupportNode { Type = SupportNodeType.Base, Position = new(2, 0, 0), BaseShape = SupportBaseShape.Disc },
        };
        foreach (var node in nodes) graph.AddNode(node);
        var segments = new[]
        {
            Segment(SupportSegmentType.Tip, nodes[0], nodes[2]),
            Segment(SupportSegmentType.Tip, nodes[1], nodes[2]),
            Segment(SupportSegmentType.Branch, nodes[2], nodes[3]),
            Segment(SupportSegmentType.Trunk, nodes[3], nodes[5]),
            Segment(SupportSegmentType.Bracing, nodes[3], nodes[4]),
        };
        foreach (var segment in segments) graph.AddSegment(segment);
        return (graph, nodes, segments);
    }

    private static SupportSegment Segment(SupportSegmentType type, SupportNode a, SupportNode b) =>
        new() { Type = type, NodeA = a.Id, NodeB = b.Id };
}
