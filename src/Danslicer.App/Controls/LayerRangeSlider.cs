using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace Danslicer.App.Controls;

/// <summary>A compact two-thumb horizontal slider for the Support-mode layer range.</summary>
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

    private static readonly Pen TrackPen = new(new SolidColorBrush(Color.Parse("#565A60")), 4);
    private static readonly Pen SelectedPen = new(new SolidColorBrush(Color.Parse("#E39032")), 4);
    private static readonly IBrush ThumbFill = new SolidColorBrush(Color.Parse("#F2F2F2"));
    private static readonly Pen ThumbPen = new(new SolidColorBrush(Color.Parse("#25282C")), 1);
    private const double ThumbRadius = 7;
    private Thumb _dragging;

    private enum Thumb { None, Lower, Upper }

    static LayerRangeSlider() => AffectsRender<LayerRangeSlider>(
        MinimumProperty, MaximumProperty, LowerValueProperty, UpperValueProperty);

    public LayerRangeSlider()
    {
        MinHeight = 24;
        Focusable = true;
    }

    public double Minimum { get => GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }
    public double Maximum { get => GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
    public double LowerValue { get => GetValue(LowerValueProperty); set => SetValue(LowerValueProperty, value); }
    public double UpperValue { get => GetValue(UpperValueProperty); set => SetValue(UpperValueProperty, value); }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var y = Bounds.Height * 0.5;
        var left = ThumbRadius;
        var right = Math.Max(left, Bounds.Width - ThumbRadius);
        var lowerX = ValueToX(LowerValue, left, right);
        var upperX = ValueToX(UpperValue, left, right);
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
        var left = ThumbRadius;
        var right = Math.Max(left, Bounds.Width - ThumbRadius);
        var lowerX = ValueToX(LowerValue, left, right);
        var upperX = ValueToX(UpperValue, left, right);
        _dragging = Math.Abs(point.X - lowerX) <= Math.Abs(point.X - upperX)
            ? Thumb.Lower : Thumb.Upper;
        SetFromX(point.X);
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_dragging == Thumb.None) return;
        SetFromX(e.GetPosition(this).X);
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

    private void SetFromX(double x)
    {
        var left = ThumbRadius;
        var right = Math.Max(left + 1, Bounds.Width - ThumbRadius);
        var fraction = Math.Clamp((x - left) / (right - left), 0, 1);
        var value = Minimum + fraction * Math.Max(0, Maximum - Minimum);
        if (_dragging == Thumb.Lower)
            SetCurrentValue(LowerValueProperty, Math.Min(value, UpperValue));
        else
            SetCurrentValue(UpperValueProperty, Math.Max(value, LowerValue));
    }

    private double ValueToX(double value, double left, double right)
    {
        var span = Maximum - Minimum;
        var fraction = span <= 0 ? 0 : Math.Clamp((value - Minimum) / span, 0, 1);
        return left + fraction * (right - left);
    }
}
