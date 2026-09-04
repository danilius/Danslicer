using System.Numerics;
using Silk.NET.OpenGL;

namespace Danslicer.Render;

/// <summary>
/// The corner view cube (design 6.2): each face is a 3x3 grid of quads grouped into 26 pick
/// regions — 6 faces, 12 edges, 8 corners. It draws last, over the finished frame, in its own
/// corner viewport with backface culling and no depth test (the cube is convex), so it renders
/// identically over the classic and deferred paths. Hit testing and the region-to-view mapping
/// are pure math on the UI thread — no GL context needed until the draw.
/// </summary>
public sealed unsafe class ViewCube : IDisposable
{
    // A cell coordinate past this magnitude belongs to the edge/corner band of its face.
    private const float Band = 0.6f;
    private const float OrthoExtent = 2.1f; // > cube diagonal radius sqrt(3), any rotation fits

    private readonly GL _gl;
    private readonly ShaderProgram _shader;
    private readonly uint _vao;
    private readonly uint _vbo;
    private readonly int _vertexCount;

    public ViewCube(GL gl, bool gles)
    {
        _gl = gl;
        var preamble = Shaders.Preamble(gles);
        _shader = new ShaderProgram(gl, preamble + CubeVertex, preamble + CubeFragment);

        var verts = BuildVertices();
        _vertexCount = verts.Length / 7;
        _vao = gl.GenVertexArray();
        _vbo = gl.GenBuffer();
        gl.BindVertexArray(_vao);
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
        fixed (float* p = verts)
            gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(verts.Length * sizeof(float)), p,
                BufferUsageARB.StaticDraw);
        gl.EnableVertexAttribArray(0);
        gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 7 * sizeof(float), (void*)0);
        gl.EnableVertexAttribArray(1);
        gl.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, 7 * sizeof(float),
            (void*)(3 * sizeof(float)));
        gl.EnableVertexAttribArray(2);
        gl.VertexAttribPointer(2, 1, VertexAttribPointerType.Float, false, 7 * sizeof(float),
            (void*)(6 * sizeof(float)));
        gl.BindVertexArray(0);
    }

    // ----- Layout shared by drawing and hit testing -----

    /// <summary>Corner viewport of the cube in framebuffer pixels (GL origin, bottom-left).</summary>
    public static (int X, int Y, int Size) Rect(int width, int height, double scaling)
    {
        var size = (int)(96 * Math.Max(scaling, 0.5));
        var margin = (int)(10 * Math.Max(scaling, 0.5));
        return (width - size - margin, height - size - margin, size);
    }

    // ----- Pure-math picking -----

    /// <summary>
    /// Region under a framebuffer pixel, or -1. The mouse is unprojected through the cube's own
    /// orthographic camera and intersected with the unit cube analytically.
    /// </summary>
    public static int HitRegion(float pxX, float pxY, int width, int height, double scaling,
        in Matrix4x4 cameraView)
    {
        var (rx, ry, size) = Rect(width, height, scaling);
        // pxY arrives top-left based (pointer coords); the rect is bottom-left based.
        var glY = height - 1 - pxY;
        if (pxX < rx || pxX >= rx + size || glY < ry || glY >= ry + size) return -1;

        var u = ((pxX - rx) / size * 2f - 1f) * OrthoExtent;
        var v = ((glY - ry) / size * 2f - 1f) * OrthoExtent;

        // View-space ray straight down -Z; rotate into cube space with the transposed rotation.
        var rot = RotationOnly(cameraView);
        Matrix4x4.Invert(rot, out var toCube);
        var origin = Vector3.Transform(new Vector3(u, v, 3f), toCube);
        var direction = Vector3.TransformNormal(new Vector3(0, 0, -1f), toCube);

        if (!RayUnitBox(origin, direction, out var hit)) return -1;
        return RegionId(Component(hit.X), Component(hit.Y), Component(hit.Z));

        static int Component(float c) => c > Band ? 1 : c < -Band ? -1 : 0;
    }

    /// <summary>
    /// Camera angles for a region: look at the cube from the region's outward direction.
    /// Poles keep the current yaw so Top/Bottom do not spin the plate.
    /// </summary>
    public static (float YawDegrees, float PitchDegrees) ViewAngles(int region, float currentYawDegrees)
    {
        var (x, y, z) = RegionComponents(region);
        var e = Vector3.Normalize(new Vector3(x, y, z));
        var pitch = MathF.Asin(Math.Clamp(e.Z, -1f, 1f)) * 180f / MathF.PI;
        var yaw = MathF.Abs(e.Z) > 0.999f
            ? currentYawDegrees
            : MathF.Atan2(e.Y, e.X) * 180f / MathF.PI;
        return (yaw, pitch);
    }

    // ----- Drawing -----

    public void Draw(int width, int height, double scaling, in Matrix4x4 cameraView, int hoverRegion)
    {
        var gl = _gl;
        var (rx, ry, size) = Rect(width, height, scaling);
        if (size <= 0 || rx < 0 || ry < 0) return;

        gl.Viewport(rx, ry, (uint)size, (uint)size);
        gl.Disable(EnableCap.DepthTest);
        gl.Disable(EnableCap.Blend);
        gl.Enable(EnableCap.CullFace);
        gl.CullFace(TriangleFace.Back);

        var projection = Matrix4x4.CreateOrthographic(OrthoExtent * 2, OrthoExtent * 2, -4f, 4f);
        _shader.Use();
        _shader.Set("uView", RotationOnly(cameraView));
        _shader.Set("uProjection", projection);
        _shader.Set("uHover", hoverRegion < 0 ? -10f : hoverRegion);
        gl.BindVertexArray(_vao);
        gl.DrawArrays(PrimitiveType.Triangles, 0, (uint)_vertexCount);
        gl.BindVertexArray(0);

        gl.Disable(EnableCap.CullFace);
        gl.Enable(EnableCap.DepthTest);
        gl.Viewport(0, 0, (uint)width, (uint)height);
    }

    public void Dispose()
    {
        _gl.DeleteBuffer(_vbo);
        _gl.DeleteVertexArray(_vao);
        _shader.Dispose();
    }

    // ----- Internals -----

    private static Matrix4x4 RotationOnly(in Matrix4x4 view)
    {
        var m = view;
        m.M41 = 0; m.M42 = 0; m.M43 = 0;
        return m;
    }

    private static int RegionId(int x, int y, int z) => (x + 1) * 9 + (y + 1) * 3 + (z + 1);

    private static (int X, int Y, int Z) RegionComponents(int region) =>
        (region / 9 - 1, region / 3 % 3 - 1, region % 3 - 1);

    private static bool RayUnitBox(Vector3 origin, Vector3 direction, out Vector3 hit)
    {
        hit = default;
        float tMin = float.NegativeInfinity, tMax = float.PositiveInfinity;
        for (var axis = 0; axis < 3; axis++)
        {
            var o = axis == 0 ? origin.X : axis == 1 ? origin.Y : origin.Z;
            var d = axis == 0 ? direction.X : axis == 1 ? direction.Y : direction.Z;
            if (MathF.Abs(d) < 1e-8f)
            {
                if (o < -1 || o > 1) return false;
                continue;
            }
            var t1 = (-1 - o) / d;
            var t2 = (1 - o) / d;
            if (t1 > t2) (t1, t2) = (t2, t1);
            tMin = MathF.Max(tMin, t1);
            tMax = MathF.Min(tMax, t2);
            if (tMin > tMax) return false;
        }
        hit = origin + direction * tMin;
        return true;
    }

    /// <summary>Blender axis palette; negative faces dimmed, border cells darkened a touch.</summary>
    private static Vector3 FaceColor(int axis, int sign)
    {
        var c = axis switch
        {
            0 => new Vector3(0.84f, 0.31f, 0.36f),
            1 => new Vector3(0.47f, 0.72f, 0.23f),
            _ => new Vector3(0.28f, 0.52f, 0.86f),
        };
        return sign > 0 ? c : c * 0.55f;
    }

    private static float[] BuildVertices()
    {
        var data = new List<float>(54 * 6 * 7);
        Span<float> edges = [-1f, -Band, Band, 1f];
        for (var axis = 0; axis < 3; axis++)
        {
            foreach (var sign in new[] { 1, -1 })
            {
                var t1 = (axis + 1) % 3;
                var t2 = (axis + 2) % 3;
                for (var i = 0; i < 3; i++)
                for (var j = 0; j < 3; j++)
                {
                    var ci = i - 1; // cell coordinate -1|0|1 along t1
                    var cj = j - 1;
                    int rx = 0, ry = 0, rz = 0;
                    Set(ref rx, ref ry, ref rz, axis, sign);
                    Set(ref rx, ref ry, ref rz, t1, ci);
                    Set(ref rx, ref ry, ref rz, t2, cj);
                    var region = RegionId(rx, ry, rz);
                    var color = FaceColor(axis, sign);
                    if (ci != 0 || cj != 0) color *= 0.82f; // sketch the region grid
                    Quad(data, axis, sign, t1, t2,
                        edges[i], edges[i + 1], edges[j], edges[j + 1], color, region);
                }
            }
        }
        return [.. data];

        static void Set(ref int x, ref int y, ref int z, int axis, int value)
        {
            if (axis == 0) x = value;
            else if (axis == 1) y = value;
            else z = value;
        }
    }

    private static void Quad(List<float> data, int axis, int sign, int t1, int t2,
        float a0, float a1, float b0, float b1, Vector3 color, int region)
    {
        var p00 = Corner(axis, sign, t1, t2, a0, b0);
        var p10 = Corner(axis, sign, t1, t2, a1, b0);
        var p11 = Corner(axis, sign, t1, t2, a1, b1);
        var p01 = Corner(axis, sign, t1, t2, a0, b1);
        // Winding flips with the face sign so front faces stay CCW from outside.
        var quad = sign > 0
            ? new[] { p00, p10, p11, p00, p11, p01 }
            : new[] { p00, p11, p10, p00, p01, p11 };
        foreach (var p in quad)
        {
            data.Add(p.X); data.Add(p.Y); data.Add(p.Z);
            data.Add(color.X); data.Add(color.Y); data.Add(color.Z);
            data.Add(region);
        }

        static Vector3 Corner(int axis, int sign, int t1, int t2, float a, float b)
        {
            Span<float> v = stackalloc float[3];
            v[axis] = sign;
            v[t1] = a;
            v[t2] = b;
            return new Vector3(v[0], v[1], v[2]);
        }
    }

    private const string CubeVertex = """
        layout(location = 0) in vec3 aPosition;
        layout(location = 1) in vec3 aColor;
        layout(location = 2) in float aRegion;

        uniform mat4 uView;
        uniform mat4 uProjection;

        out vec3 vColor;
        flat out float vRegion;

        void main()
        {
            vColor = aColor;
            vRegion = aRegion;
            gl_Position = uProjection * uView * vec4(aPosition, 1.0);
        }
        """;

    private const string CubeFragment = """
        in vec3 vColor;
        flat in float vRegion;

        uniform float uHover;

        out vec4 fragColor;

        void main()
        {
            float hover = abs(vRegion - uHover) < 0.5 ? 1.0 : 0.0;
            vec3 color = mix(vColor, vec3(1.0, 0.78, 0.35), hover * 0.65);
            fragColor = vec4(color, 1.0);
        }
        """;
}
