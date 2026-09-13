using System.Windows.Input;
using System.ComponentModel;
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using CommunityToolkit.Mvvm.Input;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Danslicer.App.Controls;
using Danslicer.App.Controls.Refresh;
using Danslicer.App.Configuration;
using Danslicer.App.ViewModels;
using Danslicer.Core;
using Danslicer.Core.Config;
using Danslicer.Core.IO;
using Danslicer.Core.Supports.Generation;

namespace Danslicer.App.Views;

public partial class MainWindow : Window
{
    public ICommand SaveProjectCommand { get; }
    public ICommand SaveProjectAsCommand { get; }
    public ICommand NewProjectCommand { get; }
    public ICommand OpenProjectCommand { get; }
    public ICommand ImportCommand { get; }
    public ModeScopedCommand ExportCommand { get; }

    public MainWindow()
    {
        SaveProjectCommand = new AsyncRelayCommand(SaveProjectAsync);
        SaveProjectAsCommand = new AsyncRelayCommand(SaveProjectAsAsync);
        NewProjectCommand = new AsyncRelayCommand(NewProjectAsync);
        OpenProjectCommand = new AsyncRelayCommand(OpenProjectAsync);
        ImportCommand = new RelayCommand(() => OnImportClick(this, new RoutedEventArgs()));
        ExportCommand = new ModeScopedCommand(
            new RelayCommand(() => OnExportClick(this, new RoutedEventArgs())),
            () => ViewModel?.ViewMode ?? WorkspaceMode.Layout,
            WorkspaceMode.Slicing);
        _viewportPopups = new ViewportPopupGroup(
            _objectsPopupState, _supportsPopupState, _islandDetectionPopupState,
            _visibilityPopupState, _raftsPopupState, _transformPopupState, _guidedPopupState,
            _structurePopupState, _regionPopupState);
        InitializeComponent();
        InitializeWorkspace();
        if (!Environment.GetCommandLineArgs().Contains("--workspace-capture"))
            Configuration.WindowStatePersistence.Track(this, "main");
        ConfigureWorkspaceCapture();
        DataContextChanged += OnMainDataContextChanged;
        AttachPanelLayoutViewModel();
        RefreshWindowKeymap();
        SyncRenderPathMenu();
        InitializeNumericPreviews();
        Viewport.PropertyChanged += (_, e) =>
        {
            if (e.Property == ViewportControl.StatusTextProperty && DataContext is MainViewModel vm)
                vm.ViewportStatus = Viewport.StatusText;
        };
        Viewport.ToggleViewRequested += () => ViewModel?.ToggleViewCommand.Execute(null);
        Viewport.RegionFacePicked += (obj, triangle, erase) =>
            ViewModel?.PaintRegionFromFace(obj, triangle, erase);
        Viewport.RegionFaceHovered += (obj, triangle) => ViewModel?.HoverRegionFace(obj, triangle);
        Viewport.RegionStrokeStarted += (obj, erase) => ViewModel?.BeginStroke(obj, erase);
        Viewport.RegionStrokeDab += (point, triangle, radius) =>
            ViewModel?.BrushStroke(point, triangle, radius);
        Viewport.RegionStrokeEnded += () => ViewModel?.EndStroke();
        LayerView.ToggleViewRequested += () => ViewModel?.ToggleViewCommand.Execute(null);
        LayerView.LayerStepRequested += delta => ViewModel?.StepLayer(delta);
        _uvtoolsAvailabilityTimer.Tick += (_, _) => ViewModel?.RefreshUvtoolsAvailability();
        Opened += (_, _) =>
        {
            Viewport.FrameAll();
            ViewModel?.RefreshUvtoolsAvailability();
            _uvtoolsAvailabilityTimer.Start();
            if (ViewModel is { } vm)
                vm.SupportSettings.EditSupportPresetRequested += OpenSupportPresetEditor;
        };
        Closed += (_, _) =>
        {
            _uvtoolsAvailabilityTimer.Stop();
            if (ViewModel is { } vm)
                vm.SupportSettings.EditSupportPresetRequested -= OpenSupportPresetEditor;
            _presetEditorWindow?.Close();
        };
    }

