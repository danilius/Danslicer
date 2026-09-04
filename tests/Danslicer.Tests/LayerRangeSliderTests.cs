using Danslicer.App.Controls;

namespace Danslicer.Tests;

public sealed class LayerRangeSliderTests
{
    [Fact]
    public void VerticalAxisPlacesHigherValuesAboveLowerValues()
    {
        var lower = LayerRangeSliderGeometry.ValueToAxis(
            value: 2, minimum: 0, maximum: 10, start: 7, end: 107, descending: true);
        var upper = LayerRangeSliderGeometry.ValueToAxis(
            value: 8, minimum: 0, maximum: 10, start: 7, end: 107, descending: true);

        Assert.Equal(87, lower, precision: 9);
        Assert.Equal(27, upper, precision: 9);
        Assert.True(upper < lower);
    }

    [Theory]
    [InlineData(0, 107)]
    [InlineData(2.5, 82)]
    [InlineData(10, 7)]
    public void VerticalAxisRoundTripsPointerPositions(double value, double expectedAxis)
    {
        var axis = LayerRangeSliderGeometry.ValueToAxis(
            value, minimum: 0, maximum: 10, start: 7, end: 107, descending: true);
        var roundTrip = LayerRangeSliderGeometry.AxisToValue(
            axis, minimum: 0, maximum: 10, start: 7, end: 107, descending: true);

        Assert.Equal(expectedAxis, axis, precision: 9);
        Assert.Equal(value, roundTrip, precision: 9);
    }

    [Fact]
    public void PointerCoordinatesClampToVerticalRange()
    {
        Assert.Equal(10, LayerRangeSliderGeometry.AxisToValue(
            position: -100, minimum: 0, maximum: 10, start: 7, end: 107, descending: true));
        Assert.Equal(0, LayerRangeSliderGeometry.AxisToValue(
            position: 500, minimum: 0, maximum: 10, start: 7, end: 107, descending: true));
    }

    [Fact]
    public void VerticalUpperAndLowerThumbsControlTheirCorrespondingBounds()
    {
        var afterUpperDrag = LayerRangeSliderGeometry.DragRange(
            LayerRangeSliderThumb.Upper, position: 57, minimum: 0, maximum: 10,
            start: 7, end: 107, descending: true, lower: 2, upper: 8);
        var afterLowerDrag = LayerRangeSliderGeometry.DragRange(
            LayerRangeSliderThumb.Lower, position: 67, minimum: 0, maximum: 10,
            start: 7, end: 107, descending: true, lower: 2, upper: 8);

        Assert.Equal((2d, 5d), afterUpperDrag);
        Assert.Equal((4d, 8d), afterLowerDrag);
    }
}
