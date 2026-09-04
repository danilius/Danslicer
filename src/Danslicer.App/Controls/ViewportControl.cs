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

    public static readonly StyledProperty<bool> SelectThroughSupportsProperty =
        AvaloniaProperty.Register<ViewportControl, bool>(nameof(SelectThroughSupports));

    public static readonly StyledProperty<SupportDisplayConfig> SupportDisplayProperty =
        AvaloniaProperty.Register<ViewportControl, SupportDisplayConfig>(nameof(SupportDisplay),
            new SupportDisplayConfig());

    public static readonly StyledProperty<ViewportClipRange> ClipRangeProperty =
        AvaloniaProperty.Register<ViewportControl, ViewportClipRange>(nameof(ClipRange));

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
            (int)(Bounds.Width * scaling), (int)(Bounds.Height * scaling), scaling, Camera.View);
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
    private readonly List<AuxMeshDraw> _combinedAuxMeshes = new();
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
    public bool SelectThroughSupports { get => GetValue(SelectThroughSupportsProperty); set => SetValue(SelectThroughSupportsProperty, value); }
    public SupportDisplayConfig SupportDisplay { get => GetValue(SupportDisplayProperty); set => SetValue(SupportDisplayProperty, value); }
    public ViewportClipRange ClipRange { get => GetValue(ClipRangeProperty); set => SetValue(ClipRangeProperty, value); }
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
                _subscribed.SelectionChanged -= Redraw;
                _subscribed.SupportSelectionChanged -= Redraw;
                _subscribed.SupportSelectionChanged -= MarkSelectionMeshDirty;
                _subscribed.Supports.Changed -= MarkSupportMeshesDirty;
            }
            _subscribed = Document;
            _modal = null;
            if (_subscribed is not null)
            {
                _subscribed.Changed += Redraw;
                _subscribed.SelectionChanged += Redraw;
                _subscribed.SupportSelectionChanged += Redraw;
                // Selection changes rebuild only the small selected-elements overlay; the full
                // graph mesh rebuilds only when the graph itself changes (a full rebuild froze
                // the app for seconds after a marquee selection on a generated forest).
                _subscribed.SupportSelectionChanged += MarkSelectionMeshDirty;
                _subscribed.Supports.Changed += MarkSupportMeshesDirty;
                _modal = new ModalTransform(_subscribed, Camera);
            }
            _supportMeshesDirty = true;
            _selectionMeshDirty = true;
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
        else if (change.Property == SupportSelectionModeProperty)
        {
            if (SupportWaterline is { } waterline)
                waterline.SupportModeActive = SupportSelectionMode;
            _supportMeshesDirty = true;
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
            // Rendering is shader-only: do not rebuild support meshes while either thumb moves.
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
        _combinedAuxMeshes.Clear();
        IEnumerable<SupportMeshBatch> supportBatches = SupportDisplay.Mode == SupportDisplayMode.Transparent
            ? _supportMeshes.OrderByDescending(batch =>
                Vector3.DistanceSquared(Camera.Eye, batch.SortOrigin))
            : _supportMeshes;
        foreach (var batch in supportBatches) _combinedAuxMeshes.Add(batch.Draw);
        if (_islandMarkerMesh is { } markers)
            _combinedAuxMeshes.Add(new AuxMeshDraw(markers, new Vector3(1f, 0.03f, 0.03f), 1f));
        if (_selectedSupportMesh is { } selected)
            _combinedAuxMeshes.Add(new AuxMeshDraw(selected,
                new Vector3(SupportSelectedColor.X, SupportSelectedColor.Y, SupportSelectedColor.Z),
                1f, DepthOverlay: true));
        AppendSupportLines(_depthOverlay);
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
            WaterlineZ = SupportWaterline?.WorldZ,
            WireframeEnabled = Configuration.AppConfig.Current.Viewport.WireframeEnabled,
            ShowViewCube = Configuration.AppConfig.Current.Viewport.ViewCubeEnabled,
            ViewCubeHover = _viewCubeHover,
            RenderScaling = scaling,
        });

        if (_pendingGpuPick is { } pick)
        {
            _pendingGpuPick = null;
            // Control DIPs to framebuffer pixels; the ID buffer's origin is bottom-left.
            var px = (int)(pick.Mouse.X * scaling);
            var py = height - 1 - (int)(pick.Mouse.Y * scaling);
            if (_renderer.TryPickObject(px, py, out var hit))
                ApplyObjectClick(hit, pick.Additive);
            else
                // The frame fell back to the classic path; pick the CPU way instead.
                ApplyObjectClick(PickObject(pick.Mouse), pick.Additive);
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

    /// <summary>Object click-selection semantics, shared by the CPU and ID-buffer pick paths.</summary>
    private void ApplyObjectClick(SceneObject? hitObj, bool additive)
    {
        if (Document is null) return;
        if (hitObj is null)
        {
            if (!additive)
            {
                Document.ClearSelection();
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

        if (_orbiting)
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
            _modal is not { IsActive: true })
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
                if (!_marqueeAdditive) Document.ClearSelection();
                // Double-click selects the whole support tree; single click the element.
                if (_pendingClickCount >= 2) SelectDisplayedSupportComponent(element, _marqueeAdditive);
                else Document.SelectSupportElement(element, _marqueeAdditive);
            }
            else if (!_marqueeAdditive)
            {
                Document?.ClearSelection();
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
        Camera.Zoom((float)e.Delta.Y);
        Redraw();
        e.Handled = true;
    }

    private SceneObject? PickObject(Vector2 mouse) => PickFace(mouse, out _);

    private SceneObject? PickFace(Vector2 mouse, out int triangle) => PickSurface(mouse, out triangle, out _, out _);

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
    private static readonly Vector4 MiniSupportColor = new(1f, 0.68f, 0.22f, 0.95f);
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
        if (!Document.AddManualSupport(hit, point, normal, out var reason))
            return reason == Danslicer.Core.Supports.Routing.RoutingFailureReason.ContactBlocked
                ? "Support: contact is too tight to the surface"
                : "Support: no clear path to the plate from here";
        return null;
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
    private Guid? PickSupportElement(Vector2 mouse, out float cameraDistance)
    {
        cameraDistance = float.PositiveInfinity;
        var supports = Document?.Supports;
        if (supports is null || supports.NodeCount == 0) return null;
        var w = (float)Bounds.Width;
        var h = (float)Bounds.Height;
        var eye = Camera.Eye;

        Guid? best = null;
        var bestScore = float.PositiveInfinity;
        foreach (var node in supports.Nodes)
        {
            if (node.Hidden || node.Type != Danslicer.Core.Supports.SupportNodeType.Tip ||
                !SupportDisplayPolicy.IsNodeDisplayed(supports, node, SupportDisplay, ClipRange)) continue;
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
            if (segment.Hidden || !SupportDisplayPolicy.IsSegmentDisplayed(
                    supports, segment, SupportDisplay, ClipRange)) continue;
            var a = supports.GetNode(segment.NodeA);
            var b = supports.GetNode(segment.NodeB);
            if (a.Hidden || b.Hidden) continue;
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
            if (node.Hidden || node.Type != Danslicer.Core.Supports.SupportNodeType.Base ||
                !SupportDisplayPolicy.IsNodeDisplayed(supports, node, SupportDisplay, ClipRange)) continue;
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
                        SupportDisplayPolicy.IsSegmentDisplayed(segment.Type, display),
                    includeBase: node => componentNodes.Contains(node.Id) &&
                        SupportDisplayPolicy.IsNodeDisplayed(supports, node, display));
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
                SupportDisplayPolicy.IsSegmentDisplayed(segment.Type, display),
            includeBase: node => SupportDisplayPolicy.IsNodeDisplayed(supports, node, display));
        foreach (var part in visibleParts)
            AddSupportPart(part, part.Mesh.Bounds.Center, 1f);
    }

    private const float TransparentSupportOpacity = 0.28f;
    private const float OutsideSupportModeOpacity = 0.45f;

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
            SupportRenderKind.MiniSupport => MiniSupportColor,
            SupportRenderKind.Trunk => TrunkColor,
            SupportRenderKind.Bracing => BracingColor,
            SupportRenderKind.Base => BaseColor,
            _ => BranchColor,
        };
        _supportMeshes.Add(new SupportMeshBatch(new AuxMeshDraw(
            part.Mesh,
            new Vector3(color.X, color.Y, color.Z),
            opacity * (SupportSelectionMode ? 1f : OutsideSupportModeOpacity) *
                (part.Disabled ? DisabledSupportOpacity : 1f)), sortOrigin));
    }

    private void AppendSupportLines(List<OverlayLine> lines)
    {
        var supports = Document?.Supports;
        if (Document is null || supports is null) return;
        if (SupportDisplayPolicy.ShowsLines(SupportDisplay))
        {
            foreach (var segment in supports.Segments)
            {
                if (segment.Hidden || !SupportDisplayPolicy.IsSegmentDisplayed(
                        supports, segment, SupportDisplay, ClipRange)) continue;
                var a = supports.GetNode(segment.NodeA);
                var b = supports.GetNode(segment.NodeB);
                if (a.Hidden || b.Hidden) continue;
                var color = Document.IsSupportSelected(segment.Id)
                    ? SupportSelectedColor
                    : segment.Type switch
                    {
                        SupportSegmentType.Tip => TipColor,
                        SupportSegmentType.MiniSupport => MiniSupportColor,
                        SupportSegmentType.Trunk => TrunkColor,
                        SupportSegmentType.Bracing => BracingColor,
                        _ => BranchColor,
                    };
                if (segment.Disabled || a.Disabled || b.Disabled)
                    color.W *= DisabledSupportOpacity;
                if (!SupportSelectionMode) color.W *= OutsideSupportModeOpacity;
                lines.Add(new OverlayLine(a.Position, b.Position, color));
            }
        }

        if (!SupportDisplayPolicy.ShowsContactMarkers(SupportDisplay)) return;
        foreach (var node in supports.Nodes)
        {
            if (node.Hidden || node.Type != SupportNodeType.Tip ||
                !SupportDisplayPolicy.IsNodeDisplayed(supports, node, SupportDisplay, ClipRange)) continue;
            var color = Document.IsSupportSelected(node.Id) ? SupportSelectedColor : TipMarkerColor;
            if (!SupportSelectionMode) color.W *= OutsideSupportModeOpacity;
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
                case Key.Escape when _marqueeStart is not null:
                    _marqueeStart = null;
                    _pendingClickSupport = null;
                    MarqueeRect = null;
                    break;
                case Key.Escape when _layFlatPick: _layFlatPick = false; break;
                case Key.Escape when _borderSelectArmed: _borderSelectArmed = false; break;
                case Key.Escape when Document.SupportSelection.Count > 0: Document.ClearSupportSelection(); break;
                case Key.Escape: Document.ClearSelection(); break;
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
            ? $"{projection}{spaceMouse}  ·  MMB orbit · Shift+MMB pan · wheel zoom · LMB select support · G move tip · T add support · B border select · H hide · Tab workspace · Home frame all · 1/3/7 views · 5 projection"
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
                else Document.ClearSelection();
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
