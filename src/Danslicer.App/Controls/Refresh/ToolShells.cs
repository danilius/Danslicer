using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls.Documents;

namespace Danslicer.App.Controls.Refresh;

public sealed record RefreshTool(string Id, string Label, string Icon, Action Execute);

/// <summary>Floating shell. Host owns mode persistence and command/mode scoping.</summary>
public sealed class FloatingToolbar : Border
{
    public static readonly StyledProperty<bool> ShowLabelsProperty = AvaloniaProperty.Register<FloatingToolbar, bool>(nameof(ShowLabels), true);
    public bool ShowLabels { get => GetValue(ShowLabelsProperty); set => SetValue(ShowLabelsProperty, value); }
    private readonly StackPanel _items = new() { Spacing = 4 };
    private readonly List<TextBlock> _labels = [];
    private readonly Dictionary<string, ToggleButton> _buttons = [];
    public FloatingToolbar()
    {
        Background = RefreshPalette.Panel; BorderBrush = RefreshPalette.Edge; BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(6); Padding = new Thickness(6); Margin = new Thickness(16);
        HorizontalAlignment = HorizontalAlignment.Left; VerticalAlignment = VerticalAlignment.Top;
        Child = new ScrollViewer { Content = _items, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    }
    public void AddTool(RefreshTool tool)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        row.Children.Add(RefreshIcons.Create(tool.Icon));
        var label = new TextBlock { Text = tool.Label, IsVisible = ShowLabels, VerticalAlignment = VerticalAlignment.Center };
        _labels.Add(label); row.Children.Add(label);
        var button = new ToggleButton { Content = row, FontSize = 12, MinHeight = 30, HorizontalAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(6), Foreground = RefreshPalette.Text };
        AutomationProperties.SetName(button, tool.Label); ToolTip.SetTip(button, tool.Label);
        button.Click += (_, _) => tool.Execute();
        _buttons.Add(tool.Id, button); _items.Children.Add(button);
    }
    /// <summary>Adopt a production button without replacing its command, bindings or routed handlers.</summary>
    public void AddExistingTool(Button button, string label, string icon)
    {
        button.Classes.Remove("viewportTool");
        button.Width = double.NaN; button.Height = double.NaN;
        button.MinHeight = 30; button.FontSize = 12;
        button.Margin = new Thickness(0); button.Padding = new Thickness(6);
        button.HorizontalAlignment = HorizontalAlignment.Stretch;
        button.HorizontalContentAlignment = HorizontalAlignment.Left;
        button.Foreground = RefreshPalette.Text;
        button.Background = Brushes.Transparent;
        button.BorderThickness = new Thickness(0);
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var glyph = RefreshIcons.Create(icon);
        button.Content = null;
        row.Children.Add(glyph);
        var text = new TextBlock { Text = label, FontSize = 12, IsVisible = ShowLabels, VerticalAlignment = VerticalAlignment.Center };
        _labels.Add(text); row.Children.Add(text); button.Content = row;
        AutomationProperties.SetName(button, label);
        if (ToolTip.GetTip(button) is null) ToolTip.SetTip(button, label);
        _items.Children.Add(button);
    }
    public void Select(string? id)
    {
        foreach (var (key, button) in _buttons)
        {
            button.IsChecked = key == id;
            button.Background = key == id ? RefreshPalette.Fill : RefreshPalette.Panel;
        }
    }
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ShowLabelsProperty)
            foreach (var label in _labels) label.IsVisible = ShowLabels;
    }
}

