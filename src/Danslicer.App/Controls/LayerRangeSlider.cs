using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace Danslicer.App.Controls;

/// <summary>A compact two-thumb slider for the Support-mode layer range.</summary>
public sealed class LayerRangeSlider : Control
{
    public static readonly StyledProperty<double> MinimumProperty =
        AvaloniaProperty.Register<LayerRangeSlider, double>(nameof(Minimum));
    public static readonly StyledProperty<double> MaximumProperty =
        AvaloniaProperty.Register<LayerRangeSlider, double>(nameof(Maximum), 1);
    public static readonly StyledProperty<double> LowerValueProperty =
        AvaloniaProperty.Register<LayerRangeSlider, double>(nameof(LowerValue),
            defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);
    public static readonly StyledProperty<double> UpperValueProperty =
        AvaloniaProperty.Register<LayerRangeSlider, double>(nameof(UpperValue), 1,
            defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);
    public static readonly StyledProperty<Orientation> OrientationProperty =
        AvaloniaProperty.Register<LayerRangeSlider, Orientation>(nameof(Orientation));

    private static readonly Pen TrackPen = new(new SolidColorBrush(Color.Parse("#565A60")), 4);
    private static readonly Pen SelectedPen = new(new SolidColorBrush(Color.Parse("#E39032")), 4);
    private static readonly IBrush ThumbFill = new SolidColorBrush(Color.Parse("#F2F2F2"));
    private static readonly Pen ThumbPen = new(new SolidColorBrush(Color.Parse("#25282C")), 1);
    private const double ThumbRadius = 7;
    private Thumb _dragging;

    private enum Thumb { None, Lower, Upper }

    static LayerRangeSlider() => AffectsRender<LayerRangeSlider>(
        MinimumProperty, MaximumProperty, LowerValueProperty, UpperValueProperty,
        OrientationProperty);

    public LayerRangeSlider()
    {
        MinHeight = 24;
        MinWidth = 24;
        Focusable = true;
    }

    public double Minimum { get => GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }
    public double Maximum { get => GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
    public double LowerValue { get => GetValue(LowerValueProperty); set => SetValue(LowerValueProperty, value); }
    public double UpperValue { get => GetValue(UpperValueProperty); set => SetValue(UpperValueProperty, value); }
    public Orientation Orientation { get => GetValue(OrientationProperty); set => SetValue(OrientationProperty, value); }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (Orientation == Orientation.Vertical)
        {
            var x = Bounds.Width * 0.5;
            var top = ThumbRadius;
            var bottom = Math.Max(top, Bounds.Height - ThumbRadius);
            var lowerY = LayerRangeSliderGeometry.ValueToAxis(
                LowerValue, Minimum, Maximum, top, bottom, descending: true);
            var upperY = LayerRangeSliderGeometry.ValueToAxis(
                UpperValue, Minimum, Maximum, top, bottom, descending: true);
            context.DrawLine(TrackPen, new Point(x, top), new Point(x, bottom));
            context.DrawLine(SelectedPen, new Point(x, upperY), new Point(x, lowerY));
            context.DrawEllipse(ThumbFill, ThumbPen, new Point(x, lowerY), ThumbRadius, ThumbRadius);
            context.DrawEllipse(ThumbFill, ThumbPen, new Point(x, upperY), ThumbRadius, ThumbRadius);
            return;
        }

        var y = Bounds.Height * 0.5;
        var left = ThumbRadius;
        var right = Math.Max(left, Bounds.Width - ThumbRadius);
        var lowerX = LayerRangeSliderGeometry.ValueToAxis(
            LowerValue, Minimum, Maximum, left, right, descending: false);
        var upperX = LayerRangeSliderGeometry.ValueToAxis(
            UpperValue, Minimum, Maximum, left, right, descending: false);
        context.DrawLine(TrackPen, new Point(left, y), new Point(right, y));
        context.DrawLine(SelectedPen, new Point(lowerX, y), new Point(upperX, y));
        context.DrawEllipse(ThumbFill, ThumbPen, new Point(lowerX, y), ThumbRadius, ThumbRadius);
        context.DrawEllipse(ThumbFill, ThumbPen, new Point(upperX, y), ThumbRadius, ThumbRadius);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        Focus();
        var point = e.GetPosition(this);
        var vertical = Orientation == Orientation.Vertical;
        var axisPosition = vertical ? point.Y : point.X;
        var axisLength = vertical ? Bounds.Height : Bounds.Width;
        var start = ThumbRadius;
        var end = Math.Max(start, axisLength - ThumbRadius);
        var lowerPosition = LayerRangeSliderGeometry.ValueToAxis(
            LowerValue, Minimum, Maximum, start, end, descending: vertical);
        var upperPosition = LayerRangeSliderGeometry.ValueToAxis(
            UpperValue, Minimum, Maximum, start, end, descending: vertical);
        _dragging = Math.Abs(axisPosition - lowerPosition) <= Math.Abs(axisPosition - upperPosition)
            ? Thumb.Lower : Thumb.Upper;
        SetFromPoint(point);
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_dragging == Thumb.None) return;
        SetFromPoint(e.GetPosition(this));
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_dragging == Thumb.None) return;
        _dragging = Thumb.None;
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    private void SetFromPoint(Point point)
    {
        var vertical = Orientation == Orientation.Vertical;
        var axisPosition = vertical ? point.Y : point.X;
        var axisLength = vertical ? Bounds.Height : Bounds.Width;
        var start = ThumbRadius;
        var end = Math.Max(start + 1, axisLength - ThumbRadius);
        var value = LayerRangeSliderGeometry.AxisToValue(
            axisPosition, Minimum, Maximum, start, end, descending: vertical);
        if (_dragging == Thumb.Lower)
            SetCurrentValue(LowerValueProperty, Math.Min(value, UpperValue));
        else
            SetCurrentValue(UpperValueProperty, Math.Max(value, LowerValue));
    }
}

internal static class LayerRangeSliderGeometry
{
    internal static double ValueToAxis(double value, double minimum, double maximum,
        double start, double end, bool descending)
    {
        var span = maximum - minimum;
        var fraction = span <= 0 ? 0 : Math.Clamp((value - minimum) / span, 0, 1);
        if (descending) fraction = 1 - fraction;
        return start + fraction * (end - start);
    }

    internal static double AxisToValue(double position, double minimum, double maximum,
        double start, double end, bool descending)
    {
        var fraction = end <= start ? 0 : Math.Clamp((position - start) / (end - start), 0, 1);
        if (descending) fraction = 1 - fraction;
        return minimum + fraction * Math.Max(0, maximum - minimum);
    }
}
