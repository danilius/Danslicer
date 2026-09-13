using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;

namespace Danslicer.App.Controls;

/// <summary>Session-only slice navigation: retain live navigation, restore on cancellation.</summary>
public sealed class PreviewLayerSlider : Slider
{
    private bool _dragging;
    private double _original;
    private IPointer? _pointer;
    private Thumb? _thumb;
    protected override Type StyleKeyOverride => typeof(Slider);
    public PreviewLayerSlider()
    {
        AddHandler(PointerPressedEvent, (_, e) =>
        {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            // Track.Thumb can be assigned after OnApplyTemplate. Resolve the actual pressed
            // thumb as well; capture-lost is direct and does not bubble from it to the Slider.
            if (e.Source is Visual source)
                AttachThumb(source.GetSelfAndVisualAncestors().OfType<Thumb>().FirstOrDefault() ?? _thumb);
            _original = Value; _dragging = true; _pointer = e.Pointer;
        }, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerReleasedEvent, (_, _) => { _dragging = false; _pointer = null; }, RoutingStrategies.Tunnel, handledEventsToo: true);
        PointerCaptureLost += CaptureLost;
        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (!_dragging || e.Key != Key.Escape) return;
            CancelDrag(); e.Handled = true;
        }, RoutingStrategies.Tunnel);
    }
    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        AttachThumb(e.NameScope.Find<Track>("PART_Track")?.Thumb);
    }
    private void AttachThumb(Thumb? thumb)
    {
        if (_thumb is not null) _thumb.PointerCaptureLost -= CaptureLost;
        _thumb = thumb;
        if (_thumb is not null) _thumb.PointerCaptureLost += CaptureLost;
    }
    private void CaptureLost(object? sender, PointerCaptureLostEventArgs e) => CancelDrag();
    public void CancelDrag()
    {
        if (!_dragging) return;
        _dragging = false;
        SetCurrentValue(ValueProperty, _original);
        var pointer = _pointer; _pointer = null; pointer?.Capture(null);
    }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        CancelDrag(); base.OnDetachedFromVisualTree(e);
    }
}
