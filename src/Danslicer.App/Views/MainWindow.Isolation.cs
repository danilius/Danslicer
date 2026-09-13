using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;
using Danslicer.App.Controls;
using Danslicer.App.Controls.Refresh;

namespace Danslicer.App.Views;

public partial class MainWindow
{
    private readonly Border _isolationEditor = new()
    {
        Width = 184, Padding = new Thickness(8), CornerRadius = new CornerRadius(4),
        Background = RefreshPalette.Panel, BorderBrush = RefreshPalette.Edge,
        BorderThickness = new Thickness(1), IsVisible = false
    };
    private readonly ExpressionBox _isolationLayerInput = new();
    private readonly ExpressionBox _isolationMmInput = new();
    private readonly TextBlock _isolationTitle = new() { FontSize = 12, Foreground = RefreshPalette.Text };
    private readonly DispatcherTimer _isolationDismissTimer = new() { Interval = TimeSpan.FromMilliseconds(300) };
    private LayerRangeSliderThumb _isolationThumb = LayerRangeSliderThumb.None;
    private bool _isolationHandleHovered;

    private void InitializeIsolationEditor()
    {
        var body = new StackPanel { Spacing = 4 };
        body.Children.Add(_isolationTitle);
        body.Children.Add(IsolationRow("Layer", _isolationLayerInput, ""));
        body.Children.Add(IsolationRow("Height", _isolationMmInput, "mm"));
        _isolationEditor.Child = body;
        IsolationEditorLayer.Children.Add(_isolationEditor);
        AutomationProperties.SetName(IsolationSlider, "Support layer isolation range");
        AutomationProperties.SetName(_isolationLayerInput, "Isolation layer number");
        AutomationProperties.SetName(_isolationMmInput, "Isolation height in millimetres");
        foreach (var input in new[] { _isolationLayerInput, _isolationMmInput })
        {
            input.Bind(TextBox.TextProperty, new Binding("Text") { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.LostFocus });
            input.GotFocus += (_, _) => _isolationDismissTimer.Stop();
            input.LostFocus += (_, _) => ScheduleIsolationDismiss();
        }
        IsolationSlider.ThumbActivity += thumb =>
        {
            _isolationHandleHovered = thumb != LayerRangeSliderThumb.None;
            if (thumb == LayerRangeSliderThumb.None) { ScheduleIsolationDismiss(); return; }
            // Crossing the other handle must not retarget an in-progress text edit.
            if (_isolationEditor.IsKeyboardFocusWithin && thumb != _isolationThumb) return;
            ShowIsolationEditor(thumb);
        };
        IsolationSlider.EditRequested += () => { _isolationLayerInput.Focus(); _isolationLayerInput.SelectAll(); };
        IsolationSlider.PropertyChanged += (_, e) =>
        {
            if (e.Property == LayerRangeSlider.LowerValueProperty || e.Property == LayerRangeSlider.UpperValueProperty || e.Property == BoundsProperty)
                PositionIsolationEditor();
        };
        ViewportSurface.SizeChanged += (_, _) => PositionIsolationEditor();
        IsolationEditorLayer.PropertyChanged += (_, e) =>
        {
            if (e.Property != IsVisibleProperty || IsolationEditorLayer.IsVisible) return;
            _isolationDismissTimer.Stop(); _isolationEditor.IsVisible = false;
        };
        _isolationEditor.PointerEntered += (_, _) => _isolationDismissTimer.Stop();
        _isolationEditor.PointerExited += (_, _) => ScheduleIsolationDismiss();
        _isolationEditor.SizeChanged += (_, _) => PositionIsolationEditor();
        _isolationDismissTimer.Tick += (_, _) =>
        {
            _isolationDismissTimer.Stop();
            if (!_isolationHandleHovered && !IsolationSlider.IsDragging && !_isolationEditor.IsPointerOver && !_isolationEditor.IsKeyboardFocusWithin)
                _isolationEditor.IsVisible = false;
        };
        // Editors consume Escape themselves to revert text. A second Escape from the rail
        // dismisses its hover editor, leaving the always-visible rail available.
        IsolationSlider.AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key != Key.Escape || e.Handled) return;
            _isolationEditor.IsVisible = false; _isolationDismissTimer.Stop(); e.Handled = true;
        }, RoutingStrategies.Bubble);
        Closed += (_, _) => _isolationDismissTimer.Stop();
    }

    private void UpdateIsolationPlacement()
    {
        var settings = Configuration.AppConfig.Current.Viewport;
        var scale = RenderScaling;
        var pixelHeight = (int)(Viewport.Bounds.Height * scale);
        // Use the same configured cube rectangle as rendering/hit testing, converted to DIPs.
        // This reserves clearance for larger cubes and scaled displays as well as the default.
        var cube = Danslicer.Render.ViewCube.Rect((int)(Viewport.Bounds.Width * scale), pixelHeight,
            scale, settings.ViewCubeSizePixels);
        var top = settings.ViewCubeEnabled ? (pixelHeight - cube.Y) / scale + 12 : 12;
        InspectionContent.Margin = new Thickness(0, top, 12, 12);
    }

    private static Control IsolationRow(string label, ExpressionBox input, string unit)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("42,*,22") };
        row.Children.Add(new TextBlock { Text = label, FontSize = 12, Foreground = RefreshPalette.Muted, VerticalAlignment = VerticalAlignment.Center });
        input.MinHeight = 24; input.FontSize = 12;
        Grid.SetColumn(input, 1); row.Children.Add(input);
        var suffix = new TextBlock { Text = unit, FontSize = 12, Foreground = RefreshPalette.Muted, Margin = new Thickness(3, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(suffix, 2); row.Children.Add(suffix);
        return row;
    }
    private void ScheduleIsolationDismiss()
    {
        _isolationDismissTimer.Stop(); _isolationDismissTimer.Start();
    }
    private void ShowIsolationEditor(LayerRangeSliderThumb thumb)
    {
        if (ViewModel is not { IsSupportView: true } vm) return;
        _isolationDismissTimer.Stop();
        _isolationThumb = thumb;
        _isolationTitle.Text = thumb == LayerRangeSliderThumb.Lower ? "Bottom limit" : "Top limit";
        _isolationLayerInput.DataContext = thumb == LayerRangeSliderThumb.Lower ? vm.SupportClip.LowerField : vm.SupportClip.UpperField;
        _isolationMmInput.DataContext = thumb == LayerRangeSliderThumb.Lower ? vm.SupportClip.LowerMmField : vm.SupportClip.UpperMmField;
        _isolationEditor.IsVisible = true;
        PositionIsolationEditor();
    }
    private void PositionIsolationEditor()
    {
        if (!_isolationEditor.IsVisible || _isolationThumb == LayerRangeSliderThumb.None) return;
        var point = IsolationSlider.TranslatePoint(IsolationSlider.ThumbCenter(_isolationThumb), IsolationEditorLayer);
        if (point is not { } anchor) return;
        var height = _isolationEditor.Bounds.Height > 0 ? _isolationEditor.Bounds.Height : 94;
        Canvas.SetLeft(_isolationEditor, Math.Max(8, anchor.X - _isolationEditor.Width - 32));
        var maximumTop = Math.Max(8, IsolationEditorLayer.Bounds.Height - height - 8);
        var minimumTop = Math.Min(InspectionContent.Margin.Top, maximumTop);
        Canvas.SetTop(_isolationEditor, Math.Clamp(anchor.Y - height / 2, minimumTop, maximumTop));
    }
}
