using System.Windows.Input;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using CommunityToolkit.Mvvm.Input;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Danslicer.App.Controls;
using Danslicer.App.Configuration;
using Danslicer.App.ViewModels;
using Danslicer.Core;
using Danslicer.Core.Config;
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
            rightPanel: WorkspaceGrid.ColumnDefinitions[2],
            rightPanelWidthProvider: PersistedRightPanelWidth);
        _expandedRightPanelWidth = WorkspaceGrid.ColumnDefinitions[2].Width;
        DataContextChanged += OnMainDataContextChanged;
        AttachPanelLayoutViewModel();
        RefreshWindowKeymap();
        SyncRenderPathMenu();
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
    private readonly List<KeyBinding> _windowKeyBindings = [];
    private MainViewModel? _panelLayoutViewModel;
    private GridLength _expandedRightPanelWidth = new(320);

    private void OnMainDataContextChanged(object? sender, EventArgs e) =>
        AttachPanelLayoutViewModel();

    private void AttachPanelLayoutViewModel()
    {
        if (_panelLayoutViewModel is not null)
            _panelLayoutViewModel.PropertyChanged -= OnPanelLayoutPropertyChanged;
        _panelLayoutViewModel = ViewModel;
        if (_panelLayoutViewModel is not null)
            _panelLayoutViewModel.PropertyChanged += OnPanelLayoutPropertyChanged;
        ApplyRightPanelMode();
    }

    private void OnPanelLayoutPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.ViewMode)) ApplyRightPanelMode();
    }

    private void ApplyRightPanelMode()
    {
        var splitterColumn = WorkspaceGrid.ColumnDefinitions[1];
        var rightPanelColumn = WorkspaceGrid.ColumnDefinitions[2];
        var showRightPanel = ViewModel?.ViewMode != WorkspaceMode.Support;
        if (!showRightPanel)
        {
            var currentWidth = rightPanelColumn.ActualWidth >= 220
                ? rightPanelColumn.ActualWidth
                : rightPanelColumn.Width.Value;
            if (currentWidth >= 220) _expandedRightPanelWidth = new GridLength(currentWidth);
            RightPanel.IsVisible = false;
            RightPanelSplitter.IsVisible = false;
            splitterColumn.Width = new GridLength(0);
            rightPanelColumn.MinWidth = 0;
            rightPanelColumn.Width = new GridLength(0);
            return;
        }

        RightPanel.IsVisible = true;
        RightPanelSplitter.IsVisible = true;
        splitterColumn.Width = new GridLength(5);
        rightPanelColumn.MinWidth = 220;
        if (rightPanelColumn.Width.Value <= 0)
            rightPanelColumn.Width = _expandedRightPanelWidth;
    }

    private double PersistedRightPanelWidth()
    {
        if (!RightPanel.IsVisible) return _expandedRightPanelWidth.Value;
        var currentWidth = WorkspaceGrid.ColumnDefinitions[2].ActualWidth;
        return currentWidth >= 220 ? currentWidth : _expandedRightPanelWidth.Value;
    }

    private void OnObjectsToolClick(object? sender, RoutedEventArgs e) =>
        ToggleViewportPopup(ObjectsToolPopup);

    private void OnSupportsToolClick(object? sender, RoutedEventArgs e) =>
        ToggleViewportPopup(SupportsToolPopup);

    private void OnVisibilityToolClick(object? sender, RoutedEventArgs e) =>
        ToggleViewportPopup(VisibilityToolPopup);

    private void OnRaftsToolClick(object? sender, RoutedEventArgs e) =>
        ToggleViewportPopup(RaftsToolPopup);

    private void OnViewSettingsClick(object? sender, RoutedEventArgs e)
    {
        SyncViewSettingsPopup();
        ToggleViewportPopup(ViewSettingsPopup);
    }

    private void OnViewSettingsCloseClick(object? sender, RoutedEventArgs e) =>
        CloseViewportPopup(ViewSettingsPopup, ViewportPopupCloseTrigger.HeaderButton);

    private static void ToggleViewportPopup(Popup popup) => popup.IsOpen = !popup.IsOpen;

    private void OnViewportPopupOpened(object? sender, EventArgs e)
    {
        var focusTarget = sender switch
        {
            _ when ReferenceEquals(sender, ObjectsToolPopup) => ObjectsPopupContent,
            _ when ReferenceEquals(sender, SupportsToolPopup) => SupportsPopupContent,
            _ when ReferenceEquals(sender, VisibilityToolPopup) => VisibilityPopupContent,
            _ when ReferenceEquals(sender, RaftsToolPopup) => RaftsPopupContent,
            _ when ReferenceEquals(sender, ViewSettingsPopup) => ViewSettingsPopupContent,
            _ => null,
        };
        focusTarget?.Focus();
    }

    private void OnViewportPopupClosed(object? sender, EventArgs e) => Viewport.Focus();

    private void OnViewportPopupKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        var popup = sender switch
        {
            _ when ReferenceEquals(sender, ObjectsPopupContent) => ObjectsToolPopup,
            _ when ReferenceEquals(sender, SupportsPopupContent) => SupportsToolPopup,
            _ when ReferenceEquals(sender, VisibilityPopupContent) => VisibilityToolPopup,
            _ when ReferenceEquals(sender, RaftsPopupContent) => RaftsToolPopup,
            _ when ReferenceEquals(sender, ViewSettingsPopupContent) => ViewSettingsPopup,
            _ => null,
        };
        if (popup is null) return;
        CloseViewportPopup(popup, ViewportPopupCloseTrigger.Escape);
        e.Handled = true;
    }

    private void OnObjectsPopupCloseClick(object? sender, RoutedEventArgs e) =>
        CloseViewportPopup(ObjectsToolPopup, ViewportPopupCloseTrigger.HeaderButton);

    private void OnSupportsPopupCloseClick(object? sender, RoutedEventArgs e) =>
        CloseViewportPopup(SupportsToolPopup, ViewportPopupCloseTrigger.HeaderButton);

    private void OnVisibilityPopupCloseClick(object? sender, RoutedEventArgs e) =>
        CloseViewportPopup(VisibilityToolPopup, ViewportPopupCloseTrigger.HeaderButton);

    private void OnRaftsPopupCloseClick(object? sender, RoutedEventArgs e) =>
        CloseViewportPopup(RaftsToolPopup, ViewportPopupCloseTrigger.HeaderButton);

    private static void CloseViewportPopup(Popup popup, ViewportPopupCloseTrigger trigger)
    {
        if (ViewportToolbarPolicy.ShouldClosePopup(trigger)) popup.IsOpen = false;
    }

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
    private void AddWindowKeyBinding(string actionId, Func<ICommand?> command)
    {
        var binding = new KeyBinding
        {
            Gesture = WindowKeymap.GetGesture(AppConfig.Current, actionId),
            Command = new TextInputGuardCommand(this, command),
        };
        _windowKeyBindings.Add(binding);
        KeyBindings.Add(binding);
    }

    private void RefreshWindowKeymap()
    {
        foreach (var binding in _windowKeyBindings)
            KeyBindings.Remove(binding);
        _windowKeyBindings.Clear();

        AddWindowKeyBinding(WindowKeymap.Undo, () => ViewModel?.UndoCommand);
        AddWindowKeyBinding(WindowKeymap.Redo, () => ViewModel?.RedoCommand);
        AddWindowKeyBinding(WindowKeymap.RedoAlternate, () => ViewModel?.RedoCommand);
        AddWindowKeyBinding(WindowKeymap.Delete, () => ViewModel?.DeleteCommand);
        AddWindowKeyBinding(WindowKeymap.DropToPlate, () => ViewModel?.DropToPlateScopedCommand);
        AddWindowKeyBinding(WindowKeymap.GenerateSupports, () => ViewModel?.GenerateSupportsScopedCommand);
        AddWindowKeyBinding(WindowKeymap.SelectAll, () => ViewModel?.SelectAllCommand);
        AddWindowKeyBinding(WindowKeymap.HideUnselectedSupports,
            () => ViewModel?.HideUnselectedSupportsScopedCommand);
        AddWindowKeyBinding(WindowKeymap.Slice, () => ViewModel?.SliceScopedCommand);
        AddWindowKeyBinding(WindowKeymap.SaveProject, () => SaveProjectCommand);
        AddWindowKeyBinding(WindowKeymap.SaveProjectAs, () => SaveProjectAsCommand);
        AddWindowKeyBinding(WindowKeymap.OpenProject, () => OpenProjectCommand);
        AddWindowKeyBinding(WindowKeymap.ImportMesh, () => ImportCommand);
        AddWindowKeyBinding(WindowKeymap.ExportPrint, () => ExportCommand);
        AddWindowKeyBinding(WindowKeymap.Preferences,
            () => new RelayCommand(() => OnPreferencesClick(this, new RoutedEventArgs())));

        OpenProjectMenuItem.InputGesture = WindowKeymap.GetGesture(AppConfig.Current, WindowKeymap.OpenProject);
        SaveProjectMenuItem.InputGesture = WindowKeymap.GetGesture(AppConfig.Current, WindowKeymap.SaveProject);
        SaveProjectAsMenuItem.InputGesture = WindowKeymap.GetGesture(AppConfig.Current, WindowKeymap.SaveProjectAs);
        ImportMenuItem.InputGesture = WindowKeymap.GetGesture(AppConfig.Current, WindowKeymap.ImportMesh);
        FileExportMenuItem.InputGesture = WindowKeymap.GetGesture(AppConfig.Current, WindowKeymap.ExportPrint);
        UndoMenuItem.InputGesture = WindowKeymap.GetGesture(AppConfig.Current, WindowKeymap.Undo);
        RedoMenuItem.InputGesture = WindowKeymap.GetGesture(AppConfig.Current, WindowKeymap.Redo);
        SelectAllMenuItem.InputGesture = WindowKeymap.GetGesture(AppConfig.Current, WindowKeymap.SelectAll);
        DeleteMenuItem.InputGesture = WindowKeymap.GetGesture(AppConfig.Current, WindowKeymap.Delete);
        PreferencesMenuItem.InputGesture = WindowKeymap.GetGesture(AppConfig.Current, WindowKeymap.Preferences);
        DropToPlateMenuItem.InputGesture = WindowKeymap.GetGesture(AppConfig.Current, WindowKeymap.DropToPlate);
        HideUnselectedMenuItem.InputGesture = WindowKeymap.GetGesture(AppConfig.Current, WindowKeymap.HideUnselectedSupports);
        GenerateSupportsMenuItem.InputGesture = WindowKeymap.GetGesture(AppConfig.Current, WindowKeymap.GenerateSupports);
        SliceMenuItem.InputGesture = WindowKeymap.GetGesture(AppConfig.Current, WindowKeymap.Slice);
        PrintExportMenuItem.InputGesture = WindowKeymap.GetGesture(AppConfig.Current, WindowKeymap.ExportPrint);
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
        config.Saved += OnPreferencesSaved;
        _configWindow.Closed += (_, _) => config.Saved -= OnPreferencesSaved;
        _configWindow.Closed += (_, _) => _configWindow = null;
        _configWindow.Show(this);
    }

    private void OnPreferencesSaved()
    {
        Viewport.RequestRedraw();
        RefreshWindowKeymap();
    }

    // ----- Render path (View menu) -----

    /// <summary>Reflects the persisted render-path settings into the View menu check states.</summary>
    private void SyncRenderPathMenu()
    {
        var viewport = AppConfig.Current.Viewport;
        var deferred = viewport.RenderPath == RenderPathMode.Deferred;
        DeferredRenderingMenuItem.IsChecked = deferred;
        // The shading and effect switches only affect the deferred composite pass.
        ShadingMenuItem.IsEnabled = deferred;
        ShadingStudioMenuItem.IsChecked = viewport.Shading == ViewportShadingMode.Studio;
        ShadingClayMenuItem.IsChecked = viewport.Shading == ViewportShadingMode.MatCapClay;
        ShadingMetalMenuItem.IsChecked = viewport.Shading == ViewportShadingMode.MatCapMetal;
        ShadingPearlMenuItem.IsChecked = viewport.Shading == ViewportShadingMode.MatCapPearl;
        CavityMenuItem.IsChecked = viewport.CavityEnabled;
        OutlinesMenuItem.IsChecked = viewport.OutlinesEnabled;
        FxaaMenuItem.IsChecked = viewport.FxaaEnabled;
        SyncViewSettingsPopup();
    }

    private bool _syncingViewSettings;

    private static readonly ViewportShadingMode[] ShadingOrder =
    [
        ViewportShadingMode.Studio, ViewportShadingMode.MatCapClay,
        ViewportShadingMode.MatCapMetal, ViewportShadingMode.MatCapPearl,
    ];

    /// <summary>Reflects the persisted view settings into the gear pop-out below the view cube.</summary>
    private void SyncViewSettingsPopup()
    {
        var viewport = AppConfig.Current.Viewport;
        var deferred = viewport.RenderPath == RenderPathMode.Deferred;
        // Guarded so pushing state into the ComboBox cannot write back into the config
        // (the preset-combo feedback loop is the cautionary tale).
        _syncingViewSettings = true;
        try
        {
            PopShading.ItemsSource ??= new[] { "Studio", "MatCap Clay", "MatCap Metal", "MatCap Pearl" };
            PopDeferred.IsChecked = deferred;
            PopShading.SelectedIndex = Array.IndexOf(ShadingOrder, viewport.Shading);
            PopShading.IsEnabled = deferred;
            PopCavity.IsChecked = viewport.CavityEnabled;
            PopOutlines.IsChecked = viewport.OutlinesEnabled;
            PopFxaa.IsChecked = viewport.FxaaEnabled;
            PopCavity.IsEnabled = deferred;
            PopOutlines.IsEnabled = deferred;
            PopFxaa.IsEnabled = deferred;
            PopWireframe.IsChecked = viewport.WireframeEnabled;
            PopViewCube.IsChecked = viewport.ViewCubeEnabled;
        }
        finally
        {
            _syncingViewSettings = false;
        }
    }

    private void OnPopShadingChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_syncingViewSettings || PopShading.SelectedIndex < 0) return;
        AppConfig.Current.Viewport.Shading = ShadingOrder[PopShading.SelectedIndex];
        ApplyRenderPathChange();
    }

    private void OnToggleWireframeClick(object? sender, RoutedEventArgs e)
    {
        var viewport = AppConfig.Current.Viewport;
        viewport.WireframeEnabled = !viewport.WireframeEnabled;
        ApplyRenderPathChange();
    }

    private void OnToggleViewCubeClick(object? sender, RoutedEventArgs e)
    {
        var viewport = AppConfig.Current.Viewport;
        viewport.ViewCubeEnabled = !viewport.ViewCubeEnabled;
        ApplyRenderPathChange();
    }

    private void ApplyRenderPathChange()
    {
        AppConfig.Save();
        SyncRenderPathMenu();
        Viewport.RequestRedraw();
    }

    private void OnToggleDeferredRenderingClick(object? sender, RoutedEventArgs e)
    {
        var viewport = AppConfig.Current.Viewport;
        viewport.RenderPath = viewport.RenderPath == RenderPathMode.Deferred
            ? RenderPathMode.Classic
            : RenderPathMode.Deferred;
        ApplyRenderPathChange();
    }

    private void OnShadingClick(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: string tag } &&
            Enum.TryParse<ViewportShadingMode>(tag, out var mode))
            AppConfig.Current.Viewport.Shading = mode;
        ApplyRenderPathChange();
    }

    private void OnToggleCavityClick(object? sender, RoutedEventArgs e)
    {
        AppConfig.Current.Viewport.CavityEnabled = !AppConfig.Current.Viewport.CavityEnabled;
        ApplyRenderPathChange();
    }

    private void OnToggleOutlinesClick(object? sender, RoutedEventArgs e)
    {
        AppConfig.Current.Viewport.OutlinesEnabled = !AppConfig.Current.Viewport.OutlinesEnabled;
        ApplyRenderPathChange();
    }

    private void OnToggleFxaaClick(object? sender, RoutedEventArgs e)
    {
        AppConfig.Current.Viewport.FxaaEnabled = !AppConfig.Current.Viewport.FxaaEnabled;
        ApplyRenderPathChange();
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
