using System.Numerics;
using Avalonia.Input;
using Danslicer.Core.Geometry;
using Danslicer.Core.Scene;
using Danslicer.Core.Supports;
using Danslicer.Render;

namespace Danslicer.App.Controls;

public sealed partial class ViewportControl
{
    private bool _manualBraceMode;
    public bool IsManualBraceMode => _manualBraceMode;
    public event EventHandler? ManualBraceModeChanged;
    private (Guid Id, Vector3 Point)? _manualBraceStart, _manualBraceHover;
    private string? _manualBraceRefusal;
    private bool _manualBraceSnap45;
    public bool ManualBraceSnap45
    {
        get => _manualBraceSnap45;
        set
        {
            _manualBraceSnap45 = value;
            if (_manualBraceMode) UpdateManualBrace(new Vector2((float)_lastPointer.X, (float)_lastPointer.Y));
        }
    }

    public void RefreshManualBracePreview()
    {
        if (_manualBraceMode) UpdateManualBrace(new Vector2((float)_lastPointer.X, (float)_lastPointer.Y));
    }

    public void ToggleManualBrace()
    {
        if (_manualBraceMode) { EndManualBrace(); return; }
        if (Document is null || !SupportSelectionMode || StructurePreviewActive) return;
        ExitSupportTools();
        _manualBraceMode = true;
        ManualBraceModeChanged?.Invoke(this, EventArgs.Empty);
        Cursor = new Cursor(StandardCursorType.Cross);
        Focus(); UpdateStatus(); Redraw();
    }

    private void EndManualBrace()
    {
        _manualBraceMode = false;
        ManualBraceModeChanged?.Invoke(this, EventArgs.Empty);
        _manualBraceStart = _manualBraceHover = null;
        _manualBraceRefusal = null;
        _placementGhost.Clear();
        Cursor = Cursor.Default;
        UpdateStatus(); Redraw();
    }

    private (Guid Id, Vector3 Point)? PickBraceCarrier(Vector2 mouse)
    {
        if (Document?.SupportTarget is not { } target || target.RenderState == RenderState.Hidden) return null;
        var graph = Document.Supports;
        var model = PickSurface(mouse, out _, out var surface, out _);
        var modelDistance = model is null ? float.PositiveInfinity : Vector3.Distance(Camera.Eye, surface);
        var best = SupportPickRadiusPixels;
        (Guid, Vector3)? result = null;
        foreach (var segment in graph.Segments)
        {
            if (segment.Type is not (SupportSegmentType.Trunk or SupportSegmentType.Branch) ||
                segment.Hidden || segment.Disabled || graph.OwningObjectId(segment.Id) != target.Id ||
                !SupportDisplayPolicy.IsSegmentDisplayed(graph, segment, SupportDisplay, ClipRange)) continue;
            if (!ClipRange.TryClipSegment(graph.GetNode(segment.NodeA).Position, graph.GetNode(segment.NodeB).Position,
                    out var a, out var b) ||
                Camera.WorldToScreen(a, (float)Bounds.Width, (float)Bounds.Height) is not { } pa ||
                Camera.WorldToScreen(b, (float)Bounds.Width, (float)Bounds.Height) is not { } pb) continue;
            var delta = pb - pa;
            var t = delta.LengthSquared() < 1e-6f ? 0 : Math.Clamp(Vector2.Dot(mouse - pa, delta) / delta.LengthSquared(), 0, 1);
            var distance = Vector2.Distance(mouse, pa + t * delta);
            var matrix = Camera.ViewProjection((float)(Bounds.Width / Bounds.Height));
            var wa = Vector4.Transform(new Vector4(a, 1), matrix).W;
            var wb = Vector4.Transform(new Vector4(b, 1), matrix).W;
            var axisT = (t / wb) / ((1 - t) / wa + t / wb);
            var point = Vector3.Lerp(a, b, axisT);
            if (distance >= best || Vector3.Distance(Camera.Eye, point) > modelDistance) continue;
            best = distance; result = (segment.Id, point);
        }
        return result;
    }

    internal void UpdateManualBrace(Vector2 mouse)
    {
        _placementGhost.Clear();
        _manualBraceRefusal = null;
        _manualBraceHover = PickBraceCarrier(mouse);
        if (Document is null) return;
        if (_manualBraceStart is { } stale && (!Document.Supports.TryGetSegment(stale.Id, out _) ||
            Document.Supports.OwningObjectId(stale.Id) != Document.SupportTarget?.Id)) _manualBraceStart = null;
        if (_manualBraceStart is { } first && _manualBraceHover is { } last)
        {
            var end = last.Point;
            if (ManualBraceSnap45)
            {
                var segment = Document.Supports.GetSegment(last.Id);
                var snapped = ManualBrace.Snap45(first.Point, Document.Supports.GetNode(segment.NodeA).Position,
                    Document.Supports.GetNode(segment.NodeB).Position, end);
                if (snapped is { } point && ClipRange.Contains(point)) end = point;
                else _manualBraceRefusal = "No 45° attachment here; turn off Snap brace 45° for a free angle";
            }
            _manualBraceHover = (last.Id, end);
            if (_manualBraceRefusal is null)
                Document.PreviewManualBrace(first.Id, first.Point, last.Id, end, out _manualBraceRefusal);
            var graph = new SupportGraph();
            var a = new SupportNode { Type = SupportNodeType.BraceEnd, Position = first.Point };
            var b = new SupportNode { Type = SupportNodeType.BraceEnd, Position = end };
            graph.AddNode(a); graph.AddNode(b);
            var settings = Document.SupportSettings;
            graph.AddSegment(new SupportSegment { Type = SupportSegmentType.Bracing, NodeA = a.Id, NodeB = b.Id,
                Diameter = settings.ManualBraceDiameter });
            foreach (var part in SupportRenderMesh.Build(graph))
                _placementGhost.Add(new AuxMeshDraw(part.Mesh,
                    _manualBraceRefusal is null ? PlacementGhostColor : PlacementRefusedColor, 0.65f));
        }
        if ((_manualBraceStart ?? _manualBraceHover) is { } marker)
        {
            var mesh = new MeshBuilder();
            SupportRenderMesh.AppendSphere(mesh, marker.Point, 0.7f);
            _placementGhost.Add(new AuxMeshDraw(mesh.ToMesh(), PlacementGhostColor, 0.8f));
        }
        UpdateStatus(); Redraw();
    }

    internal void ClickManualBrace()
    {
        if (_manualBraceHover is not { } hit || Document is null) return;
        if (_manualBraceStart is null) _manualBraceStart = hit;
        else if (_manualBraceRefusal is null && Document.AddManualBrace(_manualBraceStart.Value.Id,
            _manualBraceStart.Value.Point, hit.Id, hit.Point, out _manualBraceRefusal))
            _manualBraceStart = null;
        _placementGhost.Clear();
        UpdateStatus(); Redraw();
    }
}
