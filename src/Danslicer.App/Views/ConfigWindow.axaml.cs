using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Danslicer.App.ViewModels;

namespace Danslicer.App.Views;

/// <summary>
/// The preferences window: a section list and search box on the left, one long scrollable pane
/// of settings on the right. Rows carry their search keywords in <c>Tag</c>; the filter hides
/// non-matching rows and any section left empty. Settings persist and apply live through
/// <see cref="ConfigViewModel"/>, so the window needs no OK/Apply buttons.
/// </summary>
public partial class ConfigWindow : Window
{
    public ConfigWindow() : this(new ConfigViewModel())
    {
    }

    public ConfigWindow(ConfigViewModel config)
    {
        InitializeComponent();
        DataContext = config;
        Configuration.WindowStatePersistence.Track(this, "preferences");
    }

    private void OnSearchChanged(object? sender, TextChangedEventArgs e)
    {
        var query = SearchBox.Text?.Trim() ?? "";
        if (DataContext is ConfigViewModel config)
            config.Keymap.SearchText = query;
        foreach (var child in Sections.Children)
        {
            if (child is not StackPanel section) continue;
            if (section == KeymapSection)
            {
                section.IsVisible = query.Length == 0 ||
                                    (DataContext as ConfigViewModel)?.Keymap.HasVisibleBindings == true;
                continue;
            }
            var anyVisible = false;
            foreach (var row in section.GetLogicalDescendants().OfType<Control>())
            {
                if (row.Tag is not string keywords) continue; // headers and notes without tags
                row.IsVisible = query.Length == 0 || keywords.Contains(query, StringComparison.OrdinalIgnoreCase);
                anyVisible |= row.IsVisible;
            }
            section.IsVisible = query.Length == 0 || anyVisible;
        }
    }

    private void OnSectionSelected(object? sender, SelectionChangedEventArgs e)
    {
        var index = SectionList.SelectedIndex;
        if (index >= 0 && index < Sections.Children.Count)
            Sections.Children[index].BringIntoView();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (DataContext is not ConfigViewModel { Keymap.IsCapturing: true } config)
        {
            base.OnKeyDown(e);
            return;
        }

        e.Handled = true;
        if (e.Key == Key.Escape)
        {
            config.Keymap.CancelCapture();
            return;
        }
        if (e.Key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or
            Key.LeftAlt or Key.RightAlt)
            return;

        config.Keymap.Capture(new KeyGesture(e.Key, e.KeyModifiers));
    }
}
