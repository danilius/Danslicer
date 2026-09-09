using System.Numerics;
using Danslicer.Core;
using Danslicer.Core.Geometry;
using Danslicer.Core.Scene;
using Danslicer.Core.Supports;
using Danslicer.Core.Config;
using Xunit;

namespace Danslicer.Tests;

/// <summary>Moving a model in Z deletes its supports (user, 2026-09-09); X/Y moves carry them.</summary>
public class ZMoveClearsSupportsTests
{
    private static (Document Document, SceneObject Object) Supported()
    {
        var document = new Document { PlacementMode = PlacementMode.Off };
        document.SupportSettings = document.SupportSettings with { BaseGridPitch = 20f };
        var obj = new SceneObject("slab", new Mesh(
            [new(0, 0, 20), new(40, 0, 20), new(0, 40, 20)], [0, 1, 2]));
        document.AddObject(obj);
        document.Select(obj);
        Assert.True(document.AddManualSupport(obj, new Vector3(20, 20, 20), -Vector3.UnitZ));
        Assert.True(document.Supports.NodeCount > 0);
        return (document, obj);
    }

    [Fact]
    public void RaisingTheModelDeletesItsSupportsInTheSameUndoStep()
    {
        var (document, obj) = Supported();
        var before = obj.Transform;

        document.CommitTransform(obj, before, before with { Translation = before.Translation + new Vector3(0, 0, 3) },
            "Move", applyPlacement: false);

        Assert.Equal(0, document.Supports.NodeCount);
        document.Undo();
        Assert.True(document.Supports.NodeCount > 0);
        Assert.Equal(before, obj.Transform);
    }

    [Fact]
    public void ALiftThatAutoDropPutsBackStillDeletesTheSupports()
    {
        // Layout mode with auto-drop on: the model comes straight back to the plate, but the
        // user moved it in Z, and that is the rule (user report 2026-09-09).
        // A model standing on the plate (a foot at z = 0) with an overhang at z = 20.
        var document = new Document { PlacementMode = PlacementMode.AutoDrop };
        document.SupportSettings = document.SupportSettings with { BaseGridPitch = 20f };
        var obj = new SceneObject("bracket", new Mesh(
            [new(0, 0, 20), new(40, 0, 20), new(0, 40, 20), new(80, 0, 0), new(90, 0, 0), new(80, 10, 0)],
            [0, 1, 2, 3, 4, 5]));
        document.AddObject(obj);
        document.Select(obj);
        Assert.True(document.AddManualSupport(obj, new Vector3(20, 20, 20), -Vector3.UnitZ));
        var before = obj.Transform;

        document.CommitTransform(obj, before, before with { Translation = before.Translation + new Vector3(0, 0, 3) }, "Move");

        Assert.Equal(before, obj.Transform);
        Assert.Equal(0, document.Supports.NodeCount);
        document.Undo();
        Assert.True(document.Supports.NodeCount > 0);
    }

    [Fact]
    public void SlidingTheModelCarriesItsSupports()
    {
        var (document, obj) = Supported();
        var before = obj.Transform;
        var tipBefore = document.Supports.Nodes.Single(n => n.Type == SupportNodeType.Tip).Position;

        document.CommitTransform(obj, before, before with { Translation = before.Translation + new Vector3(5, 0, 0) },
            "Move", applyPlacement: false);

        var tip = document.Supports.Nodes.Single(n => n.Type == SupportNodeType.Tip).Position;
        Assert.Equal(tipBefore + new Vector3(5, 0, 0), tip);
    }

    [Fact]
    public void TheRuleItself()
    {
        var before = Transform.Identity;
        Assert.True(SupportTransformRule.MapsContactsExactly(before, before with { Translation = new Vector3(3, 4, 0) }));
        Assert.False(SupportTransformRule.MapsContactsExactly(before, before with { Translation = new Vector3(0, 0, 0.1f) }));
        Assert.True(SupportTransformRule.MapsContactsExactly(before, before with { Translation = new Vector3(0, 0, 1e-5f) }));
    }
}