/// <summary>Inline overlay, not a light-dismiss popup: interacting with the viewport cannot close it.
/// Put in a constrained Grid beside the toolbar so available space bounds scrolling.</summary>
public sealed class ToolPopout : Border
{
    private readonly TextBlock _title;
    private readonly ScrollViewer _scroll;
    public event EventHandler? CloseRequested;
    public event EventHandler? WidthCommitted;
    public string Title { get => _title.Text ?? ""; set => _title.Text = value; }
    public Control? Body { get => _scroll.Content as Control; set => _scroll.Content = value; }
    public ToolPopout()
    {
        Background = RefreshPalette.Panel; BorderBrush = RefreshPalette.Edge; BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(6); Width = 360; MinWidth = 240; Margin = new Thickness(0, 16, 16, 16);
        TextElement.SetFontSize(this, 12);
        HorizontalAlignment = HorizontalAlignment.Left; VerticalAlignment = VerticalAlignment.Top;
        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Background = RefreshPalette.Header };
        _title = new TextBlock { Margin = new Thickness(10, 4), VerticalAlignment = VerticalAlignment.Center, Foreground = RefreshPalette.Text };
        var close = new Button { Content = RefreshIcons.Create("close"), Width = 28, Height = 28, MinHeight = 0, Padding = new Thickness(6) };
        AutomationProperties.SetName(close, "Close tool settings"); ToolTip.SetTip(close, "Close (Escape)");
        close.Click += (_, _) => RequestClose();
        header.Children.Add(_title); Grid.SetColumn(close, 1); header.Children.Add(close);
        _scroll = new ScrollViewer { Margin = new Thickness(8), HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        root.Children.Add(header); Grid.SetRow(_scroll, 1); root.Children.Add(_scroll);
        var resize = new Border { Width = 6, Background = Brushes.Transparent, HorizontalAlignment = HorizontalAlignment.Right, Cursor = new Cursor(StandardCursorType.SizeWestEast), Focusable = true };
        AutomationProperties.SetName(resize, "Resize popout width"); ToolTip.SetTip(resize, "Drag to resize; Left/Right to adjust width");
        Grid.SetRowSpan(resize, 2); root.Children.Add(resize); Child = root;
        Point? resizeOrigin = null;
        double originalWidth = 0, originalRequestedWidth = 0;
        IPointer? resizePointer = null;
        void EndResize() { resizeOrigin = null; var pointer = resizePointer; resizePointer = null; pointer?.Capture(null); }
        resize.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(resize).Properties.IsLeftButtonPressed) return;
            resize.Focus(); resizeOrigin = e.GetPosition(TopLevel.GetTopLevel(this)); originalWidth = Bounds.Width; originalRequestedWidth = Width;
            resizePointer = e.Pointer; e.Pointer.Capture(resize); e.Handled = true;
        };
        resize.PointerMoved += (_, e) =>
        {
            if (resizeOrigin is not { } start) return;
            Width = Math.Clamp(originalWidth + e.GetPosition(TopLevel.GetTopLevel(this)).X - start.X, MinWidth, MaxWidth);
            e.Handled = true;
        };
        resize.PointerReleased += (_, e) => { if (resizeOrigin is null) return; EndResize(); WidthCommitted?.Invoke(this, EventArgs.Empty); e.Handled = true; };
        resize.PointerCaptureLost += (_, _) => { if (resizeOrigin is not null) Width = originalRequestedWidth; EndResize(); };
        resize.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && resizeOrigin is not null) { Width = originalRequestedWidth; EndResize(); e.Handled = true; }
            else if (e.Key is Key.Left or Key.Right) { Width = Math.Clamp(Bounds.Width + (e.Key == Key.Left ? -10 : 10), MinWidth, MaxWidth); WidthCommitted?.Invoke(this, EventArgs.Empty); e.Handled = true; }
        };
        KeyDown += (_, e) => { if (e.Key == Key.Escape && !e.Handled) { RequestClose(); e.Handled = true; } };
    }
    public void RequestClose() { IsVisible = false; CloseRequested?.Invoke(this, EventArgs.Empty); }
}

