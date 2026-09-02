using System.Numerics;
using Danslicer.Core;
using Danslicer.Core.Commands;
using Danslicer.Core.Geometry;
using Danslicer.Core.Scene;
using Danslicer.Core.Utilities;
using Danslicer.Render;

namespace Danslicer.App.Editing;

public enum TransformMode { Move, Rotate, Scale }

public enum AxisConstraint { None, X, Y, Z }

/// <summary>
/// Blender-style modal transform: G/R/S or a gizmo handle starts it, mouse movement drives it,
/// X/Y/Z constrain, typed numbers override, Enter or click confirms, Escape or right click cancels.
/// The pivot is the centre of the selection's world bounding box. Applies transforms live and
/// commits a single undo step on confirm. Has no UI dependencies so it can be unit tested.
/// </summary>
public sealed class ModalTransform
{
    private readonly Document _document;
    private readonly Camera _camera;
    private readonly List<(SceneObject Object, Transform Start)> _items = new();
    private Vector3 _pivot;
    private Vector2 _startMouse;
    private Vector2 _mouse;
    private float _width;
    private float _height;
    private float _liveValue;

    public ModalTransform(Document document, Camera camera)
    {
        _document = document;
        _camera = camera;
    }

    public bool IsActive { get; private set; }
    public TransformMode Mode { get; private set; }
    public AxisConstraint Axis { get; private set; }
    public bool PlaneConstraint { get; private set; }
    public string Numeric { get; private set; } = "";
    public Vector3 Pivot => _pivot;

    /// <summary>Snap mouse-driven values to <see cref="MoveStep"/>, <see cref="RotateStepDegrees"/>, <see cref="ScaleStep"/>.</summary>
    public bool Snap { get; set; }
    public float MoveStep { get; set; } = 1f;
    public float RotateStepDegrees { get; set; } = 5f;
    public float ScaleStep { get; set; } = 0.1f;

    /// <summary>Centre of the world bounding box of all selected objects.</summary>
    public static Vector3 SelectionPivot(Document document)
    {
        var bounds = Aabb.Empty;
        foreach (var obj in document.Selection) bounds = bounds.Union(obj.WorldBounds);
        return bounds.IsEmpty ? Vector3.Zero : bounds.Center;
    }

    public bool Begin(TransformMode mode, Vector2 mouse, float width, float height,
        AxisConstraint axis = AxisConstraint.None, bool plane = false)
    {
        if (_document.Selection.Count == 0) return false;

        _items.Clear();
        foreach (var obj in _document.Selection) _items.Add((obj, obj.Transform));
        _pivot = SelectionPivot(_document);

        Mode = mode;
        Axis = axis;
        PlaneConstraint = plane && axis != AxisConstraint.None;
        Numeric = "";
        _startMouse = _mouse = mouse;
        _width = width;
        _height = height;
        IsActive = true;
        Apply();
        return true;
    }

    /// <summary>Restarts the current modal in a different mode, keeping the original transforms.</summary>
    public void SwitchMode(TransformMode mode)
    {
        if (!IsActive) return;
        Mode = mode;
        Axis = AxisConstraint.None;
        PlaneConstraint = false;
        Numeric = "";
        Apply();
    }

    public void SetAxis(AxisConstraint axis, bool plane)
    {
        if (!IsActive) return;
        if (Axis == axis && PlaneConstraint == plane)
        {
            Axis = AxisConstraint.None;
            PlaneConstraint = false;
        }
        else
        {
            Axis = axis;
            PlaneConstraint = plane;
        }
        Apply();
    }

    public void TypeCharacter(char c)
    {
        if (!IsActive) return;
        if (c == '-')
            Numeric = Numeric.StartsWith('-') ? Numeric[1..] : "-" + Numeric;
        else if (char.IsDigit(c) || c == '.')
            Numeric += c;
        Apply();
    }

    public void Backspace()
    {
        if (!IsActive || Numeric.Length == 0) return;
        Numeric = Numeric[..^1];
        Apply();
    }

    public void Update(Vector2 mouse)
    {
        if (!IsActive) return;
        _mouse = mouse;
        Apply();
    }

