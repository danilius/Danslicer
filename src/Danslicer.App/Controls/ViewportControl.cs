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
using Danslicer.Core;
using Danslicer.Core.Commands;
using Danslicer.Core.Geometry;
using Danslicer.Core.Scene;
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

    private static readonly bool Trace = Environment.GetEnvironmentVariable("DANSLICER_TRACE") == "1";
    private static void Log(string message) { if (Trace) Console.Error.WriteLine($"[viewport] {message}"); }

    private SceneRenderer? _renderer;
    private ModalTransform? _modal;
    private Document? _subscribed;
    private readonly Gizmo _gizmo = new();
    private Point _lastPointer;
    private bool _orbiting;
    private bool _panning;
    private bool _gizmoDragging;
    private bool _ctrlHeld;
    private readonly List<OverlayLine> _overlay = new();
    private readonly List<OverlayLine> _depthOverlay = new();
    private ISixAxisInput? _sixAxis;
    private DispatcherTimer? _sixAxisTimer;

    public Camera Camera { get; } = new();

    /// <summary>Raised on Tab so the host can switch between the model and layer views.</summary>
    public event Action? ToggleViewRequested;

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
            }
            _subscribed = Document;
            _modal = null;
            if (_subscribed is not null)
            {
                _subscribed.Changed += Redraw;
                _subscribed.SelectionChanged += Redraw;
                _subscribed.SupportSelectionChanged += Redraw;
                _modal = new ModalTransform(_subscribed, Camera);
            }
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
        else if (change.Property == ShowOverhangsProperty)
        {
            Redraw();
        }
    }

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
        AppendSupportLines(_depthOverlay);
        if (_modal is { IsActive: true }) _overlay.AddRange(_modal.OverlayLines);
        UpdateGizmo();
        // Hide the gizmo during keyboard-driven modals; keep it while dragging a handle.
        if (_modal is not { IsActive: true } || _gizmoDragging) _gizmo.AppendLines(Camera, _overlay);

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
            ShowOverhangs = ShowOverhangs,
            OverhangAngleDegrees = Configuration.AppConfig.Current.Viewport.OverhangAngleDegrees,
        });
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

            if (_layFlatPick)
            {
                _layFlatPick = false;
                TryLayFlat(m);
                UpdateStatus();
                e.Handled = true;
                return;
            }

            // Gizmo handle: start a constrained modal that ends on release.
            UpdateGizmo();
            var handle = _gizmo.HitTest(Camera, m, (float)Bounds.Width, (float)Bounds.Height);
            if (handle != GizmoHandle.None && Document.Selection.Count > 0)
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

            var hitObj = PickSurface(m, out _, out var surfacePoint, out _);
            var objDistance = hitObj is null ? float.PositiveInfinity : Vector3.Distance(Camera.Eye, surfacePoint);
            var support = PickSupportElement(m, out var supportDistance);
            var additive = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
            // Support lines are thin, so give them the tie against the surface right behind them.
            if (support is { } element && supportDistance <= objDistance + 0.5f)
            {
                if (!additive) Document.ClearSelection();
                // Double-click selects the whole support tree; single click the element.
                if (e.ClickCount >= 2) Document.SelectSupportComponent(element, additive);
                else Document.SelectSupportElement(element, additive);
            }
            else if (hitObj is null)
            {
                if (!additive)
                {
                    Document.ClearSelection();
                    Document.ClearSupportSelection();
                }
            }
            else
            {
                if (!additive) Document.ClearSupportSelection();
                if (additive) Document.ToggleSelection(hitObj);
                else Document.Select(hitObj);
            }
            e.Handled = true;
        }
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
        else if (Document is not null && Document.Selection.Count > 0)
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
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_orbiting || _panning)
        {
            _orbiting = _panning = false;
            e.Pointer.Capture(null);
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
            if (local.IntersectMesh(obj.Mesh, out var tri) is not { } t) continue;
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

    private static readonly Vector4 NeckColor = new(1f, 0.85f, 0.3f, 0.95f);
    private static readonly Vector4 PillarColor = new(0.55f, 0.75f, 0.95f, 0.95f);
    private static readonly Vector4 TrunkColor = new(0.75f, 0.85f, 1f, 0.95f);
    private static readonly Vector4 BracingColor = new(0.5f, 0.9f, 0.6f, 0.95f);
    private static readonly Vector4 TipColor = new(1f, 0.55f, 0.25f, 1f);

    private bool TryAddSupport(Vector2 mouse)
    {
        if (Document is null) return false;
        var hit = PickSurface(mouse, out _, out var point, out var normal);
        if (hit is null) return false;
        Document.AddManualSupport(hit, point, normal);
        return true;
    }

    // ----- Tip move (G with a single tip selected) -----

    private Guid? _tipDrag;
    private List<(Danslicer.Core.Supports.SupportNode Node, Vector3 Position, Vector3 Normal)>? _tipDragBefore;

    private Guid? SelectedTip()
    {
        if (Document is null || Document.SupportSelection.Count != 1) return null;
        var id = Document.SupportSelection.First();
        return Document.Supports.TryGetNode(id, out var node) && node.Type == Danslicer.Core.Supports.SupportNodeType.Tip
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
            if (node.Hidden || node.Type != Danslicer.Core.Supports.SupportNodeType.Tip) continue;
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
            if (segment.Hidden) continue;
            var a = supports.GetNode(segment.NodeA);
            var b = supports.GetNode(segment.NodeB);
            if (a.Hidden || b.Hidden) continue;
            if (Camera.WorldToScreen(a.Position, w, h) is not { } pa ||
                Camera.WorldToScreen(b.Position, w, h) is not { } pb) continue;
            var ab = pb - pa;
            var len2 = ab.LengthSquared();
            var t = len2 < 1e-6f ? 0f : Math.Clamp(Vector2.Dot(mouse - pa, ab) / len2, 0f, 1f);
            var d = Vector2.Distance(mouse, pa + ab * t);
            if (d > SupportPickRadiusPixels || d >= bestScore) continue;
            bestScore = d;
            best = segment.Id;
            cameraDistance = Vector3.Distance(eye, Vector3.Lerp(a.Position, b.Position, t));
        }
        return best;
    }

    private static readonly Vector4 SupportSelectedColor = new(1f, 1f, 1f, 1f);

    private void AppendSupportLines(List<OverlayLine> lines)
    {
        var supports = Document?.Supports;
        if (Document is null || supports is null) return;
        foreach (var segment in supports.Segments)
        {
            if (segment.Hidden) continue;
            var a = supports.GetNode(segment.NodeA);
            var b = supports.GetNode(segment.NodeB);
            if (a.Hidden || b.Hidden) continue;
            var color = Document.IsSupportSelected(segment.Id) ? SupportSelectedColor : segment.Type switch
            {
                Danslicer.Core.Supports.SupportSegmentType.Neck => NeckColor,
                Danslicer.Core.Supports.SupportSegmentType.Trunk => TrunkColor,
                Danslicer.Core.Supports.SupportSegmentType.Bracing => BracingColor,
                _ => PillarColor,
            };
            if (segment.Disabled) color.W = 0.35f;
            lines.Add(new OverlayLine(a.Position, b.Position, color));
        }
        foreach (var node in supports.Nodes)
        {
            if (node.Hidden || node.Type != Danslicer.Core.Supports.SupportNodeType.Tip) continue;
            var color = Document.IsSupportSelected(node.Id) ? SupportSelectedColor : TipColor;
            const float s = 0.8f;
            var p = node.Position;
            lines.Add(new OverlayLine(p - new Vector3(s, 0, 0), p + new Vector3(s, 0, 0), color));
            lines.Add(new OverlayLine(p - new Vector3(0, s, 0), p + new Vector3(0, s, 0), color));
            lines.Add(new OverlayLine(p - new Vector3(0, 0, s), p + new Vector3(0, 0, s), color));
        }
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
                case Key.G when !ctrl && SelectedTip() is { } tipId: BeginTipDrag(tipId); break;
                case Key.G when !ctrl: ApplySnap(e.KeyModifiers); _modal.Begin(TransformMode.Move, mouse, w, h); break;
                case Key.R when !ctrl: ApplySnap(e.KeyModifiers); _modal.Begin(TransformMode.Rotate, mouse, w, h); break;
                case Key.S when !ctrl: ApplySnap(e.KeyModifiers); _modal.Begin(TransformMode.Scale, mouse, w, h); break;
                case Key.A when e.KeyModifiers.HasFlag(KeyModifiers.Alt): Document.ClearSelection(); break;
                case Key.A when !ctrl: Document.SelectAll(); break;
                // Lay flat on the face under the cursor; with nothing under it, arm a click pick.
                case Key.F when !ctrl: if (!TryLayFlat(mouse)) _layFlatPick = true; break;
                case Key.H when e.KeyModifiers.HasFlag(KeyModifiers.Alt): Document.UnhideAll(); break;
                case Key.H when !ctrl: Document.HideSelection(); break;
                // Manual support: a vertical tip-neck-pillar-base tree under the cursor.
                case Key.T when !ctrl: TryAddSupport(mouse); break;
                case Key.Escape when _layFlatPick: _layFlatPick = false; break;
                case Key.Escape when Document.SupportSelection.Count > 0: Document.ClearSupportSelection(); break;
                case Key.Escape: Document.ClearSelection(); break;
                case Key.Delete when Document.SupportSelection.Count > 0: Document.DeleteSupportSelection(); break;
                case Key.Home: FrameAll(); break;
                case Key.OemPeriod: case Key.Decimal: FrameSelected(); break;
                case Key.Tab when shift: SnapEnabled = !SnapEnabled; break;
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
        var projection = Camera.Orthographic ? "Ortho" : "Persp";
        var snap = SnapEnabled ? "Snap on" : "Snap off";
        var spaceMouse = _sixAxis is { IsConnected: true } ? " · SpaceMouse" : "";
        StatusText = $"{projection} · {snap}{spaceMouse}  ·  MMB orbit · Shift+MMB pan · wheel zoom · LMB select or drag gizmo · G/R/S transform · F lay flat · T support · Shift+Tab snap · Tab layers · Home frame all · 1/3/7 views · 5 projection";
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
    // read every poll tick so tuning applies live.
    private const float SpaceMouseOrbitPixels = 6f;
    private const float SpaceMousePanPixels = 8f;
    private const float SpaceMouseZoomSteps = 0.08f;

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
        Log("SpaceMouse connected via 3DxWare COM");
        _sixAxis = device;
        _sixAxisTimer = new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(15) };
        _sixAxisTimer.Tick += (_, _) => PollSpaceMouse();
        _sixAxisTimer.Start();
    }

    private void PollSpaceMouse()
    {
        if (_sixAxis is null) return;
        var m = _sixAxis.Poll();
        if (m.IsZero) return;

        var config = Configuration.AppConfig.Current.SpaceMouse;
        var deadzone = config.Deadzone;
        var orbit = SpaceMouseOrbitPixels * config.OrbitSensitivity;
        var pan = SpaceMousePanPixels * config.PanSensitivity;
        var zoom = SpaceMouseZoomSteps * config.ZoomSensitivity;

        var moved = false;
        if (MathF.Abs(m.Rotation.Y) > deadzone || MathF.Abs(m.Rotation.X) > deadzone)
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
}
