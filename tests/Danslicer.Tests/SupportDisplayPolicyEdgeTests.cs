using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Danslicer.Core.Config;
using Danslicer.Core.Supports;
using Xunit;

namespace Danslicer.Tests;

public sealed class SupportDisplayPolicyEdgeTests
{
    [Fact]
    public void TipsModeIgnoresShowBranchesToggle()
    {
        var display = new SupportDisplayConfig
        {
            Mode = SupportDisplayMode.Tips,
            ShowBranches = true,
        };
        var (graph, _, segments) = CompleteGraph();
        var branchSegment = segments.First(segment => segment.Type == SupportSegmentType.Branch);

        Assert.False(SupportDisplayPolicy.IsSegmentDisplayed(branchSegment.Type, display));
    }

    [Fact]
    public void LinesModeDisplaysAllNonHiddenSegmentTypes()
    {
        var display = new SupportDisplayConfig { Mode = SupportDisplayMode.Lines };
        var (graph, _, segments) = CompleteGraph();

        Assert.All(segments, segment =>
            Assert.True(SupportDisplayPolicy.IsSegmentDisplayed(segment.Type, display)));
    }

    [Fact]
    public void JunctionNodeWithHiddenIncidentSegmentIsDisplayedButSegmentIsNot()
    {
        var display = new SupportDisplayConfig();
        var (graph, nodes, segments) = CompleteGraph();
        var junctionNode = nodes.First(node => node.Type == SupportNodeType.Junction);
        var incidentSegment = segments.First(segment => segment.NodeA == junctionNode.Id || segment.NodeB == junctionNode.Id);
        incidentSegment.Hidden = true;

        Assert.True(SupportDisplayPolicy.IsNodeDisplayed(graph, junctionNode, display));
        Assert.False(SupportDisplayPolicy.IsElementDisplayed(graph, incidentSegment.Id, display));
    }

    [Fact]
    public void JunctionNodeWithHiddenEndpointNodeIsNotDisplayed()
    {
        var display = new SupportDisplayConfig();
        var (graph, nodes, segments) = CompleteGraph();
        var junctionNode = nodes.First(node => node.Type == SupportNodeType.Junction);
        var incidentSegment = segments.First(segment => segment.NodeA == junctionNode.Id || segment.NodeB == junctionNode.Id);
        var endpointNode = graph.GetNode(incidentSegment.NodeA == junctionNode.Id ? incidentSegment.NodeB : incidentSegment.NodeA);
        endpointNode.Hidden = true;

        Assert.False(SupportDisplayPolicy.IsElementDisplayed(graph, incidentSegment.Id, display));
    }

    [Fact]
    public void TransparentModeWithShowTipsAndWithoutContactPointsDoesNotShowContactMarkers()
    {
        var display = new SupportDisplayConfig
        {
            Mode = SupportDisplayMode.Transparent,
            ShowTips = true,
            ShowContactPointsInTransparent = false,
        };

        Assert.False(SupportDisplayPolicy.ShowsContactMarkers(display));
    }

    [Fact]
    public void TransparentModeWithShowTipsAndWithContactPointsShowsContactMarkers()
    {
        var display = new SupportDisplayConfig
        {
            Mode = SupportDisplayMode.Transparent,
            ShowTips = true,
            ShowContactPointsInTransparent = true,
        };

        Assert.True(SupportDisplayPolicy.ShowsContactMarkers(display));
    }

    [Fact]
    public void DisplayedElementIdsInContactPointsModeIncludesOnlyTips()
    {
        var display = new SupportDisplayConfig { Mode = SupportDisplayMode.ContactPoints };
        var (graph, nodes, segments) = CompleteGraph();

        var displayedIds = SupportDisplayPolicy.DisplayedElementIds(graph, display).ToList();

        Assert.Empty(displayedIds.Intersect(segments.Select(segment => segment.Id)));
        Assert.All(nodes.Where(node => node.Type == SupportNodeType.Tip).Select(node => node.Id),
            id => Assert.Contains(id, displayedIds));
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
            Segment(SupportSegmentType.MiniSupport, nodes[1], nodes[2]),
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