    /// <summary>Re-evaluates with the current mouse position, e.g. after the snap flag changed.</summary>
    public void Refresh()
    {
        if (IsActive) Apply();
    }

    public void Confirm()
    {
        if (!IsActive) return;
        IsActive = false;
        var commands = new List<IDocumentCommand>();
        foreach (var (obj, start) in _items)
        {
            var final = obj.Transform;
            if (final != start) commands.Add(new SetTransformCommand(obj, start, final, ModeName));
        }
        if (commands.Count > 0)
            _document.Execute(new CompositeCommand(ModeName, commands));
        else
            _document.NotifyTransientChange();
    }

    public void Cancel()
    {
        if (!IsActive) return;
        IsActive = false;
        foreach (var (obj, start) in _items) obj.Transform = start;
        _document.NotifyTransientChange();
    }

    private string ModeName => Mode switch
    {
        TransformMode.Move => "Move",
        TransformMode.Rotate => "Rotate",
        TransformMode.Scale => "Scale",
        _ => "Transform",
    };

    public string StatusText
    {
        get
        {
            if (!IsActive) return "";
            var constraint = Axis == AxisConstraint.None ? "" : PlaneConstraint ? $" (plane ⟂{Axis})" : $" {Axis}";
            var typed = Numeric.Length > 0 ? $"  [{Numeric}]" : "";
            var snap = Snap ? "  snap" : "";
            var value = Mode switch
            {
                TransformMode.Move => $"{_liveValue:0.00} mm",
                TransformMode.Rotate => $"{_liveValue:0.0}°",
                _ => $"×{_liveValue:0.000}",
            };
            return $"{ModeName}{constraint}: {value}{typed}{snap}    X/Y/Z axis · Shift+axis plane · type value · Ctrl toggles snap · Enter/LMB confirm · Esc/RMB cancel";
        }
    }

    /// <summary>Axis guide lines through the pivot while constrained.</summary>
    public IEnumerable<OverlayLine> OverlayLines
    {
        get
        {
            if (!IsActive || Axis == AxisConstraint.None) yield break;
            const float length = 10000f;
            foreach (var axis in new[] { AxisConstraint.X, AxisConstraint.Y, AxisConstraint.Z })
            {
                var show = PlaneConstraint ? axis != Axis : axis == Axis;
                if (!show) continue;
                var dir = AxisVector(axis);
                yield return new OverlayLine(_pivot - dir * length, _pivot + dir * length, AxisColor(axis));
            }
        }
    }

    public static Vector3 AxisVector(AxisConstraint axis) => axis switch
    {
        AxisConstraint.X => Vector3.UnitX,
        AxisConstraint.Y => Vector3.UnitY,
        _ => Vector3.UnitZ,
    };

    public static Vector4 AxisColor(AxisConstraint axis) => axis switch
    {
        AxisConstraint.X => new Vector4(0.95f, 0.30f, 0.30f, 0.9f),
        AxisConstraint.Y => new Vector4(0.45f, 0.85f, 0.35f, 0.9f),
        _ => new Vector4(0.35f, 0.55f, 0.95f, 0.9f),
    };

    private bool TryNumeric(out float value)
    {
        value = 0;
        if (Numeric.Length == 0 || Numeric == "-" || Numeric == "." || Numeric == "-.") return false;
        if (!ExpressionParser.TryEvaluate(Numeric, UnitKind.Scalar, out var d)) return false;
        value = (float)d;
        return true;
    }

    private static float SnapTo(float value, float step) => step <= 0 ? value : MathF.Round(value / step) * step;

    private void Apply()
    {
        switch (Mode)
        {
            case TransformMode.Move: ApplyMove(); break;
            case TransformMode.Rotate: ApplyRotate(); break;
            case TransformMode.Scale: ApplyScale(); break;
        }
        _document.NotifyTransientChange();
    }

