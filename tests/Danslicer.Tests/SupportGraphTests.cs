using System.Numerics;
using Danslicer.Core.Supports;

namespace Danslicer.Tests;

public class SupportGraphTests
{
    private static SupportNode Node(SupportNodeType type, float x = 0, float z = 0) =>
        new() { Type = type, Position = new Vector3(x, 0, z) };

    private static SupportSegment Join(SupportNode a, SupportNode b, SupportSegmentType type = SupportSegmentType.Pillar) =>
        new() { Type = type, NodeA = a.Id, NodeB = b.Id };

    /// <summary>Base - pillar - junction, then necks to two tips: one small tree.</summary>
    private static (SupportGraph Graph, SupportNode Base, SupportNode Junction, SupportNode TipA, SupportNode TipB) Tree()
    {
        var g = new SupportGraph();
        var b = Node(SupportNodeType.Base);
        var j = Node(SupportNodeType.Junction, z: 10);
        var t1 = Node(SupportNodeType.Tip, x: -2, z: 15);
        var t2 = Node(SupportNodeType.Tip, x: 2, z: 15);
        g.AddNode(b); g.AddNode(j); g.AddNode(t1); g.AddNode(t2);
        g.AddSegment(Join(b, j));
        g.AddSegment(Join(j, t1, SupportSegmentType.Neck));
        g.AddSegment(Join(j, t2, SupportSegmentType.Neck));
        return (g, b, j, t1, t2);
    }

    [Fact]
    public void ComponentWalksTheWholeTree()
    {
        var (g, b, _, t1, _) = Tree();
        var component = g.Component(t1.Id);
        Assert.Equal(4, component.Nodes.Count);
        Assert.Equal(3, component.Segments.Count);
        Assert.Contains(b.Id, component.Nodes);
    }

    [Fact]
    public void BracingJoinsTreesButNotSupports()
    {
        var (g, b1, j1, _, _) = Tree();
        var b2 = Node(SupportNodeType.Base, x: 10);
        var j2 = Node(SupportNodeType.Junction, x: 10, z: 10);
        g.AddNode(b2); g.AddNode(j2);
        g.AddSegment(Join(b2, j2));
        var brace = Join(j1, j2, SupportSegmentType.Bracing);
        g.AddSegment(brace);

        // Without bracing the two trees are separate supports.
        Assert.Equal(2, g.Supports().Count());
        Assert.DoesNotContain(b2.Id, g.Component(b1.Id).Nodes);

        // With bracing they are one connected component.
        var full = g.Component(b1.Id, includeBracing: true);
        Assert.Contains(b2.Id, full.Nodes);
        Assert.Contains(brace.Id, full.Segments);
    }

    [Fact]
    public void RemovingANodeRemovesItsSegments()
    {
        var (g, _, j, _, _) = Tree();
        g.RemoveNode(j.Id);
        Assert.Equal(3, g.NodeCount);
        Assert.Equal(0, g.SegmentCount);
    }

    [Fact]
    public void RejectsDanglingAndSelfReferentialSegments()
    {
        var g = new SupportGraph();
        var a = Node(SupportNodeType.Base);
        g.AddNode(a);
        Assert.Throws<InvalidOperationException>(() =>
            g.AddSegment(new SupportSegment { Type = SupportSegmentType.Pillar, NodeA = a.Id, NodeB = Guid.NewGuid() }));
        Assert.Throws<InvalidOperationException>(() =>
            g.AddSegment(new SupportSegment { Type = SupportSegmentType.Pillar, NodeA = a.Id, NodeB = a.Id }));
    }

    [Fact]
    public void RegenerationSelectsUnpinnedElementsOfARegionOnly()
    {
        var (g, b, j, t1, _) = Tree();
        var region = Guid.NewGuid();
        var pass = new SupportOrigin(region, 1);
        b.Origin = pass;
        j.Origin = pass;
        t1.Origin = pass;
        j.Pinned = true; // edited by the user: survives regeneration
        foreach (var s in g.Segments) s.Origin = pass;

        var (nodes, segments) = g.UnpinnedElementsOf(region);
        Assert.Contains(b, nodes);
        Assert.Contains(t1, nodes);
        Assert.DoesNotContain(j, nodes);            // pinned
        Assert.Equal(3, segments.Count);
        Assert.Empty(g.UnpinnedElementsOf(Guid.NewGuid()).Nodes); // other regions untouched

        // Manual elements are never selected, even unpinned.
        b.Origin = SupportOrigin.Manual;
        Assert.DoesNotContain(b, g.UnpinnedElementsOf(region).Nodes);
    }

    [Fact]
    public void ChangedFiresOnStructuralEdits()
    {
        var g = new SupportGraph();
        var fired = 0;
        g.Changed += () => fired++;
        var a = Node(SupportNodeType.Base);
        var b = Node(SupportNodeType.Junction, z: 5);
        g.AddNode(a);
        g.AddNode(b);
        var s = Join(a, b);
        g.AddSegment(s);
        g.RemoveSegment(s.Id);
        g.RemoveNode(a.Id);
        Assert.Equal(5, fired);
    }
}
