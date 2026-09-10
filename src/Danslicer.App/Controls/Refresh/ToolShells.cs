using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;

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
        var button = new ToggleButton { Content = row, MinHeight = 36, HorizontalAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(8), Foreground = RefreshPalette.Text };
        AutomationProperties.SetName(button, tool.Label); ToolTip.SetTip(button, tool.Label);
        button.Click += (_, _) => tool.Execute();
        _buttons.Add(tool.Id, button); _items.Children.Add(button);
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
    public string Title { get => _title.Text ?? ""; set => _title.Text = value; }
    public Control? Body { get => _scroll.Content as Control; set => _scroll.Content = value; }
    public ToolPopout()
    {
        Background = RefreshPalette.Panel; BorderBrush = RefreshPalette.Edge; BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(6); MaxWidth = 360; Margin = new Thickness(0, 16, 16, 16);
        HorizontalAlignment = HorizontalAlignment.Stretch; VerticalAlignment = VerticalAlignment.Top;
        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Background = RefreshPalette.Header };
        _title = new TextBlock { Margin = new Thickness(12, 8), VerticalAlignment = VerticalAlignment.Center, Foreground = RefreshPalette.Text };
        var close = new Button { Content = RefreshIcons.Create("close"), Width = 36, Height = 36, Padding = new Thickness(7) };
        AutomationProperties.SetName(close, "Close tool settings"); ToolTip.SetTip(close, "Close (Escape)");
        close.Click += (_, _) => RequestClose();
        header.Children.Add(_title); Grid.SetColumn(close, 1); header.Children.Add(close);
        _scroll = new ScrollViewer { Margin = new Thickness(8), HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        root.Children.Add(header); Grid.SetRow(_scroll, 1); root.Children.Add(_scroll); Child = root;
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
    private bool _expanded = true;
    public string SectionId { get; }
    public event EventHandler? ExpansionChanged;
    public event EventHandler<int>? MoveRequested;
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
        Background = RefreshPalette.Panel;
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Background = RefreshPalette.Header };
        _disclosure = new Button { HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left, MinHeight = 34, Padding = new Thickness(8, 4), Background = RefreshPalette.Header, Foreground = RefreshPalette.Text };
        _disclosure.Click += (_, _) => IsExpanded = !IsExpanded;
        var grip = new Button { Content = RefreshIcons.Create("grip"), Width = 34, Height = 34, Padding = new Thickness(6), Background = RefreshPalette.Header };
        AutomationProperties.SetName(grip, $"Reorder {title}"); ToolTip.SetTip(grip, "Drag to reorder; Alt+Up/Down");
        double? origin = null;
        grip.AddHandler(PointerPressedEvent, (_, e) =>
        {
            if (!e.GetCurrentPoint(grip).Properties.IsLeftButtonPressed) return;
            origin = e.GetPosition(this).Y; e.Pointer.Capture(grip); e.Handled = true;
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        grip.AddHandler(PointerReleasedEvent, (_, e) =>
        {
            if (origin is not { } start) return;
            var delta = e.GetPosition(this).Y - start;
            origin = null; e.Pointer.Capture(null);
            if (Math.Abs(delta) >= 6) MoveRequested?.Invoke(this, delta > 0 ? 1 : -1);
            e.Handled = true;
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        grip.PointerCaptureLost += (_, _) => origin = null;
        grip.KeyDown += (_, e) =>
        {
            if (e.KeyModifiers.HasFlag(KeyModifiers.Alt) && e.Key is Key.Up or Key.Down)
            { MoveRequested?.Invoke(this, e.Key == Key.Up ? -1 : 1); e.Handled = true; }
        };
        header.Children.Add(_disclosure); Grid.SetColumn(grip, 1); header.Children.Add(grip);
        _body = new ContentControl { Margin = new Thickness(8) };
        _root.Children.Add(header); _root.Children.Add(_body); Child = _root; Update();
    }
    private void Update()
    {
        _disclosure.Content = $"{(IsExpanded ? "⌄" : "›")}   {_title}";
        AutomationProperties.SetName(_disclosure, $"{_title}, {(IsExpanded ? "expanded" : "collapsed")}");
        _body.IsVisible = IsExpanded;
    }
}
