using Danslicer.App.Controls.Refresh;
using Danslicer.Core.Utilities;

namespace Danslicer.Tests;

public sealed class UiRefreshNumericTests
{
    [Fact]
    public void SliderTracksPointerAcrossRangeRegardlessOfKeyboardStep()
    {
        var edit = new NumericEditSession(0.35, 100);
        Assert.Equal(0.35, edit.MoveSlider(102, 200, false, 0, 0.7));
        Assert.Equal(0.42, edit.MoveSlider(120, 200, false, 0, 0.7), 10);
        Assert.Equal(0.427, edit.MoveSlider(122, 200, false, 0, 0.7), 10);
        Assert.Equal(0.7, edit.MoveSlider(250, 200, false, 0, 0.7));
        Assert.Equal(0, edit.MoveSlider(-10, 200, false, 0, 0.7));
        Assert.Equal(0.35, edit.Original);
    }

    [Fact]
    public void SliderFineAdjustmentAndResizedTracksUseTheirActualWidth()
    {
        var edit = new NumericEditSession(50, 100);
        Assert.Equal(51, edit.MoveSlider(120, 200, true, 0, 100), 10);
        Assert.Equal(52, edit.MoveSlider(140, 200, true, 0, 100), 10);
        Assert.Equal(35, edit.MoveSlider(140, 400, false, 0, 100), 10);
    }

    [Theory]
    [InlineData(1.234567, 1.23)]
    [InlineData(0.555, 0.56)]
    [InlineData(-1.235, -1.24)]
    public void NumericEditsHaveAtMostTwoDecimalPlaces(double input, double expected) =>
        Assert.Equal(expected, NumericEditSession.RoundValue(input));

    [Fact]
    public void ClickJitterDoesNotStartScrubbing()
    {
        var edit = new NumericEditSession(12, 100);
        Assert.Equal(12, edit.Move(103, 1, false, 0, 100));
        Assert.False(edit.IsDragging);
    }
    [Fact]
    public void FineModifierChangesOnlySubsequentMotionAndClamps()
    {
        var edit = new NumericEditSession(10, 0);
        Assert.Equal(15, edit.Move(5, 1, false, 0, 20));
        Assert.Equal(16, edit.Move(15, 1, true, 0, 20));
        Assert.Equal(20, edit.Move(100, 1, false, 0, 20));
        Assert.Equal(19, edit.Move(99, 1, false, 0, 20));
        Assert.Equal(10, edit.Original); // application value/undo origin unaffected by previews
    }
    [Theory]
    [InlineData("1cm + 2mm", true, 12)]
    [InlineData("2 * (3 + 4)", true, 14)]
    [InlineData("1 / 0", false, 0)]
    [InlineData("1000", false, 0)]
    [InlineData("garbage", false, 0)]
    public void ExpressionsUseExistingParserAndRejectInvalidValues(string expression, bool valid, double expected)
    {
        Assert.Equal(valid, NumericEditSession.TryExpression(expression, UnitKind.Length, 0, 100, out var result));
        if (valid) Assert.Equal(expected, result);
    }
}
