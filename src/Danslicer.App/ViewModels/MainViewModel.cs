using System.Collections.ObjectModel;
using System.Numerics;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Danslicer.App.Configuration;
using Danslicer.Core;
using Danslicer.Core.Config;
using Danslicer.Core.IO;
using Danslicer.Core.Printers;
using Danslicer.Core.Scene;
using Danslicer.Core.Slicing;
using Danslicer.Core.Supports;
using Danslicer.Core.Supports.Generation;
using Danslicer.Core.Utilities;

namespace Danslicer.App.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private bool _syncingSelection;
    private bool _loadingPlacement;
    private CancellationTokenSource? _sliceCancellation;
    private CancellationTokenSource? _generationCancellation;
    private bool _applyingGenerationBatch;
    private bool _changingViewMode;

    public Document Document { get; } = new();

    public ObservableCollection<SceneObject> Objects { get; } = new();

    public PrintSettingsViewModel PrintSettings { get; }

    private IReadOnlyList<PrinterDefinition> _printerOptions = [];
    private IReadOnlyList<string> _printerDisplayNames = [];
    private int _selectedPrinterIndex = -1;

    public IReadOnlyList<string> PrinterDisplayNames => _printerDisplayNames;

    public int SelectedPrinterIndex
    {
        get => _selectedPrinterIndex;
        set
        {
            if (value == _selectedPrinterIndex || value < 0 || value >= _printerOptions.Count) return;
            _selectedPrinterIndex = value;
            Document.Printer = _printerOptions[value];
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedPrinterName));
            Document.NotifyTransientChange();
            ViewportStatus = $"Printer: {Document.Printer.Name}.";
        }
    }

    public string SelectedPrinterName => Document.Printer.Name;

    /// <summary>The one live support-settings model shared by Preferences and the Support panel.</summary>
    public ConfigViewModel SupportSettings { get; }
    public LayerRangeClipViewModel SupportClip { get; } = new();
    public HoverWaterlineViewModel SupportWaterline { get; } = new();
    public ViewportClipRange ViewportClipRange => SupportClip.Range;

    public ModeScopedCommand DropToPlateScopedCommand { get; }
    public ModeScopedCommand HideScopedCommand { get; }
    public ModeScopedCommand UnhideAllScopedCommand { get; }
    public ModeScopedCommand HideUnselectedSupportsScopedCommand { get; }
    public ModeScopedCommand GenerateSupportsScopedCommand { get; }
    public ModeScopedCommand SliceScopedCommand { get; }

    private readonly List<ModeScopedCommand> _modeScopedCommands;

    [ObservableProperty]
    public partial SceneObject? SelectedObject { get; set; }

    [ObservableProperty]
    public partial string Title { get; set; } = "Danslicer";

    public string? ProjectPath { get; private set; }

    [ObservableProperty]
    public partial string ViewportStatus { get; set; } = "";

    [ObservableProperty]
    public partial string HistoryStatus { get; set; } = "";

    [ObservableProperty]
    public partial string DimensionsText { get; set; } = "";

    [ObservableProperty]
    public partial string TriangleText { get; set; } = "";

    // ----- Viewport tools -----

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsModelView), nameof(IsLayoutView), nameof(IsSupportView), nameof(IsLayersView),
        nameof(ViewportTools), nameof(IsObjectListSelectionEnabled), nameof(IsObjectsToolVisible),
        nameof(IsSupportsToolVisible), nameof(IsVisibilityToolVisible), nameof(IsRaftsToolVisible))]
    public partial WorkspaceMode ViewMode { get; set; } = WorkspaceMode.Layout;

    public IReadOnlyList<ViewportTool> ViewportTools => ViewportToolbarPolicy.ToolsFor(ViewMode);

    public bool IsObjectsToolVisible => ViewportToolbarPolicy.IsAvailable(ViewportTool.Objects, ViewMode);
    public bool IsSupportsToolVisible => ViewportToolbarPolicy.IsAvailable(ViewportTool.Supports, ViewMode);
    public bool IsVisibilityToolVisible => ViewportToolbarPolicy.IsAvailable(ViewportTool.Visibility, ViewMode);
    public bool IsRaftsToolVisible => ViewportToolbarPolicy.IsAvailable(ViewportTool.Rafts, ViewMode);

    /// <summary>
    /// Object rows remain useful context in Support and Slicing, but only Layout owns object
    /// selection. Disabling the list prevents it from fighting those modes' selection models.
    /// </summary>
    public bool IsObjectListSelectionEnabled => ViewportToolbarPolicy.CanSelectObjects(ViewMode);

    public bool IsModelView
    {
        get => ViewMode != WorkspaceMode.Slicing;
    }

    public bool IsLayoutView
    {
        get => ViewMode == WorkspaceMode.Layout;
        set { if (value) ViewMode = WorkspaceMode.Layout; }
    }

    public bool IsSupportView
    {
        get => ViewMode == WorkspaceMode.Support;
        set { if (value) ViewMode = WorkspaceMode.Support; }
    }

    public bool IsLayersView
    {
        get => ViewMode == WorkspaceMode.Slicing;
        set { if (value) ViewMode = WorkspaceMode.Slicing; }
    }

    partial void OnViewModeChanged(WorkspaceMode value)
    {
        SupportClip.Active = value == WorkspaceMode.Support;
        SupportWaterline.SupportModeActive = value == WorkspaceMode.Support;
        _changingViewMode = true;
        try
        {
            if (value == WorkspaceMode.Layout)
            {
                Document.ClearSupportSelection();
                if (SelectedObject is { } target) Document.Select(target);
            }
            else
            {
                Document.ClearSelection();
                if (value == WorkspaceMode.Slicing) Document.ClearSupportSelection();
            }
        }
        finally
        {
            _changingViewMode = false;
        }
        DeleteCommand.NotifyCanExecuteChanged();
        DropToPlateCommand.NotifyCanExecuteChanged();
        HideCommand.NotifyCanExecuteChanged();
        foreach (var command in _modeScopedCommands) command.NotifyModeChanged();
    }

    [ObservableProperty]
    public partial bool ShowMoveGizmo { get; set; } = true;

    [ObservableProperty]
    public partial bool ShowRotateGizmo { get; set; }

    [ObservableProperty]
    public partial bool ShowScaleGizmo { get; set; }

    [ObservableProperty]
    public partial bool SnapEnabled { get; set; }

    [ObservableProperty]
    public partial bool ShowOverhangs { get; set; }

    [ObservableProperty]
    public partial bool SelectThroughSupports { get; set; }

    [ObservableProperty]
    public partial bool AutoDropEnabled { get; set; } = true;

    // ----- Slicing -----

    [ObservableProperty]
    public partial bool IsSlicing { get; set; }

    [ObservableProperty]
    public partial double SliceProgress { get; set; }

    [ObservableProperty]
    public partial bool IsGeneratingSupports { get; set; }

    [ObservableProperty]
    public partial double GenerationProgress { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSlice))]
    public partial SliceResult? LastSlice { get; set; }

    public bool HasSlice => LastSlice is not null;

    [ObservableProperty]
    public partial string SliceSummary { get; set; } = "Not sliced yet.";

    [ObservableProperty]
    public partial int PreviewLayer { get; set; }

    [ObservableProperty]
    public partial int PreviewLayerMax { get; set; }

    [ObservableProperty]
    public partial string PreviewLayerText { get; set; } = "";

    [ObservableProperty]
    public partial WriteableBitmap? PreviewImage { get; set; }

    public NumericField[] Position { get; }
    public NumericField[] Rotation { get; }
    public NumericField[] Scale { get; }
    public NumericField PlacementHeight { get; }

    public MainViewModel()
    {
        // Keep the document pointed at the live persisted settings. Each support operation takes
        // its own value snapshot, so edits affect the next generation/manual placement only.
        Document.SupportSettings = AppConfig.Current.Supports;
        Document.ApplyResinPreset(AppConfig.Current.FindResinPreset(ResinPreset.DefaultId) ??
                                  AppConfig.Current.ResinPresets.FirstOrDefault() ?? ResinPreset.Default);
        PrintSettings = new PrintSettingsViewModel(Document);
        SupportSettings = new ConfigViewModel(Document);
        SupportClip.Changed += () => OnPropertyChanged(nameof(ViewportClipRange));
        SupportSettings.Saved += () =>
        {
            Document.SupportSettings = AppConfig.Current.Supports;
            RefreshPrinterOptions();
            SupportSettings.Resins.Refresh();
        };
        DropToPlateScopedCommand = new ModeScopedCommand(
            DropToPlateCommand, () => ViewMode, WorkspaceMode.Layout);
        HideScopedCommand = new ModeScopedCommand(
            HideCommand, () => ViewMode, WorkspaceMode.Layout, WorkspaceMode.Support);
        UnhideAllScopedCommand = new ModeScopedCommand(
            UnhideAllCommand, () => ViewMode, WorkspaceMode.Layout, WorkspaceMode.Support);
        HideUnselectedSupportsScopedCommand = new ModeScopedCommand(
            HideUnselectedSupportsCommand, () => ViewMode, WorkspaceMode.Support);
        GenerateSupportsScopedCommand = new ModeScopedCommand(
            GenerateSupportsCommand, () => ViewMode, WorkspaceMode.Support);
        SliceScopedCommand = new ModeScopedCommand(
            SliceCommand, () => ViewMode, WorkspaceMode.Slicing);
        _modeScopedCommands =
        [
            DropToPlateScopedCommand,
            HideScopedCommand,
            UnhideAllScopedCommand,
            HideUnselectedSupportsScopedCommand,
            GenerateSupportsScopedCommand,
            SliceScopedCommand,
        ];
        Position = MakeAxisFields(UnitKind.Length, "0.###", (t, axis, v) => t with { Translation = SetAxis(t.Translation, axis, (float)v) });
        Rotation = MakeAxisFields(UnitKind.Angle, "0.##", (t, axis, v) => t with { EulerDegrees = SetAxis(t.EulerDegrees, axis, (float)v) });
        Scale = MakeAxisFields(UnitKind.Scalar, "0.####", (t, axis, v) => t with { Scale = SetAxis(t.Scale, axis, (float)v) });
        var placement = AppConfig.Current.Placement;
        placement.HeightMm = MathF.Max(0, placement.HeightMm);
        Document.PlacementHeightMm = placement.HeightMm;
        _loadingPlacement = true;
        AutoDropEnabled = placement.Mode != PlacementMode.Off;
        _loadingPlacement = false;
        ApplyAutoPlacementMode(save: false);
        NumericField? placementHeight = null;
        placementHeight = new NumericField("Height", UnitKind.Length, "0.###", value =>
        {
            var height = MathF.Max(0, (float)value);
            Document.PlacementHeightMm = height;
            AppConfig.Current.Placement.HeightMm = height;
            ApplyAutoPlacementMode(save: false);
            AppConfig.Save();
            placementHeight!.SetValue(height);
        });
        PlacementHeight = placementHeight;
        PlacementHeight.SetValue(Document.PlacementHeightMm);

        Document.Scene.ObjectAdded += o => Objects.Add(o);
        Document.Scene.ObjectRemoved += o => Objects.Remove(o);
        Document.SelectionChanged += OnDocumentSelectionChanged;
        Document.SupportSelectionChanged += () =>
        {
            HideUnselectedSupportsCommand.NotifyCanExecuteChanged();
            DeleteCommand.NotifyCanExecuteChanged();
            HideCommand.NotifyCanExecuteChanged();
        };
        Document.Changed += OnDocumentChanged;
        RefreshPrinterOptions(notifyDocument: false);
        OnDocumentChanged();
    }

    private NumericField[] MakeAxisFields(UnitKind kind, string format, Func<Transform, int, double, Transform> edit)
    {
        var labels = new[] { "X", "Y", "Z" };
        var fields = new NumericField[3];
        for (int axis = 0; axis < 3; axis++)
        {
            var a = axis;
            fields[axis] = new NumericField(labels[axis], kind, format, value =>
            {
                var obj = SelectedObject;
                if (obj is null) return;
                var before = obj.Transform;
                var requested = edit(before, a, value);
                if (requested == before) return;
                Document.CommitTransform(obj, before, requested, "Edit transform");
            });
        }
        return fields;
    }

    private static Vector3 SetAxis(Vector3 v, int axis, float value) => axis switch
    {
        0 => v with { X = value },
        1 => v with { Y = value },
        _ => v with { Z = value },
    };

    private void OnDocumentSelectionChanged()
    {
        if (_syncingSelection) return;
        _syncingSelection = true;
        try
        {
            if (!_changingViewMode && ViewMode == WorkspaceMode.Layout)
                SelectedObject = Document.Selection.FirstOrDefault();
        }
        finally
        {
            _syncingSelection = false;
        }
        RefreshFields();
        DeleteCommand.NotifyCanExecuteChanged();
        DropToPlateCommand.NotifyCanExecuteChanged();
        HideCommand.NotifyCanExecuteChanged();
        GenerateSupportsCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedObjectChanged(SceneObject? value)
    {
        if (_syncingSelection) return;
        _syncingSelection = true;
        try
        {
            if (ViewMode == WorkspaceMode.Layout)
            {
                if (value is null) Document.ClearSelection();
                else Document.Select(value);
            }
        }
        finally
        {
            _syncingSelection = false;
        }
        RefreshFields();
        GenerateSupportsCommand.NotifyCanExecuteChanged();
    }

    partial void OnAutoDropEnabledChanged(bool value)
    {
        if (!_loadingPlacement) ApplyAutoPlacementMode(save: true);
    }

    private void ApplyAutoPlacementMode(bool save)
    {
        var mode = !AutoDropEnabled
            ? PlacementMode.Off
            : Document.PlacementHeightMm <= 1e-6f
                ? PlacementMode.AutoDrop
                : PlacementMode.RaiseAbovePlate;
        Document.PlacementMode = mode;
        AppConfig.Current.Placement.Mode = mode;
        if (save) AppConfig.Save();
    }

    private void OnDocumentChanged()
    {
        if (IsGeneratingSupports && !_applyingGenerationBatch)
            _generationCancellation?.Cancel();
        RefreshFields();
        var undo = Document.History.UndoName;
        var redo = Document.History.RedoName;
        HistoryStatus = (undo is null ? "" : $"Undo: {undo}") + (redo is null ? "" : $"   Redo: {redo}");
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
        SliceCommand.NotifyCanExecuteChanged();
        SupportClip.RefreshBounds(Document.Scene.WorldBounds, Document.Printer.BuildVolume.Z);

        // Geometry changed: the slice no longer matches the scene.
        if (LastSlice is not null && !IsSlicing) InvalidateSlice();
    }

    private void RefreshFields()
    {
        var obj = SelectedObject;
        if (obj is null)
        {
            DimensionsText = "";
            TriangleText = "";
            foreach (var f in Position.Concat(Rotation).Concat(Scale)) f.SetValue(0);
            return;
        }

        var t = obj.Transform;
        var euler = t.EulerDegrees;
        for (int i = 0; i < 3; i++)
        {
            Position[i].SetValue(Component(t.Translation, i));
            Rotation[i].SetValue(Component(euler, i));
            Scale[i].SetValue(Component(t.Scale, i));
        }
        var size = obj.WorldBounds.Size;
        DimensionsText = $"{size.X:0.##} × {size.Y:0.##} × {size.Z:0.##} mm";
        TriangleText = $"{obj.Mesh.TriangleCount:N0} triangles";
    }

    private static float Component(Vector3 v, int i) => i switch { 0 => v.X, 1 => v.Y, _ => v.Z };

    public void ImportMesh(string path)
    {
        var mesh = MeshFile.Read(path);
        var obj = new SceneObject(System.IO.Path.GetFileNameWithoutExtension(path), mesh);
        var b = mesh.Bounds;
        obj.Transform = Transform.Identity with { Translation = new Vector3(-b.Center.X, -b.Center.Y, -b.Min.Z) };
        Document.AddObject(obj);
    }

    public void SaveProject(string path, ProjectViewState viewState)
    {
        ProjectFile.Save(path, Document, viewState);
        ProjectPath = System.IO.Path.GetFullPath(path);
        Title = $"{System.IO.Path.GetFileNameWithoutExtension(ProjectPath)} — Danslicer";
        ViewportStatus = $"Saved {System.IO.Path.GetFileName(ProjectPath)}.";
    }

    public ProjectViewState OpenProject(string path)
    {
        var loaded = ProjectFile.Load(path);
        Document.ReplaceWith(loaded.Document);
        SupportClip.RefreshBounds(Document.Scene.WorldBounds, Document.Printer.BuildVolume.Z,
            reset: true);
        PrintSettings.Refresh();
        RefreshPrinterOptions(notifyDocument: false);
        SupportSettings.Resins.Refresh();
        SelectedObject = null;
        LastSlice = null;
        PreviewImage = null;
        PreviewLayerText = "";
        SliceSummary = "Not sliced yet.";
        ViewMode = loaded.ViewState.WorkspaceMode;
        ProjectPath = System.IO.Path.GetFullPath(path);
        Title = $"{System.IO.Path.GetFileNameWithoutExtension(ProjectPath)} — Danslicer";
        ViewportStatus = $"Opened {System.IO.Path.GetFileName(ProjectPath)}.";
        return loaded.ViewState;
    }

    /// <summary>
    /// Rebuilds the slicing choices after Preferences changes. A project-embedded definition is
    /// kept as a document-only option when its id is absent from this machine's user config.
    /// </summary>
    public void RefreshPrinterOptions(bool notifyDocument = true)
    {
        var current = Document.Printer;
        var options = AppConfig.Current.Printers.ToList();
        var index = options.FindIndex(printer =>
            string.Equals(printer.Id, current.Id, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            options.Add(current);
            index = options.Count - 1;
        }
        else if (options[index] != current)
        {
            Document.Printer = options[index];
        }

        _printerOptions = options;
        _printerDisplayNames = options.Select(printer =>
            printer.Name + (AppConfig.Current.FindPrinter(printer.Id) is null ? " (project)" : "")).ToArray();
        _selectedPrinterIndex = index;
        OnPropertyChanged(nameof(PrinterDisplayNames));
        OnPropertyChanged(nameof(SelectedPrinterIndex));
        OnPropertyChanged(nameof(SelectedPrinterName));
        if (notifyDocument && Document.Printer != current) Document.NotifyTransientChange();
    }

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void Undo() => Document.Undo();
    private bool CanUndo() => Document.History.CanUndo;

    [RelayCommand(CanExecute = nameof(CanRedo))]
    private void Redo() => Document.Redo();
    private bool CanRedo() => Document.History.CanRedo;

    [RelayCommand(CanExecute = nameof(CanDelete))]
    private void Delete()
    {
        if (ViewMode == WorkspaceMode.Layout) Document.DeleteSelection();
        else if (ViewMode == WorkspaceMode.Support) Document.DeleteSupportSelection();
    }

    private bool CanDelete() => ViewMode switch
    {
        WorkspaceMode.Layout => HasSelection(),
        WorkspaceMode.Support => HasSupportSelection(),
        _ => false,
    };

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void DropToPlate() => Document.DropSelectionToPlate();

    [RelayCommand(CanExecute = nameof(CanHide))]
    private void Hide()
    {
        if (ViewMode == WorkspaceMode.Layout) Document.HideSelection();
        else if (ViewMode == WorkspaceMode.Support) Document.HideSelectedSupportElements();
    }

    private bool CanHide() => ViewMode switch
    {
        WorkspaceMode.Layout => HasSelection(),
        WorkspaceMode.Support => HasSupportSelection(),
        _ => false,
    };

    [RelayCommand]
    private void UnhideAll() => Document.UnhideAll();

    [RelayCommand(CanExecute = nameof(HasSupportSelection))]
    private void HideUnselectedSupports() => Document.HideUnselectedSupportElements();

    private bool HasSupportSelection() => Document.SupportSelection.Count > 0;

    [RelayCommand(CanExecute = nameof(CanGenerateSupports))]
    private async Task GenerateSupports()
    {
        if (IsGeneratingSupports) return;
        var obj = SelectedObject;
        if (obj is null) return;
        var request = Document.CaptureSupportGeneration(obj, seed: 0);
        _generationCancellation = new CancellationTokenSource();
        var token = _generationCancellation.Token;
        SupportGenerationBatch? batch = null;
        IsGeneratingSupports = true;
        GenerationProgress = 0;
        GenerateSupportsCommand.NotifyCanExecuteChanged();
        ViewportStatus = "Generating supports…";
        try
        {
            var generationProgress = new Progress<SupportGenerationProgress>(p =>
            {
                GenerationProgress = p.Fraction * 0.8;
                ViewportStatus = $"{p.Stage}: {p.Completed} / {p.Total}";
            });
            var prepared = await Task.Run(
                () => Document.ComputeSupportGeneration(request, token, generationProgress), token);
            token.ThrowIfCancellationRequested();
            batch = new SupportGenerationBatch(Document.Supports, Document.History, prepared);
            while (!batch.IsFinished)
            {
                token.ThrowIfCancellationRequested();
                _applyingGenerationBatch = true;
                try { batch.CommitNextBatch(); }
                finally { _applyingGenerationBatch = false; }
                GenerationProgress = 0.8 + batch.Progress * 0.2;
                ViewportStatus = $"Adding supports… {GenerationProgress * 100:0}%";
                await Task.Yield();
            }
            _applyingGenerationBatch = true;
            try { batch.Complete(); }
            finally { _applyingGenerationBatch = false; }
            var result = prepared.Summary;
            ViewportStatus = result.CandidateCount == 0
                ? "Generate supports: no support tips were needed."
                : $"Generate supports: {result.GeneratedTipCount} tips added, {result.UnroutedTipCount} unrouted.";
        }
        catch (OperationCanceledException)
        {
            if (batch is not null)
            {
                _applyingGenerationBatch = true;
                try { batch.Cancel(); }
                finally { _applyingGenerationBatch = false; }
            }
            ViewportStatus = "Support generation cancelled; no supports were added.";
        }
        catch (Exception ex)
        {
            if (batch is not null)
            {
                _applyingGenerationBatch = true;
                try { batch.Cancel(); }
                finally { _applyingGenerationBatch = false; }
            }
            ViewportStatus = $"Support generation failed: {ex.Message}";
        }
        finally
        {
            IsGeneratingSupports = false;
            _generationCancellation?.Dispose();
            _generationCancellation = null;
            GenerateSupportsCommand.NotifyCanExecuteChanged();
        }
    }

    private bool CanGenerateSupports() => SelectedObject is not null && !IsGeneratingSupports;

    [RelayCommand]
    private void CancelSupportGeneration()
    {
        ViewportStatus = "Cancelling support generation…";
        _generationCancellation?.Cancel();
    }

    [RelayCommand]
    private void SelectAll()
    {
        if (ViewMode == WorkspaceMode.Support)
        {
            Document.ClearSelection();
            Document.SelectSupportElements(SupportDisplayPolicy.DisplayedElementIds(
                Document.Supports, AppConfig.Current.Viewport.SupportDisplay, ViewportClipRange));
            return;
        }
        WorkspaceSelection.SelectAll(Document, ViewMode);
    }

    [RelayCommand]
    private void ToggleView()
        => ViewMode = WorkspaceNavigation.Next(ViewMode, HasSlice);

    [RelayCommand]
    private void ToggleSnap() => SnapEnabled = !SnapEnabled;

    public void StepLayer(int delta)
    {
        if (LastSlice is null) return;
        PreviewLayer = Math.Clamp(PreviewLayer + delta, 0, PreviewLayerMax);
    }

    private bool HasSelection() => Document.Selection.Count > 0;

    // ----- Slicing -----

    private bool CanSlice() => !IsSlicing && Document.Scene.Objects.Any(o => o.RenderState != RenderState.Hidden);

    /// <summary>Slices the scene in the background. Returns the result, or null on failure or cancel.</summary>
    [RelayCommand(CanExecute = nameof(CanSlice))]
    private async Task<SliceResult?> Slice()
    {
        if (IsSlicing) return null;
        IsSlicing = true;
        SliceProgress = 0;
        SliceCommand.NotifyCanExecuteChanged();
        _sliceCancellation = new CancellationTokenSource();
        var token = _sliceCancellation.Token;
        var objects = Document.Scene.Objects.ToList();
        var printer = Document.Printer;
        var settings = Document.PrintSettings;
        var resin = Document.ResinSettings;
        var progress = new Progress<double>(p =>
        {
            SliceProgress = p;
            ViewportStatus = $"Slicing… {p * 100:0}%";
        });

        try
        {
            var result = await Task.Run(() => Slicer.Slice(objects, printer, settings, progress, token,
                Document.Supports, resin), token);
            LastSlice = result;
            SliceSummary =
                $"{result.LayerCount} layers × {settings.LayerHeight:0.###} mm = {result.PrintHeight:0.##} mm\n" +
                $"{result.VolumeMl:0.##} ml resin\n" +
                $"≈ {TimeSpan.FromSeconds(result.EstimatedSeconds):h\\:mm\\:ss}\n" +
                $"footprint X {result.MinX:0.#}…{result.MaxX:0.#}  Y {result.MinY:0.#}…{result.MaxY:0.#} mm";
            PreviewLayerMax = Math.Max(0, result.LayerCount - 1);
            PreviewLayer = Math.Min(PreviewLayer, PreviewLayerMax);
            UpdatePreview();
            ViewMode = WorkspaceMode.Slicing;
            ViewportStatus = $"Sliced {result.LayerCount} layers.  Tab returns to the model view · Page Up/Down or Ctrl+wheel steps layers · wheel zooms · drag pans";
            return result;
        }
        catch (OperationCanceledException)
        {
            ViewportStatus = "Slicing cancelled.";
            return null;
        }
        catch (Exception ex)
        {
            ViewportStatus = $"Slicing failed: {ex.Message}";
            SliceSummary = ex.Message;
            return null;
        }
        finally
        {
            IsSlicing = false;
            _sliceCancellation = null;
            SliceCommand.NotifyCanExecuteChanged();
        }
    }

    public void CancelSlice() => _sliceCancellation?.Cancel();

    /// <summary>Slices if needed, then writes the printer file.</summary>
    public async Task<bool> ExportAsync(string path)
    {
        var result = LastSlice;
        if (result is null || result.Settings != Document.PrintSettings ||
            result.ResinSettings != Document.ResinSettings)
            result = await Slice();
        if (result is null) return false;

        try
        {
            await Task.Run(() => PhotonWorkshopWriter.Write(result, path));
            ViewportStatus = $"Exported {System.IO.Path.GetFileName(path)}: {result.LayerCount} layers, {result.VolumeMl:0.##} ml.";
            return true;
        }
        catch (Exception ex)
        {
            ViewportStatus = $"Export failed: {ex.Message}";
            return false;
        }
    }

    /// <summary>Drops a stale slice and returns to the model view.</summary>
    public void InvalidateSlice()
    {
        LastSlice = null;
        PreviewImage = null;
        PreviewLayerText = "";
        SliceSummary = "Scene changed since the last slice.";
    }

    partial void OnPreviewLayerChanged(int value) => UpdatePreview();

    private void UpdatePreview()
    {
        var result = LastSlice;
        if (result is null || result.LayerCount == 0)
        {
            PreviewImage = null;
            PreviewLayerText = "";
            return;
        }

        var index = Math.Clamp(PreviewLayer, 0, result.LayerCount - 1);
        var layer = result.Layers[index];
        var w = result.Printer.ResolutionX;
        var h = result.Printer.ResolutionY;

        var bitmap = PreviewImage;
        if (bitmap is null || bitmap.PixelSize.Width != w || bitmap.PixelSize.Height != h)
            bitmap = new WriteableBitmap(new PixelSize(w, h), new Avalonia.Vector(96, 96), PixelFormats.Gray8, AlphaFormat.Opaque);

        using (var fb = bitmap.Lock())
        {
            unsafe
            {
                var dst = (byte*)fb.Address;
                if (fb.RowBytes == w)
                {
                    layer.Decode(w, h, new Span<byte>(dst, w * h));
                }
                else
                {
                    var pixels = new byte[w * h];
                    layer.Decode(w, h, pixels);
                    for (int y = 0; y < h; y++)
                        pixels.AsSpan(y * w, w).CopyTo(new Span<byte>(dst + y * fb.RowBytes, w));
                }
            }
        }

        // Re-assign so bindings see a change even when the same bitmap instance was reused.
        PreviewImage = null;
        PreviewImage = bitmap;
        PreviewLayerText = $"Layer {index + 1} / {result.LayerCount}   Z {layer.Z:0.###} mm   {layer.AreaMm2:0.#} mm²   {result.ResinSettings.ExposureForLayer(index):0.##} s";
    }
}
