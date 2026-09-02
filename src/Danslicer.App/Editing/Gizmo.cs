using System.Numerics;
using Danslicer.Core.Geometry;
using Danslicer.Render;

namespace Danslicer.App.Editing;

public enum GizmoHandle
{
    None,
    MoveX, MoveY, MoveZ,
    MoveYZ, MoveZX, MoveXY,   // plane handles, named by the axes they move in
    RotateX, RotateY, RotateZ,
    ScaleX, ScaleY, ScaleZ,
    ScaleUniform,
}

/// <summary>
/// Screen-constant transform gizmo at the selection's bounding-box centre: move arrows and plane
/// squares, rotation rings, scale cubes. Provides hit testing and overlay geometry; a hit hands off
/// to <see cref="ModalTransform"/> with the matching mode and constraint.
/// </summary>
public sealed class Gizmo
{
    private const float PixelSize = 90f;
    private const float HitRadiusPixels = 9f;
    private const int RingSegments = 64;

    public bool ShowMove { get; set; } = true;
    public bool ShowRotate { get; set; }
    public bool ShowScale { get; set; }
    public GizmoHandle Hovered { get; set; }
    public GizmoHandle Active { get; set; }

    public Vector3 Pivot { get; private set; }
    public float Size { get; private set; }
    public bool Visible => Size > 0 && (ShowMove || ShowRotate || ShowScale);

    /// <summary>Recomputes pivot and world size so the gizmo stays a constant size on screen.</summary>
    public void Update(Camera camera, Aabb selectionBounds, float viewportHeight)
    {
        if (selectionBounds.IsEmpty)
        {
            Size = 0;
            return;
        }
        Pivot = selectionBounds.Center;
        float unitsPerPixel;
        if (camera.Orthographic)
            unitsPerPixel = camera.ViewHeightAtTarget / MathF.Max(viewportHeight, 1);
        else
        {
            var distance = MathF.Max(Vector3.Dot(Pivot - camera.Eye, camera.ViewDirection), camera.Near);
            unitsPerPixel = 2f * distance * MathF.Tan(camera.FovDegrees * 0.5f * MathF.PI / 180f) / MathF.Max(viewportHeight, 1);
        }
        Size = PixelSize * unitsPerPixel;
    }

    public static (TransformMode Mode, AxisConstraint Axis, bool Plane) ToTransform(GizmoHandle handle) => handle switch
    {
        GizmoHandle.MoveX => (TransformMode.Move, AxisConstraint.X, false),
        GizmoHandle.MoveY => (TransformMode.Move, AxisConstraint.Y, false),
        GizmoHandle.MoveZ => (TransformMode.Move, AxisConstraint.Z, false),
        GizmoHandle.MoveYZ => (TransformMode.Move, AxisConstraint.X, true),
        GizmoHandle.MoveZX => (TransformMode.Move, AxisConstraint.Y, true),
        GizmoHandle.MoveXY => (TransformMode.Move, AxisConstraint.Z, true),
        GizmoHandle.RotateX => (TransformMode.Rotate, AxisConstraint.X, false),
        GizmoHandle.RotateY => (TransformMode.Rotate, AxisConstraint.Y, false),
        GizmoHandle.RotateZ => (TransformMode.Rotate, AxisConstraint.Z, false),
        GizmoHandle.ScaleX => (TransformMode.Scale, AxisConstraint.X, false),
        GizmoHandle.ScaleY => (TransformMode.Scale, AxisConstraint.Y, false),
        GizmoHandle.ScaleZ => (TransformMode.Scale, AxisConstraint.Z, false),
        _ => (TransformMode.Scale, AxisConstraint.None, false),
    };

    // ----- Geometry -----

    private static readonly (AxisConstraint Axis, Vector3 Dir)[] Axes =
    {
        (AxisConstraint.X, Vector3.UnitX),
        (AxisConstraint.Y, Vector3.UnitY),
        (AxisConstraint.Z, Vector3.UnitZ),
    };

