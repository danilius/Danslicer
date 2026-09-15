using System.Diagnostics;
using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.OpenGL;
using Avalonia.OpenGL.Controls;
using Avalonia.Threading;
using Danslicer.App.Editing;
using Danslicer.App.Input;
using Danslicer.App.ViewModels;
using Danslicer.Core;
using Danslicer.Core.Commands;
using Danslicer.Core.Config;
using Danslicer.Core.Geometry;
using Danslicer.Core.Scene;
using Danslicer.Core.Slicing;
using Danslicer.Core.Supports;
using Danslicer.Core.Supports.Rafts;
using Danslicer.Core.Supports.Generation;
using Danslicer.Render;

namespace Danslicer.App.Controls;

/// <summary>
/// The 3D viewport. Owns the camera, the renderer, the transform gizmo and the modal transform tool,
/// and translates pointer and keyboard input into camera navigation, selection and transforms.
/// </summary>
public sealed partial class ViewportControl : OpenGlControlBase
{
    public static readonly StyledProperty<Document?> DocumentProperty =
        AvaloniaProperty.Register<ViewportControl, Document?>(nameof(Document));

    public static readonly StyledProperty<string> StatusTextProperty =
        AvaloniaProperty.Register<ViewportControl, string>(nameof(StatusText), "");

    public static readonly StyledProperty<bool> ShowMoveGizmoProperty =
        AvaloniaProperty.Register<ViewportControl, bool>(nameof(ShowMoveGizmo), true);

    public static readonly StyledProperty<bool> ShowRotateGizmoProperty =
        AvaloniaProperty.Register<ViewportControl, bool>(nameof(ShowRotateGizmo));

    public static readonly StyledProperty<bool> ShowScaleGizmoProperty =
        AvaloniaProperty.Register<ViewportControl, bool>(nameof(ShowScaleGizmo));

