using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Danslicer.App.Configuration;
using Danslicer.App.Controls;
using Danslicer.App.Controls.Refresh;
using Danslicer.App.ViewModels;
using Danslicer.Core;
using Danslicer.Core.Supports;

namespace Danslicer.App.Views;

/// <summary>Fixed viewport overlay: the main viewport shows a detached, replaceable support graph.</summary>
public sealed class StructurePreviewPanel : UserControl
{
    private readonly StructurePreview _preview;
    private readonly ViewportControl _viewport;
    private readonly Document _document;
    private readonly Core.Config.SupportConfig _settings;
    private readonly ConfigViewModel _config;
    private readonly ConfigViewModel _globalConfig;
    private readonly DispatcherTimer _timer = new(DispatcherPriority.Normal) { Interval = TimeSpan.FromMilliseconds(180) };
    private readonly TextBlock _summary = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8) };
    private readonly TextBlock _constraintDetails = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6) };
    private readonly Expander _constraints = new() { Header = "Why supports remain independent", IsVisible = false, HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly Button _apply = new() { Content = "Apply", MinWidth = 90 };
    private readonly CheckBox _remember = new() { Content = "Remember these arrangement settings", IsChecked = true };
    private readonly CheckBox _single = new() { Content = "Make these tips one candelabra" };
    private readonly NumericUpDown _x = new() { Increment = 0.5m, FormatString = "0.##", Minimum = -10000, Maximum = 10000 };
    private readonly NumericUpDown _y = new() { Increment = 0.5m, FormatString = "0.##", Minimum = -10000, Maximum = 10000 };

    public StructurePreviewPanel(Document document, ViewportControl viewport, ConfigViewModel globalConfig, bool bracing)
    {
        _document = document; _viewport = viewport; _globalConfig = globalConfig;
        _settings = document.SupportSettings with { };
        _preview = new StructurePreview(document, bracing);
        _config = new ConfigViewModel(_settings, QueuePreview) { ShowParentingSettings = !bracing, ShowBracingSettings = bracing };
        var title = bracing ? "Preview bracing" : "Preview parenting";
        Width = 420; HorizontalAlignment = HorizontalAlignment.Right; VerticalAlignment = VerticalAlignment.Stretch;
        Focusable = true;
        Resources.MergedDictionaries.Add(RefreshPalette.CreateResources());
        Background = this.FindResource("AppPopupBackground") as IBrush ?? RefreshPalette.Panel;
        var root = new DockPanel { Margin = new Thickness(16), LastChildFill = true, Background = Background };
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 0, 0, 12) };
        header.Children.Add(new TextBlock { Text = title, FontWeight = FontWeight.SemiBold, FontSize = 16, VerticalAlignment = VerticalAlignment.Center });
        var close = new Button { Content = "×", MinWidth = 28, Padding = new Thickness(4, 0) };
        ToolTip.SetTip(close, "Cancel preview"); close.Click += (_, _) => Close();
        Grid.SetColumn(close, 1); header.Children.Add(close);
        DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        var footer = new StackPanel { Spacing = 8 };
        _constraints.Content = new ScrollViewer { Content = _constraintDetails, MaxHeight = 120 };
        footer.Children.Add(_summary); footer.Children.Add(_constraints); footer.Children.Add(_remember);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };
        var cancel = new Button { Content = "Cancel", MinWidth = 90 };
        cancel.Click += (_, _) => Close();
        _apply.Click += (_, _) => Apply();
        buttons.Children.Add(cancel); buttons.Children.Add(_apply); footer.Children.Add(buttons);
        DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        var body = new StackPanel { Spacing = 12 };
        body.Children.Add(new TextBlock { Text = "Preview on the model · orbit with the middle mouse button; scroll to zoom.", TextWrapping = TextWrapping.Wrap });
        body.Children.Add(new StructureSettingsView { DataContext = _config });
        if (!bracing)
        {
            var position = new StackPanel { Spacing = 6 };
            var operandNodes = document.StructureOperands().SelectMany(id => document.Supports.TryGetNode(id, out var node)
                ? document.Supports.Component(node.Id).Nodes : document.Supports.TryGetSegment(id, out var segment)
                    ? document.Supports.Component(segment.NodeA).Nodes : []).Distinct()
                .Select(document.Supports.GetNode).Where(n => n.Type == SupportNodeType.Tip).Select(n => n.Position).ToList();
            var centre = operandNodes.Count == 0 ? Vector3.Zero : (operandNodes.Aggregate(Vector3.Min) + operandNodes.Aggregate(Vector3.Max)) / 2;
            _x.Value = (decimal)centre.X; _y.Value = (decimal)centre.Y;
            position.Children.Add(new TextBlock { Text = "Trunk centre (mm)", FontWeight = FontWeight.SemiBold });
            position.Children.Add(new TextBlock { Text = "Adjust X and Y to reposition the trunk and regenerate its branches. Turn off the base grid for an exact position.", TextWrapping = TextWrapping.Wrap });
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("20,*,20,*") };
            row.Children.Add(new TextBlock { Text = "X", VerticalAlignment = VerticalAlignment.Center });
            Grid.SetColumn(_x, 1); row.Children.Add(_x);
            var label = new TextBlock { Text = "Y", VerticalAlignment = VerticalAlignment.Center }; Grid.SetColumn(label, 2); row.Children.Add(label);
            Grid.SetColumn(_y, 3); row.Children.Add(_y); position.Children.Add(row);
            position.IsVisible = false;
            _single.IsCheckedChanged += (_, _) => { position.IsVisible = _single.IsChecked == true; QueuePreview(); };
            _x.ValueChanged += (_, _) => QueuePreview(); _y.ValueChanged += (_, _) => QueuePreview();
            var manual = new StackPanel { Spacing = 6 }; manual.Children.Add(_single); manual.Children.Add(position);
            body.Children.Add(manual);
            void StyleChanged() { manual.IsVisible = _config.IsCandelabra; }
            _config.PropertyChanged += (_, _) => StyleChanged(); StyleChanged();
        }
        root.Children.Add(new ScrollViewer { Content = body, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto });
        Content = new Border { Background = Background, BorderBrush = RefreshPalette.Edge, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Child = root };
        _timer.Tick += (_, _) => { _timer.Stop(); RefreshPreview(); };
        _preview.Invalidated += Invalidated;
        _preview.BeforeUndo += () => { if (_timer.IsEnabled) { _timer.Stop(); RefreshPreview(); } };
        _preview.Dismissed += Close;
        KeyDown += (_, e) => { if (e.Key == Key.Escape) { Close(); e.Handled = true; } };
        viewport.StructurePreviewActive = true;
        viewport.StructurePreviewCancelRequested += Close;
        viewport.StructurePreviewElementClicked += ShowSupportConstraint;
        void ViewportChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
        {
            if (args.Property == ViewportControl.DocumentProperty ||
                (args.Property == ViewportControl.SupportSelectionModeProperty && !viewport.SupportSelectionMode)) Close();
        }
        viewport.PropertyChanged += ViewportChanged;
        AttachedToVisualTree += (_, _) => RefreshPreview();
        Closed += () =>
        {
            _timer.Stop(); _preview.Dispose();
            viewport.StructurePreviewCancelRequested -= Close;
            viewport.StructurePreviewElementClicked -= ShowSupportConstraint;
            viewport.PropertyChanged -= ViewportChanged;
            viewport.StructurePreviewActive = false; viewport.SetStructurePreview(null);
        };
    }

    public event Action? Closed;
    public bool Bracing => _preview.Bracing;

    public bool CommitForNextOperation()
    {
        _timer.Stop();
        RefreshPreview();
        if (_preview.CanApply)
        {
            if (!_apply.IsEnabled) return false;
            Apply();
            return _closed;
        }
        Close();
        return true;
    }
    private bool _closed;
    public void Close()
    {
        if (_closed) return;
        _closed = true;
        Closed?.Invoke();
    }

    private void ShowSupportConstraint(Guid element)
    {
        if (!_preview.ElementConstraints.TryGetValue(element, out var reason)) return;
        _constraintDetails.Text = "Selected orange support\n\n" + reason;
        _constraints.IsVisible = true;
        _constraints.IsExpanded = true;
    }

    private void QueuePreview()
    {
        _apply.IsEnabled = false;
        _summary.Text = "Updating preview…";
        _timer.Stop(); _timer.Start();
    }

    private void RefreshPreview()
    {
        try
        {
            _preview.Update(_settings, _config.IsCandelabra && _single.IsChecked == true
                ? new Vector2((float)(_x.Value ?? 0), (float)(_y.Value ?? 0)) : null);
            _viewport.SetStructurePreview(_preview.Graph, _preview.HighlightedElements);
            _summary.Text = _preview.Summary;
            _constraintDetails.Text = "Click an orange support to inspect its routing blocker.\n\n" + _preview.ConstraintDetails;
            _constraints.IsVisible = !string.IsNullOrWhiteSpace(_preview.ConstraintDetails);
            _constraints.IsExpanded = _preview.HighlightedElements.Count > 0;
            _apply.IsEnabled = _preview.CanApply;
        }
        catch (Exception ex)
        {
            _viewport.SetStructurePreview(null);
            _summary.Text = "Preview could not be built: " + ex.Message;
            _apply.IsEnabled = false;
        }
    }

    private void Invalidated()
    {
        _timer.Stop(); _viewport.SetStructurePreview(null);
        _summary.Text = _preview.Summary; _apply.IsEnabled = false;
    }

    private void Apply()
    {
        if (!_preview.Apply()) return;
        if (_remember.IsChecked == true)
        {
            ArrangementSettings.CopyTo(_settings, AppConfig.Current.Supports, _preview.Bracing);
            ArrangementSettings.CopyTo(_settings, _document.SupportSettings, _preview.Bracing);
            AppConfig.Save();
            _globalConfig.ApplyExternalSupportPresetChange();
        }
        Close();
    }
}
