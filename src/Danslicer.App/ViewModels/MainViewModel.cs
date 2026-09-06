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
using Danslicer.Core.Geometry;
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

    public bool CapInterior
    {
        get => AppConfig.Current.Viewport.CapInterior;
        set
        {
            if (value == AppConfig.Current.Viewport.CapInterior) return;
            AppConfig.Current.Viewport.CapInterior = value;
            AppConfig.Save();
            OnPropertyChanged();
        }
    }

    public ClipCapStyle CapStyle
    {
        get => AppConfig.Current.Viewport.CapStyle;
        set
        {
            if (value == AppConfig.Current.Viewport.CapStyle) return;
            AppConfig.Current.Viewport.CapStyle = value;
            AppConfig.Save();
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// The support display config the viewport actually draws with: the user's chosen display
    /// mode in Support mode, and full visibility while Layout is active — Layout arranges models
    /// with their supports, so a Support-mode working aid (hidden, tips only, transparent, …)
    /// must not follow the user across. Every consumer binds THIS
    /// rather than the raw config, so the two render paths and picking cannot disagree — see
    /// <see cref="SupportDisplayPolicy.ForWorkspace"/>.
    /// </summary>
    public SupportDisplayConfig EffectiveSupportDisplay => SupportDisplayPolicy.ForWorkspace(
        AppConfig.Current.Viewport.SupportDisplay, ViewMode == WorkspaceMode.Layout);

    public ModeScopedCommand DropToPlateScopedCommand { get; }
    public ModeScopedCommand DuplicateScopedCommand { get; }
    public ModeScopedCommand MirrorXScopedCommand { get; }
    public ModeScopedCommand MirrorYScopedCommand { get; }
    public ModeScopedCommand MirrorZScopedCommand { get; }
    public ModeScopedCommand HideScopedCommand { get; }
    public ModeScopedCommand UnhideAllScopedCommand { get; }
    public ModeScopedCommand HideUnselectedSupportsScopedCommand { get; }
    public ModeScopedCommand GenerateSupportsScopedCommand { get; }
    public ModeScopedCommand GenerateIslandSupportsScopedCommand { get; }
    public ModeScopedCommand DetectIslandsScopedCommand { get; }
    public ModeScopedCommand SliceScopedCommand { get; }

    private readonly List<ModeScopedCommand> _modeScopedCommands;

    [ObservableProperty]
    public partial SceneObject? SelectedObject { get; set; }

    [ObservableProperty]
    public partial string Title { get; set; } = "Danslicer";

    public string? ProjectPath { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ViewportStatusDisplay))]
    public partial string ViewportStatus { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ViewportStatusDisplay), nameof(HasSliceWarning))]
    public partial string? SliceWarning { get; set; }

    public string ViewportStatusDisplay => SliceWarning is null
        ? ViewportStatus
        : $"{SliceWarning}  {ViewportStatus}";

    public bool HasSliceWarning => SliceWarning is not null;

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
        nameof(IsSupportsToolVisible), nameof(IsIslandSupportToolVisible),
        nameof(IsIslandDetectionToolVisible), nameof(IsVisibilityToolVisible), nameof(IsRaftsToolVisible),
        nameof(IsUvtoolsCheckToolVisible), nameof(EffectiveSupportDisplay))]
    public partial WorkspaceMode ViewMode { get; set; } = WorkspaceMode.Layout;

    public IReadOnlyList<ViewportTool> ViewportTools => ViewportToolbarPolicy.ToolsFor(ViewMode);

    public bool IsObjectsToolVisible => ViewportToolbarPolicy.IsAvailable(ViewportTool.Objects, ViewMode);
    public bool IsSupportsToolVisible => ViewportToolbarPolicy.IsAvailable(ViewportTool.Supports, ViewMode);
    public bool IsIslandSupportToolVisible => ViewportToolbarPolicy.IsAvailable(ViewportTool.IslandSupport, ViewMode);
    public bool IsIslandDetectionToolVisible => ViewportToolbarPolicy.IsAvailable(ViewportTool.IslandDetection, ViewMode);
    public bool IsVisibilityToolVisible => ViewportToolbarPolicy.IsAvailable(ViewportTool.Visibility, ViewMode);
    public bool IsRaftsToolVisible => ViewportToolbarPolicy.IsAvailable(ViewportTool.Rafts, ViewMode);
    public bool IsUvtoolsCheckToolVisible =>
        ViewportToolbarPolicy.IsAvailable(ViewportTool.UvtoolsCheck, ViewMode);

    /// <summary>
    /// Object rows are live wherever an object selection means something: Layout arranges the
    /// selected model, Support supports it. Slicing has no object-level operations.
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

    /// <summary>
    /// Shows or hides one model from the Objects list, in Layout and in Support mode alike. The
    /// list is the only way to hide a model in Support mode, where H means "hide support
    /// elements"; Layout's H on the selection is unaffected.
    /// </summary>
    [RelayCommand]
    private void ToggleObjectVisibility(SceneObject? obj)
    {
        if (obj is null) return;
        Document.SetObjectHidden(obj, obj.RenderState != RenderState.Hidden);
    }

    /// <summary>
    /// Re-reads one model's file from disk and swaps the new geometry in — the update button
    /// beside each entry in the Objects list, for when the model has been changed in CAD. The
    /// object keeps its name, transform and place in the scene; its painted region and its
    /// supports are indexed against the old mesh, so they go, and one undo brings all three
    /// back together.
    /// </summary>
    [RelayCommand]
    private void ReloadObject(SceneObject? obj)
    {
        if (obj?.SourcePath is not { Length: > 0 } path) return;
        try
        {
            var mesh = MeshFile.Read(path);
            Document.ReloadObject(obj, mesh);
            ViewportStatus = $"Updated {obj.Name} from {System.IO.Path.GetFileName(path)}.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                                       InvalidDataException or NotSupportedException)
        {
            ViewportStatus = $"Could not update {obj.Name}: {ex.Message}";
        }
    }

    // ----- Support regions (DESIGN 8.3 stage 2) -----

    /// <summary>Dihedral limit for click-to-grow, in degrees. Live: changing it re-computes the
    /// highlight under the cursor, since the operation keeps no state between clicks.</summary>
    [ObservableProperty]
    public partial double RegionDihedralDegrees { get; set; } = 30;

    // The three region numbers are edited through NumericField like every other number in the
    // app — expressions and units included. Binding an ExpressionBox straight at a double looked
    // like it worked and silently kept the old value, which is how a 0° patch angle still grew
    // across a 90° edge.
    public NumericField RegionDihedralField { get; }
    public NumericField RegionOverhangField { get; }
    public NumericField RegionBrushRadiusField { get; }

    partial void OnRegionDihedralDegreesChanged(double value)
    {
        RegionDihedralField.SetValue(value);
        RefreshRegionHover();
    }

    partial void OnRegionOverhangDegreesChanged(double value) => RegionOverhangField.SetValue(value);
    partial void OnRegionBrushRadiusPixelsChanged(double value) => RegionBrushRadiusField.SetValue(value);

    /// <summary>Threshold for "select what faces down", in generation's convention: measured from
    /// vertical, strict greater-than. Defaults to the support settings' own overhang angle so the
    /// two agree unless the user deliberately parts them.</summary>
    [ObservableProperty]
    public partial double RegionOverhangDegrees { get; set; } = 45;

    /// <summary>Which of the two face sets the operations edit: the support region, or the
    /// keep-clean region that overrides it.</summary>
    [ObservableProperty]
    public partial bool EditingKeepCleanRegion { get; set; }

    /// <summary>While set, a viewport click paints faces instead of selecting supports.</summary>
    [ObservableProperty]
    public partial bool RegionPickMode { get; set; }

    public string RegionSetName => EditingKeepCleanRegion ? "keep-clean region" : "support region";

    /// <summary>
    /// Applies a set operation to the region being edited, as one undoable step.
    ///
    /// <para><b>Empty means two different things, deliberately.</b> To GENERATION an empty support
    /// region means "every face" — the compatibility guard from stage 1. To these EDITING
    /// operations it means the literal empty set, because a user who inverts an unpainted object
    /// expects to end up with every face painted, not with a no-op. The two readings agree on
    /// what actually gets supported, which is what matters: painting every face explicitly and
    /// painting nothing at all generate the same supports.</para>
    /// </summary>
    private void EditRegion(Func<Mesh, IReadOnlySet<int>, IReadOnlySet<int>> operation, string name)
    {
        if (SelectedObject is not { } obj) return;
        var regions = obj.Regions;
        var current = EditingKeepCleanRegion ? regions.KeepCleanFaces : regions.Faces;
        var next = operation(obj.Mesh, current);
        Document.SetSupportRegions(obj, EditingKeepCleanRegion
            ? ObjectSupportRegions.From(regions.Faces, next)
            : ObjectSupportRegions.From(next, regions.KeepCleanFaces), name);
        ViewportStatus = $"{name}: {next.Count} of {obj.Mesh.TriangleCount} faces in the {RegionSetName}.";
    }

    private SceneObject? _hoverObject;
    private int _hoverTriangle = -1;

    /// <summary>
    /// The patch the cursor is over, as object plus faces, or null when it is over nothing. The
    /// viewport draws it as a highlight so the user can see what a click would take before
    /// committing to it — the same set the click then paints, computed by the same call.
    /// </summary>
    [ObservableProperty]
    public partial RegionHoverPreview? RegionHover { get; set; }

    /// <summary>The cursor moved over a face (or off the model, with a null object).</summary>
    public void HoverRegionFace(SceneObject? obj, int triangle)
    {
        if (ReferenceEquals(obj, _hoverObject) && triangle == _hoverTriangle) return;
        _hoverObject = obj;
        _hoverTriangle = triangle;
        RefreshRegionHover();
    }

    /// <summary>Recomputes the highlight: after a move, or after the angle that shapes it changed.</summary>
    private void RefreshRegionHover()
    {
        if (!RegionPickMode || RegionBrushMode || _hoverObject is not { } obj || _hoverTriangle < 0 ||
            !SupportTargetPolicy.CanSupport(Document.SupportTarget, obj))
        {
            RegionHover = null;
            return;
        }
        RegionHover = new RegionHoverPreview(obj,
            SupportRegionSelection.GrowByDihedral(obj.Mesh, [_hoverTriangle], (float)RegionDihedralDegrees));
    }

    partial void OnRegionPickModeChanged(bool value) => RefreshRegionHover();
    partial void OnRegionBrushModeChanged(bool value) => RefreshRegionHover();

    /// <summary>
    /// Selects <paramref name="obj"/> for painting, or refuses it. Support mode works on one
    /// model (<see cref="SupportTargetPolicy"/>), and painting is no exception: with a target
    /// already chosen, a stroke that strays onto a neighbour must not paint it and must not take
    /// the target away by selecting it. With no target chosen, this is the click that picks one.
    /// </summary>
    private bool TakePaintTarget(SceneObject obj)
    {
        if (SupportTargetPolicy.RefusalMessage(Document.SupportTarget, obj) is { } refusal)
        {
            ViewportStatus = refusal;
            return false;
        }
        if (!ReferenceEquals(obj, SelectedObject)) SelectedObject = obj;
        return true;
    }

    /// <summary>Click-to-grow: the picked face plus everything reachable across edges that turn
    /// by no more than <see cref="RegionDihedralDegrees"/>. Shift-click erases the same patch.</summary>
    public void PaintRegionFromFace(SceneObject obj, int triangle, bool erase)
    {
        if (!TakePaintTarget(obj)) return;
        var patch = SupportRegionSelection.GrowByDihedral(obj.Mesh, [triangle], (float)RegionDihedralDegrees);
        EditRegion((_, current) =>
        {
            var next = new HashSet<int>(current);
            if (erase) next.ExceptWith(patch);
            else next.UnionWith(patch);
            return next;
        }, erase ? "Erase region patch" : "Paint region patch");
    }

    /// <summary>While set, a painting drag lays a brush stroke instead of growing a patch from
    /// one click.</summary>
    [ObservableProperty]
    public partial bool RegionBrushMode { get; set; }

    /// <summary>
    /// Brush radius in SCREEN pixels, adjustable while painting. A brush is aimed with the eye,
    /// so it keeps the size it looks: a millimetre radius would grow and shrink as the user
    /// zooms. The viewport converts to world units at the point being painted.
    /// </summary>
    [ObservableProperty]
    public partial double RegionBrushRadiusPixels { get; set; } = 24;

    private SupportRegionStroke? _stroke;
    private SceneObject? _strokeObject;

    /// <summary>
    /// Starts a brush stroke on <paramref name="obj"/>. Every dab until <see cref="EndStroke"/>
    /// belongs to this one stroke, and the whole stroke is one undo step: the dabs update the
    /// object's region directly so the paint appears under the cursor, and the undoable edit is
    /// committed once, at the end, from where the region stood when the stroke began.
    /// </summary>
    public void BeginStroke(SceneObject obj, bool erase)
    {
        if (!TakePaintTarget(obj)) return;
        _strokeObject = obj;
        _stroke = new SupportRegionStroke(obj.Regions, EditingKeepCleanRegion, erase);
    }

    /// <summary>
    /// One dab of the brush, centred on a surface point on a picked face.
    /// <paramref name="worldRadius"/> is the on-screen radius converted to world units by the
    /// viewport, which is the only part of the app that knows the camera.
    /// </summary>
    public void BrushStroke(Vector3 surfacePoint, int triangle, float worldRadius)
    {
        if (_stroke is null || _strokeObject is not { } obj) return;
        // The stroke works in the object's own space, because that is where its faces live.
        if (!Matrix4x4.Invert(obj.Transform.ToMatrix(), out var worldToLocal)) return;
        var local = Vector3.Transform(surfacePoint, worldToLocal);
        var scale = obj.Transform.Scale;
        var localRadius = worldRadius /
            MathF.Max(MathF.Max(MathF.Abs(scale.X), MathF.Abs(scale.Y)), MathF.Abs(scale.Z));
        if (!_stroke.Add(SupportRegionBrush.FacesWithin(obj.Mesh, local, localRadius, triangle))) return;
        // Live feedback only — not an undoable edit. EndStroke commits the whole stroke.
        obj.Regions = _stroke.Apply();
        Document.NotifyTransientChange();
    }

    /// <summary>Commits the stroke as a single undoable edit, or drops it if it painted nothing.</summary>
    public void EndStroke()
    {
        var stroke = _stroke;
        var obj = _strokeObject;
        _stroke = null;
        _strokeObject = null;
        if (stroke is null || obj is null) return;
        var painted = stroke.Apply();
        // Rewind to the pre-stroke region first: SetSupportRegions is what records the undo, and
        // it can only record a change it actually performs.
        obj.Regions = stroke.Before;
        Document.SetSupportRegions(obj, painted,
            stroke.Erasing ? "Erase support region" : "Paint support region");
        ViewportStatus = stroke.Touched.Count == 0
            ? "Brush: nothing painted."
            : $"Brush: {stroke.Touched.Count} faces {(stroke.Erasing ? "erased from" : "added to")} the {RegionSetName}.";
    }

    [RelayCommand(CanExecute = nameof(HasRegionTarget))]
    private void SelectFacingDownRegion() => EditRegion(
        (mesh, _) => SupportRegionSelection.FacingDown(mesh,
            SelectedObject?.Transform.ToMatrix() ?? Matrix4x4.Identity,
            (float)RegionOverhangDegrees),
        "Select faces pointing down");

    [RelayCommand(CanExecute = nameof(HasRegionTarget))]
    private void InvertRegion() => EditRegion(SupportRegionSelection.Invert, "Invert region");

    [RelayCommand(CanExecute = nameof(HasRegionTarget))]
    private void GrowRegion() => EditRegion(SupportRegionSelection.Grow, "Grow region");

    [RelayCommand(CanExecute = nameof(HasRegionTarget))]
    private void ShrinkRegion() => EditRegion(SupportRegionSelection.Shrink, "Shrink region");

    [RelayCommand(CanExecute = nameof(HasRegionTarget))]
    private void ConnectedRegion() => EditRegion(
        (mesh, current) => SupportRegionSelection.Connected(mesh, current), "Select connected");

    [RelayCommand(CanExecute = nameof(HasRegionTarget))]
    private void ClearRegion() => EditRegion((_, _) => new HashSet<int>(), "Clear region");

    private bool HasRegionTarget() => SelectedObject is not null;

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
            else if (value == WorkspaceMode.Support)
            {
                // Support mode keeps exactly one object selected: the support target. It is the
                // same object the user had in Layout, it draws in the selection colour there and
                // here, and Document.SupportTarget reads it back for generation and manual
                // placement. A multi-selection collapses to the active object.
                if (SelectedObject is { } target) Document.Select(target);
                else Document.ClearSelection();
            }
            else
            {
                Document.ClearSelection();
                Document.ClearSupportSelection();
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
    [NotifyPropertyChangedFor(nameof(HasDetectedIslands))]
    public partial IReadOnlyList<DetectedIsland> DetectedIslands { get; set; } = [];

    [ObservableProperty]
    public partial bool IsDetectingIslands { get; set; }

    public bool HasDetectedIslands => DetectedIslands.Count > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSlice))]
    public partial SliceResult? LastSlice { get; set; }

    public bool HasSlice => LastSlice is not null;

    private string? _lastExportPath;
    private UvtoolsLaunchAvailability _uvtoolsLaunchAvailability =
        UvtoolsLauncher.GetAvailability(null);

    public string? LastExportPath => _lastExportPath;
    public bool CanCheckWithUvtools => _uvtoolsLaunchAvailability.IsEnabled;
    public string UvtoolsCheckTooltip => _uvtoolsLaunchAvailability.Tooltip;

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
        // The display mode lives in the shared settings view-model, so the effective value the
        // viewport binds has to be re-read whenever that changes, not only on a mode switch.
        SupportSettings.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(ConfigViewModel.SupportDisplay) or null)
                OnPropertyChanged(nameof(EffectiveSupportDisplay));
        };
        SupportSettings.Saved += () =>
        {
            Document.SupportSettings = AppConfig.Current.Supports;
            RefreshPrinterOptions();
            SupportSettings.Resins.Refresh();
        };
        DuplicateScopedCommand = new ModeScopedCommand(
            DuplicateCommand, () => ViewMode, WorkspaceMode.Layout);
        MirrorXScopedCommand = new ModeScopedCommand(
            MirrorXCommand, () => ViewMode, WorkspaceMode.Layout);
        MirrorYScopedCommand = new ModeScopedCommand(
            MirrorYCommand, () => ViewMode, WorkspaceMode.Layout);
        MirrorZScopedCommand = new ModeScopedCommand(
            MirrorZCommand, () => ViewMode, WorkspaceMode.Layout);
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
        GenerateIslandSupportsScopedCommand = new ModeScopedCommand(
            GenerateIslandSupportsCommand, () => ViewMode, WorkspaceMode.Support);
        DetectIslandsScopedCommand = new ModeScopedCommand(
            DetectIslandsCommand, () => ViewMode, WorkspaceMode.Support);
        SliceScopedCommand = new ModeScopedCommand(
            SliceCommand, () => ViewMode, WorkspaceMode.Slicing);
        _modeScopedCommands =
        [
            DuplicateScopedCommand,
            MirrorXScopedCommand,
            MirrorYScopedCommand,
            MirrorZScopedCommand,
            DropToPlateScopedCommand,
            HideScopedCommand,
            UnhideAllScopedCommand,
            HideUnselectedSupportsScopedCommand,
            GenerateSupportsScopedCommand,
            GenerateIslandSupportsScopedCommand,
            DetectIslandsScopedCommand,
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

        RegionDihedralField = new NumericField("Patch angle", UnitKind.Angle, "0.##",
            value => RegionDihedralDegrees = Math.Clamp(value, 0, 180));
        RegionOverhangField = new NumericField("Down angle", UnitKind.Angle, "0.##",
            value => RegionOverhangDegrees = Math.Clamp(value, 0, 90));
        RegionBrushRadiusField = new NumericField("Brush radius", UnitKind.Scalar, "0",
            value => RegionBrushRadiusPixels = Math.Clamp(value, 2, 400), suffix: "px");
        RegionDihedralField.SetValue(RegionDihedralDegrees);
        RegionOverhangField.SetValue(RegionOverhangDegrees);
        RegionBrushRadiusField.SetValue(RegionBrushRadiusPixels);

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
            if (!_changingViewMode && ViewMode is WorkspaceMode.Layout or WorkspaceMode.Support)
                SelectedObject = Document.Selection.FirstOrDefault();
        }
        finally
        {
            _syncingSelection = false;
        }
        RefreshFields();
        DeleteCommand.NotifyCanExecuteChanged();
        DuplicateCommand.NotifyCanExecuteChanged();
        MirrorXCommand.NotifyCanExecuteChanged();
        MirrorYCommand.NotifyCanExecuteChanged();
        MirrorZCommand.NotifyCanExecuteChanged();
        DropToPlateCommand.NotifyCanExecuteChanged();
        HideCommand.NotifyCanExecuteChanged();
        GenerateSupportsCommand.NotifyCanExecuteChanged();
        GenerateIslandSupportsCommand.NotifyCanExecuteChanged();
        DetectIslandsCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedObjectChanged(SceneObject? value)
    {
        if (_syncingSelection) return;
        _syncingSelection = true;
        try
        {
            if (ViewMode is WorkspaceMode.Layout or WorkspaceMode.Support)
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
        GenerateIslandSupportsCommand.NotifyCanExecuteChanged();
        DetectIslandsCommand.NotifyCanExecuteChanged();
        NotifyRegionCommands();
    }

    private void NotifyRegionCommands()
    {
        SelectFacingDownRegionCommand.NotifyCanExecuteChanged();
        InvertRegionCommand.NotifyCanExecuteChanged();
        GrowRegionCommand.NotifyCanExecuteChanged();
        ShrinkRegionCommand.NotifyCanExecuteChanged();
        ConnectedRegionCommand.NotifyCanExecuteChanged();
        ClearRegionCommand.NotifyCanExecuteChanged();
    }

    partial void OnEditingKeepCleanRegionChanged(bool value) => OnPropertyChanged(nameof(RegionSetName));

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
        if (DetectedIslands.Count > 0) DetectedIslands = [];
        RefreshFields();
        var undo = Document.History.UndoName;
        var redo = Document.History.RedoName;
        HistoryStatus = (undo is null ? "" : $"Undo: {undo}") + (redo is null ? "" : $"   Redo: {redo}");
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
        SliceCommand.NotifyCanExecuteChanged();
        // Layer height first: the clip boxes read in layer numbers, so a print-settings change
        // has to reach them before the bounds do.
        SupportClip.LayerHeightMm = Document.PrintSettings.LayerHeight;
        SupportClip.RefreshBounds(VisiblePrintBounds(), Document.Printer.BuildVolume.Z);

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
        var obj = new SceneObject(System.IO.Path.GetFileNameWithoutExtension(path), mesh)
        {
            SourcePath = System.IO.Path.GetFullPath(path),
        };
        var b = mesh.Bounds;
        // Centred on the plate in XY; the height is the placement mode's business, so an import
        // lands exactly where Auto Drop (or the raise-above-plate offset) says it should, instead
        // of being seated by a rule of its own that the first transform would then overrule.
        // With placement off the model keeps the height it was authored at.
        obj.Transform = Document.ApplyPlacement(obj,
            Transform.Identity with { Translation = new Vector3(-b.Center.X, -b.Center.Y, 0f) });
        Document.AddObject(obj);
    }

    public void SaveProject(string path, ProjectViewState viewState)
    {
        ProjectFile.Save(path, Document, viewState);
        ProjectPath = System.IO.Path.GetFullPath(path);
        Title = $"{System.IO.Path.GetFileNameWithoutExtension(ProjectPath)} — Danslicer";
        ViewportStatus = $"Saved {System.IO.Path.GetFileName(ProjectPath)}.";
    }

    /// <summary>
    /// Starts an empty project on the same machine setup. The caller confirms first when
    /// <see cref="Document"/>.HasContent — this method does not ask, so that the confirmation
    /// lives in one place (the window) rather than being duplicated per entry point.
    /// </summary>
    public void NewProject()
    {
        Document.Clear();
        SupportClip.LayerHeightMm = Document.PrintSettings.LayerHeight;
        SupportClip.RefreshBounds(VisiblePrintBounds(), Document.Printer.BuildVolume.Z, reset: true);
        SelectedObject = null;
        LastSlice = null;
        SliceWarning = null;
        PreviewImage = null;
        PreviewLayerText = "";
        SliceSummary = "Not sliced yet.";
        ViewMode = WorkspaceMode.Layout;
        ProjectPath = null;
        Title = "Danslicer";
        ViewportStatus = "New project.";
    }

    public ProjectViewState OpenProject(string path)
    {
        var loaded = ProjectFile.Load(path);
        Document.ReplaceWith(loaded.Document);
        SupportClip.LayerHeightMm = Document.PrintSettings.LayerHeight;
        SupportClip.RefreshBounds(VisiblePrintBounds(), Document.Printer.BuildVolume.Z,
            reset: true);
        PrintSettings.Refresh();
        RefreshPrinterOptions(notifyDocument: false);
        SupportSettings.Resins.Refresh();
        SelectedObject = null;
        LastSlice = null;
        SliceWarning = null;
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
    /// The Z range the clip slider spans: the combined bounding box of the models on the plate,
    /// hidden ones excluded, recomputed from the live transforms every time the document changes,
    /// so moving a model in Layout moves the numbers with it.
    ///
    /// <para>Supports are deliberately NOT included. They reach down to the plate and out beyond
    /// the models, so unioning them stretched the range past anything the user could point at and
    /// made the layer numbers unrelatable to the models they were reading. Nothing disappears as
    /// a result: a range sitting at both extremes does not clip at all
    /// (<see cref="ViewportClipRange.IsClipping"/>), so supports outside the model range are
    /// hidden only once the user actually drags a handle — which is what the tool is for.</para>
    /// </summary>
    private Aabb VisiblePrintBounds() => Document.Scene.WorldBounds;

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

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Duplicate()
    {
        var count = Document.DuplicateSelection().Count;
        if (count > 0) ViewportStatus = count == 1 ? "Duplicated object." : $"Duplicated {count} objects.";
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void MirrorX() => MirrorSelection(ObjectMirrorAxis.X);

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void MirrorY() => MirrorSelection(ObjectMirrorAxis.Y);

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void MirrorZ() => MirrorSelection(ObjectMirrorAxis.Z);

    private void MirrorSelection(ObjectMirrorAxis axis)
    {
        Document.MirrorSelection(axis);
        ViewportStatus = $"Mirrored selection on {axis}.";
    }

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
    private Task GenerateSupports() => GenerateSupportsCore(SupportGenerationScope.Full);

    [RelayCommand(CanExecute = nameof(CanGenerateSupports))]
    private Task GenerateIslandSupports() => GenerateSupportsCore(SupportGenerationScope.IslandsOnly);

    private async Task GenerateSupportsCore(SupportGenerationScope scope)
    {
        if (IsGeneratingSupports) return;
        var obj = SelectedObject;
        if (obj is null) return;
        var request = Document.CaptureSupportGeneration(obj, seed: 0, scope);
        _generationCancellation = new CancellationTokenSource();
        var token = _generationCancellation.Token;
        SupportGenerationBatch? batch = null;
        IsGeneratingSupports = true;
        GenerationProgress = 0;
        GenerateSupportsCommand.NotifyCanExecuteChanged();
        GenerateIslandSupportsCommand.NotifyCanExecuteChanged();
        var label = scope == SupportGenerationScope.IslandsOnly ? "island supports" : "supports";
        ViewportStatus = $"Generating {label}…";
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
                ? $"Generate {label}: no support tips were needed."
                : $"Generate {label}: {result.GeneratedTipCount} tips added, {result.UnroutedTipCount} unrouted.";
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
            GenerateIslandSupportsCommand.NotifyCanExecuteChanged();
        }
    }

    private bool CanGenerateSupports() => SelectedObject is not null && !IsGeneratingSupports;

    [RelayCommand(CanExecute = nameof(CanDetectIslands))]
    private async Task DetectIslands()
    {
        if (IsDetectingIslands || SelectedObject is not { } obj) return;
        IsDetectingIslands = true;
        DetectIslandsCommand.NotifyCanExecuteChanged();
        ViewportStatus = "Detecting islands…";
        try
        {
            var request = Document.CaptureIslandDetection(obj);
            DetectedIslands = await Task.Run(() => Document.ComputeIslandDetection(request));
            ViewportStatus = DetectedIslands.Count == 0
                ? "Island detection: no unsupported islands."
                : $"Island detection: {DetectedIslands.Count} unsupported islands.";
        }
        catch (Exception ex)
        {
            DetectedIslands = [];
            ViewportStatus = $"Island detection failed: {ex.Message}";
        }
        finally
        {
            IsDetectingIslands = false;
            DetectIslandsCommand.NotifyCanExecuteChanged();
        }
    }

    private bool CanDetectIslands() => SelectedObject is not null && !IsDetectingIslands;

    [RelayCommand]
    private void ClearIslandDetection()
    {
        DetectedIslands = [];
        ViewportStatus = "Island markers cleared.";
    }

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
            // The support target stays selected: select-all takes supports, not the model.
            Document.SelectSupportElements(SupportDisplayPolicy.DisplayedElementIds(
                Document.Supports, EffectiveSupportDisplay, ViewportClipRange));
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
                Document.Supports, resin, allowOutOfBounds: true), token);
            LastSlice = result;
            SliceWarning = result.BuildVolumeWarning;
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
            _lastExportPath = System.IO.Path.GetFullPath(path);
            RefreshUvtoolsAvailability();
            ViewportStatus = $"Exported {System.IO.Path.GetFileName(path)}: {result.LayerCount} layers, {result.VolumeMl:0.##} ml.";
            return true;
        }
        catch (Exception ex)
        {
            ViewportStatus = $"Export failed: {ex.Message}";
            return false;
        }
    }

    public void RefreshUvtoolsAvailability()
    {
        var availability = UvtoolsLauncher.GetAvailability(_lastExportPath);
        if (availability == _uvtoolsLaunchAvailability) return;
        _uvtoolsLaunchAvailability = availability;
        OnPropertyChanged(nameof(CanCheckWithUvtools));
        OnPropertyChanged(nameof(UvtoolsCheckTooltip));
    }

    /// <summary>Drops a stale slice and returns to the model view.</summary>
    public void InvalidateSlice()
    {
        LastSlice = null;
        SliceWarning = null;
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