/// <summary>Only the right grip starts a reorder (six-pixel threshold). Alt+Up/Down on the
/// grip offers the same operation without a pointer. Host persists order and expansion.</summary>
public sealed class ReorderableExpander : Border
{
    private readonly StackPanel _root = new();
    private readonly Button _disclosure;
    private readonly ContentControl _body;
    private readonly Button _grip;
    private bool _expanded = true;
    public string SectionId { get; }
    public event EventHandler? ExpansionChanged;
    public event EventHandler<int>? MoveRequested;
    public event EventHandler? OrderCommitted;
    public bool IsExpanded
    {
        get => _expanded;
        set { if (_expanded == value) return; _expanded = value; Update(); ExpansionChanged?.Invoke(this, EventArgs.Empty); }
    }
    public Control? Body { get => _body.Content as Control; set => _body.Content = value; }
    private readonly string _title;
    public ReorderableExpander(string id, string title)
    {
        SectionId = id; _title = title;
        Background = RefreshPalette.Panel; TextElement.SetFontSize(this, 12);
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Background = RefreshPalette.Header };
        _disclosure = new Button { HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left, Height = 26, MinHeight = 0, Padding = new Thickness(8, 2), Background = RefreshPalette.Header, Foreground = RefreshPalette.Text };
        _disclosure.Click += (_, _) => IsExpanded = !IsExpanded;
        var grip = _grip = new Button { Content = RefreshIcons.Create("grip"), Width = 26, Height = 26, MinHeight = 0, Padding = new Thickness(4), Background = RefreshPalette.Header, Cursor = new Cursor(StandardCursorType.SizeAll) };
        AutomationProperties.SetName(grip, $"Reorder {title}"); ToolTip.SetTip(grip, "Drag to reorder; Alt+Up/Down");
        double? origin = null;
        double originalTop = 0, lastPointerY = 0;
        int originalIndex = -1;
        var translation = new TranslateTransform(); RenderTransform = translation;
        IPointer? dragPointer = null;
        bool dragging = false, moving = false;
        void Settle()
        {
            translation.Transitions = new Transitions { new DoubleTransition { Property = TranslateTransform.YProperty, Duration = TimeSpan.FromMilliseconds(150) } };
            translation.Y = 0; ZIndex = 0;
        }
        void Move(int offset, double visualY)
        {
            if (offset == 0 || Parent is not Panel panel) return;
            // Hosts remove/reinsert the real section. Ignore transient capture loss during reparenting.
            var previousTops = panel.Children.OfType<ReorderableExpander>()
                .Where(section => section != this)
                .ToDictionary(section => section, section => section.Bounds.Y + ((TranslateTransform)section.RenderTransform!).Y);
            moving = true;
            try
            {
                MoveRequested?.Invoke(this, offset);
                panel.UpdateLayout();
                translation.Y = visualY - Bounds.Y;
                foreach (var (section, top) in previousTops) section.AnimateDisplacement(top);
                grip.Focus();
                dragPointer?.Capture(grip);
            }
            finally { moving = false; }
        }
        void CancelDrag()
        {
            var visualY = Bounds.Y + translation.Y;
            if (dragging && Parent is Panel panel) Move(originalIndex - panel.Children.IndexOf(this), visualY);
            origin = null; dragging = false;
            var pointer = dragPointer; dragPointer = null; pointer?.Capture(null); Settle();
        }
        void UpdateDrag(double pointerY)
        {
            if (origin is not { } start) return;
            var delta = pointerY - start;
            if (!dragging && Math.Abs(delta) < 6) return;
            var direction = Math.Sign(pointerY - lastPointerY);
            lastPointerY = pointerY;
            dragging = true; ZIndex = 10;
            var visualY = originalTop + delta;
            translation.Y = visualY - Bounds.Y;
            if (Parent is not Panel panel || direction == 0) return;
            var index = panel.Children.IndexOf(this);
            var center = _grip.TranslatePoint(new Point(0, _grip.Bounds.Height / 2), panel)!.Value.Y;
            var destination = index;
            for (var i = index + direction; i >= 0 && i < panel.Children.Count; i += direction)
            {
                if (panel.Children[i] is not ReorderableExpander neighbour) break;
                var targetCenter = neighbour._grip.TranslatePoint(new Point(0, neighbour._grip.Bounds.Height / 2), panel)!.Value.Y;
                if (direction > 0 ? center <= targetCenter : center >= targetCenter) break;
                destination = i;
            }
            Move(destination - index, visualY);
        }
        grip.AddHandler(PointerPressedEvent, (_, e) =>
        {
            if (!e.GetCurrentPoint(grip).Properties.IsLeftButtonPressed || Parent is not Panel panel) return;
            grip.Focus(); translation.Transitions = null; translation.Y = 0;
            originalTop = Bounds.Y; originalIndex = panel.Children.IndexOf(this);
            origin = lastPointerY = e.GetPosition(TopLevel.GetTopLevel(this)).Y; dragPointer = e.Pointer;
            e.Pointer.Capture(grip); e.Handled = true;
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        grip.PointerMoved += (_, e) =>
        {
            if (origin is null) return;
            UpdateDrag(e.GetPosition(TopLevel.GetTopLevel(this)).Y); e.Handled = true;
        };
        grip.AddHandler(PointerReleasedEvent, (_, e) =>
        {
            if (origin is null) return;
            UpdateDrag(e.GetPosition(TopLevel.GetTopLevel(this)).Y);
            var changed = dragging && Parent is Panel panel && panel.Children.IndexOf(this) != originalIndex;
            origin = null; dragPointer = null; e.Pointer.Capture(null);
            dragging = false; Settle();
            if (changed) OrderCommitted?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        grip.PointerCaptureLost += (_, _) => { if (!moving && origin is not null) CancelDrag(); };
        grip.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && origin is not null) { CancelDrag(); e.Handled = true; }
            else if (origin is null && e.KeyModifiers.HasFlag(KeyModifiers.Alt) && e.Key is Key.Up or Key.Down)
            {
                MoveRequested?.Invoke(this, e.Key == Key.Up ? -1 : 1);
                OrderCommitted?.Invoke(this, EventArgs.Empty); e.Handled = true;
            }
        };
        header.Children.Add(_disclosure); Grid.SetColumn(grip, 1); header.Children.Add(grip);
        _body = new ContentControl { Margin = new Thickness(8) };
        _root.Children.Add(header); _root.Children.Add(_body); Child = _root; Update();
    }
    private void AnimateDisplacement(double previousVisualTop)
    {
        var transform = (TranslateTransform)RenderTransform!;
        var offset = previousVisualTop - Bounds.Y;
        // FLIP: keep the displaced section at its visible position, then ease into its new slot.
        transform.Transitions = null;
        transform.Y = offset;
        transform.Transitions = new Transitions
        {
            new DoubleTransition
            {
                Property = TranslateTransform.YProperty,
                Duration = TimeSpan.FromMilliseconds(250),
                Easing = new CubicEaseOut()
            }
        };
        transform.Y = 0;
    }
    private void Update()
    {
        _disclosure.Content = $"{(IsExpanded ? "⌄" : "›")}   {_title}";
        AutomationProperties.SetName(_disclosure, $"{_title}, {(IsExpanded ? "expanded" : "collapsed")}");
        _body.IsVisible = IsExpanded;
    }
}
