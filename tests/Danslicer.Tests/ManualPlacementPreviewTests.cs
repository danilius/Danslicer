using System.Numerics;
using Danslicer.Core;
using Danslicer.Core.Geometry;
using Danslicer.Core.Scene;
using Xunit;

namespace Danslicer.Tests;

/// <summary>Placement mode (user note 2026-09-08): the ghost is the support a click would place.</summary>
public class ManualPlacementPreviewTests
{
    private static (Document Document, SceneObject Object) Overhang()
    {
        var document = new Document();
        document.SupportSettings = document.SupportSettings with { BaseGridPitch = 20f };
        var obj = new SceneObject("slab", new Mesh(
            [new(0, 0, 20), new(40, 0, 20), new(0, 40, 20)], [0, 1, 2]));
        document.AddObject(obj);
        document.Select(obj);
        return (document, obj);
    }

    [Fact]
    public void PreviewRoutesWithoutAddingAndMatchesThePlacement()
    {
        var (document, obj) = Overhang();
        var contact = new Vector3(20, 20, 20);

        var undoBefore = document.History.UndoName;
        var preview = document.PreviewManualSupport(obj, contact, -Vector3.UnitZ, out var reason);

        Assert.NotNull(preview);
        Assert.Null(reason);
        Assert.Equal(0, document.Supports.NodeCount); // nothing placed, nothing on the undo stack
        Assert.Equal(undoBefore, document.History.UndoName);

        Assert.True(document.AddManualSupport(obj, contact, -Vector3.UnitZ));
        Assert.Equal(preview.AddedNodes.Count, document.Supports.NodeCount);
        Assert.Equal(preview.AddedSegments.Count, document.Supports.SegmentCount);
        Assert.Equal(preview.AddedNodes.Select(n => n.Position), document.Supports.Nodes.Select(n => n.Position));
    }

    [Fact]
    public void PreviewIsNullOffTheTarget()
    {
        var (document, obj) = Overhang();
        var other = new SceneObject("other", new Mesh(
            [new(100, 0, 20), new(140, 0, 20), new(100, 40, 20)], [0, 1, 2]));
        document.AddObject(other);
        document.Select(obj);

        var preview = document.PreviewManualSupport(other, new Vector3(120, 20, 20), -Vector3.UnitZ, out var reason);

        Assert.Null(preview);
        Assert.Null(reason); // a target refusal, not a routing failure
    }
}
