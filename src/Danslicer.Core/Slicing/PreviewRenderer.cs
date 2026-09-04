using System.Numerics;
using Danslicer.Core.Geometry;

namespace Danslicer.Core.Slicing;

/// <summary>
/// CPU software renderer that produces the small shaded three-quarter thumbnail embedded in the
/// PWMX file, replacing the older flat top-down height-map silhouette. Deliberately not GL-based:
/// the slicer runs headless (including under test), a 224x168 thumbnail is trivial to rasterise
/// on the CPU, and a software path is deterministic and easy to unit test.
/// </summary>
public static class PreviewRenderer
{
    /// <summary>
    /// Camera convention: a right-handed, Z-up turntable identical in spirit to
    /// Danslicer.Render.Camera's default startup view (Yaw -60 deg, Pitch 30 deg) and using the
    /// same View/Projection/NDC-to-screen mapping (System.Numerics' left-handed
    /// CreateLookAt/CreatePerspectiveFieldOfView, ndc.x -&gt; screen x left-to-right, ndc.y flipped
    /// to a top-left-origin image). The point of matching that math exactly is that the thumbnail
    /// then reads the same way round as the app's own 3D viewport.
    ///
    /// This fixes the historical mirroring: the old preview was accumulated straight out of the
    /// rasterised LCD layer buffer, which represents the exposure image as projected upward
    /// through the vat (i.e. viewed from below the plate), not the top-down view a human expects.
    /// The layer geometry itself was never wrong -- only that presentation reused a buffer with
    /// the "wrong" handedness for a thumbnail. Rendering independently from world-space
    /// geometry with the viewport's own camera convention sidesteps that entirely.
    /// </summary>
    private const float YawDegrees = -60f;
    private const float PitchDegrees = 30f;
    private const float FovDegrees = 40f;

    /// <summary>Extra headroom beyond the tight bounding-sphere fit, so the model doesn't touch the frame edge.</summary>
    private const float FrameMargin = 1.25f;

    private const float Ambient = 0.35f;
    private const float Diffuse = 0.65f;

    private static readonly Vector3 BackgroundColor = new(40, 42, 46);
    private static readonly Vector3 ModelColor = new(150, 170, 210);
    private static readonly Vector3 SupportColor = new(214, 130, 68);

    /// <summary>Fixed world-space light direction (points from the surface toward the light). Roughly front-upper-right.</summary>
    private static readonly Vector3 LightDir = Vector3.Normalize(new Vector3(-0.35f, -0.55f, 0.75f));

    public readonly record struct RenderObject(Mesh Mesh, Matrix4x4 Transform);

    private readonly struct RawTriangle
    {
        public readonly Vector3 A, B, C;
        public readonly Vector3 BaseColor;
        public RawTriangle(Vector3 a, Vector3 b, Vector3 c, Vector3 baseColor)
        {
            A = a; B = b; C = c; BaseColor = baseColor;
        }
    }

    /// <summary>
    /// Renders <paramref name="objects"/> (already filtered to visible ones, in local space with
    /// their own world transform) plus, when given, the visible non-disabled members of
    /// <paramref name="supports"/> (already in world space) into a <paramref name="width"/> x
    /// <paramref name="height"/> RGB565 buffer in the exact byte layout
    /// <see cref="Danslicer.Core.IO.PhotonWorkshopWriter"/> expects. An empty scene yields a flat
    /// background image rather than throwing.
    /// </summary>
    /// <summary>A background-filled preview of the requested size, used when nothing renders.</summary>
    public static byte[] Blank(int width, int height)
    {
        var data = new byte[width * height * 2];
        var background = Rgb565(BackgroundColor);
        for (int i = 0; i < width * height; i++)
            WritePixel(data, i, background);
        return data;
    }

