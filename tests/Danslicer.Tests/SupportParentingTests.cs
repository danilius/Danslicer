using System.Numerics;
using Danslicer.Core;
using Danslicer.Core.Config;
using Danslicer.Core.Geometry;
using Danslicer.Core.Scene;
using Danslicer.Core.Supports;

namespace Danslicer.Tests;

/// <summary>
/// Parenting (SUPPORT-GEOMETRY-SPEC "Parenting"): single supports re-routed together share
/// trunks, in grid and free mode, as one undo step, with tips never lost.
/// </summary>
public sealed class SupportParentingTests
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

    /// <summary>A floating slab with a row of single supports, each routed as if alone.</summary>
    private static (Document Document, SceneObject Slab) SlabWithSingles(int count, float pitch, bool grid)
    {
        var document = new Document();
        var slab = new SceneObject("slab", Box(new Vector3(-30, -30, 20), new Vector3(30, 30, 26)));
        document.AddObject(slab);
        document.Select(slab);
        document.SupportSettings = new SupportConfig
        {
            UseBaseGrid = grid,
            BaseGridPitch = 6f,
            IndependentManualSupports = true, // every single support stands alone to begin with
        };
        for (var i = 0; i < count; i++)
            Assert.True(document.AddManualSupport(slab, new Vector3(i * pitch - (count - 1) * pitch / 2f, 0, 20), -Vector3.UnitZ));
        document.SupportSettings = document.SupportSettings with { IndependentManualSupports = false };
        return (document, slab);
    }

    private static int Count(Document d, SupportNodeType type) => d.Supports.Nodes.Count(n => n.Type == type);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ParentingSharesTrunksAndKeepsEveryTip(bool grid)
    {
        var (document, _) = SlabWithSingles(5, 2.5f, grid);
        var tipsBefore = Count(document, SupportNodeType.Tip);
        var basesBefore = Count(document, SupportNodeType.Base);
        Assert.Equal(5, basesBefore);

        var outcome = document.ParentSupports();

        Assert.NotNull(outcome);
        Assert.Equal(5, outcome.Operands);
        Assert.Equal(basesBefore, outcome.TrunksBefore);
        Assert.Equal(tipsBefore, Count(document, SupportNodeType.Tip));
        Assert.True(Count(document, SupportNodeType.Base) < basesBefore,
            $"{Count(document, SupportNodeType.Base)} bases remain of {basesBefore}");
        Assert.Equal(Count(document, SupportNodeType.Base), outcome.TrunksAfter);
        Assert.Equal("Parent supports", document.History.UndoName);
    }

    [Fact]
    public void ParentingIsOneUndoStepThatRestoresEverything()
    {
        var (document, _) = SlabWithSingles(4, 2.5f, grid: true);
        var nodeIds = document.Supports.Nodes.Select(n => n.Id).OrderBy(id => id).ToList();
        var segmentIds = document.Supports.Segments.Select(s => s.Id).OrderBy(id => id).ToList();

        Assert.NotNull(document.ParentSupports());
        Assert.True(document.Undo());

        Assert.Equal(nodeIds, document.Supports.Nodes.Select(n => n.Id).OrderBy(id => id).ToList());
        Assert.Equal(segmentIds, document.Supports.Segments.Select(s => s.Id).OrderBy(id => id).ToList());
    }

    [Fact]
    public void ParentingWorksOnTheSelectionOnly()
    {
        var (document, _) = SlabWithSingles(6, 2.5f, grid: true);
        var tips = document.Supports.Nodes.Where(n => n.Type == SupportNodeType.Tip).OrderBy(n => n.Position.X).ToList();
        var untouched = document.Supports.Component(tips[5].Id);
        document.SelectSupportElements(tips.Take(3).Select(t => t.Id));

        var outcome = document.ParentSupports();

        Assert.NotNull(outcome);
        Assert.Equal(3, outcome.Operands);
        Assert.All(untouched.Nodes, id => Assert.True(document.Supports.TryGetNode(id, out _)));
        Assert.All(untouched.Segments, id => Assert.True(document.Supports.TryGetSegment(id, out _)));
        Assert.Empty(document.SupportSelection);
    }

    [Fact]
    public void SelectingASegmentSelectsItsSupportForParenting()
    {
        var (document, _) = SlabWithSingles(3, 2.5f, grid: true);
        // One segment from each of two different supports: the trunk under each of two tips.
        var tips = document.Supports.Nodes.Where(n => n.Type == SupportNodeType.Tip).Take(2).ToList();
        var segments = tips.Select(t => document.Supports.SegmentsAt(t.Id).First().Id).ToList();
        document.SelectSupportElements(segments);

        var outcome = document.ParentSupports();

        Assert.NotNull(outcome);
        Assert.Equal(2, outcome.Operands);
    }

    [Fact]
    public void FewerThanTwoSupportsIsNothingToParent()
    {
        var (document, _) = SlabWithSingles(1, 2.5f, grid: true);
        Assert.Null(document.ParentSupports());
        Assert.NotEqual("Parent supports", document.History.UndoName);
    }

    [Fact]
    public void MinTipsPerTrunkSecondPassDoesNotCrashAndUndoes()
    {
        // The user's screen-test settings of 2026-09-08: a high minimum forces the second pass,
        // whose removals target trunks the first pass adds. Building them up front crashed.
        var (document, _) = SlabWithSingles(6, 2.5f, grid: true);
        document.SupportSettings = document.SupportSettings with
        {
            ParentingMinTipsPerTrunk = 20, ParentingRounds = 2,
            ParentingMaxBranchLength = 200f, ParentingTrunkRange = 200f,
            ParentingMaxConeBend = 90f, ParentingMaxBranchesPerTrunk = 30,
        };
        var tipsBefore = Count(document, SupportNodeType.Tip);
        var nodeIds = document.Supports.Nodes.Select(n => n.Id).OrderBy(id => id).ToList();

        var outcome = document.ParentSupports();

        Assert.NotNull(outcome);
        Assert.Equal(tipsBefore, Count(document, SupportNodeType.Tip));
        Assert.True(document.Undo());
        Assert.Equal(nodeIds, document.Supports.Nodes.Select(n => n.Id).OrderBy(id => id).ToList());
        Assert.True(document.Redo());
        Assert.Equal(tipsBefore, Count(document, SupportNodeType.Tip));
    }

    [Fact]
    public void RoundsPickTheFewestTrunks()
    {
        var (document, _) = SlabWithSingles(5, 2.5f, grid: true);
        document.SupportSettings = document.SupportSettings with { ParentingRounds = 3 };
        var many = document.ParentSupports()!;
        document.Undo();
        document.SupportSettings = document.SupportSettings with { ParentingRounds = 1 };
        var one = document.ParentSupports()!;

        Assert.True(many.TrunksAfter <= one.TrunksAfter);
    }
}
