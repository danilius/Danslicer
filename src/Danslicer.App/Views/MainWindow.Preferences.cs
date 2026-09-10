using Avalonia;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Danslicer.App.Configuration;
using Danslicer.App.Controls.Refresh;

namespace Danslicer.App.Views;

public partial class MainWindow
{
    private WorkspacePreferences _workspacePreferences = null!;

    private void InitializeWorkspacePreferences()
    {
        _workspacePreferences = WorkspacePreferences.Load(AppConfig.WorkspacePath);
        _workspaceToolbar.ShowLabels = _workspacePreferences.ShowToolbarLabels;
        _recentProjects.AddRange(_workspacePreferences.RecentProjects);
        _workspaceToolbar.PropertyChanged += (_, e) =>
        {
            if (e.Property != FloatingToolbar.ShowLabelsProperty) return;
            _workspacePreferences.ShowToolbarLabels = _workspaceToolbar.ShowLabels;
            SaveWorkspacePreferences();
        };
        foreach (var popup in _workspacePopouts)
        {
            var id = popup.Name ?? throw new InvalidOperationException("A persisted popout needs a stable name");
            popup.Shell.Width = _workspacePreferences.Width(id, popup.Shell.Width);
            popup.Shell.WidthCommitted += (_, _) =>
            {
                _workspacePreferences.PopoutWidths[id] = popup.Shell.Width;
                SaveWorkspacePreferences();
            };
            var groups = popup.GetLogicalDescendants().OfType<ReorderableExpander>()
                .Select(s => s.Parent).OfType<Panel>().Distinct().ToArray();
            foreach (var panel in groups)
            {
                var sections = panel.Children.OfType<ReorderableExpander>().ToArray();
                // Group identity is captured before restoring order; scoped by stable XAML popout name.
                var group = id + "/" + sections[0].SectionId;
                var ordered = _workspacePreferences.Order(group, sections.Select(s => s.SectionId));
                var slots = panel.Children.Select((c, i) => (c, i)).Where(x => x.c is ReorderableExpander).Select(x => x.i).ToArray();
                foreach (var section in sections) panel.Children.Remove(section);
                for (var i = 0; i < ordered.Count; i++) panel.Children.Insert(slots[i], sections.Single(s => s.SectionId == ordered[i]));
                foreach (var section in sections)
                {
                    var key = group + "/" + section.SectionId;
                    if (_workspacePreferences.Expanded.TryGetValue(key, out var expanded)) section.IsExpanded = expanded;
                    section.ExpansionChanged += (_, _) =>
                    {
                        _workspacePreferences.Expanded[key] = section.IsExpanded;
                        SaveWorkspacePreferences();
                    };
                    // Live swaps are transient until drop; cancellation must not write the preview order.
                    section.OrderCommitted += (_, _) =>
                    {
                        _workspacePreferences.SectionOrder[group] = panel.Children.OfType<ReorderableExpander>().Select(s => s.SectionId).ToList();
                        SaveWorkspacePreferences();
                    };
                }
            }
        }
    }

    private void SaveWorkspacePreferences()
    {
        if (_workspacePreferences is null) return;
        _workspacePreferences.RecentProjects = _recentProjects.ToList();
        try
        {
            // The modeless preset editor owns this namespace and may have saved since
            // the main window loaded its workspace snapshot.
            var latest = WorkspacePreferences.Load(AppConfig.WorkspacePath);
            foreach (var pair in latest.SectionOrder.Where(p => p.Key.StartsWith("SupportPresetEditor/", StringComparison.Ordinal)))
                _workspacePreferences.SectionOrder[pair.Key] = pair.Value;
            foreach (var pair in latest.Expanded.Where(p => p.Key.StartsWith("SupportPresetEditor/", StringComparison.Ordinal)))
                _workspacePreferences.Expanded[pair.Key] = pair.Value;
            _workspacePreferences.Save(AppConfig.WorkspacePath);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            if (ViewModel is { } vm) vm.ViewportStatus = "Could not save workspace preferences: " + e.Message;
        }
    }
}