    public static byte[] Render(IReadOnlyList<RenderObject> objects, Supports.SupportGraph? supports,
        int width, int height)
    {
        var data = new byte[width * height * 2];
        var backgroundRgb565 = Rgb565(BackgroundColor);
        for (int i = 0; i < width * height; i++)
            WritePixel(data, i, backgroundRgb565);

        var triangles = new List<RawTriangle>();

        foreach (var obj in objects)
            CollectTriangles(obj.Mesh, obj.Transform, ModelColor, triangles);

        if (supports is not null)
        {
            foreach (var part in Supports.SupportRenderMesh.Build(supports))
            {
                if (part.Disabled) continue;
                CollectTriangles(part.Mesh, Matrix4x4.Identity, SupportColor, triangles);
            }
        }

        // Frame from the triangles we actually kept, not from Mesh.Bounds: CollectTriangles has
        // already dropped any non-finite geometry, so a single bad vertex anywhere upstream cannot
        // poison the camera. A thumbnail must never be able to fail a slice.
        var bounds = Aabb.Empty;
        foreach (var tri in triangles)
            bounds = bounds.Include(tri.A).Include(tri.B).Include(tri.C);

        if (triangles.Count == 0 || bounds.IsEmpty) return data;

        var (view, proj, eye) = BuildCamera(bounds, width, height);
        var viewProj = view * proj;
        var depth = new float[width * height];
        Array.Fill(depth, float.PositiveInfinity);

        foreach (var tri in triangles)
            RasterizeTriangle(tri, eye, viewProj, width, height, depth, data);

        return data;
    }

