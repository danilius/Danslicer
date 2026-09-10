using Danslicer.App.Controls.Refresh;
using Danslicer.Core.Utilities;

namespace Danslicer.Tests;

public sealed class UiRefreshNumericTests
{
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