    private MainViewModel? ViewModel => DataContext as MainViewModel;
    private PrinterPresetEditorWindow? _printerEditorWindow;
    private ConfigWindow? _configWindow;
    private SupportPresetEditorWindow? _presetEditorWindow;
    private readonly List<KeyBinding> _windowKeyBindings = [];
    private MainViewModel? _panelLayoutViewModel;
    private readonly ViewportPopupState _objectsPopupState = new(ViewportTool.Objects);
    private readonly ViewportPopupState _supportsPopupState = new(ViewportTool.Supports);
    private readonly ViewportPopupState _islandDetectionPopupState = new(ViewportTool.IslandDetection);
    private readonly ViewportPopupState _visibilityPopupState = new(ViewportTool.Visibility);
    private readonly ViewportPopupState _raftsPopupState = new(ViewportTool.Rafts);
    private readonly ViewportPopupState _transformPopupState = new(ViewportTool.Transform);
    private readonly ViewportPopupState _guidedPopupState = new(ViewportTool.Guided);
    private readonly ViewportPopupState _structurePopupState = new(ViewportTool.Structure);
    private readonly ViewportPopupState _regionPopupState = new(ViewportTool.Region);
    // Declared after the states it groups: field initializers run in declaration order.
    private readonly ViewportPopupGroup _viewportPopups;
    private readonly DispatcherTimer _uvtoolsAvailabilityTimer = new()
        { Interval = TimeSpan.FromSeconds(1) };

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
        ApplyViewportPopupMode();
    }

    private void OnPanelLayoutPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.ViewMode) or nameof(MainViewModel.SelectedObject))
        {
            ScrubField.CancelActive();
            IsolationSlider.CancelDrag();
            SlicePreviewSlider.CancelDrag();
        }
        if (e.PropertyName == nameof(MainViewModel.Title))
        {
            RememberProject();
            ProjectDropdown.Content = $"{(ViewModel?.ProjectPath is { } path ? System.IO.Path.GetFileNameWithoutExtension(path) : "Project")} ▾";
        }
        if (e.PropertyName != nameof(MainViewModel.ViewMode)) return;
        ApplyRightPanelMode();
        ApplyViewportPopupMode();
    }

    private void ApplyRightPanelMode() => ApplyWorkspaceMode();

    private void OnObjectsToolClick(object? sender, RoutedEventArgs e) =>
        ToggleViewportPopup(_objectsPopupState);

    private void OnSupportsToolClick(object? sender, RoutedEventArgs e) =>
        ToggleViewportPopup(_supportsPopupState);

    private void OnIslandDetectionToolClick(object? sender, RoutedEventArgs e)
    {
        ToggleViewportPopup(_islandDetectionPopupState);
    }

    private void OnVisibilityToolClick(object? sender, RoutedEventArgs e) =>
        ToggleViewportPopup(_visibilityPopupState);

    private void OnRaftsToolClick(object? sender, RoutedEventArgs e) =>
        ToggleViewportPopup(_raftsPopupState);

    private void OnParentSupportsClick(object? sender, RoutedEventArgs e) => Viewport.ParentSupports();
    private void OnBraceSupportsClick(object? sender, RoutedEventArgs e) => Viewport.BraceSupports();
    private void OnUnbraceSupportsClick(object? sender, RoutedEventArgs e) => Viewport.UnbraceSupports();
    private void OnSelectBracesClick(object? sender, RoutedEventArgs e) => Viewport.SelectBraces();
    private void OnEditSupportClick(object? sender, RoutedEventArgs e) => Viewport.ToggleSupportEdit();
    private void OnPlaceSupportsClick(object? sender, RoutedEventArgs e) => Viewport.StartGuidedTool(ViewportControl.GuidedTool.Place);

    private void OnTransformToolClick(object? sender, RoutedEventArgs e) =>
        ToggleViewportPopup(_transformPopupState);

    private void OnGuidedToolClick(object? sender, RoutedEventArgs e) =>
        ToggleViewportPopup(_guidedPopupState);

    private void OnStructureToolClick(object? sender, RoutedEventArgs e) =>
        ToggleViewportPopup(_structurePopupState);

    private void OnRegionToolClick(object? sender, RoutedEventArgs e) =>
        ToggleViewportPopup(_regionPopupState);

    /// <summary>Generate Supports straight from the toolbar; no pop-out, the settings have their own.</summary>
    private void OnGenerateToolClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel?.GenerateSupportsScopedCommand.CanExecute(null) == true)
            ViewModel.GenerateSupportsScopedCommand.Execute(null);
    }

    /// <summary>
    /// A guided-tool button (user rule 2026-09-08: every key has a button). The button's Tag
    /// names the tool; the pop-out stays open so the next tool is one click away, and the
    /// viewport takes focus so the gesture's clicks and keys land there.
    /// </summary>
    private void OnGuidedToolButtonClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string name } ||
            !Enum.TryParse<ViewportControl.GuidedTool>(name, out var tool)) return;
        Viewport.StartGuidedTool(tool);
    }

    private void OnViewSettingsClick(object? sender, RoutedEventArgs e)
    {
        SyncViewSettingsPopup();
        // View settings is the one pop-out with no mode-dependent state of its own, so it joins
        // the mutual exclusion here rather than through the group.
        var opening = !ViewSettingsPopup.IsOpen;
        if (opening) { CloseViewportToolPopups(); CloseExtraPopouts(); }
        ViewSettingsPopup.IsOpen = opening;
    }

    private void OnViewSettingsCloseClick(object? sender, RoutedEventArgs e) =>
        CloseViewportPopup(ViewSettingsPopup, ViewportPopupCloseTrigger.HeaderButton);

    /// <summary>
    /// Opens one pop-out and closes every other, including View settings: only one panel ever
    /// floats over the viewport. Clicking the open one's own icon closes it, as before.
    /// </summary>
    private void ToggleViewportPopup(ViewportPopupState state)
    {
        CloseExtraPopouts();
        _viewportPopups.Toggle(state);
        ViewSettingsPopup.IsOpen = false;
        ApplyViewportPopupMode();
    }

    private void CloseViewportToolPopups()
    {
        _viewportPopups.CloseAll();
        ApplyViewportPopupMode();
    }

    private void ApplyViewportPopupMode()
    {
        ApplyViewportPopupState(_objectsPopupState, ObjectsToolPopup);
        ApplyViewportPopupState(_supportsPopupState, SupportsToolPopup);
        ApplyViewportPopupState(_islandDetectionPopupState, IslandDetectionToolPopup);
        ApplyViewportPopupState(_visibilityPopupState, VisibilityToolPopup);
        ApplyViewportPopupState(_raftsPopupState, RaftsToolPopup);
        ApplyViewportPopupState(_transformPopupState, TransformToolPopup);
        ApplyViewportPopupState(_guidedPopupState, GuidedToolPopup);
        ApplyViewportPopupState(_structurePopupState, StructureToolPopup);
        ApplyViewportPopupState(_regionPopupState, RegionToolPopup);
    }

    private void ApplyViewportPopupState(ViewportPopupState state, WorkspacePopout popup) =>
        popup.IsOpen = state.IsVisible(ViewModel?.ViewMode ?? WorkspaceMode.Layout);

    private void OnViewportPopupOpened(object? sender, EventArgs e)
    {
        var focusTarget = sender switch
        {
            _ when ReferenceEquals(sender, ObjectsToolPopup) => ObjectsPopupContent,
            _ when ReferenceEquals(sender, SupportsToolPopup) => SupportsPopupContent,
            _ when ReferenceEquals(sender, IslandDetectionToolPopup) => IslandDetectionPopupContent,
            _ when ReferenceEquals(sender, VisibilityToolPopup) => VisibilityPopupContent,
            _ when ReferenceEquals(sender, RaftsToolPopup) => RaftsPopupContent,
            _ when ReferenceEquals(sender, TransformToolPopup) => TransformPopupContent,
            _ when ReferenceEquals(sender, GuidedToolPopup) => GuidedPopupContent,
            _ when ReferenceEquals(sender, ViewSettingsPopup) => ViewSettingsPopupContent,
            _ => null,
        };
        focusTarget?.Focus();
    }

    private void OnViewportPopupClosed(object? sender, EventArgs e)
    {
        if (sender is WorkspacePopout popup) popup.PlacementTarget?.Focus();
    }

    private void OnViewportPopupKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        var popup = sender switch
        {
            _ when ReferenceEquals(sender, ObjectsPopupContent) => ObjectsToolPopup,
            _ when ReferenceEquals(sender, SupportsPopupContent) => SupportsToolPopup,
            _ when ReferenceEquals(sender, IslandDetectionPopupContent) => IslandDetectionToolPopup,
            _ when ReferenceEquals(sender, VisibilityPopupContent) => VisibilityToolPopup,
            _ when ReferenceEquals(sender, RaftsPopupContent) => RaftsToolPopup,
            _ when ReferenceEquals(sender, TransformPopupContent) => TransformToolPopup,
            _ when ReferenceEquals(sender, GuidedPopupContent) => GuidedToolPopup,
            _ when ReferenceEquals(sender, ViewSettingsPopupContent) => ViewSettingsPopup,
            _ => null,
        };
        if (popup is null) return;
        CloseViewportPopup(popup, ViewportPopupCloseTrigger.Escape);
        e.Handled = true;
    }

    private void OnObjectsPopupCloseClick(object? sender, RoutedEventArgs e) =>
        CloseViewportPopup(_objectsPopupState, ObjectsToolPopup, ViewportPopupCloseTrigger.HeaderButton);

    private void OnSupportsPopupCloseClick(object? sender, RoutedEventArgs e) =>
        CloseViewportPopup(_supportsPopupState, SupportsToolPopup, ViewportPopupCloseTrigger.HeaderButton);

    private void OnIslandDetectionPopupCloseClick(object? sender, RoutedEventArgs e) =>
        CloseViewportPopup(_islandDetectionPopupState, IslandDetectionToolPopup,
            ViewportPopupCloseTrigger.HeaderButton);

    private void OnIslandFindingClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: DetectedIsland island })
            Viewport.FocusPoint(island.Position);
    }

    private void OnVisibilityPopupCloseClick(object? sender, RoutedEventArgs e) =>
        CloseViewportPopup(_visibilityPopupState, VisibilityToolPopup, ViewportPopupCloseTrigger.HeaderButton);

    private void OnRaftsPopupCloseClick(object? sender, RoutedEventArgs e) =>
        CloseViewportPopup(_raftsPopupState, RaftsToolPopup, ViewportPopupCloseTrigger.HeaderButton);

    private void OnTransformPopupCloseClick(object? sender, RoutedEventArgs e) =>
        CloseViewportPopup(_transformPopupState, TransformToolPopup, ViewportPopupCloseTrigger.HeaderButton);

    private void OnGuidedPopupCloseClick(object? sender, RoutedEventArgs e) =>
        CloseViewportPopup(_guidedPopupState, GuidedToolPopup, ViewportPopupCloseTrigger.HeaderButton);

    private void OnStructurePopupCloseClick(object? sender, RoutedEventArgs e) =>
        CloseViewportPopup(_structurePopupState, StructureToolPopup, ViewportPopupCloseTrigger.HeaderButton);

    private void OnRegionPopupCloseClick(object? sender, RoutedEventArgs e) =>
        CloseViewportPopup(_regionPopupState, RegionToolPopup, ViewportPopupCloseTrigger.HeaderButton);

    private void CloseViewportPopup(
        ViewportPopupState state, WorkspacePopout popup, ViewportPopupCloseTrigger trigger)
    {
        state.Close(trigger);
        ApplyViewportPopupState(state, popup);
    }

    private void CloseViewportPopup(WorkspacePopout popup, ViewportPopupCloseTrigger trigger)
    {
        var state = popup switch
        {
            _ when ReferenceEquals(popup, ObjectsToolPopup) => _objectsPopupState,
            _ when ReferenceEquals(popup, SupportsToolPopup) => _supportsPopupState,
            _ when ReferenceEquals(popup, IslandDetectionToolPopup) => _islandDetectionPopupState,
            _ when ReferenceEquals(popup, VisibilityToolPopup) => _visibilityPopupState,
            _ when ReferenceEquals(popup, RaftsToolPopup) => _raftsPopupState,
            _ when ReferenceEquals(popup, TransformToolPopup) => _transformPopupState,
            _ when ReferenceEquals(popup, GuidedToolPopup) => _guidedPopupState,
            _ when ReferenceEquals(popup, StructureToolPopup) => _structurePopupState,
            _ when ReferenceEquals(popup, RegionToolPopup) => _regionPopupState,
            _ => null,
        };
        if (state is not null)
        {
            CloseViewportPopup(state, popup, trigger);
            return;
        }
        if (ViewportToolbarPolicy.ShouldClosePopup(trigger)) popup.IsOpen = false;
    }

    private void OnPrinterPresetsClick(object? sender, RoutedEventArgs e)
    {
        if (_printerEditorWindow is { } open) { open.Activate(); return; }
        if (ViewModel is not { } main) return;
        var editor = main.SupportSettings.Printers;
        editor.SelectedIndex = editor.Items.ToList().FindIndex(p => p.Id == main.Document.Printer.Id);
        _printerEditorWindow = new PrinterPresetEditorWindow(editor, printer =>
        {
            if (main.Document.Printer == printer) return;
            main.Document.Printer = printer;
            main.Document.NotifyTransientChange();
            main.RefreshPrinterOptions();
        });
        _printerEditorWindow.Closed += (_, _) =>
        {
            editor.NameDraft = editor.SelectedPrinter?.Name ?? "";
            _printerEditorWindow = null;
        };
        _printerEditorWindow.Show(this);
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

    /// <summary>
    /// Empties the scene for a new project, asking first when there is something to lose. The
    /// question is asked here, once, so every future entry point to "new" gets the same guard.
    /// </summary>
    private async Task NewProjectAsync()
    {
        if (ViewModel is not { } vm) return;
        if (vm.IsGeneratingSupports || vm.IsSlicing)
        {
            vm.ViewportStatus = "Wait for the current operation before starting a new project.";
            return;
        }
        if (vm.Document.HasContent && !await ConfirmDialog.AskAsync(this, "New project",
                "The current scene has models in it. Starting a new project discards them, " +
                "along with their supports and the undo history.",
                confirmText: "Discard"))
            return;
        vm.NewProject();
        Viewport.FrameAll();
        Viewport.Focus();
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
            RememberProject();
            ApplyProjectViewState(vm.OpenProject(path));
            RememberProject();
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
        AddWindowKeyBinding(WindowKeymap.DuplicateObjects, () => ViewModel?.DuplicateScopedCommand);
        AddWindowKeyBinding(WindowKeymap.MirrorX, () => ViewModel?.MirrorXScopedCommand);
        AddWindowKeyBinding(WindowKeymap.MirrorY, () => ViewModel?.MirrorYScopedCommand);
        AddWindowKeyBinding(WindowKeymap.MirrorZ, () => ViewModel?.MirrorZScopedCommand);
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
        AddWindowKeyBinding(WindowKeymap.RegionFacingDown, () => ViewModel?.SelectFacingDownRegionCommand);
        AddWindowKeyBinding(WindowKeymap.RegionInvert, () => ViewModel?.InvertRegionCommand);
        AddWindowKeyBinding(WindowKeymap.RegionGrow, () => ViewModel?.GrowRegionCommand);
        AddWindowKeyBinding(WindowKeymap.RegionShrink, () => ViewModel?.ShrinkRegionCommand);
        AddWindowKeyBinding(WindowKeymap.RegionConnected, () => ViewModel?.ConnectedRegionCommand);

        OpenProjectMenuItem.InputGesture = WindowKeymap.GetGesture(AppConfig.Current, WindowKeymap.OpenProject);
        SaveProjectMenuItem.InputGesture = WindowKeymap.GetGesture(AppConfig.Current, WindowKeymap.SaveProject);
        SaveProjectAsMenuItem.InputGesture = WindowKeymap.GetGesture(AppConfig.Current, WindowKeymap.SaveProjectAs);
        ImportMenuItem.InputGesture = WindowKeymap.GetGesture(AppConfig.Current, WindowKeymap.ImportMesh);
        FileExportMenuItem.InputGesture = WindowKeymap.GetGesture(AppConfig.Current, WindowKeymap.ExportPrint);
        UndoMenuItem.InputGesture = WindowKeymap.GetGesture(AppConfig.Current, WindowKeymap.Undo);
        RedoMenuItem.InputGesture = WindowKeymap.GetGesture(AppConfig.Current, WindowKeymap.Redo);
        SelectAllMenuItem.InputGesture = WindowKeymap.GetGesture(AppConfig.Current, WindowKeymap.SelectAll);
        DeleteMenuItem.InputGesture = WindowKeymap.GetGesture(AppConfig.Current, WindowKeymap.Delete);
        DuplicateObjectsMenuItem.InputGesture = WindowKeymap.GetGesture(AppConfig.Current, WindowKeymap.DuplicateObjects);
        MirrorXMenuItem.InputGesture = WindowKeymap.GetGesture(AppConfig.Current, WindowKeymap.MirrorX);
        MirrorYMenuItem.InputGesture = WindowKeymap.GetGesture(AppConfig.Current, WindowKeymap.MirrorY);
        MirrorZMenuItem.InputGesture = WindowKeymap.GetGesture(AppConfig.Current, WindowKeymap.MirrorZ);
        PreferencesMenuItem.InputGesture = WindowKeymap.GetGesture(AppConfig.Current, WindowKeymap.Preferences);
        DropToPlateMenuItem.InputGesture = WindowKeymap.GetGesture(AppConfig.Current, WindowKeymap.DropToPlate);
        HideUnselectedMenuItem.InputGesture = WindowKeymap.GetGesture(AppConfig.Current, WindowKeymap.HideUnselectedSupports);
        GenerateSupportsMenuItem.InputGesture = WindowKeymap.GetGesture(AppConfig.Current, WindowKeymap.GenerateSupports);
        SliceMenuItem.InputGesture = WindowKeymap.GetGesture(AppConfig.Current, WindowKeymap.Slice);
        PrintExportMenuItem.InputGesture = WindowKeymap.GetGesture(AppConfig.Current, WindowKeymap.ExportPrint);
        ApplyKeymapTooltips();
    }

    /// <summary>
    /// Toolbar buttons whose function has a keymap binding show it in the tooltip (user,
    /// 2026-09-09); the keys are the user's, so the text is built here rather than in XAML.
    /// </summary>
    private void ApplyKeymapTooltips()
    {
        static string Key(string actionId) => WindowKeymap.GetGestureText(AppConfig.Current, actionId);
        ToolTip.SetTip(AddObjectToolButton, $"Add object: import an STL or OBJ mesh into the layout ({Key(WindowKeymap.ImportMesh)})");
        ToolTip.SetTip(GenerateToolButton, $"Generate supports for the support target, with the recipe under Support settings ({Key(WindowKeymap.GenerateSupports)})");
        ToolTip.SetTip(RegionFacingDownButton, $"Add every face that points down past the Down angle to the region ({Key(WindowKeymap.RegionFacingDown)})");
        ToolTip.SetTip(RegionInvertButton, $"Swap painted and unpainted faces ({Key(WindowKeymap.RegionInvert)})");
        ToolTip.SetTip(RegionGrowButton, $"Add the faces next to the region ({Key(WindowKeymap.RegionGrow)})");
        ToolTip.SetTip(RegionShrinkButton, $"Remove the region's outermost ring of faces ({Key(WindowKeymap.RegionShrink)})");
        ToolTip.SetTip(RegionConnectedButton, $"Extend the region to every face connected to it ({Key(WindowKeymap.RegionConnected)})");
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
        config.ViewportSaved += OnPreferencesSaved;
        config.ViewportPreviewed += OnPreferencesSaved;
        _configWindow.Closed += (_, _) => { config.Saved -= OnPreferencesSaved; config.ViewportSaved -= OnPreferencesSaved; config.ViewportPreviewed -= OnPreferencesSaved; };
        _configWindow.Closed += (_, _) => _configWindow = null;
        _configWindow.Show(this);
    }

    private void OnPreferencesSaved()
    {
        SyncRenderPathMenu();
        UpdateIsolationPlacement();
        Viewport.RequestRedraw();
        RefreshWindowKeymap();
    }

    // ----- Render path (View menu) -----

    /// <summary>Reflects the persisted render-path settings into the View menu check states.</summary>
    private void SyncRenderPathMenu()
    {
        var viewport = AppConfig.Current.Viewport;
        // Classic is only ever reached by the automatic fallback after a GL failure, so the
        // shading switches follow it rather than a user choice.
        var deferred = viewport.RenderPath == RenderPathMode.Deferred;
        ShadingMenuItem.IsEnabled = deferred;
        ShadingStudioMenuItem.IsChecked = viewport.Shading == ViewportShadingMode.Studio;
        ShadingClayMenuItem.IsChecked = viewport.Shading == ViewportShadingMode.MatCapClay;
        ShadingMetalMenuItem.IsChecked = viewport.Shading == ViewportShadingMode.MatCapMetal;
        ShadingPearlMenuItem.IsChecked = viewport.Shading == ViewportShadingMode.MatCapPearl;
        AoMenuItem.IsChecked = viewport.AmbientOcclusionEnabled;
        AoMenuItem.IsEnabled = deferred;
        ReflectionsMenuItem.IsChecked = viewport.PlateReflectionsEnabled;
        CavityMenuItem.IsChecked = viewport.CavityEnabled;
        OutlinesMenuItem.IsChecked = viewport.OutlinesEnabled;
        FxaaMenuItem.IsChecked = viewport.FxaaEnabled;
        PlateShadowsMenuItem.IsChecked = viewport.PlateShadowsEnabled;
        UpdateIsolationPlacement();
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
            PopShading.SelectedIndex = Array.IndexOf(ShadingOrder, viewport.Shading);
            PopShading.IsEnabled = deferred;
            PopShadowMode.ItemsSource ??= new[] { "Off", "Working", "Presentation" };
            PopShadowMode.SelectedIndex = (int)viewport.ModelShadows;
            var shadows = Danslicer.Render.ShadowEffects.FromConfig(viewport);
            PopShadowStrength.Value = shadows.Strength;
            PopShadowSoftness.Value = shadows.SoftnessMm;
            PopShadowStrength.IsEnabled = PopShadowSoftness.IsEnabled = shadows.Mode != ModelShadowMode.Off;
            PopAo.IsChecked = viewport.AmbientOcclusionEnabled;
            PopAo.IsEnabled = deferred;
            PopAoStrength.Value = viewport.AmbientOcclusionStrength;
            PopAoRadius.Value = viewport.AmbientOcclusionRadiusMm;
            PopAoStrength.IsEnabled = PopAoRadius.IsEnabled = deferred;
            PopReflections.IsChecked = viewport.PlateReflectionsEnabled;
            PopCavity.IsChecked = viewport.CavityEnabled;
            PopOutlines.IsChecked = viewport.OutlinesEnabled;
            PopOutlineWidth.Value = viewport.OutlineWidthPixels;
            PopOutlineWidth.IsEnabled = deferred;
            PopFxaa.IsChecked = viewport.FxaaEnabled;
            PopCavity.IsEnabled = deferred;
            PopOutlines.IsEnabled = deferred;
            PopFxaa.IsEnabled = deferred;
            PopWireframe.IsChecked = viewport.WireframeEnabled;
            PopViewCube.IsChecked = viewport.ViewCubeEnabled;
            PopPlateShadows.IsChecked = viewport.PlateShadowsEnabled;
        }
        finally
        {
            _syncingViewSettings = false;
        }
    }

    private void OnPopShadowModeChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_syncingViewSettings || PopShadowMode.SelectedIndex < 0) return;
        var requestedMode = (ModelShadowMode)PopShadowMode.SelectedIndex;
        ScrubField.CancelActive();
        AppConfig.Current.Viewport.ModelShadows = requestedMode;
        ApplyRenderPathChange();
    }

    private void OnShadowStrengthCommitted(object? sender, Danslicer.App.Controls.Refresh.NumericCommittedEventArgs e)
    {
        if (sender is ScrubField { CommittedPreview: true }) return;
        if (_syncingViewSettings) return;
        var viewport = AppConfig.Current.Viewport;
        if (viewport.ModelShadows == ModelShadowMode.Presentation) viewport.PresentationShadowStrength = (float)e.NewValue;
        else if (viewport.ModelShadows == ModelShadowMode.Working) viewport.WorkingShadowStrength = (float)e.NewValue;
        ApplyRenderPathChange();
    }

    private void OnShadowSoftnessCommitted(object? sender, Danslicer.App.Controls.Refresh.NumericCommittedEventArgs e)
    {
        if (sender is ScrubField { CommittedPreview: true }) return;
        if (_syncingViewSettings) return;
        var viewport = AppConfig.Current.Viewport;
        if (viewport.ModelShadows == ModelShadowMode.Presentation) viewport.PresentationShadowSoftnessMm = (float)e.NewValue;
        else if (viewport.ModelShadows == ModelShadowMode.Working) viewport.WorkingShadowSoftnessMm = (float)e.NewValue;
        ApplyRenderPathChange();
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

    private void OnTogglePlateShadowsClick(object? sender, RoutedEventArgs e)
    {
        var viewport = AppConfig.Current.Viewport;
        viewport.PlateShadowsEnabled = !viewport.PlateShadowsEnabled;
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

    private void OnShadingClick(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: string tag } &&
            Enum.TryParse<ViewportShadingMode>(tag, out var mode))
            AppConfig.Current.Viewport.Shading = mode;
        ApplyRenderPathChange();
    }

    private void OnAoStrengthCommitted(object? sender, Danslicer.App.Controls.Refresh.NumericCommittedEventArgs e)
    {
        if (sender is ScrubField { CommittedPreview: true }) return;
        AppConfig.Current.Viewport.AmbientOcclusionStrength = (float)e.NewValue;
        ApplyRenderPathChange();
    }

    private void OnAoRadiusCommitted(object? sender, Danslicer.App.Controls.Refresh.NumericCommittedEventArgs e)
    {
        if (sender is ScrubField { CommittedPreview: true }) return;
        AppConfig.Current.Viewport.AmbientOcclusionRadiusMm = (float)e.NewValue;
        ApplyRenderPathChange();
    }

    private void OnToggleAoClick(object? sender, RoutedEventArgs e)
    {
        AppConfig.Current.Viewport.AmbientOcclusionEnabled = !AppConfig.Current.Viewport.AmbientOcclusionEnabled;
        ApplyRenderPathChange();
    }

    private void OnToggleReflectionsClick(object? sender, RoutedEventArgs e)
    {
        AppConfig.Current.Viewport.PlateReflectionsEnabled = !AppConfig.Current.Viewport.PlateReflectionsEnabled;
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

    private void OnNewProjectClick(object? sender, RoutedEventArgs e) => NewProjectCommand.Execute(null);
    private void OnOpenProjectClick(object? sender, RoutedEventArgs e) => OpenProjectCommand.Execute(null);
    private void OnSaveProjectClick(object? sender, RoutedEventArgs e) => SaveProjectCommand.Execute(null);
    private void OnSaveProjectAsClick(object? sender, RoutedEventArgs e) => SaveProjectAsCommand.Execute(null);

    /// <summary>Save-as from the crash dialog: the same picker the menu item opens.</summary>
    public void RequestSaveProjectAs() => SaveProjectAsCommand.Execute(null);

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

    private void OnUvtoolsCheckClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel) return;
        viewModel.RefreshUvtoolsAvailability();
        if (!viewModel.CanCheckWithUvtools)
        {
            viewModel.ViewportStatus = viewModel.UvtoolsCheckTooltip;
            return;
        }

        var executablePath = AppConfig.Current.UvtoolsExecutablePath.Trim();
        if (executablePath.Length == 0)
        {
            viewModel.ViewportStatus =
                "Set the UVtools executable path in Preferences before running the check.";
            return;
        }
        if (!File.Exists(executablePath))
        {
            viewModel.ViewportStatus =
                "The configured UVtools executable was not found. Update it in Preferences.";
            return;
        }

        try
        {
            var process = Process.Start(UvtoolsLauncher.CreateStartInfo(
                executablePath, viewModel.LastExportPath!));
            viewModel.ViewportStatus = process is null
                ? "UVtools could not be started. Check its path in Preferences."
                : $"Opened {Path.GetFileName(viewModel.LastExportPath)} in UVtools.";
        }
        catch (Exception ex)
        {
            viewModel.ViewportStatus = $"UVtools failed to start: {ex.Message}";
        }
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
