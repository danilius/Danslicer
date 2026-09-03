using System.Numerics;
using Danslicer.Core;
using Danslicer.Core.Geometry;
using Danslicer.Core.Scene;

namespace Danslicer.Tests;

public class HideTests
{
    private static Mesh Triangle() =>
        new(new[] { Vector3.Zero, Vector3.UnitX, Vector3.UnitY }, new[] { 0, 1, 2 });

    [Fact]
    public void HideSelectionHidesDeselectsAndUndoes()
    {
        var doc = new Document();
        var a = new SceneObject("a", Triangle());
        var b = new SceneObject("b", Triangle());
        doc.AddObject(a);
        doc.AddObject(b);
        doc.Select(a);

        doc.HideSelection();
        Assert.Equal(RenderState.Hidden, a.RenderState);
        Assert.Equal(RenderState.Normal, b.RenderState);
        Assert.Empty(doc.Selection);

        doc.Undo();
        Assert.Equal(RenderState.Normal, a.RenderState);
    }

    [Fact]
    public void UnhideAllRestoresEveryHiddenObject()
    {
        var doc = new Document();
        var a = new SceneObject("a", Triangle());
        var b = new SceneObject("b", Triangle());
        doc.AddObject(a);
        doc.AddObject(b);
        doc.Select(a, additive: true);
        doc.Select(b, additive: true);
        doc.HideSelection();
        Assert.Equal(RenderState.Hidden, a.RenderState);
        Assert.Equal(RenderState.Hidden, b.RenderState);

        doc.UnhideAll();
        Assert.Equal(RenderState.Normal, a.RenderState);
        Assert.Equal(RenderState.Normal, b.RenderState);

        // Nothing hidden: a second call must not add an undo step.
        var undoName = doc.History.UndoName;
        doc.UnhideAll();
        Assert.Equal(undoName, doc.History.UndoName);
    }
}
