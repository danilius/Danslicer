using System.Numerics;
using Danslicer.Core;
using Danslicer.Core.Supports;

namespace Danslicer.Tests;

/// <summary>
/// Deleting a support element must not leave useless residue: fragments with no tip (a pillar
/// supporting nothing) or no base (a floating stub) go with it, in the same undo step.
/// </summary>
public sealed class SupportDeletePruneTests
{
    private static (Document Document, SupportNode Tip, SupportNode Junction, SupportNode Base)
        SimpleTree()
    {
        var document = new Document();
        var tip = new SupportNode { Type = SupportNodeType.Tip, Position = new Vector3(0, 0, 10) };
        var junction = new SupportNode { Type = SupportNodeType.Junction, Position = new Vector3(0, 0, 8) };
        var baseNode = new SupportNode { Type = SupportNodeType.Base, Position = Vector3.Zero };
        document.Supports.AddNode(tip);
        document.Supports.AddNode(junction);
        document.Supports.AddNode(baseNode);
        document.Supports.AddSegment(new SupportSegment
            { Type = SupportSegmentType.Neck, NodeA = tip.Id, NodeB = junction.Id });
        document.Supports.AddSegment(new SupportSegment
            { Type = SupportSegmentType.Pillar, NodeA = junction.Id, NodeB = baseNode.Id });
        return (document, tip, junction, baseNode);
    }

    [Fact]
    public void DeletingTheTipRemovesTheWholeTree()
    {
        var (document, tip, _, _) = SimpleTree();
        document.SelectSupportElement(tip.Id);

        document.DeleteSupportSelection();

        Assert.Equal(0, document.Supports.NodeCount);
        Assert.Equal(0, document.Supports.SegmentCount);
    }

    [Fact]
    public void DeletingTheBaseRemovesTheWholeTree()
    {
        var (document, _, _, baseNode) = SimpleTree();
        document.SelectSupportElement(baseNode.Id);

        document.DeleteSupportSelection();

        Assert.Equal(0, document.Supports.NodeCount);
        Assert.Equal(0, document.Supports.SegmentCount);
    }

    [Fact]
    public void PruneIsOneUndoStep()
    {
        var (document, tip, _, _) = SimpleTree();
        document.SelectSupportElement(tip.Id);
        document.DeleteSupportSelection();
        Assert.Equal(0, document.Supports.NodeCount);

        document.Undo();

        Assert.Equal(3, document.Supports.NodeCount);
        Assert.Equal(2, document.Supports.SegmentCount);
    }

    [Fact]
    public void DeletingOneTipOfABranchedTreeKeepsTheRest()
    {
        var (document, tip, junction, _) = SimpleTree();
        var secondTip = new SupportNode { Type = SupportNodeType.Tip, Position = new Vector3(1, 0, 10) };
        document.Supports.AddNode(secondTip);
        document.Supports.AddSegment(new SupportSegment
            { Type = SupportSegmentType.Neck, NodeA = secondTip.Id, NodeB = junction.Id });

        document.SelectSupportElement(tip.Id);
        document.DeleteSupportSelection();

        // The remaining fragment still has a tip and the base, so it survives intact.
        Assert.Equal(3, document.Supports.NodeCount);
        Assert.Equal(2, document.Supports.SegmentCount);
        Assert.True(document.Supports.TryGetNode(secondTip.Id, out _));
    }

    [Fact]
    public void BracingDoesNotKeepATiplessFragmentAlive()
    {
        var (document, tip, junction, _) = SimpleTree();

        // Second full tree in the same document, braced to the first at the junctions.
        var tip2 = new SupportNode { Type = SupportNodeType.Tip, Position = new Vector3(5, 0, 10) };
        var junction2 = new SupportNode { Type = SupportNodeType.Junction, Position = new Vector3(5, 0, 8) };
        var base2 = new SupportNode { Type = SupportNodeType.Base, Position = new Vector3(5, 0, 0) };
        document.Supports.AddNode(tip2);
        document.Supports.AddNode(junction2);
        document.Supports.AddNode(base2);
        document.Supports.AddSegment(new SupportSegment
            { Type = SupportSegmentType.Neck, NodeA = tip2.Id, NodeB = junction2.Id });
        document.Supports.AddSegment(new SupportSegment
            { Type = SupportSegmentType.Pillar, NodeA = junction2.Id, NodeB = base2.Id });
        document.Supports.AddSegment(new SupportSegment
            { Type = SupportSegmentType.Bracing, NodeA = junction.Id, NodeB = junction2.Id });

        document.SelectSupportElement(tip.Id);
        document.DeleteSupportSelection();

        // First tree is gone (bracing does not make its stub a support); second tree survives.
        Assert.False(document.Supports.TryGetNode(junction.Id, out _));
        Assert.True(document.Supports.TryGetNode(junction2.Id, out _));
        Assert.Equal(3, document.Supports.NodeCount);
        Assert.Equal(2, document.Supports.SegmentCount);
    }
}
