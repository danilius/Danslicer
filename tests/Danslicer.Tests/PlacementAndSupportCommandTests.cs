using System.Numerics;
using Danslicer.Core;
using Danslicer.Core.Config;
using Danslicer.Core.Geometry;
using Danslicer.Core.Scene;
using Danslicer.Core.Supports;

namespace Danslicer.Tests;

public sealed class PlacementAndSupportCommandTests
{
    private static Mesh Box(Vector3 min, Vector3 max)
    {
        var p = new[]
        {
            new Vector3(min.X, min.Y, min.Z), new(max.X, min.Y, min.Z),
            new(max.X, max.Y, min.Z), new(min.X, max.Y, min.Z),
            new(min.X, min.Y, max.Z), new(max.X, min.Y, max.Z),
            new(max.X, max.Y, max.Z), new(min.X, max.Y, max.Z),
        };
        int[] indices =
        [
            0, 3, 2, 0, 2, 1, 4, 5, 6, 4, 6, 7,
            0, 1, 5, 0, 5, 4, 2, 3, 7, 2, 7, 6,
            0, 4, 7, 0, 7, 3, 1, 2, 6, 1, 6, 5,
        ];
        return new Mesh(p, indices);
    }

    [Fact]
    public void AutoDropIsPartOfTheTransformUndoStep()
    {
        var doc = new Document();
        var obj = new SceneObject("box", Box(new(-1, -1, 0), new(1, 1, 2)));
        doc.AddObject(obj);
        var before = obj.Transform;
        var requested = before with { Translation = new Vector3(12, 3, 20) };

        doc.CommitTransform(obj, before, requested, "Move");

        Assert.Equal(12f, obj.Transform.Translation.X);
        Assert.Equal(0f, obj.WorldBounds.Min.Z, 5);
        Assert.Equal("Move", doc.History.UndoName);
        doc.Undo();
        Assert.Equal(before, obj.Transform);
    }

    [Fact]
    public void RaiseAndOffModesRespectTheRequestedTransform()
    {
        var doc = new Document();
        var obj = new SceneObject("box", Box(new(-1, -1, -1), new(1, 1, 1)));
        doc.AddObject(obj);
        var before = obj.Transform;
        var requested = before with { Translation = new Vector3(4, 5, 20) };

        doc.PlacementMode = PlacementMode.RaiseAbovePlate;
        doc.PlacementHeightMm = 7.5f;
        doc.CommitTransform(obj, before, requested);
        Assert.Equal(7.5f, obj.WorldBounds.Min.Z, 5);

        doc.PlacementMode = PlacementMode.Off;
        before = obj.Transform;
        requested = before with { Translation = new Vector3(4, 5, 13) };
        doc.CommitTransform(obj, before, requested);
        Assert.Equal(requested, obj.Transform);
    }

    [Fact]
    public void HideUnselectedSupportsIsUndoable()
    {
        var doc = new Document();
        var selected = new SupportNode { Type = SupportNodeType.Tip, Position = new(0, 0, 5) };
        var other = new SupportNode { Type = SupportNodeType.Base, Position = Vector3.Zero };
        var segment = new SupportSegment
            { Type = SupportSegmentType.Pillar, NodeA = selected.Id, NodeB = other.Id };
        doc.Supports.AddNode(selected);
        doc.Supports.AddNode(other);
        doc.Supports.AddSegment(segment);
        doc.SelectSupportElement(selected.Id);

        doc.HideUnselectedSupportElements();

        Assert.False(selected.Hidden);
        Assert.True(other.Hidden);
        Assert.True(segment.Hidden);
        Assert.Equal("Hide unselected supports", doc.History.UndoName);
        doc.Undo();
        Assert.False(other.Hidden);
        Assert.False(segment.Hidden);

        doc.HideUnselectedSupportElements();
        doc.UnhideAll();
        Assert.False(other.Hidden);
        Assert.False(segment.Hidden);
        Assert.Equal("Unhide supports", doc.History.UndoName);
    }

    [Fact]
    public void GenerateSupportsCommitsOneNonManualUndoablePass()
    {
        var doc = new Document();
        var obj = new SceneObject("floating", Box(new(-5, -5, 5), new(5, 5, 15)));
        doc.AddObject(obj);

        var summary = doc.GenerateSupports(obj);

        Assert.True(summary.GeneratedTipCount > 0);
        Assert.Equal("Generate supports", doc.History.UndoName);
        Assert.All(doc.Supports.Nodes, node => Assert.False(node.Origin.IsManual));
        Assert.All(doc.Supports.Segments, segment => Assert.False(segment.Origin.IsManual));
        doc.Undo();
        Assert.Equal(0, doc.Supports.NodeCount);
    }
}