    private static bool IsFinite(Vector3 v) =>
        float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);

    private static void CollectTriangles(Mesh mesh, Matrix4x4 transform, Vector3 baseColor, List<RawTriangle> output)
    {
        for (int t = 0; t < mesh.TriangleCount; t++)
        {
            mesh.GetTriangle(t, out var a, out var b, out var c);
            var pa = Vector3.Transform(a, transform);
            var pb = Vector3.Transform(b, transform);
            var pc = Vector3.Transform(c, transform);
            if (!IsFinite(pa) || !IsFinite(pb) || !IsFinite(pc)) continue;
            output.Add(new RawTriangle(pa, pb, pc, baseColor));
        }
    }

    private static (Matrix4x4 View, Matrix4x4 Projection, Vector3 Eye) BuildCamera(Aabb bounds, int width, int height)
    {
        var target = bounds.Center;
        var radius = MathF.Max(bounds.Radius, 1e-3f);
        if (!IsFinite(target) || !float.IsFinite(radius))
        {
            // Unreachable once bounds come from finite triangles, but a camera built from NaN
            // throws out of Matrix4x4.CreatePerspectiveFieldOfView and would fail the whole slice.
            target = Vector3.Zero;
            radius = 1f;
        }
        var fovRad = FovDegrees * MathF.PI / 180f;
        var distance = radius / MathF.Sin(fovRad * 0.5f) * FrameMargin;

        var yaw = YawDegrees * MathF.PI / 180f;
        var pitch = PitchDegrees * MathF.PI / 180f;
        var cp = MathF.Cos(pitch);
        var dir = new Vector3(cp * MathF.Cos(yaw), cp * MathF.Sin(yaw), MathF.Sin(pitch));
        var eye = target + dir * distance;

        var view = Matrix4x4.CreateLookAt(eye, target, Vector3.UnitZ);
        var aspect = width / (float)height;
        var near = MathF.Max(distance * 0.01f, 0.01f);
        var far = distance * 4f + 1000f;
        var proj = Matrix4x4.CreatePerspectiveFieldOfView(fovRad, aspect, near, far);
        return (view, proj, eye);
    }

    private static void RasterizeTriangle(in RawTriangle tri, Vector3 eye, in Matrix4x4 viewProj,
        int width, int height, float[] depth, byte[] data)
    {
        var normal = Vector3.Cross(tri.B - tri.A, tri.C - tri.A);
        var lengthSq = normal.LengthSquared();
        if (lengthSq < 1e-18f) return; // degenerate
        normal /= MathF.Sqrt(lengthSq);

        var centroid = (tri.A + tri.B + tri.C) / 3f;
        if (Vector3.Dot(normal, eye - centroid) <= 0f) return; // backface cull

        var brightness = Ambient + Diffuse * MathF.Max(Vector3.Dot(normal, LightDir), 0f);
        var color = new Vector3(
            Math.Clamp(tri.BaseColor.X * brightness, 0f, 255f),
            Math.Clamp(tri.BaseColor.Y * brightness, 0f, 255f),
            Math.Clamp(tri.BaseColor.Z * brightness, 0f, 255f));
        var rgb565 = Rgb565(color);

        if (!TryProject(tri.A, viewProj, width, height, out var p0) ||
            !TryProject(tri.B, viewProj, width, height, out var p1) ||
            !TryProject(tri.C, viewProj, width, height, out var p2))
            return;

        var area = Edge(p0, p1, p2);
        if (MathF.Abs(area) < 1e-9f) return;

        var minX = Math.Max(0, (int)MathF.Floor(MathF.Min(p0.X, MathF.Min(p1.X, p2.X))));
        var maxX = Math.Min(width - 1, (int)MathF.Ceiling(MathF.Max(p0.X, MathF.Max(p1.X, p2.X))));
        var minY = Math.Max(0, (int)MathF.Floor(MathF.Min(p0.Y, MathF.Min(p1.Y, p2.Y))));
        var maxY = Math.Min(height - 1, (int)MathF.Ceiling(MathF.Max(p0.Y, MathF.Max(p1.Y, p2.Y))));
        if (minX > maxX || minY > maxY) return;

        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                var pt = new Vector3(x + 0.5f, y + 0.5f, 0);
                var w0 = Edge(p1, p2, pt);
                var w1 = Edge(p2, p0, pt);
                var w2 = Edge(p0, p1, pt);
                var inside = area > 0 ? w0 >= 0 && w1 >= 0 && w2 >= 0 : w0 <= 0 && w1 <= 0 && w2 <= 0;
                if (!inside) continue;

                var b0 = w0 / area;
                var b1 = w1 / area;
                var b2 = w2 / area;
                var z = b0 * p0.Z + b1 * p1.Z + b2 * p2.Z;

                var idx = y * width + x;
                if (z >= depth[idx]) continue;
                depth[idx] = z;
                WritePixel(data, idx, rgb565);
            }
        }
    }

    private static bool TryProject(Vector3 world, in Matrix4x4 viewProj, int width, int height, out Vector3 screen)
    {
        var clip = Vector4.Transform(new Vector4(world, 1f), viewProj);
        if (clip.W <= 1e-5f)
        {
            screen = default;
            return false;
        }
        var ndc = new Vector3(clip.X, clip.Y, clip.Z) / clip.W;
        screen = new Vector3((ndc.X * 0.5f + 0.5f) * width, (1f - (ndc.Y * 0.5f + 0.5f)) * height, ndc.Z);
        return true;
    }

    private static float Edge(Vector3 a, Vector3 b, Vector3 c) =>
        (c.X - a.X) * (b.Y - a.Y) - (c.Y - a.Y) * (b.X - a.X);

    private static ushort Rgb565(Vector3 color)
    {
        var r = (byte)Math.Clamp(color.X, 0f, 255f);
        var g = (byte)Math.Clamp(color.Y, 0f, 255f);
        var b = (byte)Math.Clamp(color.Z, 0f, 255f);
        return (ushort)(((r >> 3) << 11) | ((g >> 2) << 5) | (b >> 3));
    }

    private static void WritePixel(byte[] data, int pixelIndex, ushort rgb565)
    {
        data[pixelIndex * 2] = (byte)rgb565;
        data[pixelIndex * 2 + 1] = (byte)(rgb565 >> 8);
    }
}
