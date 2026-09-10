using System.Numerics;
using Danslicer.Core.Geometry;

namespace Danslicer.Render;

/// <summary>
/// Turntable camera with Z up. Orbits about <see cref="Target"/>; supports perspective and orthographic
/// projection. All angles in radians.
/// </summary>
public sealed class Camera
{
    private const float MaxPitch = 89.9f * MathF.PI / 180f;

    /// <summary>
    /// Rotation per pixel of pointer movement for <see cref="Orbit"/>, the one orbit path in the
    /// app. The view cube's drag-orbit is faster than a viewport drag, but it gets there by
    /// pre-scaling its pixel deltas against this number (see <see cref="ViewCubeDragGesture"/>)
    /// rather than by owning a second rotation rule and a second pitch clamp.
    /// </summary>
    public const float OrbitRadiansPerPixel = 0.008f;

    public Vector3 Target { get; set; } = new(0, 0, 30);
    public float Distance { get; set; } = 400;
    /// <summary>Rotation about Z. 0 looks along -X from +X; -90 degrees is the front view looking along +Y.</summary>
    public float Yaw { get; set; } = -60f * MathF.PI / 180f;
    /// <summary>Elevation above the XY plane.</summary>
    public float Pitch { get; set; } = 30f * MathF.PI / 180f;
    public float FovDegrees { get; set; } = 40;
    public bool Orthographic { get; set; }

    public Vector3 Eye
    {
        get
        {
            var cp = MathF.Cos(Pitch);
            var dir = new Vector3(cp * MathF.Cos(Yaw), cp * MathF.Sin(Yaw), MathF.Sin(Pitch));
            return Target + dir * Distance;
        }
    }

    public Vector3 ViewDirection => Vector3.Normalize(Target - Eye);
    public Vector3 Right => Vector3.Normalize(Vector3.Cross(ViewDirection, Vector3.UnitZ));
    public Vector3 Up => Vector3.Normalize(Vector3.Cross(Right, ViewDirection));

    public Matrix4x4 View => Matrix4x4.CreateLookAt(Eye, Target, Vector3.UnitZ);

    public float Near => MathF.Max(Distance * 0.002f, 0.05f);
    public float Far => Distance * 20f + 2000f;

    /// <summary>Height of the view frustum at the target distance, in world units.</summary>
    public float ViewHeightAtTarget => 2f * Distance * MathF.Tan(FovDegrees * 0.5f * MathF.PI / 180f);

    public Matrix4x4 Projection(float aspect)
    {
        if (Orthographic)
        {
            var h = ViewHeightAtTarget;
            return Matrix4x4.CreateOrthographic(h * aspect, h, -Far, Far);
        }
        return Matrix4x4.CreatePerspectiveFieldOfView(FovDegrees * MathF.PI / 180f, aspect, Near, Far);
    }

    public Matrix4x4 ViewProjection(float aspect) => View * Projection(aspect);

    public void Orbit(float dxPixels, float dyPixels)
    {
        Yaw -= dxPixels * OrbitRadiansPerPixel;
        Pitch = Math.Clamp(Pitch + dyPixels * OrbitRadiansPerPixel, -MaxPitch, MaxPitch);
    }

    public void Pan(float dxPixels, float dyPixels, float viewportHeightPixels)
    {
        var unitsPerPixel = ViewHeightAtTarget / MathF.Max(viewportHeightPixels, 1);
        Target += -Right * (dxPixels * unitsPerPixel) + Up * (dyPixels * unitsPerPixel);
    }

    public void Zoom(float steps) => Distance = Math.Clamp(Distance * MathF.Pow(0.85f, steps), 1f, 50000f);

    public void Frame(Aabb bounds)
    {
        if (bounds.IsEmpty) return;
        Target = bounds.Center;
        var radius = MathF.Max(bounds.Radius, 1f);
        Distance = radius / MathF.Sin(FovDegrees * 0.5f * MathF.PI / 180f) * 1.1f;
    }

    public void SetView(float yawDegrees, float pitchDegrees)
    {
        Yaw = yawDegrees * MathF.PI / 180f;
        Pitch = Math.Clamp(pitchDegrees * MathF.PI / 180f, -MaxPitch, MaxPitch);
    }

    public void ViewFront() => SetView(-90, 0);
    public void ViewBack() => SetView(90, 0);
    public void ViewRight() => SetView(0, 0);
    public void ViewLeft() => SetView(180, 0);
    public void ViewTop() => SetView(-90, 89.9f);
    public void ViewBottom() => SetView(-90, -89.9f);

    /// <summary>World-space ray through a pixel. Pixel origin is top-left.</summary>
    public Ray ScreenToRay(float px, float py, float width, float height)
    {
        var ndcX = px / width * 2f - 1f;
        var ndcY = 1f - py / height * 2f;
        Matrix4x4.Invert(ViewProjection(width / height), out var inv);
        var near = Unproject(new Vector3(ndcX, ndcY, -1f), inv);
        var far = Unproject(new Vector3(ndcX, ndcY, 1f), inv);
        return new Ray(near, Vector3.Normalize(far - near));
    }

    private static Vector3 Unproject(Vector3 ndc, in Matrix4x4 invViewProj)
    {
        var v = Vector4.Transform(new Vector4(ndc, 1f), invViewProj);
        return new Vector3(v.X, v.Y, v.Z) / v.W;
    }

    /// <summary>Projects a world point to pixel coordinates. Null when behind the camera.</summary>
    public Vector2? WorldToScreen(Vector3 world, float width, float height)
    {
        var clip = Vector4.Transform(new Vector4(world, 1f), ViewProjection(width / height));
        if (clip.W <= 1e-6f) return null;
        var ndc = new Vector2(clip.X, clip.Y) / clip.W;
        return new Vector2((ndc.X + 1f) * 0.5f * width, (1f - ndc.Y) * 0.5f * height);
    }
}