    private IEnumerable<(GizmoHandle Handle, Vector3 A, Vector3 B, Vector4 Color)> Segments(Camera camera)
    {
        var s = Size;
        if (ShowMove)
        {
            foreach (var (axis, dir) in Axes)
            {
                var handle = axis switch { AxisConstraint.X => GizmoHandle.MoveX, AxisConstraint.Y => GizmoHandle.MoveY, _ => GizmoHandle.MoveZ };
                var color = ModalTransform.AxisColor(axis);
                var from = Pivot + dir * (s * 0.18f);
                var tip = Pivot + dir * s;
                yield return (handle, from, tip, color);
                // Arrow head: four struts back from the tip.
                var (u, v) = Perpendiculars(dir);
                var back = tip - dir * (s * 0.16f);
                foreach (var side in new[] { u, -u, v, -v })
                    yield return (handle, tip, back + side * (s * 0.05f), color);
            }

            // Plane squares between each pair of axes.
            foreach (var (handle, a, b) in new[]
            {
                (GizmoHandle.MoveXY, Vector3.UnitX, Vector3.UnitY),
                (GizmoHandle.MoveYZ, Vector3.UnitY, Vector3.UnitZ),
                (GizmoHandle.MoveZX, Vector3.UnitZ, Vector3.UnitX),
            })
            {
                var color = Vector4.Lerp(AxisColorOf(a), AxisColorOf(b), 0.5f);
                var c = Pivot + (a + b) * (s * 0.40f);
                var ha = a * (s * 0.09f);
                var hb = b * (s * 0.09f);
                var p0 = c - ha - hb; var p1 = c + ha - hb; var p2 = c + ha + hb; var p3 = c - ha + hb;
                yield return (handle, p0, p1, color);
                yield return (handle, p1, p2, color);
                yield return (handle, p2, p3, color);
                yield return (handle, p3, p0, color);
            }
        }

        if (ShowRotate)
        {
            var radius = s * 1.15f;
            foreach (var (axis, dir) in Axes)
            {
                var handle = axis switch { AxisConstraint.X => GizmoHandle.RotateX, AxisConstraint.Y => GizmoHandle.RotateY, _ => GizmoHandle.RotateZ };
                var color = ModalTransform.AxisColor(axis);
                var (u, v) = Perpendiculars(dir);
                var prev = Pivot + u * radius;
                for (int i = 1; i <= RingSegments; i++)
                {
                    var t = i * MathF.Tau / RingSegments;
                    var p = Pivot + (u * MathF.Cos(t) + v * MathF.Sin(t)) * radius;
                    yield return (handle, prev, p, color);
                    prev = p;
                }
            }
        }

        if (ShowScale)
        {
            foreach (var (axis, dir) in Axes)
            {
                var handle = axis switch { AxisConstraint.X => GizmoHandle.ScaleX, AxisConstraint.Y => GizmoHandle.ScaleY, _ => GizmoHandle.ScaleZ };
                var color = ModalTransform.AxisColor(axis);
                var c = Pivot + dir * (s * 1.35f);
                if (!ShowMove) yield return (handle, Pivot + dir * (s * 0.18f), c, color);
                foreach (var seg in Cube(c, s * 0.06f)) yield return (handle, seg.A, seg.B, color);
            }
            // Uniform scale: a small screen-facing circle at the pivot.
            var white = new Vector4(0.9f, 0.9f, 0.9f, 0.9f);
            var r = s * 0.14f;
            var right = camera.Right;
            var up = camera.Up;
            var last = Pivot + right * r;
            for (int i = 1; i <= 24; i++)
            {
                var t = i * MathF.Tau / 24;
                var p = Pivot + (right * MathF.Cos(t) + up * MathF.Sin(t)) * r;
                yield return (GizmoHandle.ScaleUniform, last, p, white);
                last = p;
            }
        }
    }

    private static Vector4 AxisColorOf(Vector3 axis) =>
        axis.X > 0.5f ? ModalTransform.AxisColor(AxisConstraint.X)
        : axis.Y > 0.5f ? ModalTransform.AxisColor(AxisConstraint.Y)
        : ModalTransform.AxisColor(AxisConstraint.Z);

    private static (Vector3 U, Vector3 V) Perpendiculars(Vector3 dir)
    {
        var u = MathF.Abs(dir.Z) > 0.9f ? Vector3.UnitX : Vector3.UnitZ;
        u = Vector3.Normalize(Vector3.Cross(dir, u));
        var v = Vector3.Cross(dir, u);
        return (u, v);
    }

    private static IEnumerable<(Vector3 A, Vector3 B)> Cube(Vector3 c, float h)
    {
        var p = new Vector3[8];
        for (int i = 0; i < 8; i++)
            p[i] = c + new Vector3((i & 1) == 0 ? -h : h, (i & 2) == 0 ? -h : h, (i & 4) == 0 ? -h : h);
        int[][] edges = { new[] { 0, 1 }, new[] { 2, 3 }, new[] { 4, 5 }, new[] { 6, 7 }, new[] { 0, 2 }, new[] { 1, 3 }, new[] { 4, 6 }, new[] { 5, 7 }, new[] { 0, 4 }, new[] { 1, 5 }, new[] { 2, 6 }, new[] { 3, 7 } };
        foreach (var e in edges) yield return (p[e[0]], p[e[1]]);
    }

    // ----- Rendering -----

    public void AppendLines(Camera camera, List<OverlayLine> lines)
    {
        if (!Visible) return;
        var highlight = new Vector4(1f, 0.9f, 0.3f, 1f);
        foreach (var (handle, a, b, color) in Segments(camera))
        {
            var lit = handle == Active || (Active == GizmoHandle.None && handle == Hovered);
            lines.Add(new OverlayLine(a, b, lit ? highlight : color));
        }
    }

    // ----- Hit testing -----

    /// <summary>Returns the handle under the mouse. Small handles win over rings when both are close.</summary>
    public GizmoHandle HitTest(Camera camera, Vector2 mouse, float width, float height)
    {
        if (!Visible) return GizmoHandle.None;
        var best = GizmoHandle.None;
        var bestScore = float.PositiveInfinity;
        foreach (var (handle, a, b, _) in Segments(camera))
        {
            var pa = camera.WorldToScreen(a, width, height);
            var pb = camera.WorldToScreen(b, width, height);
            if (pa is null || pb is null) continue;
            var d = DistanceToSegment(mouse, pa.Value, pb.Value);
            if (d > HitRadiusPixels) continue;
            // Rings are large and easy to hit by accident; bias towards the compact handles.
            var score = d + (IsRing(handle) ? 4f : 0f);
            if (score < bestScore)
            {
                bestScore = score;
                best = handle;
            }
        }
        return best;
    }

    private static bool IsRing(GizmoHandle h) => h is GizmoHandle.RotateX or GizmoHandle.RotateY or GizmoHandle.RotateZ;

    private static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        var ab = b - a;
        var len2 = ab.LengthSquared();
        if (len2 < 1e-6f) return Vector2.Distance(p, a);
        var t = Math.Clamp(Vector2.Dot(p - a, ab) / len2, 0f, 1f);
        return Vector2.Distance(p, a + ab * t);
    }
}
