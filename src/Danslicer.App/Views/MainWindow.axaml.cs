using System.Windows.Input;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.Input;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Danslicer.App.Controls;
using Danslicer.App.ViewModels;

namespace Danslicer.App.Views;

public partial class MainWindow : Window
{
    public ICommand ImportCommand { get; }
    public ICommand ExportCommand { get; }

    public MainWindow()
    {
        ImportCommand = new RelayCommand(() => OnImportClick(this, new RoutedEventArgs()));
        ExportCommand = new RelayCommand(() => OnExportClick(this, new RoutedEventArgs()));
        InitializeComponent();
        Configuration.WindowStatePersistence.Track(this, "main",
            WorkspaceGrid.ColumnDefinitions[0], WorkspaceGrid.ColumnDefinitions[4]);
        AddWindowKeyBinding("Ctrl+Z", () => ViewModel?.UndoCommand);
        AddWindowKeyBinding("Ctrl+Shift+Z", () => ViewModel?.RedoCommand);
        AddWindowKeyBinding("Ctrl+Y", () => ViewModel?.RedoCommand);
        AddWindowKeyBinding("Delete", () => ViewModel?.DeleteCommand);
        AddWindowKeyBinding("Ctrl+D", () => ViewModel?.DropToPlateCommand);
        AddWindowKeyBinding("Ctrl+G", () => ViewModel?.GenerateSupportsCommand);
        AddWindowKeyBinding("Ctrl+A", () => ViewModel?.SelectAllCommand);
        AddWindowKeyBinding("Shift+H", () => ViewModel?.HideUnselectedSupportsCommand);
        AddWindowKeyBinding("Ctrl+R", () => ViewModel?.SliceCommand);
        AddWindowKeyBinding("Ctrl+I", () => ImportCommand);
        AddWindowKeyBinding("Ctrl+E", () => ExportCommand);
        AddWindowKeyBinding("Ctrl+OemComma",
            () => new RelayCommand(() => OnPreferencesClick(this, new RoutedEventArgs())));
        Viewport.PropertyChanged += (_, e) =>
        {
            if (e.Property == ViewportControl.StatusTextProperty && DataContext is MainViewModel vm)
                vm.ViewportStatus = Viewport.StatusText;
        };
        Viewport.ToggleViewRequested += () => ViewModel?.ToggleViewCommand.Execute(null);
        LayerView.ToggleViewRequested += () => ViewModel?.ToggleViewCommand.Execute(null);
        LayerView.LayerStepRequested += delta => ViewModel?.StepLayer(delta);
        Opened += (_, _) => Viewport.FrameAll();
    }

    private MainViewModel? ViewModel => DataContext as MainViewModel;
    private ConfigWindow? _configWindow;

    /// <summary>
    /// Registers application shortcuts in one place and lets focused text editors handle the same
    /// gestures themselves. This avoids window-level KeyBindings preempting editing commands.
    /// </summary>
    private void AddWindowKeyBinding(string gesture, Func<ICommand?> command)
    {
        KeyBindings.Add(new KeyBinding
        {
            Gesture = KeyGesture.Parse(gesture),
            Command = new TextInputGuardCommand(this, command),
        });
    }

    private sealed class TextInputGuardCommand(Window owner, Func<ICommand?> command) : ICommand
    {
        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }

        public bool CanExecute(object? parameter)
        {
            if (owner.FocusManager?.GetFocusedElement() is TextBox) return false;
            return command()?.CanExecute(parameter) == true;
        }

        public void Execute(object? parameter) => command()?.Execute(parameter);
    }

    /// <summary>Preferences is non-modal so the viewport stays live while tuning; one instance.</summary>
    private void OnPreferencesClick(object? sender, RoutedEventArgs e)
    {
        if (_configWindow is { } open)
        {
            open.Activate();
            return;
        }
        _configWindow = new ConfigWindow();
        if (_configWindow.DataContext is ConfigViewModel config)
            config.Saved += Viewport.RequestRedraw;
        _configWindow.Closed += (_, _) => _configWindow = null;
        _configWindow.Show(this);
    }

    private async void OnImportClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is null) return;
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import mesh",
            AllowMultiple = true,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Meshes") { Patterns = new[] { "*.stl", "*.obj" } },
                new FilePickerFileType("STL meshes") { Patterns = new[] { "*.stl" } },
                new FilePickerFileType("OBJ meshes") { Patterns = new[] { "*.obj" } },
                FilePickerFileTypes.All,
            },
        });

        foreach (var file in files)
        {
            var path = file.TryGetLocalPath();
            if (path is null) continue;
            try
            {
                ViewModel.ImportMesh(path);
            }
            catch (Exception ex)
            {
                ViewModel.ViewportStatus = $"Import failed: {ex.Message}";
            }
        }
        Viewport.FrameAll();
        Viewport.Focus();
    }

    private async void OnExportClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is null) return;
        var printer = ViewModel.Document.Printer;
        var suggested = ViewModel.Document.Scene.Objects.FirstOrDefault()?.Name ?? "print";
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = $"Export for {printer.Name}",
            SuggestedFileName = $"{suggested}.{printer.FileExtension}",
            DefaultExtension = printer.FileExtension,
            FileTypeChoices = new[]
            {
                new FilePickerFileType(printer.Name) { Patterns = new[] { $"*.{printer.FileExtension}" } },
            },
        });
        var path = file?.TryGetLocalPath();
        if (path is null) return;
        await ViewModel.ExportAsync(path);
    }

    private void OnLayFlatClick(object? sender, RoutedEventArgs e)
    {
        Viewport.BeginLayFlatPick();
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
