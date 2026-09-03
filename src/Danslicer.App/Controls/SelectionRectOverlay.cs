using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Danslicer.App.Controls;

/// <summary>
/// Draws the marquee rectangle ABOVE the viewport. The viewport's OpenGL composition surface
/// renders over its own 2D drawing context, so the rectangle must live in a sibling control
/// layered on top; this one is hit-test invisible and simply mirrors the viewport's
/// <see cref="ViewportControl.MarqueeRect"/>.
/// </summary>
public sealed class SelectionRectOverlay : Control
{
    public static readonly StyledProperty<Rect?> RectProperty =
        AvaloniaProperty.Register<SelectionRectOverlay, Rect?>(nameof(Rect));

    private static readonly IBrush Fill = new SolidColorBrush(Color.FromArgb(28, 80, 150, 255));
    private static readonly IPen Border = new Pen(new SolidColorBrush(Color.FromArgb(230, 120, 185, 255)), 1);

    public SelectionRectOverlay()
    {
        IsHitTestVisible = false;
    }

    public Rect? Rect
    {
        get => GetValue(RectProperty);
        set => SetValue(RectProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == RectProperty) InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        if (Rect is not { } rect) return;
        context.FillRectangle(Fill, rect);
        context.DrawRectangle(Border, rect);
    }
}
