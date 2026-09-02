using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace Danslicer.App.Controls;

/// <summary>
/// Full-size 2D view of one sliced layer with zoom and pan. Wheel zooms about the cursor, middle or
/// left drag pans, Home fits. Page Up/Down and the arrow keys step layers via <see cref="LayerStepRequested"/>.
/// </summary>
public sealed class LayerPreviewControl : Control
{
    public static readonly StyledProperty<Bitmap?> BitmapProperty =
        AvaloniaProperty.Register<LayerPreviewControl, Bitmap?>(nameof(Bitmap));

    private double _zoom = 1;          // 1 = fit
    private Vector _pan;               // pixels, applied after fitting
    private Point _lastPointer;
    private bool _panning;

    public event Action<int>? LayerStepRequested;
    public event Action? ToggleViewRequested;

    public Bitmap? Bitmap
    {
        get => GetValue(BitmapProperty);
        set => SetValue(BitmapProperty, value);
    }

    public LayerPreviewControl()
    {
        Focusable = true;
        ClipToBounds = true;
        RenderOptions.SetBitmapInterpolationMode(this, BitmapInterpolationMode.HighQuality);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == BitmapProperty) InvalidateVisual();
    }

    private Rect ImageRect()
    {
        var bmp = Bitmap;
        if (bmp is null) return default;
        var w = Bounds.Width;
        var h = Bounds.Height;
        var scale = Math.Min(w / bmp.PixelSize.Width, h / bmp.PixelSize.Height) * _zoom;
        var iw = bmp.PixelSize.Width * scale;
        var ih = bmp.PixelSize.Height * scale;
        return new Rect((w - iw) / 2 + _pan.X, (h - ih) / 2 + _pan.Y, iw, ih);
    }

    public override void Render(DrawingContext context)
    {
        context.FillRectangle(new SolidColorBrush(Color.FromRgb(0x14, 0x15, 0x17)), new Rect(Bounds.Size));
        var bmp = Bitmap;
        if (bmp is null)
        {
            var text = new FormattedText("No slice yet. Press Ctrl+R to slice.", System.Globalization.CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, Typeface.Default, 14, new SolidColorBrush(Color.FromRgb(0x9A, 0xA0, 0xA6)));
            context.DrawText(text, new Point((Bounds.Width - text.Width) / 2, (Bounds.Height - text.Height) / 2));
            return;
        }
        var dest = ImageRect();
        context.DrawImage(bmp, new Rect(bmp.Size), dest);
        context.DrawRectangle(new Pen(new SolidColorBrush(Color.FromArgb(0x80, 0xFF, 0xFF, 0xFF)), 1), dest);
    }

    public void Fit()
    {
        _zoom = 1;
        _pan = default;
        InvalidateVisual();
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            LayerStepRequested?.Invoke(e.Delta.Y > 0 ? 1 : -1);
            e.Handled = true;
            return;
        }
        var factor = Math.Pow(1.15, e.Delta.Y);
        var newZoom = Math.Clamp(_zoom * factor, 1, 64);
        factor = newZoom / _zoom;
        // Zoom about the cursor: keep the image point under the pointer fixed.
        var p = e.GetPosition(this);
        var centre = new Point(Bounds.Width / 2, Bounds.Height / 2) + _pan;
        var offset = p - centre;
        _pan += offset - offset * factor;
        _zoom = newZoom;
        if (_zoom <= 1.0001) _pan = default;
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        var props = e.GetCurrentPoint(this).Properties;
        if (props.IsMiddleButtonPressed || props.IsLeftButtonPressed)
        {
            _panning = true;
            _lastPointer = e.GetPosition(this);
            e.Pointer.Capture(this);
            e.Handled = true;
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_panning) return;
        var p = e.GetPosition(this);
        _pan += p - _lastPointer;
        _lastPointer = p;
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_panning)
        {
            _panning = false;
            e.Pointer.Capture(null);
        }
    }

    protected override void OnPointerEntered(PointerEventArgs e)
    {
        base.OnPointerEntered(e);
        Focus();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        var big = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 10 : 1;
        switch (e.Key)
        {
            case Key.PageUp: case Key.Up: case Key.Right: LayerStepRequested?.Invoke(big); break;
            case Key.PageDown: case Key.Down: case Key.Left: LayerStepRequested?.Invoke(-big); break;
            case Key.Home: Fit(); break;
            case Key.Tab: ToggleViewRequested?.Invoke(); break;
            default: return;
        }
        e.Handled = true;
    }
}
