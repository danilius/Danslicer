using Avalonia.Controls;
using Danslicer.App.Configuration;
using Danslicer.App.ViewModels;
using Avalonia.LogicalTree;
using Danslicer.App.Controls.Refresh;

namespace Danslicer.App.Views;

public partial class SupportPresetEditorWindow : Window
{
    private readonly SupportPresetEditorViewModel _viewModel;

    public SupportPresetEditorWindow() : this(new SupportPresetEditorViewModel(
        AppConfig.Current.ActiveSupportPresetName, () => null))
    {
    }

    public SupportPresetEditorWindow(SupportPresetEditorViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        Resources.MergedDictionaries.Add(RefreshPalette.CreateResources());
        MainWindow.AdaptSupportSections(Content as Control);
        RestoreSections();
        Closing += (_, _) => ScrubField.CancelActive();
        Deactivated += (_, _) => ScrubField.CancelActive();
        viewModel.PreviewDocumentChanged += FramePreview;
        viewModel.CloseRequested += OnCloseRequested;
        Opened += OnOpened;
        Closed += OnClosed;
        Configuration.WindowStatePersistence.Track(this, "support-preset-editor");
    }

    private void OnOpened(object? sender, EventArgs e) => PreviewSurface.FrameAll();

    private void RestoreSections()
    {
        const string group = "SupportPresetEditor/Sections";
        var sections = this.GetLogicalDescendants().OfType<ReorderableExpander>().ToArray();
        if (sections.Length == 0 || sections[0].Parent is not Panel panel) return;
        var preferences = WorkspacePreferences.Load(AppConfig.WorkspacePath);
        var order = preferences.Order(group, sections.Select(s => s.SectionId));
        panel.Children.Clear();
        foreach (var id in order) panel.Children.Add(sections.Single(s => s.SectionId == id));
        foreach (var section in sections)
        {
            if (preferences.Expanded.TryGetValue(group + "/" + section.SectionId, out var expanded)) section.IsExpanded = expanded;
            section.OrderCommitted += (_, _) => Save();
            section.ExpansionChanged += (_, _) => Save();
        }
        void Save()
        {
            // Reload to preserve workspace changes made while this editor is open.
            var current = WorkspacePreferences.Load(AppConfig.WorkspacePath);
            current.SectionOrder[group] = panel.Children.OfType<ReorderableExpander>().Select(s => s.SectionId).ToList();
            foreach (var section in sections) current.Expanded[group + "/" + section.SectionId] = section.IsExpanded;
            current.Save(AppConfig.WorkspacePath);
        }
    }

    private void FramePreview() => PreviewSurface.FrameAll();

    private void OnCloseRequested(bool saved) => Close();

    private void OnClosed(object? sender, EventArgs e)
    {
        _viewModel.PreviewDocumentChanged -= FramePreview;
        _viewModel.CloseRequested -= OnCloseRequested;
        _viewModel.Dispose();
    }
}