    private void ApplyMove()
    {
        Vector3 delta;
        if (TryNumeric(out var typed))
        {
            var dir = Axis == AxisConstraint.None ? Vector3.UnitX : AxisVector(Axis);
            delta = PlaneConstraint ? Vector3.Zero : dir * typed;
        }
        else
        {
            var ray0 = _camera.ScreenToRay(_startMouse.X, _startMouse.Y, _width, _height);
            var ray1 = _camera.ScreenToRay(_mouse.X, _mouse.Y, _width, _height);

            if (Axis != AxisConstraint.None && !PlaneConstraint)
            {
                var a = AxisVector(Axis);
                var s0 = ray0.ClosestParameterOnLine(_pivot, a);
                var s1 = ray1.ClosestParameterOnLine(_pivot, a);
                var s = s1 - s0;
                if (Snap) s = SnapTo(s, MoveStep);
                delta = a * s;
            }
            else
            {
                var normal = Axis == AxisConstraint.None ? -_camera.ViewDirection : AxisVector(Axis);
                var t0 = ray0.IntersectPlane(_pivot, normal);
                var t1 = ray1.IntersectPlane(_pivot, normal);
                delta = t0 is { } a0 && t1 is { } a1 ? ray1.At(a1) - ray0.At(a0) : Vector3.Zero;
                if (Snap)
                    delta = new Vector3(SnapTo(delta.X, MoveStep), SnapTo(delta.Y, MoveStep), SnapTo(delta.Z, MoveStep));
            }
        }

        _liveValue = Axis != AxisConstraint.None && !PlaneConstraint ? Vector3.Dot(delta, AxisVector(Axis)) : delta.Length();
        foreach (var (obj, start) in _items)
            obj.Transform = start with { Translation = start.Translation + delta };
    }

    private void ApplyRotate()
    {
        var axis = Axis == AxisConstraint.None ? -_camera.ViewDirection : AxisVector(Axis);
        float angle;
        if (TryNumeric(out var typed))
        {
            angle = typed * MathF.PI / 180f;
        }
        else
        {
            var pivotScreen = _camera.WorldToScreen(_pivot, _width, _height) ?? new Vector2(_width * 0.5f, _height * 0.5f);
            var a0 = MathF.Atan2(_startMouse.Y - pivotScreen.Y, _startMouse.X - pivotScreen.X);
            var a1 = MathF.Atan2(_mouse.Y - pivotScreen.Y, _mouse.X - pivotScreen.X);
            var screenDelta = a1 - a0;
            // Screen Y points down, so a visually counter-clockwise drag decreases atan2.
            // Counter-clockwise about an axis pointing at the viewer is positive.
            var towardsViewer = Vector3.Dot(axis, -_camera.ViewDirection) >= 0;
            angle = towardsViewer ? -screenDelta : screenDelta;
            if (Snap) angle = SnapTo(angle * 180f / MathF.PI, RotateStepDegrees) * MathF.PI / 180f;
        }

        _liveValue = angle * 180f / MathF.PI;
        var q = Rotations.AboutAxis(axis, angle);
        foreach (var (obj, start) in _items)
        {
            var offset = Vector3.Transform(start.Translation - _pivot, q);
            obj.Transform = start with
            {
                Rotation = Rotations.Compose(start.Rotation, q),
                Translation = _pivot + offset,
            };
        }
    }

    private void ApplyScale()
    {
        float factor;
        if (TryNumeric(out var typed))
        {
            factor = typed;
        }
        else
        {
            var pivotScreen = _camera.WorldToScreen(_pivot, _width, _height) ?? new Vector2(_width * 0.5f, _height * 0.5f);
            var d0 = Vector2.Distance(_startMouse, pivotScreen);
            var d1 = Vector2.Distance(_mouse, pivotScreen);
            factor = d0 > 1e-3f ? d1 / d0 : 1f;
            if (Snap) factor = MathF.Max(SnapTo(factor, ScaleStep), ScaleStep);
        }

        _liveValue = factor;
        Vector3 scale;
        if (Axis == AxisConstraint.None)
            scale = new Vector3(factor);
        else if (PlaneConstraint)
            scale = Vector3.One * factor + AxisVector(Axis) * (1f - factor);
        else
            scale = Vector3.One + AxisVector(Axis) * (factor - 1f);

        foreach (var (obj, start) in _items)
        {
            var offset = (start.Translation - _pivot) * scale;
            obj.Transform = start with
            {
                Scale = start.Scale * scale,
                Translation = _pivot + offset,
            };
        }
    }
}
