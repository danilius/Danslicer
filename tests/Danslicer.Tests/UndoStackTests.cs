using System.Numerics;
using Danslicer.Core;
using Danslicer.Core.Commands;
using Danslicer.Core.Geometry;
using Danslicer.Core.Scene;

namespace Danslicer.Tests;

public class UndoStackTests
{
    private static Mesh Triangle() => new(
        new[] { Vector3.Zero, Vector3.UnitX, Vector3.UnitY },
        new[] { 0, 1, 2 });

    [Fact]
    public void AddUndoRedoRoundTrips()
    {
        var doc = new Document();
        var obj = new SceneObject("a", Triangle());

        doc.AddObject(obj);
        Assert.Single(doc.Scene.Objects);
        Assert.True(doc.IsSelected(obj));

        Assert.True(doc.Undo());
        Assert.Empty(doc.Scene.Objects);
        Assert.Empty(doc.Selection);

        Assert.True(doc.Redo());
        Assert.Single(doc.Scene.Objects);
        Assert.False(doc.Redo());
    }

    [Fact]
    public void NewCommandClearsRedo()
    {
        var doc = new Document();
        var a = new SceneObject("a", Triangle());
        var b = new SceneObject("b", Triangle());
        doc.AddObject(a);
        doc.Undo();
        doc.AddObject(b);
        Assert.False(doc.History.CanRedo);
        Assert.Equal(new[] { b }, doc.Scene.Objects);
    }

    [Fact]
    public void MergeLastTwoFoldsTwoStepsIntoOne()
    {
        var doc = new Document();
        var a = new SceneObject("a", Triangle());
        var b = new SceneObject("b", Triangle());
        doc.AddObject(a);
        doc.AddObject(b);

        Assert.True(doc.History.MergeLastTwo("Add both"));

        Assert.Equal("Add both", doc.History.UndoName);
        Assert.True(doc.Undo());
        Assert.Empty(doc.Scene.Objects);
        Assert.False(doc.History.CanUndo);
        Assert.True(doc.Redo());
        Assert.Equal(new[] { a, b }, doc.Scene.Objects);
    }

    [Fact]
    public void MergeLastTwoNeedsTwoSteps()
    {
        var doc = new Document();
        doc.AddObject(new SceneObject("a", Triangle()));
        Assert.False(doc.History.MergeLastTwo("nothing"));
        Assert.Equal("Add a", doc.History.UndoName);
    }

    [Fact]
    public void CompositeUndoesInReverse()
    {
        var doc = new Document();
        var obj = new SceneObject("a", Triangle());
        doc.AddObject(obj);
        var t0 = obj.Transform;
        var t1 = t0 with { Translation = new Vector3(1, 0, 0) };
        var t2 = t1 with { Translation = new Vector3(2, 0, 0) };
        doc.Execute(new CompositeCommand("Move twice", new IDocumentCommand[]
        {
            new SetTransformCommand(obj, t0, t1),
            new SetTransformCommand(obj, t1, t2),
        }));
        Assert.Equal(t2, obj.Transform);
        doc.Undo();
        Assert.Equal(t0, obj.Transform);
        Assert.Equal("Add a", doc.History.UndoName);
    }

    [Fact]
    public void RemoveRestoresOriginalIndex()
    {
        var doc = new Document();
        var a = new SceneObject("a", Triangle());
        var b = new SceneObject("b", Triangle());
        var c = new SceneObject("c", Triangle());
        doc.AddObject(a);
        doc.AddObject(b);
        doc.AddObject(c);
        doc.Select(b);
        doc.DeleteSelection();
        Assert.Equal(new[] { a, c }, doc.Scene.Objects);
        doc.Undo();
        Assert.Equal(new[] { a, b, c }, doc.Scene.Objects);
    }

    [Fact]
    public void DropToPlateMovesLowestPointToZero()
    {
        var doc = new Document();
        var obj = new SceneObject("a", Triangle());
        obj.Transform = Transform.Identity with { Translation = new Vector3(0, 0, 7) };
        doc.AddObject(obj);
        doc.DropSelectionToPlate();
        Assert.Equal(0, obj.WorldBounds.Min.Z, 5);
        doc.Undo();
        Assert.Equal(7, obj.WorldBounds.Min.Z, 5);
    }
}
