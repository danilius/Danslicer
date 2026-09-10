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

    private static readonly Pen TrackPen = new(new SolidColorBrush(Color.Parse("#565A60")), 4);
    private static readonly Pen SelectedPen = new(new SolidColorBrush(Color.Parse("#E39032")), 4);
    private static readonly IBrush ThumbFill = new SolidColorBrush(Color.Parse("#F2F2F2"));
    private static readonly Pen ThumbPen = new(new SolidColorBrush(Color.Parse("#25282C")), 1);
    private const double ThumbRadius = 7;
    private LayerRangeSliderThumb _dragging;
    private double _originalLower, _originalUpper;
    private IPointer? _pointer;
    internal LayerRangeSliderThumb ActiveThumb { get; private set; } = LayerRangeSliderThumb.Upper;
    internal event Action<LayerRangeSliderThumb>? ThumbActivity;
    internal event Action? EditRequested;
    public static readonly StyledProperty<double> StepProperty =
        AvaloniaProperty.Register<LayerRangeSlider, double>(nameof(Step), 0.05);
    public double Step { get => GetValue(StepProperty); set => SetValue(StepProperty, value); }

    internal Point ThumbCenter(LayerRangeSliderThumb thumb)
    {
        var vertical = Orientation == Orientation.Vertical;
        var length = vertical ? Bounds.Height : Bounds.Width;
        var value = thumb == LayerRangeSliderThumb.Lower ? LowerValue : UpperValue;
        var axis = LayerRangeSliderGeometry.ValueToAxis(value, Minimum, Maximum,
            ThumbRadius, Math.Max(ThumbRadius, length - ThumbRadius), vertical);
        return vertical ? new Point(Bounds.Width / 2, axis) : new Point(axis, Bounds.Height / 2);
    }

    private static double Distance(Point a, Point b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    private void Activate(LayerRangeSliderThumb thumb)
    {
        if (thumb != LayerRangeSliderThumb.None) ActiveThumb = thumb;
        ThumbActivity?.Invoke(thumb);
    }

    static LayerRangeSlider() => AffectsRender<LayerRangeSlider>(
        MinimumProperty, MaximumProperty, LowerValueProperty, UpperValueProperty,
        OrientationProperty);

    public LayerRangeSlider()
    {
        MinHeight = 24;
        MinWidth = 24;
        Focusable = true;
        LostFocus += (_, _) => { if (!IsDragging) Activate(LayerRangeSliderThumb.None); };
    }

    public double Minimum { get => GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }
    public double Maximum { get => GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
    public double LowerValue { get => GetValue(LowerValueProperty); set => SetValue(LowerValueProperty, value); }
    public double UpperValue { get => GetValue(UpperValueProperty); set => SetValue(UpperValueProperty, value); }
    public Orientation Orientation { get => GetValue(OrientationProperty); set => SetValue(OrientationProperty, value); }
    public bool IsDragging { get => GetValue(IsDraggingProperty); set => SetValue(IsDraggingProperty, value); }

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
            ? LayerRangeSliderThumb.Lower : LayerRangeSliderThumb.Upper;
        _originalLower = LowerValue; _originalUpper = UpperValue;
        _pointer = e.Pointer;
        Activate(_dragging);
        SetCurrentValue(IsDraggingProperty, true);
        SetFromPoint(point);
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_dragging == LayerRangeSliderThumb.None)
        {
            UpdateHover(e.GetPosition(this));
            return;
        }
        SetFromPoint(e.GetPosition(this));
        Activate(_dragging);
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_dragging == LayerRangeSliderThumb.None) return;
        EndDrag();
        UpdateHover(e.GetPosition(this));
        e.Handled = true;
    }

    private void UpdateHover(Point point)
    {
        var lower = Distance(point, ThumbCenter(LayerRangeSliderThumb.Lower));
        var upper = Distance(point, ThumbCenter(LayerRangeSliderThumb.Upper));
        var thumb = Math.Min(lower, upper) > 13 ? LayerRangeSliderThumb.None
            : Math.Abs(lower - upper) < 0.1 ? ActiveThumb
            : lower < upper ? LayerRangeSliderThumb.Lower : LayerRangeSliderThumb.Upper;
        Activate(thumb);
    }

    private void EndDrag()
    {
        _dragging = LayerRangeSliderThumb.None;
        SetCurrentValue(IsDraggingProperty, false);
        var pointer = _pointer; _pointer = null; pointer?.Capture(null);
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        EndDrag(); Activate(LayerRangeSliderThumb.None);
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        if (!IsDragging) Activate(LayerRangeSliderThumb.None);
    }

    protected override void OnGotFocus(FocusChangedEventArgs e)
    {
        base.OnGotFocus(e);
        Activate(ActiveThumb);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape && IsDragging)
        {
            // Restore upper first when needed so the two-way model's range clamp cannot
            // discard the original lower endpoint.
            SetCurrentValue(UpperValueProperty, Math.Max(_originalUpper, LowerValue));
            SetCurrentValue(LowerValueProperty, _originalLower);
            SetCurrentValue(UpperValueProperty, _originalUpper);
            EndDrag(); e.Handled = true; return;
        }
        if (e.Key is Key.Left or Key.Right)
            Activate(e.Key == Key.Left ? LayerRangeSliderThumb.Lower : LayerRangeSliderThumb.Upper);
        else if (e.Key is Key.Up or Key.Down)
        {
            var step = double.IsFinite(Step) && Step > 0 ? Step : 0.05;
            var delta = (e.Key == Key.Up ? step : -step) * (e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 0.1 : 1);
            if (ActiveThumb == LayerRangeSliderThumb.Lower)
                SetCurrentValue(LowerValueProperty, Math.Clamp(LowerValue + delta, Minimum, UpperValue));
            else SetCurrentValue(UpperValueProperty, Math.Clamp(UpperValue + delta, LowerValue, Maximum));
            Activate(ActiveThumb);
        }
        else if (e.Key == Key.Enter) { Activate(ActiveThumb); EditRequested?.Invoke(); }
        else return;
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
