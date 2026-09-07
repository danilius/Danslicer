using System.Numerics;
using Danslicer.Core;
using Danslicer.Core.Geometry;
using Danslicer.Core.Scene;
using Danslicer.Core.Supports;
using Danslicer.Core.Supports.Guided;

namespace Danslicer.Tests;

/// <summary>
/// Densify and thin (SUPPORT-GEOMETRY-SPEC "Guided tip placement"): runs over a set of tips,
/// what densify adds, what thin removes, and the document commands on the target's tips.
/// </summary>
public sealed class GuidedDensifyThinTests
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

    private static List<Vector3> Line(int count, float pitch, float y = 0) =>
        Enumerable.Range(0, count).Select(i => new Vector3(i * pitch, y, 0)).ToList();

    [Fact]
    public void LinksJoinNeighboursAlongALine()
    {
        var links = TipRuns.Links(Line(5, 2.5f));
        Assert.Equal(4, links.Count);
        Assert.All(links, l => Assert.Equal(1, Math.Abs(l.A - l.B)));
    }

    [Fact]
    public void LinksDoNotBridgeTwoSeparateLines()
    {
        var tips = Line(4, 2.5f).Concat(Line(4, 2.5f, y: 30)).ToList();
        var links = TipRuns.Links(tips);
        Assert.Equal(6, links.Count);
        Assert.DoesNotContain(links, l => (l.A < 4) != (l.B < 4));
    }

    [Fact]
    public void DensifyInsertsMidpointsBetweenNeighbours()
    {
        var mesh = Box(new Vector3(-20, -20, 0), new Vector3(20, 20, 10));
        var tips = Line(3, 4f).Select(p => (p, 1)).ToList();

        var added = TipRuns.Densify(mesh, tips, insertions: 1);

        Assert.Equal(new[] { 2f, 6f }, added.Select(a => MathF.Round(a.Point.X, 4)).OrderBy(x => x).ToArray());
        Assert.All(added, a => Assert.Equal(0f, a.Point.Z, 4));
    }

    [Fact]
    public void DensifyWithTwoInsertionsSpacesThemEvenly()
    {
        var mesh = Box(new Vector3(-20, -20, 0), new Vector3(20, 20, 10));
        var tips = Line(2, 6f).Select(p => (p, 1)).ToList();

        var added = TipRuns.Densify(mesh, tips, insertions: 2);

        Assert.Equal(new[] { 2f, 4f }, added.Select(a => MathF.Round(a.Point.X, 4)).OrderBy(x => x).ToArray());
    }

    [Fact]
    public void ThinKeepsEverySecondTipAndBothEnds()
    {
        var remove = TipRuns.Thin(Line(5, 2.5f), keepEvery: 2);
        Assert.Equal(new[] { 1, 3 }, remove.OrderBy(i => i).ToArray());
    }

    [Fact]
    public void ThinKeepsOneInThree()
    {
        var remove = TipRuns.Thin(Line(7, 2.5f), keepEvery: 3);
        Assert.Equal(new[] { 1, 2, 4, 5 }, remove.OrderBy(i => i).ToArray());
    }

    [Fact]
    public void ThinTreatsSeparateLinesSeparately()
    {
        var tips = Line(3, 2.5f).Concat(Line(3, 2.5f, y: 30)).ToList();
        var remove = TipRuns.Thin(tips, keepEvery: 2);
        Assert.Equal(new[] { 1, 4 }, remove.OrderBy(i => i).ToArray());
    }

    [Fact]
    public void RunOperationsRejectBadCounts()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => TipRuns.Thin(Line(3, 2f), 1));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            TipRuns.Densify(Box(Vector3.Zero, Vector3.One), [], 0));
    }

    // ----- Document commands -----

    private static (Document Document, SceneObject Box) BoxWithLineOfTips(int count, float pitch)
    {
        var document = new Document();
        var obj = new SceneObject("box", Box(new Vector3(-20, -20, 8), new Vector3(20, 20, 14)));
        document.AddObject(obj);
        document.Select(obj);
        for (var i = 0; i < count; i++)
            Assert.True(document.AddManualSupport(obj, new Vector3(i * pitch - 10, 0, 8), -Vector3.UnitZ));
        return (document, obj);
    }

    private static int TipCount(Document d) => d.Supports.Nodes.Count(n => n.Type == SupportNodeType.Tip);

    [Fact]
    public void DensifyOnTheWholeTargetFillsEveryGapAsOneUndoStep()
    {
        var (document, _) = BoxWithLineOfTips(3, 6f);
        document.SupportSettings = document.SupportSettings with { GuidedDensifyInsertions = 1 };

        var placed = document.DensifyTips(out var refused);

        Assert.Equal(2, placed);
        Assert.Equal(0, refused);
        Assert.Equal(5, TipCount(document));
        Assert.Equal("Densify", document.History.UndoName);
        Assert.True(document.Undo());
        Assert.Equal(3, TipCount(document));
    }

    [Fact]
    public void DensifyOnASelectionTouchesOnlyTheSelectedRun()
    {
        var (document, _) = BoxWithLineOfTips(4, 6f);
        var tips = document.Supports.Nodes.Where(n => n.Type == SupportNodeType.Tip)
            .OrderBy(n => n.Position.X).ToList();
        document.SelectSupportElements([tips[0].Id, tips[1].Id]);

        var placed = document.DensifyTips(out _);

        Assert.Equal(1, placed);
        var added = document.Supports.Nodes.Single(n => n.Type == SupportNodeType.Tip && !tips.Any(t => t.Id == n.Id));
        Assert.Equal(-7f, added.Position.X, 3);
    }

    [Fact]
    public void ThinRemovesEverySecondTipWithItsSupport()
    {
        var (document, _) = BoxWithLineOfTips(5, 4f);
        var nodesBefore = document.Supports.Nodes.Count;

        var removed = document.ThinTips();

        Assert.Equal(2, removed);
        Assert.Equal(3, TipCount(document));
        Assert.True(document.Supports.Nodes.Count < nodesBefore - 2); // their trunks went too
        Assert.Equal("Thin", document.History.UndoName);
        Assert.True(document.Undo());
        Assert.Equal(5, TipCount(document));
    }

    [Fact]
    public void ThinDropsRemovedTipsFromTheSelection()
    {
        var (document, _) = BoxWithLineOfTips(3, 4f);
        document.SelectSupportElements(document.Supports.Nodes.Where(n => n.Type == SupportNodeType.Tip).Select(n => n.Id));

        Assert.Equal(1, document.ThinTips());

        Assert.Equal(2, document.SupportSelection.Count);
        Assert.All(document.SupportSelection, id => Assert.True(document.Supports.TryGetNode(id, out _)));
    }

    [Fact]
    public void FewerThanTwoTipsDoesNothing()
    {
        var (document, _) = BoxWithLineOfTips(1, 4f);
        Assert.Equal(0, document.DensifyTips(out _));
        Assert.Equal(0, document.ThinTips());
    }
}
