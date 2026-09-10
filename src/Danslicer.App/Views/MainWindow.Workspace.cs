using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using Danslicer.App.Controls.Refresh;
using Danslicer.App.ViewModels;
using Danslicer.Core;

namespace Danslicer.App.Views;

public partial class MainWindow
{
    private readonly FloatingToolbar _workspaceToolbar = new() { ShowLabels = false, Margin = new Thickness(12) };
    private readonly List<WorkspacePopout> _workspacePopouts = [];
    private readonly List<string> _recentProjects = [];
    private WorkspacePopout _inspectionPopout = null!;
    private Button _printTool = null!, _layoutTool = null!, _inspectionTool = null!;

    private void InitializeWorkspace()
    {
        // Opt-in resources stay on the workspace, preserving the status bar's existing theme.
        ViewportSurface.Resources.MergedDictionaries.Add(RefreshPalette.CreateResources());
        foreach (var panel in new[] { LegacyTools, LegacyViewTools })
        {
            foreach (var popup in panel.Children.OfType<WorkspacePopout>().ToArray())
            {
                panel.Children.Remove(popup);
                ViewportSurface.Children.Add(popup);
            }
            foreach (var button in panel.Children.OfType<Button>().ToArray())
            {
                panel.Children.Remove(button);
                var label = ToolLabel(button.Name);
                _workspaceToolbar.AddExistingTool(button, label, ToolIcon(button.Name));
            }
            panel.IsVisible = false;
        }
        _layoutTool = AddWorkspaceTool("Layout options", "move", LayoutPopout);
        _printTool = AddWorkspaceTool("Print settings", "rafts", PrintPopout);
        ViewportSurface.Children.Remove(InspectionContent);
        InspectionContent.ClearValue(IsVisibleProperty);
        InspectionContent.MinHeight = 300;
        _inspectionPopout = new WorkspacePopout { Title = "Layer isolation", Content = InspectionContent };
        ViewportSurface.Children.Add(_inspectionPopout);
        _inspectionTool = AddWorkspaceTool("Layer isolation", "visibility", _inspectionPopout);
        var labels = new Button();
        _workspaceToolbar.AddExistingTool(labels, "Toolbar labels", "select");
        labels.Click += (_, _) => { _workspaceToolbar.ShowLabels = !_workspaceToolbar.ShowLabels; PositionWorkspacePopouts(); };
        ViewportSurface.Children.Add(_workspaceToolbar);
        foreach (var popup in ViewportSurface.Children.OfType<WorkspacePopout>().ToArray())
        {
            _workspacePopouts.Add(popup);
            if (popup.PlacementTarget is Button tool) popup.Title = ToolLabel(tool.Name);
            RemoveLegacyHeader(popup.Content as Control);
            popup.Initialize(() => CloseWorkspacePopout(popup));
            popup.Opened += (_, _) => { PositionWorkspacePopouts(); UpdateToolSelection(); };
            popup.Closed += (_, _) => { popup.PlacementTarget?.Focus(); UpdateToolSelection(); };
            popup.ZIndex = 20;
        }
        // Group real top-level sections without replacing their numeric editors or commands.
        if (PrintPopout.Shell.Body is StackPanel print) GroupSections(print);
        if (TransformPopupContent.Child is ScrollViewer { Content: StackPanel transform })
            foreach (var section in transform.Children.OfType<StackPanel>().ToArray()) GroupSections(section);
        ViewportSurface.SizeChanged += (_, _) => PositionWorkspacePopouts();
        _workspaceToolbar.SizeChanged += (_, _) => PositionWorkspacePopouts();
        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key != Key.Escape || (e.Handled && !ReferenceEquals(e.Source, Viewport) && !ReferenceEquals(e.Source, LayerView))) return;
            var open = _workspacePopouts.FirstOrDefault(p => p.IsOpen);
            if (open is null) return;
            CloseWorkspacePopout(open); e.Handled = true;
        }, RoutingStrategies.Bubble, handledEventsToo: true);
    }

    private static string ToolLabel(string? name) => name switch
    {
        "UvtoolsCheckButton" => "Check with UVtools", "ObjectsToolButton" => "Objects",
        "AddObjectToolButton" => "Add object", "SupportsToolButton" => "Support settings",
        "GenerateToolButton" => "Generate supports", "StructureToolButton" => "Structure",
        "TransformToolButton" => "Transform", "GuidedToolButton" => "Place supports",
        "RegionToolButton" => "Regions", "IslandSupportToolButton" => "Island supports",
        "IslandDetectionToolButton" => "Detect islands", "VisibilityToolButton" => "Visibility",
        "RaftsToolButton" => "Rafts", "ViewSettingsButton" => "View settings",
        _ => name?.Replace("ToolButton", "").Replace("Button", "") ?? "Tool"
    };
    private static string ToolIcon(string? name) => name switch
    {
        "ObjectsToolButton" or "AddObjectToolButton" => "objects",
        "TransformToolButton" => "move", "RaftsToolButton" => "rafts",
        "VisibilityToolButton" or "ViewSettingsButton" or "IslandDetectionToolButton" => "visibility",
        _ => "supports"
    };

    private Button AddWorkspaceTool(string label, string icon, WorkspacePopout popup)
    {
        var tool = new Button();
        _workspaceToolbar.AddExistingTool(tool, label, icon);
        popup.PlacementTarget = tool;
        tool.Click += (_, _) =>
        {
            var opening = !popup.IsOpen;
            CloseViewportToolPopups(); ViewSettingsPopup.IsOpen = false; CloseExtraPopouts();
            popup.IsOpen = opening;
            if (opening) popup.Shell.Focus();
        };
        return tool;
    }
    private void ApplyWorkspaceMode()
    {
        if (_printTool is null) return;
        _printTool.IsVisible = ViewModel?.IsLayersView == true;
        _layoutTool.IsVisible = ViewModel?.IsLayoutView == true;
        _inspectionTool.IsVisible = ViewModel?.IsSupportView == true;
        ViewSettingsButton.IsVisible = ViewModel?.IsModelView == true;
        CloseExtraPopouts();
        if (ViewModel?.IsModelView != true) ViewSettingsPopup.IsOpen = false;
    }
    private void CloseExtraPopouts()
    {
        PrintPopout.IsOpen = false; LayoutPopout.IsOpen = false;
        if (_inspectionPopout is not null) _inspectionPopout.IsOpen = false;
    }
    private void CloseWorkspacePopout(WorkspacePopout popup)
    {
        CloseViewportPopup(popup, ViewportPopupCloseTrigger.Escape);
        popup.PlacementTarget?.Focus();
    }
    private void UpdateToolSelection()
    {
        foreach (var popup in _workspacePopouts)
            if (popup.PlacementTarget is Button button)
                button.Background = popup.IsOpen ? RefreshPalette.Fill : Brushes.Transparent;
    }
    private void PositionWorkspacePopouts()
    {
        var bounds = ViewportSurface.Bounds;
        var narrow = bounds.Width < 950;
        Grid.SetRow(MachineSummary, narrow ? 1 : 0);
        Grid.SetColumn(MachineSummary, narrow ? 0 : 1);
        Grid.SetColumnSpan(MachineSummary, narrow ? 3 : 1);
        _workspaceToolbar.MaxHeight = Math.Max(40, bounds.Height - 24);
        foreach (var popup in _workspacePopouts)
        {
            var x = _workspaceToolbar.Bounds.Right + 8;
            var available = Math.Max(120, bounds.Width - x - 12);
            popup.Shell.MinWidth = Math.Min(240, available);
            popup.Shell.MaxWidth = available;
            var point = popup.PlacementTarget?.TranslatePoint(default, ViewportSurface);
            var y = Math.Clamp(point?.Y ?? 12, 12, Math.Max(12, bounds.Height - 260));
            popup.Margin = new Thickness(x, y, 12, 12);
            popup.Shell.MaxHeight = Math.Max(80, bounds.Height - y - 12);
        }
    }
    private static void RemoveLegacyHeader(Control? body)
    {
        if (body is not Border border) return;
        var content = border.Child is ScrollViewer scroll ? scroll.Content as Control : border.Child;
        if (content is Panel panel && panel.Children.FirstOrDefault() is Grid header &&
            header.Children.OfType<Button>().Any(b => b.Classes.Contains("popupClose")))
            panel.Children.Remove(header);
    }
    private static void GroupSections(StackPanel panel)
    {
        var children = panel.Children.ToArray();
        if (!children.OfType<TextBlock>().Any(t => t.Classes.Contains("section"))) return;
        panel.Children.Clear();
        StackPanel? body = null;
        foreach (var child in children)
        {
            if (child is TextBlock title && title.Classes.Contains("section"))
            {
                body = new StackPanel();
                var section = new ReorderableExpander(title.Text ?? "section", title.Text ?? "Settings") { Body = body, Margin = new Thickness(0, 0, 0, 4) };
                section.MoveRequested += (_, offset) =>
                {
                    var index = panel.Children.IndexOf(section);
                    var destination = Math.Clamp(index + offset, 0, panel.Children.Count - 1);
                    panel.Children.RemoveAt(index); panel.Children.Insert(destination, section);
                };
                panel.Children.Add(section);
            }
            else if (body is not null) body.Children.Add(child);
            else panel.Children.Add(child);
        }
    }

    private void OnProjectDropdownClick(object? sender, RoutedEventArgs e)
    {
        RememberProject();
        var menu = new ContextMenu();
        var open = new MenuItem { Header = "Open another projectâ€¦", Command = OpenProjectCommand };
        menu.Items.Add(open);
        menu.Items.Add(new Separator());
        if (_recentProjects.Count == 0) menu.Items.Add(new MenuItem { Header = "No recent projects this session", IsEnabled = false });
        foreach (var path in _recentProjects.ToArray())
        {
            var item = new MenuItem { Header = System.IO.Path.GetFileNameWithoutExtension(path) };
            ToolTip.SetTip(item, path);
            item.Click += (_, _) => OpenRecentProject(path);
            menu.Items.Add(item);
        }
        ProjectDropdown.ContextMenu = menu;
        menu.Open(ProjectDropdown);
    }
    private void RememberProject()
    {
        if (ViewModel?.ProjectPath is not { } path) return;
        _recentProjects.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        _recentProjects.Insert(0, path);
        if (_recentProjects.Count > 10) _recentProjects.RemoveAt(10);
    }
    private void OpenRecentProject(string path)
    {
        if (ViewModel is not { } vm) return;
        if (vm.IsGeneratingSupports || vm.IsSlicing)
        {
            vm.ViewportStatus = "Wait for the current operation before opening a project."; return;
        }
        try { RememberProject(); ApplyProjectViewState(vm.OpenProject(path)); RememberProject(); Viewport.Focus(); }
        catch (Exception ex) { vm.ViewportStatus = $"Open failed: {ex.Message}"; }
    }
}
