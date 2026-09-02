using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Danslicer.App.Controls;
using Danslicer.App.ViewModels;

namespace Danslicer.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Viewport.PropertyChanged += (_, e) =>
        {
            if (e.Property == ViewportControl.StatusTextProperty && DataContext is MainViewModel vm)
                vm.ViewportStatus = Viewport.StatusText;
        };
        Opened += (_, _) => Viewport.FrameAll();
    }

    private MainViewModel? ViewModel => DataContext as MainViewModel;

    private async void OnImportClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is null) return;
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import STL",
            AllowMultiple = true,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("STL meshes") { Patterns = new[] { "*.stl" } },
                FilePickerFileTypes.All,
            },
        });

        foreach (var file in files)
        {
            var path = file.TryGetLocalPath();
            if (path is null) continue;
            try
            {
                ViewModel.ImportStl(path);
            }
            catch (Exception ex)
            {
                ViewModel.ViewportStatus = $"Import failed: {ex.Message}";
            }
        }
        Viewport.FrameAll();
        Viewport.Focus();
    }

    private void OnExitClick(object? sender, RoutedEventArgs e) => Close();
    private void OnFrameAllClick(object? sender, RoutedEventArgs e) => Viewport.FrameAll();
    private void OnFrameSelectedClick(object? sender, RoutedEventArgs e) => Viewport.FrameSelected();
    private void OnViewFrontClick(object? sender, RoutedEventArgs e) => Viewport.SetView(c => c.ViewFront());
    private void OnViewRightClick(object? sender, RoutedEventArgs e) => Viewport.SetView(c => c.ViewRight());
    private void OnViewTopClick(object? sender, RoutedEventArgs e) => Viewport.SetView(c => c.ViewTop());
    private void OnToggleProjectionClick(object? sender, RoutedEventArgs e) => Viewport.ToggleProjection();
}
