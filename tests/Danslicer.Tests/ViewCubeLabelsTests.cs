using Danslicer.Render;

namespace Danslicer.Tests;

public sealed class ViewCubeLabelsTests
{
    [Fact]
    public void SixFacesCoverEveryAxisAndSignWithTheExpectedNumpadText()
    {
        // Matches Camera.SetView: +X=Right, -X=Left, +Y=Back, -Y=Front, +Z=Top, -Z=Bottom.
        var expected = new Dictionary<(int Axis, int Sign), string>
        {
            [(0, 1)] = "RIGHT",
            [(0, -1)] = "LEFT",
            [(1, 1)] = "BACK",
            [(1, -1)] = "FRONT",
            [(2, 1)] = "TOP",
            [(2, -1)] = "BOTTOM",
        };

        Assert.Equal(6, ViewCubeLabels.Faces.Length);
        foreach (var (axis, sign, text) in ViewCubeLabels.Faces)
        {
            Assert.True(expected.TryGetValue((axis, sign), out var want),
                $"Unexpected face (axis {axis}, sign {sign}).");
            Assert.Equal(want, text);
            expected.Remove((axis, sign));
        }
        Assert.Empty(expected); // every axis/sign combination was covered exactly once
    }

    [Fact]
    public void GlyphTIsATopBarWithACenteredStem()
    {
        var pixels = new HashSet<(int Row, int Col)>(ViewCubeLabels.Glyph('T'));

        // Full top row lit.
        for (var col = 0; col < ViewCubeLabels.GlyphWidth; col++)
            Assert.Contains((0, col), pixels);
        // Every row below is just the centre column.
        for (var row = 1; row < ViewCubeLabels.GlyphHeight; row++)
        {
            Assert.Contains((row, 2), pixels);
            Assert.DoesNotContain((row, 0), pixels);
            Assert.DoesNotContain((row, 4), pixels);
        }
    }

    [Fact]
    public void UnknownCharacterThrows()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ViewCubeLabels.Glyph('9').ToList());
    }

    [Theory]
    [InlineData("TOP", 17, 7)] // 3 chars * 5 + 2 gaps
    [InlineData("BOTTOM", 35, 7)] // 6 chars * 5 + 5 gaps
    [InlineData("A", 5, 7)]
    public void MeasureAccountsForGlyphWidthAndInterCharacterGaps(string text, int width, int height)
    {
        var (w, h) = ViewCubeLabels.Measure(text);
        Assert.Equal(width, w);
        Assert.Equal(height, h);
    }

    [Fact]
    public void RasterizeStaysWithinTheMeasuredBoundsAndDoesNotOverlapBetweenLetters()
    {
        const string text = "FRONT";
        var (width, height) = ViewCubeLabels.Measure(text);
        var pixels = ViewCubeLabels.Rasterize(text).ToList();

        Assert.NotEmpty(pixels);
        Assert.Equal(pixels.Count, pixels.Distinct().Count()); // no duplicate/overlapping cells
        foreach (var (row, col) in pixels)
        {
            Assert.InRange(row, 0, height - 1);
            Assert.InRange(col, 0, width - 1);
        }

        // Every one of the five glyphs contributed at least one lit cell.
        for (var i = 0; i < text.Length; i++)
        {
            var start = i * (ViewCubeLabels.GlyphWidth + 1);
            var end = start + ViewCubeLabels.GlyphWidth - 1;
            Assert.Contains(pixels, p => p.Col >= start && p.Col <= end);
        }
    }

    [Fact]
    public void RunsAreMaximalCoverEveryLitCellAndNeverTouchEachOther()
    {
        const string text = "BOTTOM";
        var lit = new HashSet<(int Row, int Col)>(ViewCubeLabels.Rasterize(text));
        var runs = ViewCubeLabels.Runs(text).ToList();

        // Every run is lit end to end, and nothing is covered twice.
        var covered = new HashSet<(int Row, int Col)>();
        foreach (var (row, col, length) in runs)
        {
            Assert.True(length >= 1);
            for (var i = 0; i < length; i++)
            {
                Assert.Contains((row, col + i), lit);
                Assert.True(covered.Add((row, col + i)), $"cell ({row},{col + i}) covered twice");
            }
            // Maximal: the cells immediately either side must be unlit, or a run was split.
            Assert.DoesNotContain((row, col - 1), lit);
            Assert.DoesNotContain((row, col + length), lit);
        }

        Assert.Equal(lit, covered); // and nothing was dropped
    }

    [Fact]
    public void RunsCollapseASolidBarIntoOneQuadNotFive()
    {
        // 'T' is a full-width top bar over a centre stem: 1 run of 5 then 6 runs of 1.
        var runs = ViewCubeLabels.Runs("T").ToList();

        Assert.Equal((0, 0, ViewCubeLabels.GlyphWidth), runs[0]);
        Assert.Equal(ViewCubeLabels.GlyphHeight, runs.Count); // one per row, not one per cell
        Assert.All(runs.Skip(1), r => Assert.Equal((r.Row, 2, 1), r));
    }
}
