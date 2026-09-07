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
using Danslicer.Core.Supports.Generation;
using Danslicer.Render;

namespace Danslicer.App.Controls;

/// <summary>
/// The 3D viewport. Owns the camera, the renderer, the transform gizmo and the modal transform tool,
/// and translates pointer and keyboard input into camera navigation, selection and transforms.
/// </summary>
public sealed class ViewportControl : OpenGlControlBase
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
    private static void Log(string message) { if (Trace) Console.Error.WriteLine($"[viewport] {message}"); }

    private SceneRenderer? _renderer;
    private ModalTransform? _modal;
    private Document? _subscribed;
    private HoverWaterlineViewModel? _subscribedWaterline;
    // A Layout-mode click awaiting ID-buffer resolution on the next rendered frame (design 6.5).
    private (Vector2 Mouse, bool Additive)? _pendingGpuPick;
    private int _viewCubeHover = -1;

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
    private DispatcherTimer? _sixAxisTimer;
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
            if (SupportWaterline is { } waterline)
                waterline.SupportModeActive = SupportSelectionMode;
            // A guided gesture is Support-mode only; leaving the mode abandons it.
            if (!SupportSelectionMode && _lineGesture is not null) CancelLineGesture();
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
        foreach (var (z, face) in planes) AddSupportCaps(document.Supports, z, face);
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
        _combinedAuxMeshes.AddRange(_regionOverlays);
        _combinedAuxMeshes.AddRange(_clipCaps);
        if (_islandMarkerMesh is { } markers)
            _combinedAuxMeshes.Add(new AuxMeshDraw(markers, new Vector3(1f, 0.03f, 0.03f), 1f));
        if (_selectedSupportMesh is { } selected)
            _combinedAuxMeshes.Add(new AuxMeshDraw(selected,
                new Vector3(SupportSelectedColor.X, SupportSelectedColor.Y, SupportSelectedColor.Z),
                1f, DepthOverlay: true));
        AppendSupportLines(_depthOverlay);
        AppendBrushCursor(_overlay);
        AppendLineGesture(_depthOverlay, _overlay);
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
            OverhangColorA = Configuration.AppConfig.ParseColor(
                Configuration.AppConfig.Current.Viewport.OverhangColorA, new Vector3(0.98f, 0.80f, 0.15f)),
            OverhangColorB = Configuration.AppConfig.ParseColor(
                Configuration.AppConfig.Current.Viewport.OverhangColorB, new Vector3(0.90f, 0.12f, 0.10f)),
            OverhangCheckerSizeMm = Configuration.AppConfig.Current.Viewport.OverhangCheckerSizeMm,
            RenderPath = Configuration.AppConfig.Current.Viewport.RenderPath,
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
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        SupportWaterline?.Clear();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Log($"pointer pressed {e.GetPosition(this)} focused={IsFocused}");
        Focus();
        var props = e.GetCurrentPoint(this).Properties;
        _lastPointer = e.GetPosition(this);

        if (props.IsMiddleButtonPressed)
        {
            if (e.KeyModifiers.HasFlag(KeyModifiers.Shift)) _panning = true;
            else _orbiting = true;
            e.Pointer.Capture(this);
            e.Handled = true;
            return;
        }

        if (_lineGesture is not null)
        {
            // A click adds a vertex; the second click of a double-click places the line.
            if (props.IsLeftButtonPressed)
            {
                if (_lineGesture.PlacesOnClick)
                {
                    UpdateLineGestureCursor(MouseVector(e));
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

            // The view cube floats over everything, so it wins the click.
            if (HitViewCube(_lastPointer) is var cubeRegion && cubeRegion >= 0)
            {
                var (yaw, pitch) = ViewCube.ViewAngles(cubeRegion, Camera.Yaw * 180f / MathF.PI);
                Camera.SetView(yaw, pitch);
                Redraw();
                e.Handled = true;
                return;
            }

            if (!SupportSelectionMode && _layFlatPick)
            {
                _layFlatPick = false;
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
        else if (_modal is { IsActive: true })
        {
            ApplySnap(e.KeyModifiers);
            _modal.Update(MouseVector(e));
            UpdateStatus();
        }
        else if (!SupportSelectionMode && Document is not null && Document.Selection.Count > 0)
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
        else if (_gizmo.Hovered != GizmoHandle.None)
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

        UpdateWaterline(MouseVector(e));
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
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

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
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
    /// <summary>True between a brush press and its release: every move in between is a dab.</summary>
    private bool _brushing;
    /// <summary>Where the brush ring is drawn; null when the cursor is off the model.</summary>
    private Vector3? _brushCursor;

    /// <summary>Arms lay-flat: the next left click on a face lays the object on it.</summary>
    public void BeginLayFlatPick()
    {
        _layFlatPick = true;
        UpdateStatus();
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

    private string? TryAddSupport(Vector2 mouse)
    {
        if (Document is null) return null;
        var hit = PickSurface(mouse, out _, out var point, out var normal);
        if (hit is null) return null;
        // Only the support target takes supports; a click on any other model says so rather than
        // silently doing nothing. Document refuses it as well — this is the visible half.
        if (Danslicer.Core.Supports.SupportTargetPolicy.RefusalMessage(Document.SupportTarget, hit)
            is { } refusal) return refusal;
        if (!Document.AddManualSupport(hit, point, normal, out var reason))
            return reason == Danslicer.Core.Supports.Routing.RoutingFailureReason.ContactBlocked
                ? "Support: contact is too tight to the surface"
                : "Support: no clear path to the plate from here";
        return null;
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
    private enum GuidedKind { Line, Polygon, Edge }

    private string? BeginLineGesture(Vector2 mouse, GuidedKind kind = GuidedKind.Line)
    {
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
            var placed = Document.PlaceGuidedTips(target, candidates, gesture.Name, out var refused);
            status = refused == 0
                ? $"{gesture.Name}: {placed} placed"
                : $"{gesture.Name}: {placed} of {placed + refused} placed · {refused} had no clear path";
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
        var supports = Document?.Supports;
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
        _selectedSupportMesh = Document is { } document && document.SupportSelection.Count > 0 &&
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
        var supports = Document?.Supports;
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
        var supports = Document?.Supports;
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
                case Key.F when !ctrl && !SupportSelectionMode: if (!TryLayFlat(mouse)) _layFlatPick = true; break;
                case Key.H when e.KeyModifiers.HasFlag(KeyModifiers.Alt): Document.UnhideAll(); break;
                case Key.H when shift && !ctrl && SupportSelectionMode: Document.HideUnselectedSupportElements(); break;
                case Key.H when !ctrl && SupportSelectionMode: Document.HideSelectedSupportElements(); break;
                case Key.H when !ctrl: Document.HideSelection(); break;
                // Manual support under the cursor (Support mode only), routed around the model.
                // (Shift+T's blind straight drop was removed 2026-09-03 at the user's request.)
                case Key.T when !ctrl && !shift && SupportSelectionMode: statusAfterUpdate = TryAddSupport(mouse); break;
                // Guided line of supports (SUPPORT-GEOMETRY-SPEC "Guided tip placement").
                case Key.L when !ctrl && !shift && SupportSelectionMode: statusAfterUpdate = BeginLineGesture(mouse); break;
                case Key.P when !ctrl && !shift && SupportSelectionMode: statusAfterUpdate = BeginLineGesture(mouse, GuidedKind.Polygon); break;
                case Key.E when !ctrl && !shift && SupportSelectionMode: statusAfterUpdate = BeginLineGesture(mouse, GuidedKind.Edge); break;
                case Key.Escape when _marqueeStart is not null:
                    _marqueeStart = null;
                    _pendingClickSupport = null;
                    MarqueeRect = null;
                    break;
                case Key.Escape when _layFlatPick: _layFlatPick = false; break;
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
            StatusText = lineGesture.PlacesOnClick
                ? $"{lineGesture.Name}: {(_linePreview.Count == 0 ? "hover near a sharp edge" : tips)} · pitch {pitch} mm  ·  LMB place · wheel/digits pitch · RMB/Esc cancel"
                : $"{lineGesture.Name}: {tips} · pitch {pitch} mm{surface}  ·  LMB add point · double-click/Enter place · Backspace remove point · wheel/digits pitch · RMB/Esc cancel";
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
        var spaceMouse = _sixAxis is { IsConnected: true }
            ? (_spaceMouseRotationLock ? " · SpaceMouse (rot locked)" : " · SpaceMouse")
            : "";
        StatusText = SupportSelectionMode
            ? $"{projection}{spaceMouse}  ·  MMB orbit · Shift+MMB pan · wheel zoom · LMB select support · G move tip · T add support · L support line · P support polygon · E support edge · B border select · H hide · Tab workspace · Home frame all · 1/3/7 views · 5 projection"
            : $"{projection} · {snap}{spaceMouse}  ·  MMB orbit · Shift+MMB pan · wheel zoom · LMB select or drag gizmo · G/R/S transform · F lay flat · Shift+Tab snap · Tab workspace · Home frame all · 1/3/7 views · 5 projection";
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _gizmo.ShowMove = ShowMoveGizmo;
        _gizmo.ShowRotate = ShowRotateGizmo;
        _gizmo.ShowScale = ShowScaleGizmo;
        ConnectSpaceMouse();
        UpdateStatus();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _sixAxisTimer?.Stop();
        _sixAxisTimer = null;
        _sixAxis?.Dispose();
        _sixAxis = null;
    }

    // ----- SpaceMouse -----

    // Base step sizes per poll at full cap deflection, expressed in the camera's pixel/step units
    // so the camera's own clamping applies. Signs follow 3Dconnexion camera mode: push forward to
    // zoom in, tilt forward to pitch down, twist to yaw. Roll is locked, as designed. The user
    // scales and flips these through the SpaceMouse section of the config window; settings are
    // read every poll tick so tuning applies live. Calibrated 2026-09-03 on the user's SpaceMouse
    // Pro so that sensitivity 1.0 is their tuned feel (the original guesses were 50x too fast).
    private const float SpaceMouseOrbitPixels = 0.12f;
    private const float SpaceMousePanPixels = 0.16f;
    private const float SpaceMouseZoomSteps = 0.0016f;

    private void ConnectSpaceMouse()
    {
        if (!OperatingSystem.IsWindows() || _sixAxis is not null) return;
        var device = new TdxSpaceMouse();
        if (!device.TryConnect())
        {
            Log("SpaceMouse: 3DxWare COM not available");
            device.Dispose();
            return;
        }
        Log(device.ButtonsConnected
            ? "SpaceMouse connected via 3DxWare COM, buttons hooked"
            : "SpaceMouse connected via 3DxWare COM, button events unavailable");
        _sixAxis = device;
        _sixAxisTimer = new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(15) };
        _sixAxisTimer.Tick += (_, _) => PollSpaceMouse();
        _sixAxisTimer.Start();
    }

    private void PollSpaceMouse()
    {
        if (_sixAxis is null) return;
        foreach (var press in _sixAxis.DrainButtonPresses()) HandleSpaceMouseButton(press);
        var m = _sixAxis.Poll();
        if (m.IsZero) return;

        var config = Configuration.AppConfig.Current.SpaceMouse;
        var deadzone = config.Deadzone;
        var orbit = SpaceMouseOrbitPixels * config.OrbitSensitivity;
        var pan = SpaceMousePanPixels * config.PanSensitivity;
        var zoom = SpaceMouseZoomSteps * config.ZoomSensitivity;

        var moved = false;
        if (!_spaceMouseRotationLock &&
            (MathF.Abs(m.Rotation.Y) > deadzone || MathF.Abs(m.Rotation.X) > deadzone))
        {
            Camera.Orbit(
                -m.Rotation.Y * orbit * (config.InvertOrbitYaw ? -1f : 1f),
                -m.Rotation.X * orbit * (config.InvertOrbitPitch ? -1f : 1f));
            moved = true;
        }
        if (MathF.Abs(m.Translation.X) > deadzone || MathF.Abs(m.Translation.Y) > deadzone)
        {
            Camera.Pan(
                -m.Translation.X * pan * (config.InvertPanX ? -1f : 1f),
                m.Translation.Y * pan * (config.InvertPanY ? -1f : 1f),
                (float)Bounds.Height);
            moved = true;
        }
        if (MathF.Abs(m.Translation.Z) > deadzone)
        {
            Camera.Zoom(-m.Translation.Z * zoom * (config.InvertZoom ? -1f : 1f));
            moved = true;
        }
        if (moved) Redraw();
    }

    // Locks the device's rotation axes only (3Dconnexion convention); MMB orbit stays available.
    private bool _spaceMouseRotationLock;

    private void HandleSpaceMouseButton(SixAxisButtonPress press)
    {
        Log($"SpaceMouse button {press.Button} (code {press.RawCode})");
        switch (press.Button)
        {
            // Esc mirrors the keyboard's priority chain in OnKeyDown.
            case SixAxisButton.Escape when _modal is not null && Document is not null:
                if (_modal.IsActive) { _modal.Cancel(); _gizmoDragging = false; }
                else if (_tipDrag is not null) CancelTipDrag();
                else if (_layFlatPick) _layFlatPick = false;
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
