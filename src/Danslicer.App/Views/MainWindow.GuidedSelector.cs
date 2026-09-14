using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Danslicer.App.Controls;
using Danslicer.App.Controls.Refresh;

namespace Danslicer.App.Views;

public partial class MainWindow
{
    private readonly Flyout _guidedSelector = new() { Placement = PlacementMode.Right };
    private readonly DispatcherTimer _guidedHoldTimer = new() { Interval = TimeSpan.FromMilliseconds(350) };
    private ViewportControl.GuidedTool _selectedGuidedTool = ViewportControl.GuidedTool.Place;
    private IPointer? _guidedPointer;
    private bool _guidedHoldOpened;
    private StackPanel _guidedToolRow = null!;
    private readonly Dictionary<ViewportControl.GuidedTool, Button> _guidedChoices = [];

    private static string GuidedShortcut(ViewportControl.GuidedTool tool) => tool switch
    {
        ViewportControl.GuidedTool.Place => "T", ViewportControl.GuidedTool.Line => "L",
        ViewportControl.GuidedTool.Polygon => "P", ViewportControl.GuidedTool.Edge => "E",
        ViewportControl.GuidedTool.Ring => "R", ViewportControl.GuidedTool.Contour => "C",
        ViewportControl.GuidedTool.Densify => "D", _ => "Shift+D"
    };

    private void InitializeGuidedSelector()
    {
        _guidedToolRow = (StackPanel)GuidedToolButton.Content!;
        GuidedToolButton.Content = null;
        var content = new Grid();
        content.Children.Add(_guidedToolRow);
        content.Children.Add(new TextBlock
        {
            Text = "◢", FontSize = 8, Foreground = RefreshPalette.Muted,
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, -5, -5), IsHitTestVisible = false
        });
        GuidedToolButton.Content = content;
        GuidedToolButton.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        var choices = new StackPanel { Spacing = 2 };
        foreach (var tool in Enum.GetValues<ViewportControl.GuidedTool>())
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("24,*,Auto"), ColumnSpacing = 8 };
            row.Children.Add(RefreshIcons.Create(tool.ToString().ToLowerInvariant()));
            var label = new TextBlock { Text = tool.ToString(), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(label, 1); row.Children.Add(label);
            var shortcut = new TextBlock { Text = GuidedShortcut(tool), Foreground = RefreshPalette.Muted, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(shortcut, 2); row.Children.Add(shortcut);
            var button = new Button { Content = row, HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch, MinWidth = 210, Padding = new Thickness(8, 6) };
            AutomationProperties.SetName(button, $"{tool} ({GuidedShortcut(tool)})");
            button.Click += (_, _) =>
            {
                _selectedGuidedTool = tool;
                UpdateGuidedTool();
                _guidedSelector.Hide();
                Viewport.StartGuidedTool(tool);
            };
            _guidedChoices.Add(tool, button);
            choices.Children.Add(button);
        }
        _guidedSelector.Content = choices;
        _guidedSelector.Opened += (_, _) => _guidedChoices[_selectedGuidedTool].Focus();
        choices.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { _guidedSelector.Hide(); GuidedToolButton.Focus(); e.Handled = true; }
        };
        _guidedHoldTimer.Tick += (_, _) =>
        {
            _guidedHoldTimer.Stop();
            _guidedHoldOpened = true;
            OpenGuidedSelector();
        };
        // Handle pointer input before Button so a completed hold cannot also activate a tool.
        GuidedToolButton.AddHandler(PointerPressedEvent, (_, e) =>
        {
            if (!e.GetCurrentPoint(GuidedToolButton).Properties.IsLeftButtonPressed) return;
            _guidedHoldOpened = false;
            _guidedPointer = e.Pointer;
            e.Pointer.Capture(GuidedToolButton);
            GuidedToolButton.Focus();
            var point = e.GetPosition(GuidedToolButton);
            if (point.X >= GuidedToolButton.Bounds.Width - 12 && point.Y >= GuidedToolButton.Bounds.Height - 12)
            {
                _guidedHoldOpened = true;
                OpenGuidedSelector();
            }
            else _guidedHoldTimer.Start();
            e.Handled = true;
        }, RoutingStrategies.Tunnel);
        GuidedToolButton.AddHandler(PointerReleasedEvent, (_, e) =>
        {
            if (e.Pointer != _guidedPointer || e.InitialPressMouseButton != MouseButton.Left) return;
            var activate = !_guidedHoldOpened && new Rect(GuidedToolButton.Bounds.Size).Contains(e.GetPosition(GuidedToolButton));
            CancelGuidedHold();
            if (activate) Viewport.StartGuidedTool(_selectedGuidedTool);
            e.Handled = true;
        }, RoutingStrategies.Tunnel);
        GuidedToolButton.PointerCaptureLost += (_, _) => CancelGuidedHold();
        GuidedToolButton.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && _guidedPointer is not null)
            { CancelGuidedHold(); _guidedSelector.Hide(); e.Handled = true; return; }
            if (e.Key == Key.Down && e.KeyModifiers.HasFlag(KeyModifiers.Alt))
            { OpenGuidedSelector(); e.Handled = true; }
        };
        Deactivated += (_, _) => { CancelGuidedHold(); _guidedSelector.Hide(); };
        Closed += (_, _) => CancelGuidedHold();
        Viewport.ActiveGuidedToolChanged += (_, _) =>
        {
            if (Viewport.ActiveGuidedTool is { } tool) _selectedGuidedTool = tool;
            UpdateGuidedTool();
        };
        UpdateGuidedTool();
    }

    private void CancelGuidedHold()
    {
        _guidedHoldTimer.Stop();
        var pointer = _guidedPointer;
        _guidedPointer = null;
        pointer?.Capture(null);
    }

    private void OpenGuidedSelector()
    {
        CloseViewportToolPopups();
        CloseExtraPopouts();
        ViewSettingsPopup.IsOpen = false;
        _guidedSelector.ShowAt(GuidedToolButton);
    }

    private void OnGuidedToolClick(object? sender, RoutedEventArgs e) => Viewport.StartGuidedTool(_selectedGuidedTool);

    private void UpdateGuidedTool()
    {
        _guidedToolRow.Children[0] = RefreshIcons.Create(_selectedGuidedTool.ToString().ToLowerInvariant());
        ((TextBlock)_guidedToolRow.Children[1]).Text = _selectedGuidedTool.ToString();
        var name = $"{_selectedGuidedTool} ({GuidedShortcut(_selectedGuidedTool)})";
        AutomationProperties.SetName(GuidedToolButton, name);
        ToolTip.SetTip(GuidedToolButton, $"{name} — hold, click the corner, or Alt+Down to choose a support tool");
        var active = Viewport.ActiveGuidedTool;
        GuidedToolButton.Background = active is not null ? RefreshPalette.Fill : Brushes.Transparent;
        foreach (var (tool, button) in _guidedChoices)
            button.Background = tool == active ? RefreshPalette.Fill : Brushes.Transparent;
    }
}
