using System.Numerics;
using Danslicer.Core;
using Danslicer.Core.Geometry;
using Danslicer.Core.Scene;
using Danslicer.Core.Supports;

namespace Danslicer.Tests;

public sealed class WorkspaceSelectionTests
{
    [Fact]
    public void SelectAllIsModeDependent()
    {
        var doc = new Document();
        var obj = new SceneObject("object", Triangle());
        doc.AddObject(obj);
        doc.ClearSelection();
        var node = new SupportNode { Type = SupportNodeType.Tip, Position = Vector3.Zero };
        doc.Supports.AddNode(node);

        WorkspaceSelection.SelectAll(doc, WorkspaceMode.Slicing);
        Assert.Empty(doc.Selection);
        Assert.Empty(doc.SupportSelection);

        WorkspaceSelection.SelectAll(doc, WorkspaceMode.Layout);
        Assert.Single(doc.Selection);
        Assert.Empty(doc.SupportSelection);

        doc.ClearSelection();
        WorkspaceSelection.SelectAll(doc, WorkspaceMode.Support);
        Assert.Empty(doc.Selection);
        Assert.Contains(node.Id, doc.SupportSelection);
    }

    [Fact]
    public void MarqueeUsesProjectedElementPositionsAndSkipsHiddenElements()
    {
        var graph = new SupportGraph();
        var inside = new SupportNode { Type = SupportNodeType.Tip, Position = new(5, 5, 0) };
        var outside = new SupportNode { Type = SupportNodeType.Base, Position = new(20, 20, 0) };
        var hidden = new SupportNode { Type = SupportNodeType.Tip, Position = new(4, 4, 0), Hidden = true };
        graph.AddNode(inside);
        graph.AddNode(outside);
        graph.AddNode(hidden);
        var segment = new SupportSegment
            { Type = SupportSegmentType.Pillar, NodeA = inside.Id, NodeB = outside.Id };
        graph.AddSegment(segment);

        var selected = SupportMarqueeSelection.ElementsInside(graph,
            p => new Vector2(p.X, p.Y), Vector2.Zero, new Vector2(15, 15));

        Assert.Contains(inside.Id, selected);
        Assert.Contains(segment.Id, selected); // midpoint (12.5, 12.5)
        Assert.DoesNotContain(outside.Id, selected);
        Assert.DoesNotContain(hidden.Id, selected);
    }

    private static Mesh Triangle() => new(
        [Vector3.Zero, Vector3.UnitX, Vector3.UnitY], [0, 1, 2]);
}
