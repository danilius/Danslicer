using System.Windows.Input;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.Input;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Danslicer.App.Controls;
using Danslicer.App.Configuration;
using Danslicer.App.ViewModels;
using Danslicer.Core;
using Danslicer.Core.IO;

namespace Danslicer.App.Views;

public partial class MainWindow : Window
{
    public ICommand SaveProjectCommand { get; }
    public ICommand SaveProjectAsCommand { get; }
    public ICommand OpenProjectCommand { get; }
    public ICommand ImportCommand { get; }
    public ModeScopedCommand ExportCommand { get; }

    public MainWindow()
    {
        SaveProjectCommand = new AsyncRelayCommand(SaveProjectAsync);
        SaveProjectAsCommand = new AsyncRelayCommand(SaveProjectAsAsync);
        OpenProjectCommand = new AsyncRelayCommand(OpenProjectAsync);
        ImportCommand = new RelayCommand(() => OnImportClick(this, new RoutedEventArgs()));
        ExportCommand = new ModeScopedCommand(
            new RelayCommand(() => OnExportClick(this, new RoutedEventArgs())),
            () => ViewModel?.ViewMode ?? WorkspaceMode.Layout,
            WorkspaceMode.Slicing);
        InitializeComponent();
        Configuration.WindowStatePersistence.Track(this, "main",
            WorkspaceGrid.ColumnDefinitions[0], WorkspaceGrid.ColumnDefinitions[4]);
        AddWindowKeyBinding("Ctrl+Z", () => ViewModel?.UndoCommand);
        AddWindowKeyBinding("Ctrl+Shift+Z", () => ViewModel?.RedoCommand);
        AddWindowKeyBinding("Ctrl+Y", () => ViewModel?.RedoCommand);
        AddWindowKeyBinding("Delete", () => ViewModel?.DeleteCommand);
        AddWindowKeyBinding("Ctrl+D", () => ViewModel?.DropToPlateScopedCommand);
        AddWindowKeyBinding("Ctrl+G", () => ViewModel?.GenerateSupportsScopedCommand);
        AddWindowKeyBinding("Ctrl+A", () => ViewModel?.SelectAllCommand);
        AddWindowKeyBinding("Shift+H", () => ViewModel?.HideUnselectedSupportsScopedCommand);
        AddWindowKeyBinding("Ctrl+R", () => ViewModel?.SliceScopedCommand);
        AddWindowKeyBinding("Ctrl+S", () => SaveProjectCommand);
        AddWindowKeyBinding("Ctrl+Shift+S", () => SaveProjectAsCommand);
        AddWindowKeyBinding("Ctrl+O", () => OpenProjectCommand);
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
        Opened += (_, _) =>
        {
            Viewport.FrameAll();
            if (ViewModel is { } vm)
                vm.SupportSettings.EditSupportPresetRequested += OpenSupportPresetEditor;
        };
        Closed += (_, _) =>
        {
            if (ViewModel is { } vm)
                vm.SupportSettings.EditSupportPresetRequested -= OpenSupportPresetEditor;
            _presetEditorWindow?.Close();
        };
    }

    private MainViewModel? ViewModel => DataContext as MainViewModel;
    private ConfigWindow? _configWindow;
    private SupportPresetEditorWindow? _presetEditorWindow;

    private void OpenSupportPresetEditor()
    {
        if (_presetEditorWindow is { } open)
        {
            open.Activate();
            return;
        }
        var main = ViewModel;
        if (main is null) return;
        var presetName = AppConfig.Current.ActiveSupportPresetName;
        if (AppConfig.Current.FindSupportPreset(presetName) is null) return;
        var editor = new SupportPresetEditorViewModel(presetName, () => ViewModel?.SelectedObject);
        editor.Saved += main.SupportSettings.ApplyExternalSupportPresetChange;
        _presetEditorWindow = new SupportPresetEditorWindow(editor);
        _presetEditorWindow.Closed += (_, _) =>
        {
            editor.Saved -= main.SupportSettings.ApplyExternalSupportPresetChange;
            _presetEditorWindow = null;
        };
        _presetEditorWindow.Show(this);
    }

    private async Task SaveProjectAsync()
    {
        if (ViewModel is not { } vm) return;
        if (vm.IsGeneratingSupports || vm.IsSlicing)
        {
            vm.ViewportStatus = "Wait for the current operation before saving a project.";
            return;
        }
        if (vm.ProjectPath is null)
        {
            await SaveProjectAsAsync();
            return;
        }
        try
        {
            vm.SaveProject(vm.ProjectPath, CaptureProjectViewState());
        }
        catch (Exception ex)
        {
            vm.ViewportStatus = $"Save failed: {ex.Message}";
        }
    }

