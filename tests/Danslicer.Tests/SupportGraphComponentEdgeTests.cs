using System;
using System.Linq;
using System.Numerics;
using Danslicer.Core.Supports;
using Xunit;

namespace Danslicer.Tests;

public class SupportGraphComponentEdgeTests
{
    private static SupportNode Node(SupportNodeType type, float x = 0, float z = 0) =>
        new() { Type = type, Position = new Vector3(x, 0, z) };

    private static SupportSegment Join(SupportNode a, SupportNode b, SupportSegmentType type = SupportSegmentType.Branch) =>
        new() { Type = type, NodeA = a.Id, NodeB = b.Id };

    /// <summary>Creates a simple tip-junction-trunk-base tree.</summary>
    private static (SupportGraph Graph, SupportNode Base, SupportNode Junction, SupportNode Tip) SimpleTree(float offsetX = 0, float offsetZ = 0)
    {
        var g = new SupportGraph();
        var b = Node(SupportNodeType.Base, offsetX, offsetZ);
        var j = Node(SupportNodeType.Junction, offsetX, offsetZ + 10);
        var t = Node(SupportNodeType.Tip, offsetX, offsetZ + 15);
        g.AddNode(b); g.AddNode(j); g.AddNode(t);
        g.AddSegment(Join(b, j));
        g.AddSegment(Join(j, t, SupportSegmentType.Tip));
        return (g, b, j, t);
    }

    [Fact]
    public void ComponentExcludesBracingSegments()
    {
        var (g, b1, _, t1) = SimpleTree();
        var (g2, b2, _, t2) = SimpleTree(10, 10);
        g.AddNode(b2); g.AddNode(t2);
        g.AddSegment(Join(b1, b2, SupportSegmentType.Bracing));

        var component1 = g.Component(t1.Id);
        var component2 = g.Component(t2.Id);

        Assert.DoesNotContain(b2.Id, component1.Nodes);
        Assert.DoesNotContain(b1.Id, component2.Nodes);
    }

    [Fact]
    public void ComponentIncludesBranchSegments()
    {
        var (g, b1, _, t1) = SimpleTree();
        var (g2, b2, _, t2) = SimpleTree(10, 10);
        g.AddNode(b2); g.AddNode(t2);
        g.AddSegment(Join(b1, b2, SupportSegmentType.Branch));
        g.AddSegment(Join(b2, t2, SupportSegmentType.Branch));

        var component1 = g.Component(t1.Id);
        var component2 = g.Component(t2.Id);

        Assert.Contains(b2.Id, component1.Nodes);
        Assert.Contains(b1.Id, component2.Nodes);
    }

    [Fact]
    public void HiddenFlagDoesNotAffectComponentMembership()
    {
        var (g, b, j, t) = SimpleTree();
        j.Hidden = true;

        var component = g.Component(t.Id);

        Assert.Contains(j.Id, component.Nodes);
    }

    [Fact]
    public void ComponentOfIsolatedNodeIsJustThatNode()
    {
        var g = new SupportGraph();
        var isolatedNode = Node(SupportNodeType.Base, 5, 5);
        g.AddNode(isolatedNode);

        var component = g.Component(isolatedNode.Id);

        Assert.Single(component.Nodes);
        Assert.Contains(isolatedNode.Id, component.Nodes);
        Assert.Empty(component.Segments);
    }
}
