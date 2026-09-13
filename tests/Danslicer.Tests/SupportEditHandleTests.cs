using System.Numerics;
using Danslicer.Core.Supports;
using Xunit;

namespace Danslicer.Tests;

/// <summary>
/// Edit-mode handles (user, 2026-09-08: click a support, Space, move base / trunk / tip).
/// </summary>
public class SupportEditHandleTests
{
    private static SupportNode Node(SupportNodeType type, Vector3 position) =>
        new() { Type = type, Position = position };

    private static SupportSegment Segment(SupportSegmentType type, SupportNode a, SupportNode b) =>
        new() { Type = type, NodeA = a.Id, NodeB = b.Id, Diameter = 1f };

    /// <summary>Tip → cone junction → split trunk (branch junction) → base, with a branch tip.</summary>
    private static (SupportGraph Graph, SupportNode Tip, SupportNode Cone, SupportNode Split, SupportNode Base,
        SupportNode BranchTip, SupportSegment LowerTrunk, SupportSegment UpperTrunk) Tree()
    {
        var graph = new SupportGraph();
        var tip = Node(SupportNodeType.Tip, new(10, 10, 30));
        var cone = Node(SupportNodeType.Junction, new(10, 10, 28));
        var split = Node(SupportNodeType.Junction, new(10, 10, 15));
        var plate = Node(SupportNodeType.Base, new(10, 10, 0));
        var branchTip = Node(SupportNodeType.Tip, new(16, 10, 21));
        foreach (var n in new[] { tip, cone, split, plate, branchTip }) graph.AddNode(n);
        var tipSeg = Segment(SupportSegmentType.Tip, tip, cone);
        var upper = Segment(SupportSegmentType.Trunk, cone, split);
        var lower = Segment(SupportSegmentType.Trunk, split, plate);
        var branch = Segment(SupportSegmentType.Branch, split, branchTip);
        foreach (var s in new[] { tipSeg, upper, lower, branch }) graph.AddSegment(s);
        return (graph, tip, cone, split, plate, branchTip, lower, upper);
    }

    [Fact]
    public void HandlesCoverEveryNodeAndEachTrunkSegmentAtItsMidpoint()
    {
        var t = Tree();
        var handles = SupportEditing.HandlesOf(t.Graph, t.LowerTrunk.Id); // any element seeds it

        Assert.Equal(7, handles.Count); // 5 nodes + 2 trunk segments
        Assert.Contains(handles, h => h.Kind == SupportHandleKind.Base && h.ElementId == t.Base.Id);
        Assert.Contains(handles, h => h.Kind == SupportHandleKind.Tip && h.ElementId == t.Tip.Id);
        Assert.Contains(handles, h => h.Kind == SupportHandleKind.Tip && h.ElementId == t.BranchTip.Id);
        Assert.Equal(2, handles.Count(h => h.Kind == SupportHandleKind.Junction));
        var lower = Assert.Single(handles, h => h.Kind == SupportHandleKind.Trunk && h.ElementId == t.LowerTrunk.Id);
        Assert.Equal(new Vector3(10, 10, 7.5f), lower.Position);
    }

    [Fact]
    public void HandlesIgnoreBraceEndsAndOtherSupports()
    {
        var t = Tree();
        var other = Node(SupportNodeType.Base, new(40, 40, 0));
        var otherTip = Node(SupportNodeType.Tip, new(40, 40, 20));
        t.Graph.AddNode(other);
        t.Graph.AddNode(otherTip);
        t.Graph.AddSegment(Segment(SupportSegmentType.Trunk, other, otherTip));
        var endA = Node(SupportNodeType.BraceEnd, new(10, 10, 10));
        var endB = Node(SupportNodeType.BraceEnd, new(40, 40, 15));
        t.Graph.AddNode(endA);
        t.Graph.AddNode(endB);
        t.Graph.AddSegment(Segment(SupportSegmentType.Bracing, endA, endB));

        var handles = SupportEditing.HandlesOf(t.Graph, t.Tip.Id);

        Assert.Equal(7, handles.Count);
        Assert.DoesNotContain(handles, h => h.ElementId == other.Id || h.ElementId == endA.Id);
    }

    [Fact]
    public void ANodeHandleMovesOnlyItsNode()
    {
        var t = Tree();
        var baseHandle = SupportEditing.HandlesOf(t.Graph, t.Tip.Id).Single(h => h.Kind == SupportHandleKind.Base);

        var moved = SupportEditing.AffectedByHandle(t.Graph, baseHandle);

        Assert.Equal([t.Base], moved);
    }

    [Fact]
    public void ATrunkHandleMovesTheWholeColumnWithItsBraceEnds()
    {
        var t = Tree();
        var endOnColumn = Node(SupportNodeType.BraceEnd, new(10, 10, 10));
        var endElsewhere = Node(SupportNodeType.BraceEnd, new(40, 40, 10));
        t.Graph.AddNode(endOnColumn);
        t.Graph.AddNode(endElsewhere);
        t.Graph.AddSegment(Segment(SupportSegmentType.Bracing, endOnColumn, endElsewhere));
        var trunk = SupportEditing.HandlesOf(t.Graph, t.Tip.Id)
            .Single(h => h.Kind == SupportHandleKind.Trunk && h.ElementId == t.UpperTrunk.Id);

        var moved = SupportEditing.AffectedByHandle(t.Graph, trunk).Select(n => n.Id).ToHashSet();

        // Both trunk segments form one column: cone junction, split junction and base come along,
        // the brace end riding the column too. Tips and the far brace end stay.
        Assert.Equal(new HashSet<Guid> { t.Cone.Id, t.Split.Id, t.Base.Id, endOnColumn.Id }, moved);
    }

    [Fact]
    public void TranslateXyKeepsHeightsAndSnapFindsTheNearestGridPoint()
    {
        var t = Tree();
        var origins = new[] { (t.Base, t.Base.Position), (t.Split, t.Split.Position) };

        SupportEditing.TranslateXY(t.Graph, origins, new Vector2(2, -3));

        Assert.Equal(new Vector3(12, 7, 0), t.Base.Position);
        Assert.Equal(new Vector3(12, 7, 15), t.Split.Position);
        Assert.Equal(new Vector2(12, 6), SupportEditing.SnapToBaseGrid(new Vector2(13.4f, 7.2f), 6f));
        Assert.Equal(new Vector2(13.4f, 7.2f), SupportEditing.SnapToBaseGrid(new Vector2(13.4f, 7.2f), 0f));
    }
}