    private async Task SaveProjectAsAsync()
    {
        if (ViewModel is not { } vm) return;
        if (vm.IsGeneratingSupports || vm.IsSlicing)
        {
            vm.ViewportStatus = "Wait for the current operation before saving a project.";
            return;
        }
        var suggested = vm.ProjectPath is { } current
            ? System.IO.Path.GetFileName(current)
            : $"{vm.Document.Scene.Objects.FirstOrDefault()?.Name ?? "project"}.{ProjectFile.Extension}";
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save Danslicer project",
            SuggestedFileName = suggested,
            DefaultExtension = ProjectFile.Extension,
            FileTypeChoices =
            [
                new FilePickerFileType("Danslicer project")
                    { Patterns = [$"*.{ProjectFile.Extension}"] },
            ],
        });
        var path = file?.TryGetLocalPath();
        if (path is null) return;
        try
        {
            vm.SaveProject(path, CaptureProjectViewState());
        }
        catch (Exception ex)
        {
            vm.ViewportStatus = $"Save failed: {ex.Message}";
        }
    }

    private async Task OpenProjectAsync()
    {
        if (ViewModel is not { } vm) return;
        if (vm.IsGeneratingSupports || vm.IsSlicing)
        {
            vm.ViewportStatus = "Wait for the current operation before opening a project.";
            return;
        }
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open Danslicer project",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Danslicer project")
                    { Patterns = [$"*.{ProjectFile.Extension}"] },
                FilePickerFileTypes.All,
            ],
        });
        var path = files.FirstOrDefault()?.TryGetLocalPath();
        if (path is null) return;
        try
        {
            ApplyProjectViewState(vm.OpenProject(path));
            Viewport.Focus();
        }
        catch (Exception ex)
        {
            vm.ViewportStatus = $"Open failed: {ex.Message}";
        }
    }

    private ProjectViewState CaptureProjectViewState() => new()
    {
        CameraTarget = Viewport.Camera.Target,
        CameraDistance = Viewport.Camera.Distance,
        CameraYaw = Viewport.Camera.Yaw,
        CameraPitch = Viewport.Camera.Pitch,
        CameraFovDegrees = Viewport.Camera.FovDegrees,
        CameraOrthographic = Viewport.Camera.Orthographic,
        WorkspaceMode = ViewModel?.ViewMode ?? WorkspaceMode.Layout,
    };

    private void ApplyProjectViewState(ProjectViewState state)
    {
        var defaults = new ProjectViewState();
        var camera = Viewport.Camera;
        camera.Target = IsFinite(state.CameraTarget) ? state.CameraTarget : defaults.CameraTarget;
        camera.Distance = float.IsFinite(state.CameraDistance)
            ? Math.Clamp(state.CameraDistance, 1f, 50_000f) : defaults.CameraDistance;
        camera.Yaw = float.IsFinite(state.CameraYaw) ? state.CameraYaw : defaults.CameraYaw;
        camera.Pitch = float.IsFinite(state.CameraPitch)
            ? Math.Clamp(state.CameraPitch, -89.9f * MathF.PI / 180f, 89.9f * MathF.PI / 180f)
            : defaults.CameraPitch;
        camera.FovDegrees = float.IsFinite(state.CameraFovDegrees)
            ? Math.Clamp(state.CameraFovDegrees, 1f, 179f) : defaults.CameraFovDegrees;
        camera.Orthographic = state.CameraOrthographic;
        Viewport.RequestRedraw();
    }

    private static bool IsFinite(System.Numerics.Vector3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

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
        var config = ViewModel?.SupportSettings ?? new ConfigViewModel();
        _configWindow = new ConfigWindow(config);
        config.Saved += Viewport.RequestRedraw;
        _configWindow.Closed += (_, _) => config.Saved -= Viewport.RequestRedraw;
        _configWindow.Closed += (_, _) => _configWindow = null;
        _configWindow.Show(this);
    }

    private void OnOpenProjectClick(object? sender, RoutedEventArgs e) => OpenProjectCommand.Execute(null);
    private void OnSaveProjectClick(object? sender, RoutedEventArgs e) => SaveProjectCommand.Execute(null);
    private void OnSaveProjectAsClick(object? sender, RoutedEventArgs e) => SaveProjectAsCommand.Execute(null);

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
        if (ViewModel is not { IsLayersView: true }) return;
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
        if (ViewModel is not { IsLayoutView: true }) return;
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
