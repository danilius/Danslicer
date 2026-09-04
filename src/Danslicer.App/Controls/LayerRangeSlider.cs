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
    public static readonly StyledProperty<bool> IsDraggingProperty =
        AvaloniaProperty.Register<LayerRangeSlider, bool>(nameof(IsDragging),
            defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    public static readonly StyledProperty<IBrush> TrackBrushProperty =
        AvaloniaProperty.Register<LayerRangeSlider, IBrush>(nameof(TrackBrush),
            new SolidColorBrush(Color.Parse("#565A60")));
    public static readonly StyledProperty<IBrush> SelectionBrushProperty =
        AvaloniaProperty.Register<LayerRangeSlider, IBrush>(nameof(SelectionBrush),
            new SolidColorBrush(Color.Parse("#E39032")));
    public static readonly StyledProperty<IBrush> ThumbBrushProperty =
        AvaloniaProperty.Register<LayerRangeSlider, IBrush>(nameof(ThumbBrush),
            new SolidColorBrush(Color.Parse("#F2F2F2")));
    public static readonly StyledProperty<IBrush> ThumbBorderBrushProperty =
        AvaloniaProperty.Register<LayerRangeSlider, IBrush>(nameof(ThumbBorderBrush),
            new SolidColorBrush(Color.Parse("#25282C")));
    private const double ThumbRadius = 7;
    private LayerRangeSliderThumb _dragging;

    static LayerRangeSlider() => AffectsRender<LayerRangeSlider>(
        MinimumProperty, MaximumProperty, LowerValueProperty, UpperValueProperty,
        OrientationProperty, TrackBrushProperty, SelectionBrushProperty,
        ThumbBrushProperty, ThumbBorderBrushProperty);

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
    public bool IsDragging { get => GetValue(IsDraggingProperty); set => SetValue(IsDraggingProperty, value); }
    public IBrush TrackBrush { get => GetValue(TrackBrushProperty); set => SetValue(TrackBrushProperty, value); }
    public IBrush SelectionBrush { get => GetValue(SelectionBrushProperty); set => SetValue(SelectionBrushProperty, value); }
    public IBrush ThumbBrush { get => GetValue(ThumbBrushProperty); set => SetValue(ThumbBrushProperty, value); }
    public IBrush ThumbBorderBrush { get => GetValue(ThumbBorderBrushProperty); set => SetValue(ThumbBorderBrushProperty, value); }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var trackPen = new Pen(TrackBrush, 4);
        var selectedPen = new Pen(SelectionBrush, 4);
        var thumbPen = new Pen(ThumbBorderBrush, 1);
        if (Orientation == Orientation.Vertical)
        {
            var x = Bounds.Width * 0.5;
            var top = ThumbRadius;
            var bottom = Math.Max(top, Bounds.Height - ThumbRadius);
            var lowerY = LayerRangeSliderGeometry.ValueToAxis(
                LowerValue, Minimum, Maximum, top, bottom, descending: true);
            var upperY = LayerRangeSliderGeometry.ValueToAxis(
                UpperValue, Minimum, Maximum, top, bottom, descending: true);
            context.DrawLine(trackPen, new Point(x, top), new Point(x, bottom));
            context.DrawLine(selectedPen, new Point(x, upperY), new Point(x, lowerY));
            context.DrawEllipse(ThumbBrush, thumbPen, new Point(x, lowerY), ThumbRadius, ThumbRadius);
            context.DrawEllipse(ThumbBrush, thumbPen, new Point(x, upperY), ThumbRadius, ThumbRadius);
            return;
        }

        var y = Bounds.Height * 0.5;
        var left = ThumbRadius;
        var right = Math.Max(left, Bounds.Width - ThumbRadius);
        var lowerX = LayerRangeSliderGeometry.ValueToAxis(
            LowerValue, Minimum, Maximum, left, right, descending: false);
        var upperX = LayerRangeSliderGeometry.ValueToAxis(
            UpperValue, Minimum, Maximum, left, right, descending: false);
        context.DrawLine(trackPen, new Point(left, y), new Point(right, y));
        context.DrawLine(selectedPen, new Point(lowerX, y), new Point(upperX, y));
        context.DrawEllipse(ThumbBrush, thumbPen, new Point(lowerX, y), ThumbRadius, ThumbRadius);
        context.DrawEllipse(ThumbBrush, thumbPen, new Point(upperX, y), ThumbRadius, ThumbRadius);
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
            ? LayerRangeSliderThumb.Lower : LayerRangeSliderThumb.Upper;
        SetCurrentValue(IsDraggingProperty, true);
        SetFromPoint(point);
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_dragging == LayerRangeSliderThumb.None) return;
        SetFromPoint(e.GetPosition(this));
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_dragging == LayerRangeSliderThumb.None) return;
        _dragging = LayerRangeSliderThumb.None;
        SetCurrentValue(IsDraggingProperty, false);
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
        var (lower, upper) = LayerRangeSliderGeometry.DragRange(
            _dragging, axisPosition, Minimum, Maximum, start, end,
            descending: vertical, LowerValue, UpperValue);
        if (_dragging == LayerRangeSliderThumb.Lower)
            SetCurrentValue(LowerValueProperty, lower);
        else
            SetCurrentValue(UpperValueProperty, upper);
    }
}

internal enum LayerRangeSliderThumb { None, Lower, Upper }

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

    internal static (double Lower, double Upper) DragRange(LayerRangeSliderThumb thumb,
        double position, double minimum, double maximum, double start, double end,
        bool descending, double lower, double upper)
    {
        var value = AxisToValue(position, minimum, maximum, start, end, descending);
        return thumb switch
        {
            LayerRangeSliderThumb.Lower => (Math.Min(value, upper), upper),
            LayerRangeSliderThumb.Upper => (lower, Math.Max(value, lower)),
            _ => (lower, upper),
        };
    }
}