    public static readonly StyledProperty<bool> SnapEnabledProperty =
        AvaloniaProperty.Register<ViewportControl, bool>(nameof(SnapEnabled), defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    public static readonly StyledProperty<bool> ShowOverhangsProperty =
        AvaloniaProperty.Register<ViewportControl, bool>(nameof(ShowOverhangs));

    public static readonly StyledProperty<bool> SupportSelectionModeProperty =
        AvaloniaProperty.Register<ViewportControl, bool>(nameof(SupportSelectionMode));

    public static readonly StyledProperty<bool> RegionPickModeProperty =
        AvaloniaProperty.Register<ViewportControl, bool>(nameof(RegionPickMode));

    public static readonly StyledProperty<bool> RegionBrushModeProperty =
        AvaloniaProperty.Register<ViewportControl, bool>(nameof(RegionBrushMode));

    public static readonly StyledProperty<double> RegionBrushRadiusPixelsProperty =
        AvaloniaProperty.Register<ViewportControl, double>(nameof(RegionBrushRadiusPixels), 24d);

    public static readonly StyledProperty<IReadOnlySet<int>?> RegionHoverFacesProperty =
        AvaloniaProperty.Register<ViewportControl, IReadOnlySet<int>?>(nameof(RegionHoverFaces));

    public static readonly StyledProperty<SceneObject?> RegionHoverObjectProperty =
        AvaloniaProperty.Register<ViewportControl, SceneObject?>(nameof(RegionHoverObject));

    public static readonly StyledProperty<bool> SelectThroughSupportsProperty =
        AvaloniaProperty.Register<ViewportControl, bool>(nameof(SelectThroughSupports));

    public static readonly StyledProperty<SupportDisplayConfig> SupportDisplayProperty =
        AvaloniaProperty.Register<ViewportControl, SupportDisplayConfig>(nameof(SupportDisplay),
            new SupportDisplayConfig());

    public static readonly StyledProperty<ViewportClipRange> ClipRangeProperty =
        AvaloniaProperty.Register<ViewportControl, ViewportClipRange>(nameof(ClipRange));

    public static readonly StyledProperty<bool> CapInteriorProperty =
        AvaloniaProperty.Register<ViewportControl, bool>(nameof(CapInterior), true);

    public static readonly StyledProperty<ClipCapStyle> CapStyleProperty =
        AvaloniaProperty.Register<ViewportControl, ClipCapStyle>(nameof(CapStyle), ClipCapStyle.Sliced);

    public static readonly StyledProperty<bool> ClipDraggingProperty =
        AvaloniaProperty.Register<ViewportControl, bool>(nameof(ClipDragging));

    public static readonly StyledProperty<HoverWaterlineViewModel?> SupportWaterlineProperty =
        AvaloniaProperty.Register<ViewportControl, HoverWaterlineViewModel?>(nameof(SupportWaterline));

    public static readonly StyledProperty<IReadOnlyList<DetectedIsland>> IslandMarkersProperty =
        AvaloniaProperty.Register<ViewportControl, IReadOnlyList<DetectedIsland>>(
            nameof(IslandMarkers), Array.Empty<DetectedIsland>());

    /// <summary>The live marquee rectangle in viewport coordinates; null when no drag is active.
    /// Drawn by a sibling overlay control, above the GL composition surface.</summary>
    public static readonly StyledProperty<Rect?> MarqueeRectProperty =
        AvaloniaProperty.Register<ViewportControl, Rect?>(nameof(MarqueeRect));

    private static readonly bool Trace = Environment.GetEnvironmentVariable("DANSLICER_TRACE") == "1";
    private static void Log(string message)
    {
        if (!Trace) return;
        Console.Error.WriteLine($"[viewport] {message}");
        Danslicer.Core.Diagnostics.CrashLog.Write($"[viewport] {message}");
    }

    private SceneRenderer? _renderer;
    private ModalTransform? _modal;
    private Document? _subscribed;
    private HoverWaterlineViewModel? _subscribedWaterline;
    // A Layout-mode click awaiting ID-buffer resolution on the next rendered frame (design 6.5).
    private (Vector2 Mouse, bool Additive)? _pendingGpuPick;
    private int _viewCubeHover = -1;
    /// <summary>A press on the cube, still deciding whether it is a snap-click or a drag-orbit.</summary>
    private ViewCubeDragGesture? _viewCubeDrag;

    private int HitViewCube(Point pos)
    {
        if (!Configuration.AppConfig.Current.Viewport.ViewCubeEnabled) return -1;
        var scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0;
        return ViewCube.HitRegion((float)(pos.X * scaling), (float)(pos.Y * scaling),
            (int)(Bounds.Width * scaling), (int)(Bounds.Height * scaling), scaling, Camera.View,
            Configuration.AppConfig.Current.Viewport.ViewCubeSizePixels);
    }
    private readonly Gizmo _gizmo = new();
    private Point _lastPointer;
    private bool _orbiting;
    private bool _panning;
    private bool _gizmoDragging;
    private bool _ctrlHeld;
    private readonly List<OverlayLine> _overlay = new();
    private readonly List<OverlayLine> _depthOverlay = new();
    private readonly List<SupportMeshBatch> _supportMeshes = new();
    private bool _supportMeshesDirty = true;
    private ISixAxisInput? _sixAxis;
    private bool _sixAxisFramesRunning;
    private int _sixAxisFrameGeneration;
    private readonly SixAxisMotionTiming _sixAxisTiming = new();
    private readonly SixAxisMotionFilter _sixAxisFilter = new();
    private Point? _marqueeStart;
    private Point _marqueeCurrent;
    private bool _marqueeAdditive;
    private bool _borderSelectArmed;
    /// <summary>A support element under the button-down point: selected on a click-release,
    /// abandoned once the drag becomes a marquee.</summary>
    private Guid? _pendingClickSupport;
    private int _pendingClickCount;
    private bool _selectionMeshDirty = true;
    private bool _regionOverlayDirty = true;
    private readonly List<AuxMeshDraw> _regionOverlays = new();
    private readonly List<AuxMeshDraw> _combinedAuxMeshes = new();
    private readonly List<AuxMeshDraw> _clipCaps = new();
    private readonly Dictionary<SceneObject, (Matrix4x4 World, MeshSlicer.PreparedMesh Mesh)>
        _preparedClipMeshes = new();
    private bool _clipCapsDirty = true;
    private Mesh? _selectedSupportMesh;
    private Mesh? _islandMarkerMesh;
    private const float MarqueeClickThresholdPixels = 3f;
    private readonly record struct SupportMeshBatch(AuxMeshDraw Draw, Vector3 SortOrigin);

    public Camera Camera { get; } = new();

    /// <summary>Raised on Tab so the host can switch between the model and layer views.</summary>
    public event Action? ToggleViewRequested;

    /// <summary>Redraw on demand, e.g. after a config change that affects rendering.</summary>
    public void RequestRedraw() => Redraw();

    public Document? Document
    {
        get => GetValue(DocumentProperty);
        set => SetValue(DocumentProperty, value);
    }

    /// <summary>Live text for the status bar: modal tool readout or navigation hints.</summary>
    public string StatusText
    {
        get => GetValue(StatusTextProperty);
        private set => SetValue(StatusTextProperty, value);
    }

    public bool ShowMoveGizmo { get => GetValue(ShowMoveGizmoProperty); set => SetValue(ShowMoveGizmoProperty, value); }
    public bool ShowRotateGizmo { get => GetValue(ShowRotateGizmoProperty); set => SetValue(ShowRotateGizmoProperty, value); }
    public bool ShowScaleGizmo { get => GetValue(ShowScaleGizmoProperty); set => SetValue(ShowScaleGizmoProperty, value); }
    public bool SnapEnabled { get => GetValue(SnapEnabledProperty); set => SetValue(SnapEnabledProperty, value); }
    public bool ShowOverhangs { get => GetValue(ShowOverhangsProperty); set => SetValue(ShowOverhangsProperty, value); }
    public bool SupportSelectionMode { get => GetValue(SupportSelectionModeProperty); set => SetValue(SupportSelectionModeProperty, value); }

    /// <summary>
    /// While set, a left click in Support mode picks a mesh face for support-region painting and
    /// reports it through <see cref="RegionFacePicked"/> instead of selecting support elements.
    /// The control does no region work itself: what a picked face means — grow by dihedral, add,
    /// erase — belongs to the view model that owns the region edit.
    /// </summary>
    public bool RegionPickMode { get => GetValue(RegionPickModeProperty); set => SetValue(RegionPickModeProperty, value); }

    /// <summary>
    /// With <see cref="RegionPickMode"/>, a press-and-drag paints with the brush instead of
    /// growing a patch from a single click. The two are different tools over the same region.
    /// </summary>
    public bool RegionBrushMode { get => GetValue(RegionBrushModeProperty); set => SetValue(RegionBrushModeProperty, value); }

    /// <summary>
    /// Brush radius in SCREEN pixels. A brush is aimed with the eye, so it should stay the size
    /// it looks wherever the camera is: a millimetre radius grows and shrinks as you zoom, which
    /// is exactly what a painting tool must not do. The control converts to world units at the
    /// point being painted, since only it knows the camera.
    /// </summary>
    public double RegionBrushRadiusPixels
    {
        get => GetValue(RegionBrushRadiusPixelsProperty);
        set => SetValue(RegionBrushRadiusPixelsProperty, value);
    }

    /// <summary>The faces a region click would paint, highlighted under the cursor.</summary>
    public IReadOnlySet<int>? RegionHoverFaces
    {
        get => GetValue(RegionHoverFacesProperty);
        set => SetValue(RegionHoverFacesProperty, value);
    }

    /// <summary>The object those faces belong to.</summary>
    public SceneObject? RegionHoverObject
    {
        get => GetValue(RegionHoverObjectProperty);
        set => SetValue(RegionHoverObjectProperty, value);
    }

    /// <summary>The face under the cursor while region painting is armed; -1 for none.</summary>
    public event Action<SceneObject?, int>? RegionFaceHovered;

    /// <summary>A brush stroke began on this object; the flag is true for an erasing stroke.</summary>
    public event Action<SceneObject, bool>? RegionStrokeStarted;

    /// <summary>
    /// One dab: a world-space surface point, the triangle under it, and the brush radius in world
    /// units at that point. The radius travels with the dab because it depends on where the point
    /// is relative to the camera, which changes as the stroke moves across the model.
    /// </summary>
    public event Action<Vector3, int, float>? RegionStrokeDab;

    /// <summary>The stroke ended and should be committed as one undo step.</summary>
    public event Action? RegionStrokeEnded;

    /// <summary>
    /// The object and triangle index under a region-painting click, and whether the click was an
    /// erase (Shift held) rather than an add. The modifier is read here because this is where the
    /// pointer event is: a listener has no way to ask what was held at the time.
    /// </summary>
    public event Action<SceneObject, int, bool>? RegionFacePicked;
    public bool SelectThroughSupports { get => GetValue(SelectThroughSupportsProperty); set => SetValue(SelectThroughSupportsProperty, value); }
    public SupportDisplayConfig SupportDisplay { get => GetValue(SupportDisplayProperty); set => SetValue(SupportDisplayProperty, value); }
    public ViewportClipRange ClipRange { get => GetValue(ClipRangeProperty); set => SetValue(ClipRangeProperty, value); }
    public bool CapInterior { get => GetValue(CapInteriorProperty); set => SetValue(CapInteriorProperty, value); }
    public ClipCapStyle CapStyle { get => GetValue(CapStyleProperty); set => SetValue(CapStyleProperty, value); }
    public bool ClipDragging { get => GetValue(ClipDraggingProperty); set => SetValue(ClipDraggingProperty, value); }
    public HoverWaterlineViewModel? SupportWaterline { get => GetValue(SupportWaterlineProperty); set => SetValue(SupportWaterlineProperty, value); }
    public IReadOnlyList<DetectedIsland> IslandMarkers { get => GetValue(IslandMarkersProperty); set => SetValue(IslandMarkersProperty, value); }
    public Rect? MarqueeRect { get => GetValue(MarqueeRectProperty); private set => SetValue(MarqueeRectProperty, value); }

    public ViewportControl()
    {
        Focusable = true;
        ClipToBounds = true;
    }

    /// <summary>
    /// OpenGlControlBase draws through a composition surface, which Avalonia's hit testing ignores.
    /// Filling the bounds with a transparent brush makes the control receive pointer events.
    /// </summary>
    public override void Render(DrawingContext context)
    {
        context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));
        base.Render(context);
    }

    protected override void OnGotFocus(FocusChangedEventArgs e)
    {
        base.OnGotFocus(e);
        Log("got focus");
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == DocumentProperty)
        {
            if (_subscribed is not null)
            {
                _subscribed.Changed -= Redraw;
                _subscribed.Changed -= MarkClipCapsDirty;
                _subscribed.SelectionChanged -= Redraw;
                _subscribed.SelectionChanged -= MarkClipCapsDirty;
                _subscribed.SupportSelectionChanged -= Redraw;
                _subscribed.SupportSelectionChanged -= MarkSelectionMeshDirty;
                _subscribed.SupportSelectionChanged -= FollowSelectionInSupportEdit;
                _subscribed.Supports.Changed -= MarkSupportMeshesDirty;
                _subscribed.Changed -= MarkSupportMeshesDirty;
                _subscribed.Changed -= MarkRegionOverlayDirty;
            }
            _subscribed = Document;
            _modal = null;
            if (_subscribed is not null)
            {
                _subscribed.Changed += Redraw;
                _subscribed.Changed += MarkClipCapsDirty;
                _subscribed.SelectionChanged += Redraw;
                _subscribed.SelectionChanged += MarkClipCapsDirty;
                _subscribed.SupportSelectionChanged += Redraw;
                // Selection changes rebuild only the small selected-elements overlay; the full
                // graph mesh rebuilds only when the graph itself changes (a full rebuild froze
                // the app for seconds after a marquee selection on a generated forest).
                _subscribed.SupportSelectionChanged += MarkSelectionMeshDirty;
                _subscribed.SupportSelectionChanged += FollowSelectionInSupportEdit;
                _subscribed.Supports.Changed += MarkSupportMeshesDirty;
                // Hiding a model hides its supports, and that is an object change, not a graph
                // change — without this the supports stayed on screen until something else
                // happened to rebuild them (a mode switch, typically).
                _subscribed.Changed += MarkSupportMeshesDirty;
                // A region is object state, so it changes on paint, undo, project load and any
                // object move — all of which raise Document.Changed.
                _subscribed.Changed += MarkRegionOverlayDirty;
                _modal = new ModalTransform(_subscribed, Camera);
            }
            _supportMeshesDirty = true;
            _selectionMeshDirty = true;
            _regionOverlayDirty = true;
            _preparedClipMeshes.Clear();
            MarkClipCapsDirty();
            Redraw();
        }
        else if (change.Property == ShowMoveGizmoProperty || change.Property == ShowRotateGizmoProperty ||
                 change.Property == ShowScaleGizmoProperty)
        {
            _gizmo.ShowMove = ShowMoveGizmo;
            _gizmo.ShowRotate = ShowRotateGizmo;
            _gizmo.ShowScale = ShowScaleGizmo;
            Redraw();
        }
        else if (change.Property == SnapEnabledProperty)
        {
            ApplySnap();
            UpdateStatus();
        }
        else if (change.Property == RegionHoverFacesProperty ||
                 change.Property == RegionHoverObjectProperty)
        {
            MarkRegionOverlayDirty();
        }
        else if (change.Property == SupportSelectionModeProperty)
        {
            if (SupportSelectionMode && _layFlatPick) EndLayFlatPick();
            if (SupportWaterline is { } waterline)
                waterline.SupportModeActive = SupportSelectionMode;
            // A guided gesture is Support-mode only; leaving the mode abandons it.
            if (!SupportSelectionMode && _lineGesture is not null) CancelLineGesture();
            if (!SupportSelectionMode && _editSupport is not null) EndSupportEdit();
            if (!SupportSelectionMode && _placementMode) EndPlacementMode();
            if (!SupportSelectionMode && _manualBraceMode) EndManualBrace();
            _supportMeshesDirty = true;
            // The region overlay is Support-mode only, so a mode change rebuilds it too.
            MarkRegionOverlayDirty();
            UpdateStatus();
            Redraw();
        }
        else if (change.Property == SupportWaterlineProperty)
        {
            if (_subscribedWaterline is not null)
                _subscribedWaterline.Changed -= OnWaterlineChanged;
            _subscribedWaterline = SupportWaterline;
            if (_subscribedWaterline is not null)
            {
                _subscribedWaterline.SupportModeActive = SupportSelectionMode;
                _subscribedWaterline.Changed += OnWaterlineChanged;
            }
            UpdateStatus();
            Redraw();
        }
        else if (change.Property == IslandMarkersProperty)
        {
            var builder = new MeshBuilder();
            foreach (var marker in IslandMarkers)
                SupportRenderMesh.AppendSphere(builder, marker.Position, marker.MarkerRadiusMm);
            _islandMarkerMesh = IslandMarkers.Count == 0 ? null : builder.ToMesh();
            Redraw();
        }
        else if (change.Property == SupportDisplayProperty)
        {
            _supportMeshesDirty = true;
            _selectionMeshDirty = true;
            MarkClipCapsDirty();
            if (Document is { } document)
            {
                var retained = document.SupportSelection
                    .Where(id => SupportDisplayPolicy.IsElementDisplayed(
                        document.Supports, id, SupportDisplay)).ToList();
                if (retained.Count != document.SupportSelection.Count)
                    document.SelectSupportElements(retained);
            }
            Redraw();
        }
        else if (change.Property == ClipRangeProperty)
        {
            if (Document is { } document)
            {
                var retained = document.SupportSelection
                    .Where(id => SupportDisplayPolicy.IsElementDisplayed(
                        document.Supports, id, SupportDisplay, ClipRange)).ToList();
                if (retained.Count != document.SupportSelection.Count)
                    document.SelectSupportElements(retained);
            }
            // Main/support meshes remain shader-only. Exact slice caps are throttled separately.
            QueueClipCapRangeRebuild();
            Redraw();
        }
        else if (change.Property == CapInteriorProperty || change.Property == CapStyleProperty)
        {
            _clipCapsDirty = true;
            RebuildClipCaps();
            Redraw();
        }
        else if (change.Property == ClipDraggingProperty && !ClipDragging)
        {
            // Pointer release always gets one exact final rebuild. Full-resolution Drogon caps
            // measured 22.9-27.3 ms at dense sections, so drag-time rebuilds are intentionally
            // release-only rather than taking longer than a 60 Hz frame.
            _clipCapsDirty = true;
            RebuildClipCaps();
            Redraw();
        }
        else if (change.Property == ShowOverhangsProperty)
        {
            Redraw();
        }
    }

    // The marquee lives in a sibling overlay control (MainWindow) because the GL composition
    // surface draws over this control's own 2D layer: rectangles painted in Render() are
    // invisible behind it, which is why the earlier InvalidateVisual fix changed nothing.
    private void Redraw()
    {
        if (Dispatcher.UIThread.CheckAccess()) RequestNextFrameRendering();
        else Dispatcher.UIThread.Post(RequestNextFrameRendering);
    }

    private void MarkClipCapsDirty() => _clipCapsDirty = true;

    private bool ShouldBuildSlicedCaps => Document is not null && ClipCapPolicy.ShouldBuildExactCaps(
        CapInterior, CapStyle, Configuration.AppConfig.Current.Viewport.RenderPath, ClipRange.IsClipping);

    /// <summary>
    /// Render-path menu/pop-out toggles change <c>AppConfig</c> directly rather than through an
    /// Avalonia property, so they cannot trigger <see cref="OnPropertyChanged"/>; the host calls
    /// this after flipping the setting so a Painted cap style re-resolves against the new path
    /// (exact CPU caps on Classic, screen-space caps on Deferred).
    /// </summary>
    public void NotifyRenderPathChanged()
    {
        _clipCapsDirty = true;
        RebuildClipCaps();
        Redraw();
    }

    private void QueueClipCapRangeRebuild()
    {
        _clipCapsDirty = true;
        if (!ShouldBuildSlicedCaps)
        {
            _clipCaps.Clear();
            _clipCapsDirty = false;
            return;
        }
        // Exact mesh slicing is deliberately release-only on slider drags (see timing above).
        // Numeric-field edits are not drags and therefore rebuild immediately.
        if (!ClipDragging) RebuildClipCaps();
    }

    private void RebuildClipCaps()
    {
        _clipCaps.Clear();
        _clipCapsDirty = false;
        if (!ShouldBuildSlicedCaps || Document is not { } document) return;

        var planes = ActiveClipPlanes().ToArray();
        var liveObjects = document.Scene.Objects.ToHashSet();
        foreach (var stale in _preparedClipMeshes.Keys.Where(obj => !liveObjects.Contains(obj)).ToList())
            _preparedClipMeshes.Remove(stale);

        var objectColor = new Vector3(0.70f, 0.71f, 0.74f);
        var selectedColor = new Vector3(0.96f, 0.60f, 0.18f);
        foreach (var obj in document.Scene.Objects)
        {
            if (obj.RenderState == RenderState.Hidden) continue;
            var world = obj.Transform.ToMatrix();
            if (!_preparedClipMeshes.TryGetValue(obj, out var cached) || cached.World != world)
            {
                cached = (world, new MeshSlicer.PreparedMesh(obj.Mesh, world));
                _preparedClipMeshes[obj] = cached;
            }
            var color = document.IsSelected(obj) ? selectedColor
                : obj.RenderState == RenderState.Highlighted
                    ? Vector3.Lerp(objectColor, selectedColor, 0.4f)
                    : objectColor;
            var opacity = obj.RenderState == RenderState.Ghosted ? 0.25f : 1f;
            foreach (var (z, face) in planes)
                AddCap(ClipCapBuilder.Build(cached.Mesh, z, face), color, opacity);
        }

        if (!SupportDisplayPolicy.ShowsMeshes(SupportDisplay)) return;
        foreach (var (z, face) in planes) AddSupportCaps(_structurePreviewGraph ?? document.Supports, z, face);
    }

    private IEnumerable<(double Z, ClipCapFace Face)> ActiveClipPlanes() => ClipRange.ActiveCapPlanes()
        .Select(plane => ((double)plane.Z, plane.Upper ? ClipCapFace.Upper : ClipCapFace.Lower));

    private void AddSupportCaps(SupportGraph graph, double z, ClipCapFace face)
    {
        var opacity = SupportDisplay.Mode == SupportDisplayMode.Transparent
            ? TransparentSupportOpacity
            : 1f;
        AddCategory(SupportSegmentType.Tip, TipColor, SupportNodeType.Tip);
        AddCategory(SupportSegmentType.Branch, BranchColor);
        AddCategory(SupportSegmentType.Trunk, TrunkColor);
        AddCategory(SupportSegmentType.Bracing, BracingColor);
        AddCategory(null, BaseColor, SupportNodeType.Base);
        if (Document is not null && SupportDisplayPolicy.ShowsRafts(SupportDisplay))
        {
            var raftSections = new Clipper2Lib.Paths64();
            foreach (var obj in Document.Scene.Objects)
            {
                if (obj.RenderState == RenderState.Hidden || obj.Raft is not { } parameters) continue;
                var outline = Document.RaftTopOutline(obj);
                if (outline is null) continue;
                raftSections.AddRange(RaftBuilder.SectionAt(outline, parameters, z));
            }
            if (raftSections.Count > 0)
                AddCap(ClipCapBuilder.Build(raftSections, z, face),
                    new Vector3(BaseColor.X, BaseColor.Y, BaseColor.Z), opacity);
        }
        return;

        void AddCategory(SupportSegmentType? segmentType, Vector4 rgba,
            SupportNodeType? nodeType = null)
        {
            var sections = SupportSliceGeometry.SectionsAt(graph, z,
                includeSegment: segment => segmentType == segment.Type &&
                    !SupportDisplayPolicy.IsHiddenBy(segment.Hidden, SupportDisplay) &&
                    !SupportDisplayPolicy.IsHiddenBy(graph.GetNode(segment.NodeA).Hidden, SupportDisplay) &&
                    !SupportDisplayPolicy.IsHiddenBy(graph.GetNode(segment.NodeB).Hidden, SupportDisplay) &&
                    SupportDisplayPolicy.IsSegmentDisplayed(segment.Type, SupportDisplay),
                includeNode: node => nodeType == node.Type &&
                    !SupportDisplayPolicy.IsHiddenBy(node.Hidden, SupportDisplay) &&
                    NodeBelongsToCategory(node, segmentType));
            AddCap(ClipCapBuilder.Build(sections, z, face),
                new Vector3(rgba.X, rgba.Y, rgba.Z), opacity);
        }

        bool NodeBelongsToCategory(SupportNode node, SupportSegmentType? segmentType)
        {
            if (node.Type == SupportNodeType.Base)
                return SupportDisplay.Mode is SupportDisplayMode.Full or SupportDisplayMode.Transparent &&
                    SupportDisplay.ShowBases;
            if (node.Type != SupportNodeType.Tip || segmentType is null) return false;
            var incident = graph.SegmentsAt(node.Id);
            return segmentType == SupportSegmentType.Tip
                ? (incident.Count == 0 && SupportDisplay.ShowTips ||
                   incident.Any(s => s.Type == SupportSegmentType.Tip &&
                       SupportDisplayPolicy.IsSegmentDisplayed(s.Type, SupportDisplay)))
                : incident.Any(s => s.Type == segmentType &&
                    SupportDisplayPolicy.IsSegmentDisplayed(s.Type, SupportDisplay));
        }
    }

    private void AddCap(Mesh? mesh, Vector3 color, float opacity)
    {
        if (mesh is not null) _clipCaps.Add(new AuxMeshDraw(mesh, color, opacity));
    }

    // ----- OpenGL lifecycle -----

    protected override void OnOpenGlInit(GlInterface gl)
    {
        _renderer = new SceneRenderer(gl.GetProcAddress);
    }

    protected override void OnOpenGlDeinit(GlInterface gl)
    {
        _renderer?.Dispose();
        _renderer = null;
    }

    protected override void OnOpenGlRender(GlInterface gl, int fb)
    {
        if (_renderer is null || Document is null) return;
        var renderStart = Trace || SpaceMouseDiagnostics.Enabled ? Stopwatch.GetTimestamp() : 0;
        try { RenderFrameCore(gl, fb); }
        finally
        {
            if (Trace)
                _traceRenderMaxMs = Math.Max(_traceRenderMaxMs, Stopwatch.GetElapsedTime(renderStart).TotalMilliseconds);
            if (SpaceMouseDiagnostics.Enabled)
                SpaceMouseDiagnostics.Write("RENDER", $"viewport={GetHashCode()} durationMs={Stopwatch.GetElapsedTime(renderStart).TotalMilliseconds:F3} yaw={Camera.Yaw:F6} pitch={Camera.Pitch:F6} distance={Camera.Distance:F4} target={Camera.Target} gc={GC.CollectionCount(0)},{GC.CollectionCount(1)},{GC.CollectionCount(2)}");
        }
    }

    private void RenderFrameCore(GlInterface gl, int fb)
    {
        if (_renderer is null || Document is null) return;
        var scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0;
        var width = Math.Max(1, (int)(Bounds.Width * scaling));
        var height = Math.Max(1, (int)(Bounds.Height * scaling));

        _overlay.Clear();
        _depthOverlay.Clear();
        if (_supportMeshesDirty) RebuildSupportMeshes();
        if (_selectionMeshDirty) RebuildSelectionMesh();
        if (_regionOverlayDirty) RebuildRegionOverlays();
        if (_clipCapsDirty && !ClipDragging) RebuildClipCaps();
        _combinedAuxMeshes.Clear();
        IEnumerable<SupportMeshBatch> supportBatches = SupportDisplay.Mode == SupportDisplayMode.Transparent
            ? _supportMeshes.OrderByDescending(batch =>
                Vector3.DistanceSquared(Camera.Eye, batch.SortOrigin))
            : _supportMeshes;
        foreach (var batch in supportBatches) _combinedAuxMeshes.Add(batch.Draw);
        _combinedAuxMeshes.AddRange(_placementGhost);
        _combinedAuxMeshes.AddRange(_regionOverlays);
        // Exact caps belong to the previous plane until release; show open clipped surfaces
        // during the simplified preview instead of drawing stale geometry at the old height.
        if (!ClipDragging) _combinedAuxMeshes.AddRange(_clipCaps);
        if (_islandMarkerMesh is { } markers)
            _combinedAuxMeshes.Add(new AuxMeshDraw(markers, new Vector3(1f, 0.03f, 0.03f), 1f));
        if (_selectedSupportMesh is { } selected)
            _combinedAuxMeshes.Add(new AuxMeshDraw(selected,
                _structurePreviewGraph is null ? new Vector3(SupportSelectedColor.X, SupportSelectedColor.Y, SupportSelectedColor.Z) : new Vector3(1f, 0.45f, 0.08f),
                1f, DepthOverlay: true));
        AppendSupportLines(_depthOverlay);
        AppendBrushCursor(_overlay);
        AppendBaseGridMarkers(_depthOverlay);
        AppendLineGesture(_depthOverlay, _overlay);
        AppendSupportEditHandles(_overlay);
        if (_modal is { IsActive: true }) _overlay.AddRange(_modal.OverlayLines);
        if (!SupportSelectionMode) UpdateGizmo();
        // Hide the gizmo during keyboard-driven modals; keep it while dragging a handle.
        if (!SupportSelectionMode && (_modal is not { IsActive: true } || _gizmoDragging))
            _gizmo.AppendLines(Camera, _overlay);

        _renderer.Render(new RenderFrame
        {
            Framebuffer = fb,
            Width = width,
            Height = height,
            Camera = Camera,
            Scene = Document.Scene,
            IsSelected = Document.IsSelected,
            Printer = Document.Printer,
            Overlay = _overlay,
            DepthOverlay = _depthOverlay,
            AuxMeshes = _combinedAuxMeshes,
            ShowOverhangs = ShowOverhangs,
            OverhangAngleDegrees = Configuration.AppConfig.Current.Viewport.OverhangAngleDegrees,
            PlateOpacityFromBelow = Configuration.AppConfig.Current.Viewport.PlateOpacityFromBelow,
            ShowPlateShadows = Configuration.AppConfig.Current.Viewport.PlateShadowsEnabled,
            PlateReflectionsEnabled = Configuration.AppConfig.Current.Viewport.PlateReflectionsEnabled,
            PlateReflectionStrength = Configuration.AppConfig.Current.Viewport.PlateReflectionStrength,
            OverhangColorA = Configuration.AppConfig.ParseColor(
                Configuration.AppConfig.Current.Viewport.OverhangColorA, new Vector3(0.98f, 0.80f, 0.15f)),
            OverhangColorB = Configuration.AppConfig.ParseColor(
                Configuration.AppConfig.Current.Viewport.OverhangColorB, new Vector3(0.90f, 0.12f, 0.10f)),
            OverhangCheckerSizeMm = Configuration.AppConfig.Current.Viewport.OverhangCheckerSizeMm,
            RenderPath = Configuration.AppConfig.Current.Viewport.RenderPath,
            Shadows = ShadowEffects.FromConfig(Configuration.AppConfig.Current.Viewport),
            Deferred = DeferredEffects.FromConfig(Configuration.AppConfig.Current.Viewport),
            ClipRange = ClipRange,
            CapInterior = CapInterior,
            CapStyle = CapStyle,
            WaterlineZ = SupportWaterline?.WorldZ,
            WireframeEnabled = Configuration.AppConfig.Current.Viewport.WireframeEnabled,
            ShowViewCube = Configuration.AppConfig.Current.Viewport.ViewCubeEnabled,
            ViewCubeHover = _viewCubeHover,
            ViewCubeSizePixels = Configuration.AppConfig.Current.Viewport.ViewCubeSizePixels,
            RenderScaling = scaling,
        });

        if (_pendingGpuPick is { } pick)
        {
            _pendingGpuPick = null;
            // Control DIPs to framebuffer pixels; the ID buffer's origin is bottom-left.
            var px = (int)(pick.Mouse.X * scaling);
            var py = height - 1 - (int)(pick.Mouse.Y * scaling);
            if (_renderer.TryPickObject(px, py, out var hit))
                // Support geometry is not in the ID buffer, so a click that lands on a support
                // reads as empty space there. Ask the CPU pick before believing it.
                ApplyObjectClick(hit ?? SupportOwnerAt(pick.Mouse, float.PositiveInfinity), pick.Additive);
            else
                // The frame fell back to the classic path; pick the CPU way instead.
                ApplyObjectClick(PickObject(pick.Mouse) ??
                    SupportOwnerAt(pick.Mouse, float.PositiveInfinity), pick.Additive);
        }
    }

    private void OnWaterlineChanged()
    {
        UpdateStatus();
        Redraw();
    }

    private void UpdateGizmo()
    {
        if (Document is null) return;
        var bounds = Aabb.Empty;
        foreach (var o in Document.Selection) bounds = bounds.Union(o.WorldBounds);
        _gizmo.Update(Camera, bounds, (float)Bounds.Height);
    }

    // ----- Camera commands -----

    public void FrameAll()
    {
        if (Document is null) return;
        var bounds = Document.Scene.WorldBounds;
        if (bounds.IsEmpty)
        {
            var v = Document.Printer.BuildVolume;
            bounds = new Aabb(new Vector3(-v.X / 2, -v.Y / 2, 0), new Vector3(v.X / 2, v.Y / 2, v.Z * 0.3f));
        }
        Camera.Frame(bounds);
        Redraw();
    }

    public void FrameSelected()
    {
        if (Document is null || Document.Selection.Count == 0) { FrameAll(); return; }
        var bounds = Aabb.Empty;
        foreach (var o in Document.Selection) bounds = bounds.Union(o.WorldBounds);
        Camera.Frame(bounds);
        Redraw();
    }

    public void SetView(Action<Camera> view)
    {
        view(Camera);
        Redraw();
    }

    public void FocusPoint(Vector3 point)
    {
        Camera.Target = point;
        Redraw();
        Focus();
    }

    public void ToggleProjection()
    {
        Camera.Orthographic = !Camera.Orthographic;
        UpdateStatus();
        Redraw();
    }

    // ----- Pointer input -----

    private Vector2 MouseVector(PointerEventArgs e)
    {
        var p = e.GetPosition(this);
        return new Vector2((float)p.X, (float)p.Y);
    }

    protected override void OnPointerEntered(PointerEventArgs e)
    {
        base.OnPointerEntered(e);
        Log("pointer entered");
        Focus();
        if (_layFlatPick) UpdateLayFlatHover(MouseVector(e));
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        ClearLayFlatHover();
        SupportWaterline?.Clear();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Log($"pointer pressed {e.GetPosition(this)} focused={IsFocused}");
        Focus();
        var props = e.GetCurrentPoint(this).Properties;
        _lastPointer = e.GetPosition(this);

        if (_layFlatPick && props.IsRightButtonPressed)
        {
            EndLayFlatPick();
            e.Handled = true;
            return;
        }

        if (props.IsMiddleButtonPressed)
        {
            // A middle-button press takes over outright, including from a cube drag still holding
            // the pointer — otherwise the cube branch would keep swallowing moves and leave
            // _orbiting latched on after the release.
            EndViewCubeDrag();
            if (e.KeyModifiers.HasFlag(KeyModifiers.Shift)) _panning = true;
            else _orbiting = true;
            e.Pointer.Capture(this);
            e.Handled = true;
            return;
        }

        if (StructurePreviewActive)
        {
            if (props.IsLeftButtonPressed) InspectStructurePreviewAt(MouseVector(e));
            e.Handled = true; return;
        }

        if (_manualBraceMode)
        {
            UpdateManualBrace(MouseVector(e));
            if (props.IsLeftButtonPressed) ClickManualBrace();
            else if (props.IsRightButtonPressed) EndManualBrace();
            e.Handled = true;
            return;
        }

        if (_lineGesture is not null)
        {
            // A click adds a vertex; the second click of a double-click places the line.
            if (props.IsLeftButtonPressed)
            {
                UpdateLineGestureCursor(MouseVector(e));
                if (_lineGesture.ReadyToPlace)
                {
                    if (_lineGesture.AddVertex()) CommitLineGesture();
                }
                else if (e.ClickCount >= 2) CommitLineGesture();
                else LineGestureAddVertex(MouseVector(e));
            }
            else if (props.IsRightButtonPressed) CancelLineGesture();
            e.Handled = true;
            return;
        }

        if (_tipDrag is not null)
        {
            if (props.IsLeftButtonPressed) CommitTipDrag();
            else if (props.IsRightButtonPressed) CancelTipDrag();
            e.Handled = true;
            return;
        }

        if (_placementMode)
        {
            if (props.IsLeftButtonPressed)
            {
                var placed = TryAddSupport(MouseVector(e));
                // The graph changed under the ghost: drop it until the cursor moves again.
                ClearPlacementGhost();
                UpdateStatus();
                if (placed is not null) StatusText = placed;
                Redraw();
            }
            else if (props.IsRightButtonPressed) EndPlacementMode();
            e.Handled = true;
            return;
        }

        if (_editDrag is not null)
        {
            // The left button is already down; any other press cancels the drag.
            CancelSupportEditDrag();
            e.Pointer.Capture(null);
            e.Handled = true;
            return;
        }
        if (_editSupport is not null && props.IsLeftButtonPressed &&
            HitSupportEditHandle(MouseVector(e)) is { } editHit)
        {
            BeginSupportEditDrag(editHit.Handle, editHit.Axis, MouseVector(e));
            e.Pointer.Capture(this);
            e.Handled = true;
            return;
        }

        if (_modal is { IsActive: true })
        {
            if (props.IsLeftButtonPressed) _modal.Confirm();
            else if (props.IsRightButtonPressed) _modal.Cancel();
            _gizmoDragging = false;
            UpdateStatus();
            e.Handled = true;
            return;
        }

        if (props.IsLeftButtonPressed && Document is not null && _modal is not null)
        {
            var m = MouseVector(e);

            // The view cube floats over everything, so it wins the press. Whether that press is a
            // snap-click or the start of a drag-orbit is not known yet — the gesture decides on
            // the first move past its threshold, and the snap happens on release if it never does.
            if (HitViewCube(_lastPointer) is var cubeRegion && cubeRegion >= 0)
            {
                _viewCubeDrag = ViewCubeDragGesture.Begin(cubeRegion, m.X, m.Y,
                    Configuration.AppConfig.Current.Viewport.ViewCubeSizePixels);
                e.Pointer.Capture(this);
                e.Handled = true;
                return;
            }

            if (!SupportSelectionMode && _layFlatPick)
            {
                EndLayFlatPick();
                TryLayFlat(m);
                UpdateStatus();
                e.Handled = true;
                return;
            }

            // Region painting takes the click before support selection does: while it is armed
            // the user is choosing faces, not support elements — and only on the support target
            // (PickSurface scopes to it). A click that misses is SWALLOWED rather than falling
            // through to selection, which would hand the target to whatever was clicked instead;
            // painting is a modal tool and must not change which model is being worked on.
            if (SupportSelectionMode && RegionPickMode)
            {
                if (RegionBrushMode)
                {
                    if (PickSurface(m, out var brushTriangle, out var brushPoint, out _) is { } brushHit &&
                        brushTriangle >= 0)
                    {
                        _brushing = true;
                        RegionStrokeStarted?.Invoke(brushHit, e.KeyModifiers.HasFlag(KeyModifiers.Shift));
                        RegionStrokeDab?.Invoke(brushPoint, brushTriangle, WorldRadiusAt(brushPoint));
                        e.Pointer.Capture(this);
                    }
                }
                else if (PickFace(m, out var regionTriangle) is { } regionHit && regionTriangle >= 0)
                {
                    RegionFacePicked?.Invoke(regionHit, regionTriangle,
                        e.KeyModifiers.HasFlag(KeyModifiers.Shift));
                }
                e.Handled = true;
                return;
            }

            if (SupportSelectionMode && _borderSelectArmed)
            {
                _borderSelectArmed = false;
                _marqueeStart = _lastPointer;
                _marqueeCurrent = _lastPointer;
                _marqueeAdditive = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
                _pendingClickSupport = null;
                e.Pointer.Capture(this);
                e.Handled = true;
                return;
            }

            // Gizmo handle: start a constrained modal that ends on release.
            UpdateGizmo();
            var handle = SupportSelectionMode ? GizmoHandle.None :
                _gizmo.HitTest(Camera, m, (float)Bounds.Width, (float)Bounds.Height);
            if (!SupportSelectionMode && handle != GizmoHandle.None && Document.Selection.Count > 0)
            {
                var (mode, axis, plane) = Gizmo.ToTransform(handle);
                ApplySnap(e.KeyModifiers);
                if (_modal.Begin(mode, m, (float)Bounds.Width, (float)Bounds.Height, axis, plane))
                {
                    _gizmo.Active = handle;
                    _gizmoDragging = true;
                    e.Pointer.Capture(this);
                    UpdateStatus();
                    e.Handled = true;
                    return;
                }
            }

            var additive = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
            if (!SupportSelectionMode && _renderer?.CanPickDeferred == true)
            {
                // Design 6.5: the ID buffer picks. GL readback needs the context, which is only
                // current during a render pass, so the click resolves on the next frame.
                _pendingGpuPick = (m, additive);
                Redraw();
                e.Handled = true;
                return;
            }

            var hitObj = PickSurface(m, out _, out var surfacePoint, out _);
            var objDistance = hitObj is null ? float.PositiveInfinity : Vector3.Distance(Camera.Eye, surfacePoint);
            if (!SupportSelectionMode && SupportOwnerAt(m, objDistance) is { } ownerByClick)
            {
                // Outside Support mode a model and its supports are one thing, so clicking any
                // part of the support selects the model it belongs to.
                ApplyObjectClick(ownerByClick, additive);
                e.Handled = true;
                return;
            }
            if (SupportSelectionMode)
            {
                // Every LMB press arms a marquee, wherever it starts (Blender box select);
                // a release inside the click threshold becomes the click instead. The support
                // under the button-down point is remembered for that click, with lines given
                // the tie against the surface right behind them because they are thin.
                var support = PickSupportElement(m, out var supportDistance);
                _pendingClickSupport =
                    support is { } element && supportDistance <= objDistance + 0.5f ? element : null;
                _pendingClickCount = e.ClickCount;
                _marqueeStart = _lastPointer;
                _marqueeCurrent = _lastPointer;
                _marqueeAdditive = additive;
                e.Pointer.Capture(this);
                e.Handled = true;
                return;
            }
            ApplyObjectClick(hitObj, additive);
            e.Handled = true;
        }
    }

    /// <summary>
    /// Drops the object selection, except in Support mode: there the selected model IS the
    /// support target, so nothing in the viewport — an empty click, a marquee, Esc — may drop
    /// it. Only the Objects pop-out retargets.
    /// </summary>
    private void ClearObjectSelection()
    {
        if (SupportSelectionMode) return;
        Document?.ClearSelection();
    }

    /// <summary>Object click-selection semantics, shared by the CPU and ID-buffer pick paths.</summary>
    private void ApplyObjectClick(SceneObject? hitObj, bool additive)
    {
        if (Document is null) return;
        if (hitObj is null)
        {
            if (!additive)
            {
                ClearObjectSelection();
                Document.ClearSupportSelection();
            }
            return;
        }
        if (!additive) Document.ClearSupportSelection();
        if (additive) Document.ToggleSelection(hitObj);
        else Document.Select(hitObj);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var pos = e.GetPosition(this);
        var dx = (float)(pos.X - _lastPointer.X);
        var dy = (float)(pos.Y - _lastPointer.Y);
        _lastPointer = pos;

        if (_viewCubeDrag is { } cubeDrag)
        {
            // A cube drag owns the pointer completely: no marquee, no gizmo hover, and no cube
            // hover tint chasing the pointer across regions while the cube is turning under it.
            if (cubeDrag.Move((float)pos.X, (float)pos.Y) is { } delta)
            {
                Camera.Orbit(delta.DxPixels, delta.DyPixels);
                if (_viewCubeHover >= 0) _viewCubeHover = -1;
                Redraw();
            }
            return;
        }

        if (_brushing)
        {
            if (PickSurface(MouseVector(e), out var triangle, out var point, out _) is not null &&
                triangle >= 0)
            {
                _brushCursor = point;
                RegionStrokeDab?.Invoke(point, triangle, WorldRadiusAt(point));
            }
            Redraw();
        }
        else if (SupportSelectionMode && RegionPickMode && RegionBrushMode)
        {
            // The cursor ring needs a point on the surface to sit on; off the model there is
            // nothing to paint and nothing to draw.
            _brushCursor = PickSurface(MouseVector(e), out _, out var cursorPoint, out _) is null
                ? null
                : cursorPoint;
            Redraw();
        }
        else if (SupportSelectionMode && RegionPickMode)
        {
            var hit = PickFace(MouseVector(e), out var hoverTriangle);
            RegionFaceHovered?.Invoke(hit, hit is null ? -1 : hoverTriangle);
        }
        else if (_orbiting)
        {
            Camera.Orbit(dx, dy);
            Redraw();
        }
        else if (_panning)
        {
            Camera.Pan(dx, dy, (float)Bounds.Height);
            Redraw();
        }
        else if (_marqueeStart is { } marqueeStart)
        {
            _marqueeCurrent = pos;
            var dragged = PointDistance(marqueeStart, pos) >= MarqueeClickThresholdPixels;
            MarqueeRect = dragged ? new Rect(marqueeStart, pos).Normalize() : null;
        }
        else if (_lineGesture is not null)
        {
            UpdateLineGestureCursor(MouseVector(e));
        }
        else if (_tipDrag is not null)
        {
            UpdateTipDrag(MouseVector(e));
        }
        else if (_editDrag is not null)
        {
            UpdateSupportEditDrag(MouseVector(e), e.KeyModifiers);
        }
        else if (_manualBraceMode)
        {
            UpdateManualBrace(MouseVector(e));
        }
        else if (_placementMode)
        {
            UpdatePlacementGhost(MouseVector(e));
        }
        else if (_editSupport is not null && HitSupportEditHandle(MouseVector(e)) is var hover &&
                 (hover?.Handle != _editHover || (hover?.Axis ?? GizmoHandle.None) != _editHoverAxis))
        {
            _editHover = hover?.Handle;
            _editHoverAxis = hover?.Axis ?? GizmoHandle.None;
            Redraw();
        }
        else if (_modal is { IsActive: true })
        {
            ApplySnap(e.KeyModifiers);
            _modal.Update(MouseVector(e));
            UpdateStatus();
        }
        else if (!_layFlatPick && !SupportSelectionMode && Document is not null && Document.Selection.Count > 0)
        {
            UpdateGizmo();
            var handle = _gizmo.HitTest(Camera, MouseVector(e), (float)Bounds.Width, (float)Bounds.Height);
            if (handle != _gizmo.Hovered)
            {
                _gizmo.Hovered = handle;
                Cursor = handle == GizmoHandle.None ? Cursor.Default : new Cursor(StandardCursorType.Hand);
                Redraw();
            }
        }
        else if (!_layFlatPick && _gizmo.Hovered != GizmoHandle.None)
        {
            _gizmo.Hovered = GizmoHandle.None;
            Cursor = Cursor.Default;
        }

        if (!_orbiting && !_panning && _marqueeStart is null && _tipDrag is null &&
            _lineGesture is null && _modal is not { IsActive: true })
        {
            var cubeHover = HitViewCube(pos);
            if (cubeHover != _viewCubeHover)
            {
                _viewCubeHover = cubeHover;
                Redraw();
            }
        }

        if (_layFlatPick) UpdateLayFlatHover(MouseVector(e));
        UpdateWaterline(MouseVector(e));
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_viewCubeDrag is { } cubeDrag)
        {
            EndViewCubeDrag();
            e.Pointer.Capture(null);
            // Never moved past the threshold, so this was a click after all: snap to the region
            // under the button-down point, not under the release point.
            if (cubeDrag.IsClick)
            {
                var (yaw, pitch) = ViewCube.ViewAngles(cubeDrag.Region, Camera.Yaw * 180f / MathF.PI);
                Camera.SetView(yaw, pitch);
            }
            // The release can land anywhere, including off the cube; re-pick so the tint is honest.
            _viewCubeHover = HitViewCube(e.GetPosition(this));
            Redraw();
            e.Handled = true;
            return;
        }
        if (_editDrag is not null)
        {
            CommitSupportEditDrag();
            e.Pointer.Capture(null);
            e.Handled = true;
            return;
        }
        if (_brushing)
        {
            _brushing = false;
            e.Pointer.Capture(null);
            RegionStrokeEnded?.Invoke();
            e.Handled = true;
            return;
        }
        if (_orbiting || _panning)
        {
            _orbiting = _panning = false;
            e.Pointer.Capture(null);
        }
        else if (_marqueeStart is { } start)
        {
            var end = _marqueeCurrent;
            var dragged = PointDistance(start, end) >= MarqueeClickThresholdPixels;
            if (Document is not null && dragged)
            {
                var w = (float)Bounds.Width;
                var h = (float)Bounds.Height;
                var visibleObjectIds = Document.Scene.Objects
                    .Where(obj => obj.RenderState != RenderState.Hidden)
                    .Select(obj => obj.Id)
                    .ToHashSet();
                var ids = Danslicer.Core.Supports.SupportMarqueeSelection.ElementsInside(
                    Document.Supports,
                    point => Camera.WorldToScreen(point, w, h),
                    new Vector2((float)start.X, (float)start.Y),
                    new Vector2((float)end.X, (float)end.Y),
                    point => ClipRange.Contains(point) &&
                        (SelectThroughSupports || IsSupportPointVisible(point, visibleObjectIds)),
                    node => SupportDisplayPolicy.IsNodeDisplayed(
                        Document.Supports, node, SupportDisplay, ClipRange),
                    segment => SupportDisplayPolicy.IsSegmentDisplayed(
                        Document.Supports, segment, SupportDisplay, ClipRange),
                    segment => ClipRange.VisibleSegmentMidpoint(
                        Document.Supports.GetNode(segment.NodeA).Position,
                        Document.Supports.GetNode(segment.NodeB).Position));
                Document.SelectSupportElements(ids, _marqueeAdditive);
            }
            else if (Document is not null && _pendingClickSupport is { } element)
            {
                if (!_marqueeAdditive) ClearObjectSelection();
                // Double-click selects the whole support tree; single click the element.
                if (_pendingClickCount >= 2) SelectDisplayedSupportComponent(element, _marqueeAdditive);
                else Document.SelectSupportElement(element, _marqueeAdditive);
            }
            else if (!_marqueeAdditive)
            {
                ClearObjectSelection();
                Document?.ClearSupportSelection();
            }
            _pendingClickSupport = null;
            _marqueeStart = null;
            MarqueeRect = null;
            e.Pointer.Capture(null);
            Redraw();
            e.Handled = true;
        }
        else if (_gizmoDragging)
        {
            _gizmoDragging = false;
            _gizmo.Active = GizmoHandle.None;
            _modal?.Confirm();
            e.Pointer.Capture(null);
            UpdateStatus();
        }
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        // Losing capture (a window switch, a touch cancel) ends the drag where it stands. No snap:
        // there is no release position to judge, and silently jumping the camera would be worse
        // than leaving the view the user has already dragged it to.
        if (_viewCubeDrag is not null)
        {
            EndViewCubeDrag();
            Redraw();
        }
    }

    private void EndViewCubeDrag()
    {
        _viewCubeDrag = null;
        _viewCubeHover = -1;
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (SpaceMouseDiagnostics.Enabled)
            SpaceMouseDiagnostics.Write("WHEEL", $"viewport={GetHashCode()} delta={e.Delta} pointerType={e.Pointer.Type} pitch={Camera.Pitch:F6} distanceBefore={Camera.Distance:F4}");
        // During a guided gesture the wheel steps the pitch (Blender modal style), not the zoom.
        if (_lineGesture is { } gesture && e.Delta.Y != 0)
        {
            gesture.StepPitch(e.Delta.Y > 0 ? 1 : -1);
            _linePitchInput = "";
            RefreshLineGesturePreview();
            e.Handled = true;
            return;
        }
        Camera.Zoom((float)e.Delta.Y);
        if (_layFlatPick) UpdateLayFlatHover(MouseVector(e));
        Redraw();
        e.Handled = true;
    }

    private SceneObject? PickObject(Vector2 mouse) => PickFace(mouse, out _);

    private SceneObject? PickFace(Vector2 mouse, out int triangle) => PickSurface(mouse, out triangle, out _, out _);

    /// <summary>
    /// While region painting is armed, the surface under the cursor is scoped to the support
    /// target, exactly as generation and manual placement are (<see cref="SupportTargetPolicy"/>).
    /// Without this a stroke that strayed onto a neighbouring model would paint it — and select
    /// it, taking the target with it. With no target chosen every model is paintable, which is
    /// how the first click picks one.
    /// </summary>
    private bool IsPaintable(SceneObject obj) =>
        !(SupportSelectionMode && RegionPickMode) ||
        SupportTargetPolicy.CanSupport(Document?.SupportTarget, obj);

    private void UpdateWaterline(Vector2 mouse)
    {
        if (SupportWaterline is not { Enabled: true, SupportModeActive: true } waterline)
        {
            SupportWaterline?.Clear();
            return;
        }

        var hit = PickSurface(mouse, out _, out var worldPoint, out _);
        waterline.UpdateHover(hit is null ? null : worldPoint.Z);
    }

    private SceneObject? PickSurface(Vector2 mouse, out int triangle, out Vector3 worldPoint, out Vector3 worldNormal)
    {
        triangle = -1;
        worldPoint = default;
        worldNormal = Vector3.UnitZ;
        if (Document is null) return null;
        var ray = Camera.ScreenToRay(mouse.X, mouse.Y, (float)Bounds.Width, (float)Bounds.Height);
        SceneObject? best = null;
        var bestDistance = float.PositiveInfinity;
        foreach (var obj in Document.Scene.Objects)
        {
            if (obj.RenderState == RenderState.Hidden) continue;
            if (!IsPaintable(obj)) continue;
            var world = obj.Transform.ToMatrix();
            if (!Matrix4x4.Invert(world, out var toLocal)) continue;
            var local = ray.Transform(toLocal);
            if (local.IntersectMesh(obj.Mesh, out var tri,
                    localPoint => ClipRange.Contains(Vector3.Transform(localPoint, world))) is not { } t) continue;
            var hitWorld = Vector3.Transform(local.At(t), world);
            var d = Vector3.Distance(ray.Origin, hitWorld);
            if (d < bestDistance)
            {
                bestDistance = d;
                best = obj;
                triangle = tri;
                worldPoint = hitWorld;
                // World normal from transformed edges, so non-uniform scale needs no special case.
                obj.Mesh.GetTriangle(tri, out var a, out var b, out var c);
                var wa = Vector3.Transform(a, world);
                var n = Vector3.Cross(Vector3.Transform(b, world) - wa, Vector3.Transform(c, world) - wa);
                worldNormal = n.LengthSquared() > 1e-18f ? Vector3.Normalize(n) : Vector3.UnitZ;
            }
        }
        return best;
    }

    // ----- Lay flat on face -----

    private bool _layFlatPick;
    private SceneObject? _layFlatHoverObject;
    private int _layFlatHoverTriangle = -1;
    private HashSet<int>? _layFlatHoverFaces;
    /// <summary>True between a brush press and its release: every move in between is a dab.</summary>
    private bool _brushing;
    /// <summary>Where the brush ring is drawn; null when the cursor is off the model.</summary>
    private Vector3? _brushCursor;

    /// <summary>Arms lay-flat: the next left click on a face lays the object on it.</summary>
    public void BeginLayFlatPick()
    {
        _layFlatPick = true;
        _gizmo.Hovered = GizmoHandle.None;
        Cursor = new Cursor(StandardCursorType.Cross);
        UpdateLayFlatHover(new Vector2((float)_lastPointer.X, (float)_lastPointer.Y));
        UpdateStatus();
        Redraw();
    }

    private void EndLayFlatPick()
    {
        _layFlatPick = false;
        Cursor = Cursor.Default;
        ClearLayFlatHover();
        UpdateStatus();
    }

    private void ClearLayFlatHover()
    {
        _layFlatHoverObject = null;
        _layFlatHoverTriangle = -1;
        _layFlatHoverFaces = null;
        MarkRegionOverlayDirty();
    }

    private void UpdateLayFlatHover(Vector2 mouse)
    {
        int triangle = -1;
        var hit = HitViewCube(new Point(mouse.X, mouse.Y)) >= 0
            ? null : PickFace(mouse, out triangle);
        if (hit == _layFlatHoverObject && triangle == _layFlatHoverTriangle) return;
        _layFlatHoverObject = hit;
        _layFlatHoverTriangle = triangle;
        // Share the actual operation's seed-normal tolerance and edge connectivity.
        _layFlatHoverFaces = hit is null || triangle < 0
            ? null : LayFlat.Cluster(hit.Mesh, triangle).ToHashSet();
        MarkRegionOverlayDirty();
    }

    private bool TryLayFlat(Vector2 mouse)
    {
        if (Document is null) return false;
        var hit = PickFace(mouse, out var triangle);
        if (hit is null || triangle < 0) return false;
        Document.Select(hit);
        Document.LayFlatOnFace(hit, triangle);
        return true;
    }

    // ----- Manual supports -----

    private static readonly Vector4 TipColor = new(1f, 0.85f, 0.3f, 0.95f);
    private static readonly Vector4 BranchColor = new(0.55f, 0.75f, 0.95f, 0.95f);
    private static readonly Vector4 TrunkColor = new(0.75f, 0.85f, 1f, 0.95f);
    private static readonly Vector4 BracingColor = new(0.5f, 0.9f, 0.6f, 0.95f);
    private static readonly Vector4 BaseColor = new(0.8f, 0.7f, 0.5f, 0.95f);
    private static readonly Vector4 TipMarkerColor = new(1f, 0.55f, 0.25f, 1f);

    private IReadOnlyList<SupportRenderPart>? _placementPickParts;
    private (SupportGraph Graph, Guid Target, SupportDisplayConfig Display, ViewportClipRange Clip)? _placementPickKey;

    // Pick the actual rendered support surface, with the same clipping and visibility as drawing.
    // Model and support hits compete by depth so a support behind the model cannot steal a click.
    internal SceneObject? PickPlacementSurface(Vector2 mouse, out Vector3 point,
        out Vector3 normal, out bool contactOnSupport)
    {
        var model = PickSurface(mouse, out _, out point, out normal);
        contactOnSupport = false;
        if (Document?.SupportTarget is not { } target || target.RenderState == RenderState.Hidden)
            return model;
        var ray = Camera.ScreenToRay(mouse.X, mouse.Y, (float)Bounds.Width, (float)Bounds.Height);
        var distance = model is null ? float.PositiveInfinity : Vector3.Distance(ray.Origin, point);
        var graph = Document.Supports;
        var key = (graph, target.Id, SupportDisplay, ClipRange);
        if (_placementPickParts is null || _placementPickKey != key)
        {
            _placementPickKey = key;
            _placementPickParts = SupportRenderMesh.Build(graph,
            includeSegment: segment => graph.OwningObjectId(segment.Id) == target.Id &&
                SupportDisplayPolicy.IsSegmentDisplayed(graph, segment, SupportDisplay, ClipRange),
            includeBase: node => graph.OwningObjectId(node.Id) == target.Id &&
                SupportDisplayPolicy.IsNodeDisplayed(graph, node, SupportDisplay, ClipRange),
            includeHidden: SupportDisplay.ShowHiddenElements);
        }
        foreach (var part in _placementPickParts)
        {
            if (ray.IntersectMesh(part.Mesh, out var triangle, p => ClipRange.Contains(p)) is not { } t)
                continue;
            var hit = ray.At(t);
            var d = Vector3.Distance(ray.Origin, hit);
            if (d >= distance) continue;
            part.Mesh.GetTriangle(triangle, out var a, out var b, out var c);
            var n = Vector3.Cross(b - a, c - a);
            normal = n.LengthSquared() > 1e-18f ? Vector3.Normalize(n) : -Vector3.UnitZ;
            point = hit;
            distance = d;
            model = target;
            contactOnSupport = true;
        }
        return model;
    }

    private string? TryAddSupport(Vector2 mouse)
    {
        if (Document is null) return null;
        var hit = PickPlacementSurface(mouse, out var point, out var normal, out var contactOnSupport);
        if (hit is null) return null;
        // Only the support target takes supports; a click on any other model says so rather than
        // silently doing nothing. Document refuses it as well — this is the visible half.
        if (Danslicer.Core.Supports.SupportTargetPolicy.RefusalMessage(Document.SupportTarget, hit)
            is { } refusal) return refusal;
        if (!Document.AddManualSupport(hit, point, normal, out var reason, out var parenting, contactOnSupport))
            return reason == Danslicer.Core.Supports.Routing.RoutingFailureReason.ContactBlocked
                ? "Support: contact is too tight to the surface"
                : "Support: no clear path to the plate from here";
        return parenting is null ? null : $"Support: placed{AutoParentSuffix(parenting)}";
    }

    /// <summary>"→ 3 trunks" after a placement auto-parenting acted on; empty when it did not.</summary>
    private static string AutoParentSuffix(Danslicer.Core.Supports.AutoParentingOutcome? parenting)
    {
        if (parenting is null) return "";
        var trunks = parenting.Trunks == 1 ? "1 trunk" : $"{parenting.Trunks} trunks";
        var kept = parenting.Refused > 0 ? $" · {parenting.Refused} kept as they were" : "";
        return $" → {trunks}{kept}";
    }

    // ----- Guided line of supports (L in Support mode) -----
    // SUPPORT-GEOMETRY-SPEC "Guided tip placement": the gesture itself is pure Core state
    // (SurfaceLineGesture); this is the modal host — picks in, overlay and status out, and one
    // batch placement on commit.

    private static readonly Vector4 LineRouteColor = new(0.35f, 0.9f, 1f, 0.95f);
    private static readonly Vector4 LineChordColor = new(1f, 0.3f, 0.3f, 0.95f);
    private static readonly Vector4 LineGhostTipColor = new(0.7f, 1f, 0.4f, 1f);
    private const int LineBridgeSamples = 32;

    private Danslicer.Core.Supports.Guided.IGuidedGesture? _lineGesture;
    private SceneObject? _lineTarget;
    private IReadOnlyList<TipCandidate> _linePreview = Array.Empty<TipCandidate>();
    private string _linePitchInput = "";

    /// <summary>
    /// Starts the line gesture on the support target. With one tip selected the line starts
    /// from that tip's contact, so a line can extend a support already placed. Returns a status
    /// message when the gesture cannot start.
    /// </summary>
    /// <summary>Which guided placement tool a toolbar button starts (user rule 2026-09-08: every key has a button).</summary>
    public enum GuidedTool { Place, Line, Polygon, Edge, Ring, Contour, Densify, Thin }

    private GuidedTool? _activeLineTool;
    public GuidedTool? ActiveGuidedTool => _placementMode ? GuidedTool.Place : _activeLineTool;
    public event EventHandler? ActiveGuidedToolChanged;

    private void ExitSupportTools()
    {
        if (_manualBraceMode) EndManualBrace();
        if (_tipDrag is not null) CancelTipDrag();
        if (_editSupport is not null) EndSupportEdit();
        if (_placementMode) EndPlacementMode();
        if (_lineGesture is not null) CancelLineGesture();
        _borderSelectArmed = false;
    }


    /// <summary>
    /// Starts a guided tool from the toolbar, exactly as its key would from the cursor's last
    /// position. Densify and thin run at once; the others begin their gesture and take focus so
    /// the next click lands in the viewport.
    /// </summary>
    public void StartGuidedTool(GuidedTool tool)
    {
        if (Document is null || !SupportSelectionMode) return;
        var mouse = new Vector2((float)_lastPointer.X, (float)_lastPointer.Y);
        string? status = tool switch
        {
            GuidedTool.Place => PlacementToggleStatus(),
            GuidedTool.Line => BeginLineGesture(mouse),
            GuidedTool.Polygon => BeginLineGesture(mouse, GuidedKind.Polygon),
            GuidedTool.Edge => BeginLineGesture(mouse, GuidedKind.Edge),
            GuidedTool.Ring => BeginLineGesture(mouse, GuidedKind.Ring),
            GuidedTool.Contour => BeginLineGesture(mouse, GuidedKind.Contour),
            GuidedTool.Densify => DensifyStatus(),
            GuidedTool.Thin => ThinStatus(),
            _ => null,
        };
        Focus();
        UpdateStatus();
        if (status is not null) StatusText = status;
        Redraw();
    }

    private string DensifyStatus()
    {
        ExitSupportTools();
        var placed = Document!.DensifyTips(out var refused, out var parenting);
        return placed + refused == 0
            ? "Densify: nothing to add (needs two or more tips)"
            : refused == 0 ? $"Densify: {placed} placed{AutoParentSuffix(parenting)}"
            : $"Densify: {placed} of {placed + refused} placed{AutoParentSuffix(parenting)} · {refused} had no clear path";
    }

    private string ThinStatus()
    {
        ExitSupportTools();
        var removed = Document!.ThinTips();
        return removed == 0 ? "Thin: nothing to remove (needs two or more tips)" : $"Thin: {removed} removed";
    }

    /// <summary>Parenting (J), from the key or the Supports pop-out button.</summary>
    public void ParentSupports()
    {
        if (Document is null || !SupportSelectionMode) return;
        var status = ParentStatus();
        Focus();
        UpdateStatus();
        StatusText = status;
        Redraw();
    }

    public event Action<bool>? StructurePreviewRequested;
    public event Action? StructurePreviewCancelRequested;
    public event Action<Guid>? StructurePreviewElementClicked;
    private SupportGraph? _structurePreviewGraph;
    private IReadOnlySet<Guid> _structurePreviewHighlights = new HashSet<Guid>();
    public bool StructurePreviewActive { get; set; }
    internal bool InspectStructurePreviewAt(Vector2 mouse)
    {
        if (!StructurePreviewActive || PickSupportElement(mouse, out _) is not { } element ||
            !_structurePreviewHighlights.Contains(element)) return false;
        StructurePreviewElementClicked?.Invoke(element);
        return true;
    }

    public void SetStructurePreview(SupportGraph? graph, IReadOnlySet<Guid>? highlights = null)
    {
        _structurePreviewGraph = graph;
        _structurePreviewHighlights = highlights ?? new HashSet<Guid>();
        _supportMeshesDirty = true;
        _selectionMeshDirty = true;
        MarkClipCapsDirty();
        Redraw();
    }

    private string ParentStatus()
    {
        if (Document!.SupportTarget is null) return "Parent: choose the model to support first (Objects pop-out)";
        if (StructurePreviewRequested is not null) { ExitSupportTools(); StructurePreviewRequested(false); return "Parenting preview · adjust settings, then Apply or Cancel"; }
        var outcome = Document.ParentSupports();
        if (outcome is null) return "Parent: nothing to parent (needs two or more supports)";
        var refused = outcome.Refused > 0 ? $" · {outcome.Refused} kept as they were" : "";
        return outcome.TrunksAfter < outcome.TrunksBefore
            ? $"Parent: {outcome.Operands} supports → {outcome.TrunksAfter} trunks (was {outcome.TrunksBefore}){refused}"
            : $"Parent: no trunk could be shared ({outcome.TrunksBefore} trunks){refused}";
    }

    /// <summary>Bracing (K), from the key or the Supports pop-out button.</summary>
    public void BraceSupports() => RunSupportCommand(BraceStatus);

    /// <summary>Unbrace (Shift+K), from the key or the Supports pop-out button.</summary>
    public void UnbraceSupports() => RunSupportCommand(UnbraceStatus);

    /// <summary>Select braces, from the Supports pop-out button or the Object menu.</summary>
    public void SelectBraces() => RunSupportCommand(SelectBracesStatus);

    private void RunSupportCommand(Func<string> command)
    {
        if (Document is null || !SupportSelectionMode) return;
        var status = command();
        Focus();
        UpdateStatus();
        StatusText = status;
        Redraw();
    }

    private string BraceStatus()
    {
        if (Document!.SupportTarget is null) return "Brace: choose the model to support first (Objects pop-out)";
        if (StructurePreviewRequested is not null) { ExitSupportTools(); StructurePreviewRequested(true); return "Bracing preview · adjust settings, then Apply or Cancel"; }
        var outcome = Document.BraceSupports();
        if (outcome is null) return "Brace: nothing to brace (needs two or more supports)";
        return outcome.Braces == 0
            ? $"Brace: no brace fits ({outcome.Operands} supports · already braced, too short, too far apart or blocked)"
            : $"Bracing: {outcome.Braces} {(outcome.Braces == 1 ? "brace" : "braces")} added, {outcome.SupportsTied} supports tied";
    }

    private string UnbraceStatus()
    {
        if (Document!.SupportTarget is null) return "Unbrace: choose the model to support first (Objects pop-out)";
        var removed = Document.UnbraceSupports();
        return removed == 0 ? "Unbrace: no braces on these supports" : $"Unbrace: {removed} {(removed == 1 ? "brace" : "braces")} removed";
    }

    private string SelectBracesStatus()
    {
        if (Document!.SupportTarget is null) return "Select braces: choose the model to support first (Objects pop-out)";
        var selected = Document.SelectBraces();
        return selected == 0 ? "Select braces: no braces on these supports" : $"Select braces: {selected} selected";
    }

    private enum GuidedKind { Line, Polygon, Edge, Ring, Contour }

    private string? BeginLineGesture(Vector2 mouse, GuidedKind kind = GuidedKind.Line)
    {
        ExitSupportTools();
        if (Document is null) return null;
        if (Document.SupportTarget is not { } target)
            return "Guided placement: choose the model to support first (Objects pop-out)";
        var world = target.Transform.ToMatrix();
        var worldMesh = new Mesh(
            target.Mesh.Positions.Select(p => Vector3.Transform(p, world)).ToArray(),
            (int[])target.Mesh.Indices.Clone());
        _lineTarget = target;
        var pitch = Document.SupportSettings.Spacing;
        _lineGesture = kind switch
        {
            GuidedKind.Polygon => new Danslicer.Core.Supports.Guided.SurfacePolygonGesture(worldMesh, pitch),
            GuidedKind.Edge => new Danslicer.Core.Supports.Guided.CreaseFollowGesture(worldMesh, pitch,
                Document.GuidedPlacementParameters().SharpEdgeDegrees),
            GuidedKind.Ring => new Danslicer.Core.Supports.Guided.SurfaceRingGesture(worldMesh, pitch),
            GuidedKind.Contour => new Danslicer.Core.Supports.Guided.ContourGesture(worldMesh, pitch),
            _ => new Danslicer.Core.Supports.Guided.SurfaceLineGesture(worldMesh, pitch),
        };
        _linePitchInput = "";

        if (SelectedTip() is { } tipId)
        {
            var tip = Document.Supports.GetNode(tipId);
            if (tip.ContactObjectId is null || tip.ContactObjectId == target.Id)
            {
                _lineGesture.SetCursor(tip.Position,
                    Danslicer.Core.Supports.Guided.SurfacePath.NearestFace(worldMesh, tip.Position));
                _lineGesture.AddVertex();
            }
        }
        UpdateLineGestureCursor(mouse);
        _activeLineTool = kind switch
        {
            GuidedKind.Polygon => GuidedTool.Polygon, GuidedKind.Edge => GuidedTool.Edge,
            GuidedKind.Ring => GuidedTool.Ring, GuidedKind.Contour => GuidedTool.Contour,
            _ => GuidedTool.Line
        };
        ActiveGuidedToolChanged?.Invoke(this, EventArgs.Empty);
        return null;
    }

    private void UpdateLineGestureCursor(Vector2 mouse)
    {
        if (_lineGesture is not { } gesture || _lineTarget is not { } target) return;
        var hit = PickSurface(mouse, out var face, out var point, out _);
        if (hit is null || hit.Id != target.Id || face < 0)
        {
            gesture.ClearCursor();
        }
        else
        {
            // Edge follow snaps within about twelve screen pixels, whatever the zoom.
            if (gesture is Danslicer.Core.Supports.Guided.CreaseFollowGesture crease)
                crease.SnapDistanceMm = ContactMarkerHalfSize(point) * 3f;
            gesture.SetCursor(point, face);
            // The vertical-plane path failed: bridge along what the user sees on screen.
            if (gesture.CursorPathIsChord && gesture.Vertices.Count > 0)
                gesture.SetCursor(point, face, ScreenBridge(gesture.Vertices[^1], mouse, point, face));
        }
        RefreshLineGesturePreview();
    }

    /// <summary>
    /// Fallback path from the last vertex to the cursor: the screen-space segment between them,
    /// picked back onto the target at even steps. Null when too little of it lands on the model.
    /// </summary>
    private Danslicer.Core.Supports.Guided.SurfacePath? ScreenBridge((Vector3 Point, int Face) from,
        Vector2 mouse, Vector3 to, int toFace)
    {
        if (_lineTarget is not { } target) return null;
        var w = (float)Bounds.Width;
        var h = (float)Bounds.Height;
        if (Camera.WorldToScreen(from.Point, w, h) is not { } start) return null;
        var points = new List<Vector3> { from.Point };
        var faces = new List<int> { from.Face };
        var length = 0f;
        for (var i = 1; i < LineBridgeSamples; i++)
        {
            var screen = Vector2.Lerp(start, mouse, i / (float)LineBridgeSamples);
            var hit = PickSurface(screen, out var face, out var point, out _);
            if (hit is null || hit.Id != target.Id || face < 0) continue;
            length += Vector3.Distance(points[^1], point);
            points.Add(point);
            faces.Add(face);
        }
        if (points.Count < 2) return null;
        length += Vector3.Distance(points[^1], to);
        points.Add(to);
        faces.Add(toFace);
        return new Danslicer.Core.Supports.Guided.SurfacePath(points, faces, length);
    }

    private void LineGestureAddVertex(Vector2 mouse)
    {
        if (_lineGesture is not { } gesture) return;
        UpdateLineGestureCursor(mouse);
        gesture.AddVertex();
        RefreshLineGesturePreview();
    }

    private void RefreshLineGesturePreview()
    {
        if (Document is null || _lineGesture is not { } gesture) return;
        _linePreview = gesture.Preview(Document.GuidedPlacementParameters(), Document.GuidedExistingSupports());
        UpdateStatus();
        Redraw();
    }

    private void TypeLinePitch(char c)
    {
        if (c == '.' && _linePitchInput.Contains('.')) return;
        _linePitchInput += c;
        ApplyLinePitchInput();
    }

    private void ApplyLinePitchInput()
    {
        if (_lineGesture is not { } gesture) return;
        if (float.TryParse(_linePitchInput, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var pitch) && pitch > 0)
            gesture.SetPitch(pitch);
        RefreshLineGesturePreview();
    }

    private void CommitLineGesture()
    {
        if (Document is null || _lineGesture is not { } gesture || _lineTarget is not { } target)
        {
            CancelLineGesture();
            return;
        }
        var candidates = gesture.Preview(Document.GuidedPlacementParameters(), Document.GuidedExistingSupports());
        string status;
        if (candidates.Count == 0)
        {
            status = $"{gesture.Name}: nothing to place";
        }
        else
        {
            var placed = Document.PlaceGuidedTips(target, candidates, gesture.Name, out var refused, out var parenting);
            status = refused == 0
                ? $"{gesture.Name}: {placed} placed{AutoParentSuffix(parenting)}"
                : $"{gesture.Name}: {placed} of {placed + refused} placed{AutoParentSuffix(parenting)} · {refused} had no clear path";
        }
        EndLineGesture();
        StatusText = status;
    }

    private void CancelLineGesture()
    {
        EndLineGesture();
        UpdateStatus();
    }

    private void EndLineGesture()
    {
        _lineGesture = null;
        _activeLineTool = null;
        ActiveGuidedToolChanged?.Invoke(this, EventArgs.Empty);
        _lineTarget = null;
        _linePreview = Array.Empty<TipCandidate>();
        _linePitchInput = "";
        Redraw();
    }

    /// <summary>The route (depth-tested, so the far side hides) and the ghost tips it would place.</summary>
    private void AppendLineGesture(List<OverlayLine> depthLines, List<OverlayLine> lines)
    {
        if (_lineGesture is not { } gesture) return;
        foreach (var path in gesture.Route())
        {
            // A chord has left the surface: draw it red and through everything so it is seen.
            // A surface path between two different faces always has a crossing point, so a
            // two-point path across faces can only be a chord.
            var chord = path.Points.Count == 2 && path.Faces[0] != path.Faces[1];
            var target = chord ? lines : depthLines;
            var colour = chord ? LineChordColor : LineRouteColor;
            for (var i = 0; i + 1 < path.Points.Count; i++)
                target.Add(new OverlayLine(path.Points[i], path.Points[i + 1], colour));
        }
        foreach (var candidate in _linePreview)
        {
            var p = candidate.Point;
            var size = ContactMarkerHalfSize(p);
            depthLines.Add(new OverlayLine(p - Camera.Right * size, p + Camera.Right * size, LineGhostTipColor));
            depthLines.Add(new OverlayLine(p - Camera.Up * size, p + Camera.Up * size, LineGhostTipColor));
            // A short stub along the inward normal shows which way the cone would point.
            depthLines.Add(new OverlayLine(p, p - candidate.InwardNormal * size * 2f, LineGhostTipColor));
        }
        foreach (var (point, _) in gesture.Vertices)
        {
            var size = ContactMarkerHalfSize(point) * 1.5f;
            var right = Camera.Right * size;
            var up = Camera.Up * size;
            depthLines.Add(new OverlayLine(point - right - up, point + right - up, LineRouteColor));
            depthLines.Add(new OverlayLine(point + right - up, point + right + up, LineRouteColor));
            depthLines.Add(new OverlayLine(point + right + up, point - right + up, LineRouteColor));
            depthLines.Add(new OverlayLine(point - right + up, point - right - up, LineRouteColor));
        }
    }

    // ----- Manual placement mode (T in Support mode) -----
    // User note 2026-09-08: a mode instead of a one-shot key, with a ghosted support following
    // the cursor; where no support can be placed, nothing is shown.

    private static readonly Vector3 PlacementGhostColor = new(0.55f, 0.95f, 1f);
    /// <summary>A red tip alone says "nothing can go here" without leaving the mode silent (user, 2026-09-09).</summary>
    private static readonly Vector3 PlacementRefusedColor = new(1f, 0.25f, 0.2f);
    private const float PlacementGhostOpacity = 0.45f;
    /// <summary>Cursor travel on the surface below which the ghost is not re-routed.</summary>
    private const float PlacementGhostStepMm = 0.15f;

    private bool _placementMode;
    private readonly List<AuxMeshDraw> _placementGhost = new();
    private Vector3? _placementGhostPoint;
    private string? _placementRefusal;

    public bool IsPlacingSupports => _placementMode;

    private void TogglePlacementMode()
    {
        if (_placementMode) EndPlacementMode();
        else BeginPlacementMode();
    }

    private string? PlacementToggleStatus()
    {
        TogglePlacementMode();
        return null;
    }

    private void BeginPlacementMode()
    {
        if (Document is null || !SupportSelectionMode) return;
        ExitSupportTools();
        _placementMode = true;
        ActiveGuidedToolChanged?.Invoke(this, EventArgs.Empty);
        _placementRefusal = null;
        UpdatePlacementGhost(new Vector2((float)_lastPointer.X, (float)_lastPointer.Y));
        UpdateStatus();
        Redraw();
    }

    private void EndPlacementMode()
    {
        _placementMode = false;
        ActiveGuidedToolChanged?.Invoke(this, EventArgs.Empty);
        ClearPlacementGhost();
        UpdateStatus();
        Redraw();
    }

    private void ClearPlacementGhost()
    {
        _placementGhost.Clear();
        _placementGhostPoint = null;
        _placementRefusal = null;
    }

    /// <summary>
    /// Routes the support a click here would place and shows it translucent, exactly as it
    /// would be built. Off the model, on a model that is not the target, or where routing
    /// refuses, the ghost is cleared and the refusal named in the status line.
    /// </summary>
    private void UpdatePlacementGhost(Vector2 mouse)
    {
        if (Document is null) return;
        var hit = PickPlacementSurface(mouse, out var point, out var normal, out var contactOnSupport);
        if (hit is null)
        {
            // Off the model the mode still shows: a red tip hangs where the cursor meets the
            // plate, or a little way down the view ray when it misses the plate too.
            var ray = Camera.ScreenToRay(mouse.X, mouse.Y, (float)Bounds.Width, (float)Bounds.Height);
            var t = MathF.Abs(ray.Direction.Z) > 1e-5f ? -ray.Origin.Z / ray.Direction.Z : -1f;
            point = t > 0f ? ray.At(t) : ray.At(Camera.Distance);
            normal = -Vector3.UnitZ;
            if (_placementGhostPoint is { } lastOff && Vector3.Distance(lastOff, point) < PlacementGhostStepMm) return;
            _placementGhostPoint = point;
            _placementRefusal = "no model or support under the cursor";
            ShowRefusedTip(point, normal);
            UpdateStatus();
            Redraw();
            return;
        }
        if (_placementGhostPoint is { } last && Vector3.Distance(last, point) < PlacementGhostStepMm) return;
        _placementGhostPoint = point;

        _placementGhost.Clear();
        _placementRefusal = SupportTargetPolicy.RefusalMessage(Document.SupportTarget, hit);
        if (_placementRefusal is null)
        {
            var edit = Document.PreviewManualSupport(hit, point, normal, out var reason, contactOnSupport);
            if (edit is null)
            {
                _placementRefusal = reason == Danslicer.Core.Supports.Routing.RoutingFailureReason.ContactBlocked
                    ? "contact is too tight to the surface"
                    : "no clear path to the plate from here";
            }
            else
            {
                // The ghost borrows the existing node a branch joins (SupportEditPreview).
                var ghost = edit.ToGraph(Document.Supports);
                foreach (var part in SupportRenderMesh.Build(ghost))
                    _placementGhost.Add(new AuxMeshDraw(part.Mesh, PlacementGhostColor, PlacementGhostOpacity));
            }
        }
        if (_placementRefusal is not null) ShowRefusedTip(point, normal);
        UpdateStatus();
        Redraw();
    }

    /// <summary>
    /// The ghost when nothing can be placed: just the tip cone, red, at the cursor's contact
    /// (or the free point below the cursor), built at the current tip settings so it reads as
    /// the same tip the cyan ghost would have carried.
    /// </summary>
    private void ShowRefusedTip(Vector3 point, Vector3 outwardNormal)
    {
        if (Document is null) return;
        _placementGhost.Clear();
        var settings = Document.SupportSettings;
        var ghost = new SupportGraph();
        var tip = new SupportNode
        {
            Type = SupportNodeType.Tip,
            Position = point,
            SurfaceNormal = outwardNormal,
            TipShape = SupportTipShape.Cone,
            ConeLength = settings.ConeLength,
            TipDiameter = settings.TipDiameter,
            BallDiameter = settings.BallDiameter,
        };
        // The cone runs along the outward normal, as a placed cone would before any clamp.
        var junction = new SupportNode
        {
            Type = SupportNodeType.Junction,
            Position = point + Vector3.Normalize(outwardNormal) * settings.ConeLength,
        };
        ghost.AddNode(tip);
        ghost.AddNode(junction);
        ghost.AddSegment(new SupportSegment
        {
            Type = SupportSegmentType.Tip, NodeA = tip.Id, NodeB = junction.Id, Diameter = settings.TipDiameter,
        });
        foreach (var part in SupportRenderMesh.Build(ghost))
            _placementGhost.Add(new AuxMeshDraw(part.Mesh, PlacementRefusedColor, PlacementGhostOpacity + 0.2f));
    }

    // ----- Support edit mode (Space with a support selected) -----

    private static readonly Vector4 EditBaseColor = new(0.35f, 0.6f, 1f, 1f);
    private static readonly Vector4 EditJunctionColor = new(1f, 0.85f, 0.2f, 1f);
    private static readonly Vector4 EditTipColor = new(0.3f, 1f, 0.4f, 1f);
    private static readonly Vector4 EditTrunkColor = new(0.85f, 0.55f, 1f, 1f);
    private static readonly Vector4 EditActiveColor = new(1f, 1f, 1f, 1f);
    private const float EditHandlePickRadiusPixels = 10f;

    /// <summary>The seed element of the support being edited, or null outside edit mode.</summary>
    private Guid? _editSupport;
    private SupportHandle? _editHover;
    private GizmoHandle _editHoverAxis;
    private SupportHandle? _editDrag;
    private GizmoHandle _editDragAxis;
    private List<(SupportNode Node, Vector3 Position, Vector3 Normal)>? _editDragBefore;
    /// <summary>The world point the drag started from on its constraint (axis line or plane).</summary>
    private Vector3 _editDragAnchor;
    /// <summary>One gizmo per handle, rebuilt each frame (user, 2026-09-09: X/Y on base and trunk, XYZ on junctions and tips).</summary>
    private readonly List<(SupportHandle Handle, Gizmo Gizmo)> _editGizmos = new();
    private static float EditGizmoPixels => Configuration.AppConfig.Current.Viewport.SupportGizmoSizePixels;
    private static float EditGizmoLineWidth => Configuration.AppConfig.Current.Viewport.SupportGizmoLineWidth;
    private bool _editFollowingSelection;

    public bool IsEditingSupport => _editSupport is not null;

    /// <summary>
    /// Space, or the Edit button: enters edit mode on the selected support (the whole support
    /// containing the first selected element) and shows its handles; in edit mode, leaves it.
    /// </summary>
    public void ToggleSupportEdit()
    {
        if (Document is null) return;
        if (_editSupport is not null)
        {
            EndSupportEdit();
            return;
        }
        if (!SupportSelectionMode || Document.SupportSelection.Count == 0)
        {
            StatusText = "Edit support: select a support first";
            return;
        }
        BeginSupportEdit(Document.SupportSelection.First());
    }

    private void BeginSupportEdit(Guid element)
    {
        if (_manualBraceMode) EndManualBrace();
        if (_placementMode) EndPlacementMode();
        if (_lineGesture is not null) CancelLineGesture();
        _editSupport = element;
        _editHover = null;
        _editHoverAxis = GizmoHandle.None;
        _editFollowingSelection = true;
        try { SelectDisplayedSupportComponent(element, additive: false); }
        finally { _editFollowingSelection = false; }
        UpdateStatus();
        Redraw();
    }

    private void EndSupportEdit()
    {
        if (_editDrag is not null) CancelSupportEditDrag();
        _editSupport = null;
        _editHover = null;
        UpdateStatus();
        Redraw();
    }

    /// <summary>
    /// Edit mode follows the selection: clicking another support edits that one, an empty click
    /// leaves. A drag in progress is not disturbed.
    /// </summary>
    private void FollowSelectionInSupportEdit()
    {
        if (_editFollowingSelection || _editDrag is not null || Document is null ||
            _editSupport is not { } element) return;
        if (Document.IsSupportSelected(element)) return;
        if (Document.SupportSelection.Count == 0)
        {
            EndSupportEdit();
            return;
        }
        BeginSupportEdit(Document.SupportSelection.First());
    }

    /// <summary>The displayed handles of the edited support; empty once it is gone.</summary>
    private List<SupportHandle> SupportEditHandles()
    {
        if (Document is null || _editSupport is not { } element) return [];
        var handles = SupportEditing.HandlesOf(Document.Supports, element);
        handles.RemoveAll(h => !SupportDisplayPolicy.IsElementDisplayed(
            Document.Supports, h.ElementId, SupportDisplay, ClipRange));
        return handles;
    }

    /// <summary>
    /// The gizmos of the edited support, one per handle, sized and placed for this frame. Base
    /// and trunk handles get an X/Y gizmo (they stay on the plate and vertical); junctions and
    /// tips get XYZ (user, 2026-09-09).
    /// </summary>
    private void RefreshSupportEditGizmos()
    {
        var handles = SupportEditHandles();
        var height = (float)Bounds.Height;
        while (_editGizmos.Count > handles.Count) _editGizmos.RemoveAt(_editGizmos.Count - 1);
        for (var i = 0; i < handles.Count; i++)
        {
            var gizmo = i < _editGizmos.Count ? _editGizmos[i].Gizmo : new Gizmo();
            gizmo.PixelSize = EditGizmoPixels;
            gizmo.LineWidth = EditGizmoLineWidth;
            gizmo.ShowZ = handles[i].Kind is SupportHandleKind.Junction or SupportHandleKind.Tip;
            gizmo.Update(Camera, new Aabb(handles[i].Position, handles[i].Position), height);
            var active = _editDrag == handles[i] ? _editDragAxis
                : _editDrag is null && _editHover == handles[i] ? _editHoverAxis : GizmoHandle.None;
            gizmo.Hovered = active;
            gizmo.Active = _editDrag == handles[i] ? _editDragAxis : GizmoHandle.None;
            if (i < _editGizmos.Count) _editGizmos[i] = (handles[i], gizmo);
            else _editGizmos.Add((handles[i], gizmo));
        }
    }

    /// <summary>The handle and gizmo arrow or square under the mouse, nearest first.</summary>
    private (SupportHandle Handle, GizmoHandle Axis)? HitSupportEditHandle(Vector2 mouse)
    {
        RefreshSupportEditGizmos();
        var w = (float)Bounds.Width;
        var h = (float)Bounds.Height;
        foreach (var (handle, gizmo) in _editGizmos)
        {
            var axis = gizmo.HitTest(Camera, mouse, w, h);
            if (axis != GizmoHandle.None) return (handle, axis);
        }
        return null;
    }

    /// <summary>
    /// Where the mouse lands on the drag's constraint: the plane through <paramref name="origin"/>
    /// for a square, the closest point on the axis line for an arrow. Null when the ray runs
    /// parallel to the constraint or behind the eye.
    /// </summary>
    private Vector3? DragConstraintPoint(Vector2 mouse, Vector3 origin, GizmoHandle axis)
    {
        var ray = Camera.ScreenToRay(mouse.X, mouse.Y, (float)Bounds.Width, (float)Bounds.Height);
        var (_, constraint, plane) = Gizmo.ToTransform(axis);
        var dir = ModalTransform.AxisVector(constraint);
        if (plane)
        {
            var denom = Vector3.Dot(ray.Direction, dir);
            if (MathF.Abs(denom) < 1e-5f) return null;
            var t = Vector3.Dot(origin - ray.Origin, dir) / denom;
            return t < 0f ? null : ray.At(t);
        }
        // Closest point on the axis line to the mouse ray.
        var w0 = origin - ray.Origin;
        var b = Vector3.Dot(dir, ray.Direction);
        var d = Vector3.Dot(dir, w0);
        var e = Vector3.Dot(ray.Direction, w0);
        var det = 1f - b * b;
        if (det < 1e-6f) return null;
        var s = (b * e - d) / det;
        return origin + dir * s;
    }

    private void BeginSupportEditDrag(SupportHandle handle, GizmoHandle axis, Vector2 mouse)
    {
        if (Document is null) return;
        _editDragBefore = SupportEditing.AffectedByHandle(Document.Supports, handle)
            .Select(n => (n, n.Position, n.SurfaceNormal)).ToList();
        _editDragAnchor = DragConstraintPoint(mouse, handle.Position, axis) ?? handle.Position;
        _editDrag = handle;
        _editDragAxis = axis;
        UpdateStatus();
    }

    private void UpdateSupportEditDrag(Vector2 mouse, KeyModifiers modifiers)
    {
        if (Document is null || _editDrag is not { } handle || _editDragBefore is null) return;
        if (DragConstraintPoint(mouse, _editDragAnchor, _editDragAxis) is not { } point) return;
        var delta = point - _editDragAnchor;
        var (_, constraint, plane) = Gizmo.ToTransform(_editDragAxis);
        var movesX = plane ? constraint != AxisConstraint.X : constraint == AxisConstraint.X;
        var movesY = plane ? constraint != AxisConstraint.Y : constraint == AxisConstraint.Y;
        var snaps = handle.Kind is SupportHandleKind.Base or SupportHandleKind.Trunk &&
            Document.SupportSettings.UseBaseGrid && !modifiers.HasFlag(KeyModifiers.Shift);
        if (snaps)
        {
            // The base lands on the grid (SUPPORT-GEOMETRY-SPEC "Bases sit on an imaginary grid")
            // along the axes being dragged, and the rest of the column keeps its offset.
            var anchor = _editDragBefore.FirstOrDefault(b => b.Node.Type == SupportNodeType.Base);
            if (anchor.Node is null) anchor = _editDragBefore[0];
            var origin = new Vector2(anchor.Position.X, anchor.Position.Y);
            var snapped = SupportEditing.SnapToBaseGrid(origin + new Vector2(delta.X, delta.Y),
                Document.SupportSettings.BaseGridPitch) - origin;
            if (movesX) delta.X = snapped.X;
            if (movesY) delta.Y = snapped.Y;
        }
        SupportEditing.Translate(Document.Supports,
            _editDragBefore.Select(b => (b.Node, b.Position)).ToList(), delta);
    }

    private void CommitSupportEditDrag()
    {
        if (Document is not null && _editDrag is { } handle && _editDragBefore is not null)
        {
            var entries = _editDragBefore
                .Select(b => new SetSupportPositionsCommand.Entry(b.Node, b.Position, b.Normal, b.Node.Position, b.Node.SurfaceNormal))
                .Where(e => e.BeforePosition != e.AfterPosition || e.BeforeNormal != e.AfterNormal)
                .ToList();
            if (entries.Count > 0)
            {
                var name = handle.Kind switch
                {
                    SupportHandleKind.Base => "Move base",
                    SupportHandleKind.Trunk => "Move trunk",
                    SupportHandleKind.Tip => "Move tip",
                    _ => "Move junction",
                };
                Document.Execute(new SetSupportPositionsCommand(Document.Supports, entries, name));
            }
        }
        _editDrag = null;
        _editDragAxis = GizmoHandle.None;
        _editDragBefore = null;
        UpdateStatus();
    }

    private void CancelSupportEditDrag()
    {
        if (Document is not null && _editDragBefore is not null)
        {
            foreach (var (node, position, normal) in _editDragBefore)
            {
                node.Position = position;
                node.SurfaceNormal = normal;
            }
            Document.Supports.NotifyChanged();
        }
        _editDrag = null;
        _editDragAxis = GizmoHandle.None;
        _editDragBefore = null;
        UpdateStatus();
    }

    /// <summary>
    /// A gizmo on every handle, drawn through everything so a base under the model or a
    /// junction inside a forest can still be grabbed; a small marker at each pivot names the
    /// handle kind by colour.
    /// </summary>
    private void AppendSupportEditHandles(List<OverlayLine> lines)
    {
        if (_editSupport is null || Document is null) return;
        RefreshSupportEditGizmos();
        if (_editGizmos.Count == 0)
        {
            // The support was deleted or undone away under us.
            EndSupportEdit();
            return;
        }
        foreach (var (handle, gizmo) in _editGizmos)
        {
            var colour = handle.Kind switch
            {
                SupportHandleKind.Base => EditBaseColor,
                SupportHandleKind.Junction => EditJunctionColor,
                SupportHandleKind.Tip => EditTipColor,
                _ => EditTrunkColor,
            };
            var p = handle.Position;
            var size = ContactMarkerHalfSize(p) * 1.5f;
            lines.Add(new OverlayLine(p - Camera.Right * size, p + Camera.Right * size, colour, EditGizmoLineWidth));
            lines.Add(new OverlayLine(p - Camera.Up * size, p + Camera.Up * size, colour, EditGizmoLineWidth));
            gizmo.AppendLines(Camera, lines);
        }
    }

    // ----- Tip move (G with a single tip selected) -----

    private Guid? _tipDrag;
    private List<(Danslicer.Core.Supports.SupportNode Node, Vector3 Position, Vector3 Normal)>? _tipDragBefore;

    private Guid? SelectedTip()
    {
        if (Document is null || Document.SupportSelection.Count != 1) return null;
        var id = Document.SupportSelection.First();
        return Document.Supports.TryGetNode(id, out var node) &&
            node.Type == Danslicer.Core.Supports.SupportNodeType.Tip &&
            SupportDisplayPolicy.IsElementDisplayed(Document.Supports, id, SupportDisplay, ClipRange)
            ? id : null;
    }

    private void BeginTipDrag(Guid tipId)
    {
        if (Document is null) return;
        _tipDragBefore = Danslicer.Core.Supports.SupportEditing.AffectedByTipMove(Document.Supports, tipId)
            .Select(n => (n, n.Position, n.SurfaceNormal)).ToList();
        _tipDrag = tipId;
        UpdateStatus();
    }

    private void UpdateTipDrag(Vector2 mouse)
    {
        if (Document is null || _tipDrag is not { } tipId) return;
        var tip = Document.Supports.GetNode(tipId);
        var hit = PickSurface(mouse, out _, out var point, out var normal);
        if (hit is null) return;
        // Constrained to the mesh the tip contacts; a tip without a recorded contact takes any.
        if (tip.ContactObjectId is { } contactId && hit.Id != contactId) return;
        Danslicer.Core.Supports.SupportEditing.MoveTipVertical(Document.Supports, tipId, point, normal);
    }

    private void CommitTipDrag()
    {
        if (Document is not null && _tipDrag is not null && _tipDragBefore is not null)
        {
            var entries = _tipDragBefore
                .Select(b => new SetSupportPositionsCommand.Entry(b.Node, b.Position, b.Normal, b.Node.Position, b.Node.SurfaceNormal))
                .Where(e => e.BeforePosition != e.AfterPosition || e.BeforeNormal != e.AfterNormal)
                .ToList();
            if (entries.Count > 0)
                Document.Execute(new SetSupportPositionsCommand(Document.Supports, entries, "Move tip"));
        }
        _tipDrag = null;
        _tipDragBefore = null;
        UpdateStatus();
    }

    private void CancelTipDrag()
    {
        if (Document is not null && _tipDragBefore is not null)
        {
            foreach (var (node, position, normal) in _tipDragBefore)
            {
                node.Position = position;
                node.SurfaceNormal = normal;
            }
            Document.Supports.NotifyChanged();
        }
        _tipDrag = null;
        _tipDragBefore = null;
        UpdateStatus();
    }

    private const float SupportPickRadiusPixels = 8f;

    /// <summary>
    /// The support element nearest the cursor within the pick radius: tips first (they are small
    /// and sit on segments), then segments. Returns its camera distance for depth arbitration
    /// against a surface hit.
    /// </summary>
    /// <summary>
    /// The model owning the support element under <paramref name="mouse"/>, when one is nearer
    /// than <paramref name="objectDistance"/>. Layout treats a model and its supports as one
    /// object: there is no support selection there, so a click on a support means the model.
    /// </summary>
    private SceneObject? SupportOwnerAt(Vector2 mouse, float objectDistance)
    {
        if (Document is null) return null;
        if (PickSupportElement(mouse, out var supportDistance) is not { } element) return null;
        if (supportDistance > objectDistance + 0.5f) return null;
        return Document.Supports.OwningObjectId(element) is { } objectId
            ? Document.Scene.Objects.FirstOrDefault(obj => obj.Id == objectId)
            : null;
    }

    private Guid? PickSupportElement(Vector2 mouse, out float cameraDistance)
    {
        cameraDistance = float.PositiveInfinity;
        var supports = _structurePreviewGraph ?? Document?.Supports;
        if (Document is null || supports is null || supports.NodeCount == 0) return null;
        // What is not drawn is not pickable: a hidden model's supports are neither.
        var hiddenOwners = SupportOwnerVisibility.HiddenObjectIds(Document.Scene.Objects);
        var w = (float)Bounds.Width;
        var h = (float)Bounds.Height;
        var eye = Camera.Eye;

        Guid? best = null;
        var bestScore = float.PositiveInfinity;
        foreach (var node in supports.Nodes)
        {
            if (SupportDisplayPolicy.IsHiddenBy(node.Hidden, SupportDisplay) ||
                node.Type != Danslicer.Core.Supports.SupportNodeType.Tip ||
                !SupportDisplayPolicy.IsNodeDisplayed(supports, node, SupportDisplay, ClipRange)) continue;
            if (SupportOwnerVisibility.IsOwnedByHidden(node, hiddenOwners)) continue;
            if (Camera.WorldToScreen(node.Position, w, h) is not { } p) continue;
            var d = Vector2.Distance(mouse, p);
            if (d > SupportPickRadiusPixels || d >= bestScore) continue;
            bestScore = d;
            best = node.Id;
            cameraDistance = Vector3.Distance(eye, node.Position);
        }
        if (best is not null) return best; // a tip within reach wins over the segment under it

        foreach (var segment in supports.Segments)
        {
            if (SupportDisplayPolicy.IsHiddenBy(segment.Hidden, SupportDisplay) ||
                !SupportDisplayPolicy.IsSegmentDisplayed(
                    supports, segment, SupportDisplay, ClipRange)) continue;
            if (SupportOwnerVisibility.IsOwnedByHidden(supports, segment.Id, hiddenOwners)) continue;
            var a = supports.GetNode(segment.NodeA);
            var b = supports.GetNode(segment.NodeB);
            if (SupportDisplayPolicy.IsHiddenBy(a.Hidden, SupportDisplay) ||
                SupportDisplayPolicy.IsHiddenBy(b.Hidden, SupportDisplay)) continue;
            if (!ClipRange.TryClipSegment(a.Position, b.Position,
                    out var visibleA, out var visibleB) ||
                Camera.WorldToScreen(visibleA, w, h) is not { } pa ||
                Camera.WorldToScreen(visibleB, w, h) is not { } pb) continue;
            var ab = pb - pa;
            var len2 = ab.LengthSquared();
            var t = len2 < 1e-6f ? 0f : Math.Clamp(Vector2.Dot(mouse - pa, ab) / len2, 0f, 1f);
            var d = Vector2.Distance(mouse, pa + ab * t);
            if (d > SupportPickRadiusPixels || d >= bestScore) continue;
            bestScore = d;
            best = segment.Id;
            cameraDistance = Vector3.Distance(eye, Vector3.Lerp(visibleA, visibleB, t));
        }

        // A member line within the ordinary pick radius has an unambiguous, consistently sized
        // target and wins over the much larger base surface behind it.
        if (best is not null) return best;

        // Project the complete horizontal rim. A world +X radius alone is not the screen-space
        // radius in oblique views; the ordered samples cover the actual projected ellipse/conic.
        foreach (var node in supports.Nodes)
        {
            if (SupportDisplayPolicy.IsHiddenBy(node.Hidden, SupportDisplay) ||
                node.Type != Danslicer.Core.Supports.SupportNodeType.Base ||
                !SupportDisplayPolicy.IsNodeDisplayed(supports, node, SupportDisplay, ClipRange)) continue;
            if (SupportOwnerVisibility.IsOwnedByHidden(node, hiddenOwners)) continue;
            if (Camera.WorldToScreen(node.Position, w, h) is not { } p) continue;
            var d = Vector2.Distance(mouse, p);
            float? score = d <= SupportPickRadiusPixels ? d / SupportPickRadiusPixels : null;
            if (node.BaseShape != Danslicer.Core.Supports.SupportBaseShape.None)
            {
                const int rimSamples = 24;
                var rim = new List<Vector2>(rimSamples);
                var radius = node.BaseDiameter * 0.5f;
                for (var index = 0; index < rimSamples; index++)
                {
                    var angle = index * MathF.Tau / rimSamples;
                    var world = node.Position + new Vector3(
                        MathF.Cos(angle) * radius, MathF.Sin(angle) * radius, 0);
                    if (Camera.WorldToScreen(world, w, h) is { } projected) rim.Add(projected);
                }
                var discScore = Danslicer.Core.Supports.SupportDiscPicking.NormalizedScore(
                    mouse, p, rim);
                if (discScore is not null) score = discScore;
            }
            if (score is null || score.Value >= bestScore) continue;
            bestScore = score.Value;
            best = node.Id;
            cameraDistance = Vector3.Distance(eye, node.Position);
        }
        return best;
    }

    private static double PointDistance(Point a, Point b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private bool IsSupportPointVisible(Vector3 point, IReadOnlySet<Guid> visibleObjectIds)
    {
        if (Document is null || !ClipRange.Contains(point)) return false;
        var w = (float)Bounds.Width;
        var h = (float)Bounds.Height;
        if (Camera.WorldToScreen(point, w, h) is not { } screen) return false;
        var ray = Camera.ScreenToRay(screen.X, screen.Y, w, h);
        var hit = Document.RaycastMeshes(ray.Origin, ray.Direction, float.PositiveInfinity,
            visibleObjectIds);
        return hit is null || Vector3.Distance(Camera.Eye, point) <= hit.Value.Distance + 0.5f;
    }

    private static readonly Vector4 SupportSelectedColor = new(1f, 1f, 1f, 1f);
    private const float DisabledSupportOpacity = 0.35f;

    private void MarkSupportMeshesDirty()
    {
        _placementPickParts = null;
        _supportMeshesDirty = true;
        _selectionMeshDirty = true; // selected elements may have moved or vanished
        Redraw();
    }

    private void MarkSelectionMeshDirty()
    {
        _selectionMeshDirty = true;
        Redraw();
    }

    private void MarkRegionOverlayDirty()
    {
        _regionOverlayDirty = true;
        Redraw();
    }

    /// <summary>
    /// Rebuilds the painted-region overlay for every visible object (DESIGN 8.3). Drawn as
    /// depth-overlay aux meshes, so the tint sits exactly on the surface it belongs to instead of
    /// z-fighting with it, and so both render paths get it from the same geometry.
    /// </summary>
    private void RebuildRegionOverlays()
    {
        _regionOverlayDirty = false;
        _regionOverlays.Clear();
        if (Document is not { } document) return;
        if (_layFlatPick && _layFlatHoverObject is { } target &&
            document.Scene.Objects.Contains(target) && target.RenderState != RenderState.Hidden &&
            _layFlatHoverFaces is { Count: > 0 } targetFaces &&
            SupportRegionOverlay.Build(target.Mesh, target.Transform.ToMatrix(), targetFaces) is { } patch)
            _regionOverlays.Add(new AuxMeshDraw(patch, new Vector3(0.15f, 0.65f, 1f),
                0.55f, DepthOverlay: true));
        // Regions are a Support-mode concern. In Layout the user is arranging models, and a
        // painted tint there is noise on top of the thing they are trying to position.
        if (!SupportSelectionMode) return;
        foreach (var obj in document.Scene.Objects)
        {
            if (obj.RenderState == RenderState.Hidden || obj.Regions.IsEmpty) continue;
            foreach (var (mesh, color) in Danslicer.Core.Supports.SupportRegionOverlay.Build(
                         obj.Mesh, obj.Transform.ToMatrix(), obj.Regions))
                _regionOverlays.Add(new AuxMeshDraw(mesh, color,
                    Danslicer.Core.Supports.SupportRegionOverlay.Opacity, DepthOverlay: true));
        }

        // The patch under the cursor, drawn last so it reads over whatever is already painted:
        // this is what a click would take.
        if (RegionHoverObject is { } hovered && hovered.RenderState != RenderState.Hidden &&
            RegionHoverFaces is { Count: > 0 } faces &&
            Danslicer.Core.Supports.SupportRegionOverlay.Build(
                hovered.Mesh, hovered.Transform.ToMatrix(), faces) is { } preview)
        {
            _regionOverlays.Add(new AuxMeshDraw(preview,
                Danslicer.Core.Supports.SupportRegionOverlay.HoverColor,
                Danslicer.Core.Supports.SupportRegionOverlay.HoverOpacity, DepthOverlay: true));
        }
    }

    /// <summary>Re-tessellates only the selected elements as a white highlight overlay.</summary>
    private void RebuildSelectionMesh()
    {
        _selectionMeshDirty = false;
        if (_structurePreviewGraph is { } preview)
        {
            _selectedSupportMesh = SupportDisplayPolicy.ShowsMeshes(SupportDisplay) && _structurePreviewHighlights.Count > 0
                ? SupportRenderMesh.BuildSelected(preview, _structurePreviewHighlights.Contains,
                    segment => SupportDisplayPolicy.IsSegmentDisplayed(segment.Type, SupportDisplay),
                    node => SupportDisplayPolicy.IsNodeDisplayed(preview, node, SupportDisplay)) : null;
            return;
        }
        _selectedSupportMesh = _structurePreviewGraph is null && Document is { } document && document.SupportSelection.Count > 0 &&
            SupportDisplayPolicy.ShowsMeshes(SupportDisplay)
            ? Danslicer.Core.Supports.SupportRenderMesh.BuildSelected(
                document.Supports, document.IsSupportSelected,
                segment => SupportDisplayPolicy.IsSegmentDisplayed(segment.Type, SupportDisplay),
                node => SupportDisplayPolicy.IsNodeDisplayed(document.Supports, node, SupportDisplay))
            : null;
    }

    /// <summary>
    /// Rebuilds the capsule meshes for support segments — the shaded twin of the slice geometry.
    /// Tips stay as overlay crosses: a sphere at true tip diameter would hide inside the neck.
    /// </summary>
    private void RebuildSupportMeshes()
    {
        _supportMeshesDirty = false;
        _supportMeshes.Clear();
        var supports = _structurePreviewGraph ?? Document?.Supports;
        if (Document is null || supports is null) return;

        var display = SupportDisplay;
        if (!SupportDisplayPolicy.ShowsMeshes(display)) return;
        // A hidden model takes its supports with it, here and in the slicer.
        var hiddenOwners = SupportOwnerVisibility.HiddenObjectIds(Document.Scene.Objects);

        if (display.Mode == SupportDisplayMode.Transparent)
        {
            foreach (var (componentNodes, componentSegments) in supports.Supports())
            {
                // Bracing is excluded from the graph's support components. Assign each brace to
                // its NodeA component so it is emitted exactly once and shares that sort key.
                var segmentIds = new HashSet<Guid>(componentSegments);
                foreach (var brace in supports.Segments)
                    if (brace.Type == SupportSegmentType.Bracing &&
                        componentNodes.Contains(brace.NodeA)) segmentIds.Add(brace.Id);
                var parts = SupportRenderMesh.Build(supports,
                    includeSegment: segment => segmentIds.Contains(segment.Id) &&
                        !SupportOwnerVisibility.IsOwnedByHidden(supports, segment.Id, hiddenOwners) &&
                        SupportDisplayPolicy.IsSegmentDisplayed(segment.Type, display),
                    includeBase: node => componentNodes.Contains(node.Id) &&
                        !SupportOwnerVisibility.IsOwnedByHidden(node, hiddenOwners) &&
                        SupportDisplayPolicy.IsNodeDisplayed(supports, node, display),
                    includeHidden: display.ShowHiddenElements);
                if (parts.Count == 0) continue;
                var origin = componentNodes.Select(id => supports.GetNode(id).Position)
                    .Aggregate(Vector3.Zero, (sum, point) => sum + point) / componentNodes.Count;
                AddSupportParts(parts, origin, TransparentSupportOpacity);
            }
            AppendRaftMeshes(display, TransparentSupportOpacity);
            return;
        }

        // Built without selection state: the selection is a separate small overlay mesh.
        var visibleParts = SupportRenderMesh.Build(supports,
            includeSegment: segment =>
                !SupportOwnerVisibility.IsOwnedByHidden(supports, segment.Id, hiddenOwners) &&
                SupportDisplayPolicy.IsSegmentDisplayed(segment.Type, display),
            includeBase: node => !SupportOwnerVisibility.IsOwnedByHidden(node, hiddenOwners) &&
                SupportDisplayPolicy.IsNodeDisplayed(supports, node, display),
            includeHidden: display.ShowHiddenElements);
        foreach (var part in visibleParts)
            AddSupportPart(part, part.Mesh.Bounds.Center, 1f);
        AppendRaftMeshes(display, 1f);
    }

    /// <summary>
    /// One mesh per visible rafted object, in the base colour: the raft replaces the bases
    /// (spec "Rafts"). The document caches the outline; the mesh is cheap to rebuild with the
    /// support meshes, which are rebuilt exactly when the feet may have moved.
    /// </summary>
    private void AppendRaftMeshes(SupportDisplayConfig display, float opacity)
    {
        if (Document is null || !SupportDisplayPolicy.ShowsRafts(display)) return;
        foreach (var obj in Document.Scene.Objects)
        {
            if (obj.RenderState == RenderState.Hidden || obj.Raft is not { } parameters) continue;
            var outline = _structurePreviewGraph is null ? Document.RaftTopOutline(obj)
                : RaftBuilder.TopOutline(_structurePreviewGraph.Nodes
                    .Where(n => n.Type == SupportNodeType.Base && n.Origin.ObjectId == obj.Id && !n.Disabled)
                    .Select(n => new Vector2(n.Position.X, n.Position.Y)).ToList(), parameters);
            if (outline is null || outline.Count == 0) continue;
            var mesh = RaftGeometry.BuildMesh(new RaftShape(parameters, outline));
            if (mesh is null) continue;
            _supportMeshes.Add(new SupportMeshBatch(new AuxMeshDraw(mesh,
                new Vector3(BaseColor.X, BaseColor.Y, BaseColor.Z), opacity), mesh.Bounds.Center));
        }
    }

    private const float TransparentSupportOpacity = 0.28f;

    private void AddSupportParts(IEnumerable<SupportRenderPart> parts, Vector3 sortOrigin,
        float opacity)
    {
        foreach (var part in parts) AddSupportPart(part, sortOrigin, opacity);
    }

    private void AddSupportPart(SupportRenderPart part, Vector3 sortOrigin, float opacity)
    {
        var color = part.Kind switch
        {
            SupportRenderKind.Tip => TipColor,
            SupportRenderKind.Trunk => TrunkColor,
            SupportRenderKind.Bracing => BracingColor,
            SupportRenderKind.Base => BaseColor,
            _ => BranchColor,
        };
        _supportMeshes.Add(new SupportMeshBatch(new AuxMeshDraw(
            part.Mesh,
            new Vector3(color.X, color.Y, color.Z),
            opacity * (part.Disabled ? DisabledSupportOpacity : 1f)), sortOrigin));
    }

    private void AppendSupportLines(List<OverlayLine> lines)
    {
        var supports = _structurePreviewGraph ?? Document?.Supports;
        if (Document is null || supports is null) return;
        // Lines and contact markers are drawn here rather than as meshes, so they need the same
        // ownership filter: a hidden model must not leave its tip markers floating in the air.
        var hiddenOwners = SupportOwnerVisibility.HiddenObjectIds(Document.Scene.Objects);
        if (SupportDisplayPolicy.ShowsLines(SupportDisplay))
        {
            foreach (var segment in supports.Segments)
            {
                if (SupportDisplayPolicy.IsHiddenBy(segment.Hidden, SupportDisplay) ||
                    !SupportDisplayPolicy.IsSegmentDisplayed(
                        supports, segment, SupportDisplay, ClipRange)) continue;
                if (SupportOwnerVisibility.IsOwnedByHidden(supports, segment.Id, hiddenOwners)) continue;
                var a = supports.GetNode(segment.NodeA);
                var b = supports.GetNode(segment.NodeB);
                if (SupportDisplayPolicy.IsHiddenBy(a.Hidden, SupportDisplay) ||
                    SupportDisplayPolicy.IsHiddenBy(b.Hidden, SupportDisplay)) continue;
                var color = Document.IsSupportSelected(segment.Id)
                    ? SupportSelectedColor
                    : segment.Type switch
                    {
                        SupportSegmentType.Tip => TipColor,
                        SupportSegmentType.Trunk => TrunkColor,
                        SupportSegmentType.Bracing => BracingColor,
                        _ => BranchColor,
                    };
                if (segment.Disabled || a.Disabled || b.Disabled)
                    color.W *= DisabledSupportOpacity;
                lines.Add(new OverlayLine(a.Position, b.Position, color));
            }
        }

        if (!SupportDisplayPolicy.ShowsContactMarkers(SupportDisplay)) return;
        foreach (var node in supports.Nodes)
        {
            if (SupportDisplayPolicy.IsHiddenBy(node.Hidden, SupportDisplay) ||
                node.Type != SupportNodeType.Tip ||
                !SupportDisplayPolicy.IsNodeDisplayed(supports, node, SupportDisplay, ClipRange)) continue;
            if (SupportOwnerVisibility.IsOwnedByHidden(node, hiddenOwners)) continue;
            var color = Document.IsSupportSelected(node.Id) ? SupportSelectedColor : TipMarkerColor;
            var p = node.Position;
            if (SupportDisplay.Mode is SupportDisplayMode.ContactPoints or
                SupportDisplayMode.Transparent)
            {
                var size = ContactMarkerHalfSize(p);
                lines.Add(new OverlayLine(p - Camera.Right * size, p + Camera.Right * size, color));
                lines.Add(new OverlayLine(p - Camera.Up * size, p + Camera.Up * size, color));
            }
            else
            {
                // Preserve Full's established marker exactly; Lines resurrects that same path.
                const float size = 0.8f;
                lines.Add(new OverlayLine(p - new Vector3(size, 0, 0), p + new Vector3(size, 0, 0), color));
                lines.Add(new OverlayLine(p - new Vector3(0, size, 0), p + new Vector3(0, size, 0), color));
                lines.Add(new OverlayLine(p - new Vector3(0, 0, size), p + new Vector3(0, 0, size), color));
            }
        }
    }

    /// <summary>
    /// A screen-pixel length in world units at <paramref name="point"/>. Perspective makes this
    /// depend on how far away the point is; orthographic does not, which is why the camera is
    /// asked rather than assumed.
    /// </summary>
    private float PixelsToWorld(Vector3 point, double pixels)
    {
        var height = MathF.Max((float)Bounds.Height, 1f);
        var viewHeight = Camera.Orthographic
            ? Camera.ViewHeightAtTarget
            : 2f * MathF.Max(Vector3.Dot(point - Camera.Eye, Camera.ViewDirection), Camera.Near) *
                MathF.Tan(Camera.FovDegrees * 0.5f * MathF.PI / 180f);
        return viewHeight / height * (float)pixels;
    }

    private float WorldRadiusAt(Vector3 point) => PixelsToWorld(point, RegionBrushRadiusPixels);

    /// <summary>
    /// The brush ring: a circle on the screen plane at the point under the cursor, so the user
    /// can see what one dab would cover before pressing. Drawn in the overlay rather than as a
    /// 2D cursor because it has to sit on the surface it is about to paint.
    /// </summary>
    private void AppendBrushCursor(List<OverlayLine> lines)
    {
        if (!SupportSelectionMode || !RegionPickMode || !RegionBrushMode) return;
        if (_brushCursor is not { } centre) return;
        var radius = WorldRadiusAt(centre);
        if (radius <= 0) return;

        const int steps = 48;
        var right = Camera.Right * radius;
        var up = Camera.Up * radius;
        var colour = new Vector4(0.95f, 0.97f, 1f, 0.9f);
        var previous = centre + right;
        for (var i = 1; i <= steps; i++)
        {
            var angle = i * MathF.Tau / steps;
            var next = centre + right * MathF.Cos(angle) + up * MathF.Sin(angle);
            lines.Add(new OverlayLine(previous, next, colour));
            previous = next;
        }
    }

    private static readonly Vector4 BaseGridMarkerColor = new(1f, 0.85f, 0.2f, 0.8f);
    private const int MaxBaseGridMarkers = 20000;

    /// <summary>
    /// The base lattice on the plate, as small crosses at every lattice point, whenever the base
    /// grid is on in Support mode (user request 2026-09-08): the user sees where trunks may
    /// stand before generating or parenting. Plate-origin aligned, like the router's own rule.
    /// Depth-tested, so the model hides the points beneath it as it hides the plate grid.
    /// </summary>
    private void AppendBaseGridMarkers(List<OverlayLine> lines)
    {
        if (Document is null || !SupportSelectionMode || !Document.SupportSettings.UseBaseGrid) return;
        var pitch = Document.SupportSettings.BaseGridPitch;
        if (pitch <= 0.1f) return;
        var volume = Document.Printer.BuildVolume;
        var halfX = volume.X * 0.5f;
        var halfY = volume.Y * 0.5f;
        var countX = (int)MathF.Floor(halfX / pitch);
        var countY = (int)MathF.Floor(halfY / pitch);
        if ((2L * countX + 1) * (2L * countY + 1) > MaxBaseGridMarkers) return;
        var size = MathF.Min(pitch * 0.15f, 1f);
        // A hair above the plate so the crosses do not fight the plate surface for depth.
        const float z = 0.02f;
        for (var i = -countX; i <= countX; i++)
            for (var j = -countY; j <= countY; j++)
            {
                var p = new Vector3(i * pitch, j * pitch, z);
                lines.Add(new OverlayLine(p - new Vector3(size, 0, 0), p + new Vector3(size, 0, 0), BaseGridMarkerColor));
                lines.Add(new OverlayLine(p - new Vector3(0, size, 0), p + new Vector3(0, size, 0), BaseGridMarkerColor));
            }
    }

    /// <summary>Four screen pixels, clamped in world space so extreme zooms stay sensible.</summary>
    private float ContactMarkerHalfSize(Vector3 point)
    {
        var height = MathF.Max((float)Bounds.Height, 1f);
        var viewHeight = Camera.Orthographic
            ? Camera.ViewHeightAtTarget
            : 2f * MathF.Max(Vector3.Dot(point - Camera.Eye, Camera.ViewDirection), Camera.Near) *
                MathF.Tan(Camera.FovDegrees * 0.5f * MathF.PI / 180f);
        return Math.Clamp(viewHeight / height * 4f, 0.2f, 2f);
    }

    private void SelectDisplayedSupportComponent(Guid elementId, bool additive)
    {
        if (Document is null) return;
        Guid seed;
        if (Document.Supports.TryGetNode(elementId, out var node)) seed = node.Id;
        else if (Document.Supports.TryGetSegment(elementId, out var segment)) seed = segment.NodeA;
        else return;
        var component = Document.Supports.Component(seed);
        var ids = component.Nodes.Concat(component.Segments)
            .Where(id => SupportDisplayPolicy.IsElementDisplayed(
                Document.Supports, id, SupportDisplay, ClipRange));
        Document.SelectSupportElements(ids, additive);
    }

    // ----- Snapping -----

    /// <summary>Snap mode is the toggle, inverted while Ctrl is held, as in Blender.</summary>
    private void ApplySnap(KeyModifiers? modifiers = null)
    {
        if (modifiers is { } m) _ctrlHeld = m.HasFlag(KeyModifiers.Control);
        if (_modal is null) return;
        var snap = SnapEnabled ^ _ctrlHeld;
        if (_modal.Snap != snap)
        {
            _modal.Snap = snap;
            _modal.Refresh();
        }
    }

    // ----- Keyboard input -----

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (StructurePreviewActive)
        {
            // The preview blocks editing shortcuts, but Undo must still reverse its operation.
            if (Configuration.WindowKeymap.GetGesture(Configuration.AppConfig.Current,
                    Configuration.WindowKeymap.Undo).Matches(e))
            {
                Document?.Undo();
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Escape) StructurePreviewCancelRequested?.Invoke();
            e.Handled = true;
            return;
        }
        Log($"key {e.Key} mods={e.KeyModifiers}");
        if (Document is null || _modal is null) return;

        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        var ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        var handled = true;
        string? statusAfterUpdate = null;

        if (e.Key is Key.LeftCtrl or Key.RightCtrl)
        {
            ApplySnap(e.KeyModifiers | KeyModifiers.Control);
            UpdateStatus();
            return;
        }

        // Tool shortcuts must be processed before the active gesture consumes its keys.
        if (SupportSelectionMode && !_modal.IsActive &&
            (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Meta)) == 0)
        {
            GuidedTool? tool = e.Key switch
            {
                Key.T when !shift => GuidedTool.Place, Key.L when !shift => GuidedTool.Line,
                Key.P when !shift => GuidedTool.Polygon, Key.E when !shift => GuidedTool.Edge,
                Key.R when !shift => GuidedTool.Ring, Key.C when !shift => GuidedTool.Contour,
                Key.D => shift ? GuidedTool.Thin : GuidedTool.Densify, _ => null
            };
            if (tool is { } requested)
            {
                StartGuidedTool(requested);
                e.Handled = true;
                return;
            }
        }

        if (_manualBraceMode && e.Key == Key.Escape)
        {
            EndManualBrace(); e.Handled = true; return;
        }
        if (_lineGesture is { } lineGesture)
        {
            switch (e.Key)
            {
                case Key.Enter: case Key.Space: CommitLineGesture(); break;
                case Key.Escape: CancelLineGesture(); break;
                // Backspace edits a typed pitch first, then the route; with nothing left, it cancels.
                case Key.Back when _linePitchInput.Length > 0:
                    _linePitchInput = _linePitchInput[..^1];
                    ApplyLinePitchInput();
                    break;
                case Key.Back when lineGesture.HasVertices:
                    lineGesture.RemoveLastVertex();
                    RefreshLineGesturePreview();
                    break;
                case Key.Back: CancelLineGesture(); break;
                case Key.OemPeriod: case Key.Decimal: TypeLinePitch('.'); break;
                case >= Key.D0 and <= Key.D9: TypeLinePitch((char)('0' + (e.Key - Key.D0))); break;
                case >= Key.NumPad0 and <= Key.NumPad9: TypeLinePitch((char)('0' + (e.Key - Key.NumPad0))); break;
                default: handled = false; break;
            }
            if (handled)
            {
                UpdateStatus();
                e.Handled = true;
            }
            return;
        }

        if (_modal.IsActive)
        {
            switch (e.Key)
            {
                case Key.X: _modal.SetAxis(AxisConstraint.X, shift); break;
                case Key.Y: _modal.SetAxis(AxisConstraint.Y, shift); break;
                case Key.Z: _modal.SetAxis(AxisConstraint.Z, shift); break;
                case Key.G: _modal.SwitchMode(TransformMode.Move); break;
                case Key.R: _modal.SwitchMode(TransformMode.Rotate); break;
                case Key.S: _modal.SwitchMode(TransformMode.Scale); break;
                case Key.Enter: case Key.Space: _modal.Confirm(); _gizmoDragging = false; break;
                case Key.Escape: _modal.Cancel(); _gizmoDragging = false; break;
                case Key.Back: _modal.Backspace(); break;
                case Key.OemPeriod: case Key.Decimal: _modal.TypeCharacter('.'); break;
                case Key.OemMinus: case Key.Subtract: _modal.TypeCharacter('-'); break;
                case >= Key.D0 and <= Key.D9: _modal.TypeCharacter((char)('0' + (e.Key - Key.D0))); break;
                case >= Key.NumPad0 and <= Key.NumPad9: _modal.TypeCharacter((char)('0' + (e.Key - Key.NumPad0))); break;
                default: handled = false; break;
            }
        }
        else
        {
            var mouse = new Vector2((float)_lastPointer.X, (float)_lastPointer.Y);
            var w = (float)Bounds.Width;
            var h = (float)Bounds.Height;
            switch (e.Key)
            {
                case Key.Enter when _tipDrag is not null: CommitTipDrag(); break;
                case Key.Escape when _tipDrag is not null: CancelTipDrag(); break;
                // Support edit mode (user, 2026-09-08): Space enters and leaves, handles drag.
                case Key.Escape when _editDrag is not null: CancelSupportEditDrag(); break;
                case Key.Escape when _placementMode: EndPlacementMode(); break;
                case Key.Space when !ctrl && SupportSelectionMode: ToggleSupportEdit(); break;
                case Key.Escape when _editSupport is not null: EndSupportEdit(); break;
                // G with one tip selected moves the tip along the surface; otherwise the object modal.
                case Key.G when !ctrl && SupportSelectionMode && SelectedTip() is { } tipId: BeginTipDrag(tipId); break;
                case Key.G when !ctrl && !SupportSelectionMode: ApplySnap(e.KeyModifiers); _modal.Begin(TransformMode.Move, mouse, w, h); break;
                case Key.R when !ctrl && !SupportSelectionMode: ApplySnap(e.KeyModifiers); _modal.Begin(TransformMode.Rotate, mouse, w, h); break;
                case Key.S when !ctrl && !SupportSelectionMode: ApplySnap(e.KeyModifiers); _modal.Begin(TransformMode.Scale, mouse, w, h); break;
                case Key.A when e.KeyModifiers.HasFlag(KeyModifiers.Alt) && SupportSelectionMode:
                    Document.ClearSupportSelection();
                    break;
                case Key.A when e.KeyModifiers.HasFlag(KeyModifiers.Alt): Document.ClearSelection(); break;
                case Key.A when !ctrl && SupportSelectionMode:
                    Document.SelectSupportElements(SupportDisplayPolicy.DisplayedElementIds(
                        Document.Supports, SupportDisplay));
                    break;
                case Key.A when !ctrl: Document.SelectAll(); break;
                case Key.B when !ctrl && SupportSelectionMode:
                    _borderSelectArmed = true;
                    statusAfterUpdate = "Border select: drag a box · Shift extends · Esc cancels";
                    break;
                // Lay flat on the face under the cursor; with nothing under it, arm a click pick.
                case Key.F when !ctrl && !SupportSelectionMode:
                    if (!TryLayFlat(mouse)) BeginLayFlatPick();
                    else if (_layFlatPick) EndLayFlatPick();
                    break;
                case Key.H when e.KeyModifiers.HasFlag(KeyModifiers.Alt): Document.UnhideAll(); break;
                case Key.H when shift && !ctrl && SupportSelectionMode: Document.HideUnselectedSupportElements(); break;
                case Key.H when !ctrl && SupportSelectionMode: Document.HideSelectedSupportElements(); break;
                case Key.H when !ctrl: Document.HideSelection(); break;
                // Parenting (SUPPORT-GEOMETRY-SPEC "Parenting"): re-route the selected supports together.
                case Key.J when !ctrl && !shift && SupportSelectionMode: statusAfterUpdate = ParentStatus(); break;
                // Bracing (SUPPORT-GEOMETRY-SPEC "Bracing"): K braces the selected supports, Shift+K unbraces.
                case Key.K when !ctrl && shift && SupportSelectionMode: statusAfterUpdate = UnbraceStatus(); break;
                case Key.K when !ctrl && SupportSelectionMode: statusAfterUpdate = BraceStatus(); break;
                case Key.Escape when _marqueeStart is not null:
                    _marqueeStart = null;
                    _pendingClickSupport = null;
                    MarqueeRect = null;
                    break;
                case Key.Escape when _layFlatPick: EndLayFlatPick(); break;
                case Key.Escape when _borderSelectArmed: _borderSelectArmed = false; break;
                case Key.Escape when Document.SupportSelection.Count > 0: Document.ClearSupportSelection(); break;
                case Key.Escape: ClearObjectSelection(); break;
                case Key.Delete when Document.SupportSelection.Count > 0: Document.DeleteSupportSelection(); break;
                case Key.Home: FrameAll(); break;
                case Key.OemPeriod: case Key.Decimal: FrameSelected(); break;
                case Key.Tab when shift && !SupportSelectionMode: SnapEnabled = !SnapEnabled; break;
                case Key.Tab when shift: handled = false; break;
                case Key.Tab: ToggleViewRequested?.Invoke(); break;
                // Numpad views, with the main digit row as an always-available fallback for keyboards
                // without a numpad (Blender's "emulate numpad"). Digits only mean numbers inside a modal tool.
                case Key.NumPad1: case Key.D1: SetView(c => { if (ctrl) c.ViewBack(); else c.ViewFront(); }); break;
                case Key.NumPad3: case Key.D3: SetView(c => { if (ctrl) c.ViewLeft(); else c.ViewRight(); }); break;
                case Key.NumPad7: case Key.D7: SetView(c => { if (ctrl) c.ViewBottom(); else c.ViewTop(); }); break;
                case Key.NumPad5: case Key.D5: ToggleProjection(); break;
                default: handled = false; break;
            }
        }

        if (handled)
        {
            UpdateStatus();
            if (statusAfterUpdate is not null) StatusText = statusAfterUpdate;
            e.Handled = true;
        }
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        if (e.Key is Key.LeftCtrl or Key.RightCtrl)
        {
            ApplySnap(e.KeyModifiers & ~KeyModifiers.Control);
            UpdateStatus();
        }
    }

    private void UpdateStatus()
    {
        if (_modal is { IsActive: true })
        {
            StatusText = _modal.StatusText;
            return;
        }
        if (_lineGesture is { } lineGesture)
        {
            var pitch = _linePitchInput.Length > 0 ? $"{_linePitchInput}_" : $"{lineGesture.PitchMm:0.0}";
            var tips = _linePreview.Count == 1 ? "1 tip" : $"{_linePreview.Count} tips";
            var offSurface = lineGesture.CursorPathIsChord ||
                lineGesture is Danslicer.Core.Supports.Guided.SurfacePolygonGesture { ClosingPathIsChord: true };
            var surface = offSurface ? " · OFF SURFACE" : "";
            StatusText = $"{lineGesture.Name}: {tips} · pitch {pitch} mm{surface}  ·  {lineGesture.Hint} · wheel/digits pitch · RMB/Esc cancel";
            return;
        }
        if (_manualBraceMode)
        {
            StatusText = "Manual brace: " + (_manualBraceRefusal ?? (_manualBraceStart is null
                ? "click the first support at the desired height" : "click a second support to place")) +
                (ManualBraceSnap45 ? " � 45� snap" : " � free angle") + " � RMB/Esc leave";
            return;
        }
        if (_placementMode)
        {
            var refused = _placementRefusal is { } reason ? $"{reason} · " : "";
            StatusText = $"Place supports: {refused}click to place the ghosted support · a red tip means nothing fits here · RMB/T/Esc leave";
            return;
        }
        if (_editDrag is { } editDrag)
        {
            StatusText = editDrag.Kind switch
            {
                SupportHandleKind.Tip => "Move tip: drag the arrow or square · release confirm · RMB/Esc cancel",
                SupportHandleKind.Base => "Move base: drag the arrow or square (base grid snaps · Shift free) · release confirm · RMB/Esc cancel",
                SupportHandleKind.Trunk => "Move trunk: drag the column by the arrow or square (base grid snaps · Shift free) · release confirm · RMB/Esc cancel",
                _ => "Move junction: drag the arrow or square · release confirm · RMB/Esc cancel",
            };
            return;
        }
        if (_editSupport is not null)
        {
            StatusText = "Edit support: drag a gizmo · base and trunk in X/Y, junctions and tip in XYZ · click another support to edit it · Space/Esc leave";
            return;
        }
        if (_tipDrag is not null)
        {
            StatusText = "Move tip: drag over the surface · LMB/Enter confirm · RMB/Esc cancel";
            return;
        }
        if (_layFlatPick)
        {
            StatusText = "Lay flat: click a face to rest it on the plate · Esc cancel";
            return;
        }
        if (SupportWaterline?.StatusText is { } waterlineStatus)
        {
            StatusText = waterlineStatus;
            return;
        }
        var projection = Camera.Orthographic ? "Ortho" : "Persp";
        var snap = SnapEnabled ? "Snap on" : "Snap off";
        var spaceMouse = SpaceMouseConnected
            ? (_spaceMouseRotationLock ? " · SpaceMouse (rot locked)" : " · SpaceMouse")
            : "";
        StatusText = SupportSelectionMode
            ? $"{projection}{spaceMouse}  ·  MMB orbit · Shift+MMB pan · wheel zoom · LMB select support · Space edit support · G move tip · T place supports · L support line · P support polygon · E support edge · R support ring · C support contour · D densify · Shift+D thin · J parent · B border select · H hide · Tab workspace · Home frame all · 1/3/7 views · 5 projection"
            : $"{projection} · {snap}{spaceMouse}  ·  MMB orbit · Shift+MMB pan · wheel zoom · LMB select or drag gizmo · G/R/S transform · F lay flat · Shift+Tab snap · Tab workspace · Home frame all · 1/3/7 views · 5 projection";
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _gizmo.ShowMove = ShowMoveGizmo;
        _gizmo.ShowRotate = ShowRotateGizmo;
        _gizmo.ShowScale = ShowScaleGizmo;
        _spaceMouseSession.Attach(this);
        _spaceMouseWindow = TopLevel.GetTopLevel(this) as Window;
        if (_spaceMouseWindow is not null)
        {
            _spaceMouseWindow.Activated += OnSpaceMouseWindowActivated;
            if (_spaceMouseWindow.IsActive) AcquireSpaceMouse();
        }
        UpdateStatus();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (_spaceMouseWindow is not null)
        {
            _spaceMouseWindow.Activated -= OnSpaceMouseWindowActivated;
            _spaceMouseWindow = null;
        }
        ReleaseSpaceMouse();
        _spaceMouseSession.Detach(this);
    }

    private static ViewportControl? _spaceMouseOwner;
    private static readonly SixAxisSession _spaceMouseSession = new(() => OperatingSystem.IsWindows()
        ? new RawInputSpaceMouse(Log, () => _spaceMouseOwner?.OnSpaceMouseInputAvailable()) : throw new PlatformNotSupportedException());
    internal static ViewportControl? SpaceMouseOwner => _spaceMouseOwner;
    internal bool SpaceMouseConnected => _sixAxis?.IsConnected == true &&
        (!OperatingSystem.IsWindows() || _sixAxis is not RawInputSpaceMouse raw || raw.DeviceCount > 0);
    internal ISixAxisInput? SpaceMouseDevice => _sixAxis;
    private Window? _spaceMouseWindow;
    private void OnSpaceMouseWindowActivated(object? sender, EventArgs e) => AcquireSpaceMouse();

    private void AcquireSpaceMouse()
    {
        if (SpaceMouseDiagnostics.Enabled) SpaceMouseDiagnostics.Write("ACQUIRE", $"viewport={GetHashCode()}");
        // Only one viewport consumes the shared receiver. Preferences without a
        // viewport leave the current camera usable for live sensitivity tuning.
        if (_spaceMouseOwner != this) _spaceMouseOwner?.ReleaseSpaceMouse();
        _spaceMouseOwner = this;
        ConnectSpaceMouse();
        UpdateStatus();
    }

    private void ReleaseSpaceMouse()
    {
        if (SpaceMouseDiagnostics.Enabled) SpaceMouseDiagnostics.Write("RELEASE", $"viewport={GetHashCode()}");
        _sixAxisFramesRunning = false;
        _sixAxisFrameGeneration++;
        _sixAxisTiming.Reset();
        _sixAxisFilter.Reset();
        _spaceMouseSession.Release(this);
        _sixAxis = null;
        if (_spaceMouseOwner == this) _spaceMouseOwner = null;
    }

    private void OnSpaceMouseInputAvailable()
    {
        if (SpaceMouseDiagnostics.Enabled)
            SpaceMouseDiagnostics.Write("WAKE", $"viewport={GetHashCode()}");
        // Wake an idle compositor without polling/applying motion inside the native callback.
        // The existing single animation loop still combines axis reports and updates the camera.
        Redraw();
    }

    // ----- SpaceMouse -----

    // Base step sizes per 15 ms at one nominal HID unit, expressed in camera pixel/step units
    // so the camera's own clamping applies. Signs follow 3Dconnexion camera mode: push forward to
    // zoom in, tilt forward to pitch down, twist to yaw. Roll is locked, as designed. The user
    // scales and flips these through the SpaceMouse section of the config window; settings are
    // read every poll tick so tuning applies live. Calibrated 2026-09-03 on the user's SpaceMouse
    // Pro. Raw Input normalizes nominal +/-350 HID units to +/-1; convert back below to retain
    // these base rates. Driver-specific COM scaling may still require sensitivity adjustment.
    private const float SpaceMouseOrbitPixels = 0.12f;
    private const float SpaceMousePanPixels = 0.16f;
    private const float SpaceMouseZoomSteps = 0.0016f;

    private void ConnectSpaceMouse()
    {
        if (!OperatingSystem.IsWindows()) return;
        if (!_sixAxisFramesRunning && TopLevel.GetTopLevel(this) is { } topLevel)
        {
            _sixAxisFramesRunning = true;
            var generation = ++_sixAxisFrameGeneration;
            // Reuse this callback. A released/detached viewport must not restart its old loop.
            Action<TimeSpan> frame = null!;
            frame = _ =>
            {
                if (generation != _sixAxisFrameGeneration || _spaceMouseOwner != this) return;
                PollSpaceMouse();
                if (generation == _sixAxisFrameGeneration && _spaceMouseOwner == this)
                    topLevel.RequestAnimationFrame(frame);
            };
            topLevel.RequestAnimationFrame(frame);
        }
        if (_sixAxis?.IsConnected == true) return;
        var device = _spaceMouseSession.Acquire(this);
        var wasDisconnected = _sixAxis is not null;
        _sixAxis = device;
        _sixAxisTiming.Reset();
        _sixAxisFilter.Reset();
        if (device is null)
        {
            if (wasDisconnected)
            {
                Log("SpaceMouse disconnected; retrying automatically");
                UpdateStatus();
            }
            return;
        }
        UpdateStatus();
    }

    private void PollSpaceMouse()
    {
        if (_spaceMouseOwner != this) return;
        ConnectSpaceMouse();
        if (_sixAxis is null) return;
        var connected = SpaceMouseConnected;
        if (_spaceMouseWasConnected != connected)
        {
            _spaceMouseWasConnected = connected;
            UpdateStatus();
        }
        var timeScale = _sixAxisTiming.NextScale();
        foreach (var press in _sixAxis.DrainButtonPresses()) HandleSpaceMouseButton(press);
        var pollStart = Trace ? Stopwatch.GetTimestamp() : 0;
        var config = Configuration.AppConfig.Current.SpaceMouse;
        var m = _spaceMouseSession.Poll(this, config.Deadzone);
        if (Trace)
        {
            _tracePollMaxMs = Math.Max(_tracePollMaxMs, Stopwatch.GetElapsedTime(pollStart).TotalMilliseconds);
            TraceSpaceMouseStaleness(m);
        }
        var unfilteredMotion = m;
        m = _sixAxisFilter.Apply(m, config.Deadzone, timeScale * 15);
        if (SpaceMouseDiagnostics.Enabled)
            SpaceMouseDiagnostics.Write("FRAME", $"viewport={GetHashCode()} elapsedMs={_sixAxisTiming.LastElapsedMs:F3} scale={timeScale:F5} rawT={unfilteredMotion.Translation} rawR={unfilteredMotion.Rotation} filteredT={m.Translation} filteredR={m.Rotation} orbitSensitivity={config.OrbitSensitivity} panSensitivity={config.PanSensitivity} zoomSensitivity={config.ZoomSensitivity} deadzone={config.Deadzone} rotationLock={_spaceMouseRotationLock} invertYaw={config.InvertOrbitYaw} invertPitch={config.InvertOrbitPitch}");
        if (m.IsZero) return;

        var orbit = SpaceMouseOrbitPixels * RawSpaceMouseState.NominalRange * config.OrbitSensitivity * timeScale;
        var pan = SpaceMousePanPixels * RawSpaceMouseState.NominalRange * config.PanSensitivity * timeScale;
        var zoom = SpaceMouseZoomSteps * RawSpaceMouseState.NominalRange * config.ZoomSensitivity * timeScale;

        var beforeYaw = Camera.Yaw;
        var beforePitch = Camera.Pitch;
        var beforeDistance = Camera.Distance;
        var beforeTarget = Camera.Target;
        var moved = false;
        if (!_spaceMouseRotationLock &&
            (m.Rotation.Y != 0 || m.Rotation.X != 0))
        {
            Camera.Orbit(
                -m.Rotation.Y * orbit * (config.InvertOrbitYaw ? -1f : 1f),
                -m.Rotation.X * orbit * (config.InvertOrbitPitch ? -1f : 1f));
            moved = true;
        }
        if (m.Translation.X != 0 || m.Translation.Y != 0)
        {
            Camera.Pan(
                -m.Translation.X * pan * (config.InvertPanX ? -1f : 1f),
                m.Translation.Y * pan * (config.InvertPanY ? -1f : 1f),
                (float)Bounds.Height);
            moved = true;
        }
        if (m.Translation.Z != 0)
        {
            Camera.Zoom(-m.Translation.Z * zoom * (config.InvertZoom ? -1f : 1f));
            moved = true;
        }
        if (SpaceMouseDiagnostics.Enabled)
            SpaceMouseDiagnostics.Write("CAMERA", $"viewport={GetHashCode()} yawBefore={beforeYaw:F6} yawAfter={Camera.Yaw:F6} pitchBefore={beforePitch:F6} pitchAfter={Camera.Pitch:F6} deltaYawDegrees={(Camera.Yaw - beforeYaw) * 180 / MathF.PI:F5} deltaPitchDegrees={(Camera.Pitch - beforePitch) * 180 / MathF.PI:F5} distanceBefore={beforeDistance:F4} distanceAfter={Camera.Distance:F4} targetBefore={beforeTarget} targetAfter={Camera.Target}");
        if (moved) Redraw();
    }

    // Locks the device's rotation axes only (3Dconnexion convention); MMB orbit stays available.
    private bool _spaceMouseRotationLock;
    private bool _spaceMouseWasConnected;

    // DANSLICER_TRACE=1 diagnostic for choppy motion. Once a second, while the cap is being used,
    // log how many animation callbacks ran, how many polls saw deflection, how many of those
    // carried a value different from the previous poll, and the longest gap between two
    // different readings. Equal successive readings can be legitimate held deflection; actual
    // Raw Input report counts distinguish this from missing device reports.
    // Also reported: the longest managed poll call, the longest GL render, and how many
    // gen0/1/2 collections ran in the window, so a stall can be pinned on the driver call, the
    // frame, or the garbage collector.
    private SixAxisMotion _lastTracedMotion;
    private long _traceWindowStart, _traceLastChange;
    private int _tracePolls, _traceNonZero, _traceChanges;
    private long _traceMaxGapMs;
    private double _tracePollMaxMs, _traceRenderMaxMs;
    private int _traceGc0, _traceGc1, _traceGc2;
    private long _traceMotionReports;

    private void TraceSpaceMouseStaleness(SixAxisMotion m)
    {
        var now = Environment.TickCount64;
        if (_traceWindowStart == 0) _traceWindowStart = now;
        _tracePolls++;
        if (!m.IsZero)
        {
            _traceNonZero++;
            if (m != _lastTracedMotion)
            {
                _traceChanges++;
                if (_traceLastChange != 0) _traceMaxGapMs = Math.Max(_traceMaxGapMs, now - _traceLastChange);
                _traceLastChange = now;
            }
        }
        _lastTracedMotion = m;
        if (now - _traceWindowStart < 1000) return;
        var gc0 = GC.CollectionCount(0); var gc1 = GC.CollectionCount(1); var gc2 = GC.CollectionCount(2);
        var reports = OperatingSystem.IsWindows() && _sixAxis is RawInputSpaceMouse raw ? raw.MotionReports : 0;
        if (_traceNonZero > 0)
            Log($"SpaceMouse 1s: polls {_tracePolls}, deflected {_traceNonZero}, changed {_traceChanges}, max gap {_traceMaxGapMs} ms"
                + $"; HID motion reports {reports - _traceMotionReports}; max poll call {_tracePollMaxMs:F1} ms, max render {_traceRenderMaxMs:F1} ms"
                + $", GC {gc0 - _traceGc0}/{gc1 - _traceGc1}/{gc2 - _traceGc2}");
        _traceWindowStart = now;
        _traceMotionReports = reports;
        _tracePolls = _traceNonZero = _traceChanges = 0;
        _traceMaxGapMs = 0;
        _tracePollMaxMs = _traceRenderMaxMs = 0;
        _traceGc0 = gc0; _traceGc1 = gc1; _traceGc2 = gc2;
        if (m.IsZero) _traceLastChange = 0;
    }

    private void HandleSpaceMouseButton(SixAxisButtonPress press)
    {
        Log($"SpaceMouse button {press.Button} (code {press.RawCode})");
        switch (press.Button)
        {
            // Esc mirrors the keyboard's priority chain in OnKeyDown.
            case SixAxisButton.Escape when _modal is not null && Document is not null:
                if (_modal.IsActive) { _modal.Cancel(); _gizmoDragging = false; }
                else if (_tipDrag is not null) CancelTipDrag();
                else if (_layFlatPick) EndLayFlatPick();
                else if (Document.SupportSelection.Count > 0) Document.ClearSupportSelection();
                else ClearObjectSelection();
                UpdateStatus();
                break;
            // View buttons wait out an active modal drag rather than yanking its screen mapping.
            case SixAxisButton.Fit when _modal is not { IsActive: true }: FrameAll(); break;
            case SixAxisButton.ViewTop when _modal is not { IsActive: true }: SetView(c => c.ViewTop()); break;
            case SixAxisButton.ViewBottom when _modal is not { IsActive: true }: SetView(c => c.ViewBottom()); break;
            case SixAxisButton.ViewLeft when _modal is not { IsActive: true }: SetView(c => c.ViewLeft()); break;
            case SixAxisButton.ViewRight when _modal is not { IsActive: true }: SetView(c => c.ViewRight()); break;
            case SixAxisButton.ViewFront when _modal is not { IsActive: true }: SetView(c => c.ViewFront()); break;
            case SixAxisButton.ViewBack when _modal is not { IsActive: true }: SetView(c => c.ViewBack()); break;
            case SixAxisButton.RotationLock:
                _spaceMouseRotationLock = !_spaceMouseRotationLock;
                UpdateStatus();
                break;
            // Menu, digits, ISO, rolls and modifier buttons are unbound for now; the log line
            // above records what each physical button sends for the on-device test.
        }
    }
}
