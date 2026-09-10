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

    /// <summary>Default on-screen size in DIP-independent pixels, before DPI scaling. Raised from
    /// 96 to 120 by the user after seeing full-word labels at both sizes.</summary>
    public const int DefaultSizePixels = 120;

    // Margin as a fraction of the cube's size. Pinned to the original fixed-96 layout (10px at 96)
    // rather than to DefaultSizePixels, so changing the default does not silently move every cube.
    private const float MarginFraction = 10f / 96f;

    /// <summary>
    /// Clamp bounds for the configurable size (<see cref="Danslicer.Core.Config.ViewportConfig.ViewCubeSizePixels"/>).
    /// Core has no reference to this project, so <c>UserConfig.Normalize</c> repeats these two
    /// numbers rather than sharing the constant — keep them in sync if either changes.
    /// </summary>
    public const int MinSizePixels = 48;
    public const int MaxSizePixels = 192;

    /// <summary>
    /// Label text width in face-plane units (-1..1 spans the whole face); sized off the widest
    /// label ("BOTTOM") so every face uses the same glyph scale and nothing overflows the face.
    /// 0.9 of the face, matching the proportions Fusion's view cube uses for its full-word labels.
    /// </summary>
    public const float LabelTargetWidth = 1.8f;

    /// <summary>Size of one font cell in face-plane units. Public for the legibility test, which
    /// pins the rendered glyph height as a fraction of the face rather than trusting the eye.</summary>
    public static float LabelCellSize => LabelTargetWidth / ViewCubeLabels.Measure("BOTTOM").Width;

    /// <summary>Rendered glyph height as a fraction of the face (face height is 2 units).</summary>
    public static float LabelHeightFraction => LabelCellSize * ViewCubeLabels.GlyphHeight / 2f;

    // Strokes are drawn as solid runs that overlap their neighbours by this fraction of a cell, so
    // a diagonal or a corner joins up instead of leaving a hairline seam at the cube's small scale.
    private const float LabelStrokeBleed = 0.08f;

    // A dark halo of this many cells is drawn behind the glyphs. One fixed label colour cannot have
    // good contrast against both the bright +axis faces and the dimmed -axis ones; the halo is what
    // makes a single colour work everywhere, and it thickens the apparent stroke into the bargain.
    private const float LabelHaloExtent = 0.32f;

    /// <summary>The one label colour, every face: near-black on the light grey faces, the way
    /// Fusion's cube reads. The halo is a touch lighter than the lightest face, so a glyph keeps
    /// its edge where it crosses the darker region-grid cells.</summary>
    private static readonly Vector3 LabelInk = new(0.13f);
    private static readonly Vector3 LabelHalo = new(0.94f);

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

    /// <summary>
    /// Corner viewport of the cube in framebuffer pixels (GL origin, bottom-left).
    /// <paramref name="sizePixels"/> is the configured on-screen size before DPI scaling
    /// (<see cref="DefaultSizePixels"/> when unset); the margin keeps the same proportion to it
    /// that the original fixed-96 layout used (see <see cref="MarginFraction"/>), so bigger cubes
    /// get a bigger margin too.
    /// </summary>
    public static (int X, int Y, int Size) Rect(int width, int height, double scaling,
        int sizePixels = DefaultSizePixels)
    {
        var clamped = Math.Clamp(sizePixels, MinSizePixels, MaxSizePixels);
        var size = (int)(clamped * Math.Max(scaling, 0.5));
        var margin = (int)(clamped * MarginFraction * Math.Max(scaling, 0.5));
        return (width - size - margin, height - size - margin, size);
    }

    /// <summary>
    /// Camera rotation per pointer pixel while dragging the cube, in degrees. Chosen so the cube
    /// turns under the pointer like a ball of its own on-screen radius — a pixel of drag is one
    /// pixel of arc at that radius — which is what makes a cube feel grabbed rather than nudged.
    /// That comes out at about 1.42 deg/px at the default 120px cube, roughly three times a
    /// viewport orbit; the cube is a coarse control by nature, being only ~60px across.
    ///
    /// Pointer deltas arrive in DIPs, and <see cref="Rect"/> scales the cube by exactly the same
    /// DPI factor the pointer positions are scaled by, so the scaling cancels and the cube's size
    /// in pointer units is just <paramref name="sizePixels"/>. Deriving the rate from the size
    /// keeps the same "grab and turn" feel across the whole configurable range (3.5 deg/px at the
    /// 48px minimum, 0.89 deg/px at 192px) instead of making a small cube sluggish.
    /// </summary>
    public static float DragDegreesPerPixel(int sizePixels = DefaultSizePixels)
    {
        var clamped = Math.Clamp(sizePixels, MinSizePixels, MaxSizePixels);
        var pixelsPerUnit = clamped / (2f * OrthoExtent);
        // sqrt(2) is the edge-midpoint radius: between the face centres you usually grab and the
        // corners, so neither a face drag nor a corner drag feels wrong.
        var radiusPixels = MathF.Sqrt(2f) * pixelsPerUnit;
        return 180f / MathF.PI / radiusPixels;
    }

    // ----- Pure-math picking -----

    /// <summary>
    /// Region under a framebuffer pixel, or -1. The mouse is unprojected through the cube's own
    /// orthographic camera and intersected with the unit cube analytically.
    /// </summary>
    public static int HitRegion(float pxX, float pxY, int width, int height, double scaling,
        in Matrix4x4 cameraView, int sizePixels = DefaultSizePixels)
    {
        var (rx, ry, size) = Rect(width, height, scaling, sizePixels);
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

    public void Draw(int width, int height, double scaling, in Matrix4x4 cameraView, int hoverRegion,
        int sizePixels = DefaultSizePixels)
    {
        var gl = _gl;
        var (rx, ry, size) = Rect(width, height, scaling, sizePixels);
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
    /// <summary>
    /// Neutral greys. The axis colours (red/green/blue by axis, dimmed on the -side) are gone at
    /// the user's request — Fusion's cube, the reference they gave, is a plain light-grey solid.
    /// A small per-axis step and a dim on the -axis faces remain so the cube still reads as a lit
    /// object rather than a flat silhouette; the only colour left on it is the amber hover tint
    /// applied in the fragment shader, which now has the whole cube to itself.
    /// </summary>
    private static Vector3 FaceColor(int axis, int sign)
    {
        var level = axis switch
        {
            0 => 0.72f,
            1 => 0.76f,
            _ => 0.82f, // Z: the top face catches the most light, as it would in life
        };
        return new Vector3(sign > 0 ? level : level * 0.86f);
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

        // Labels draw last so they land on top of the face quads above (Draw() has no depth
        // test, so later-submitted triangles simply win the pixel — no texture, no extra pass,
        // and the same buffer/shader draws both render paths since ViewCube.Draw is shared).
        foreach (var (axis, sign, text) in ViewCubeLabels.Faces)
            AddLabel(data, axis, sign, text);

        return [.. data];

        static void Set(ref int x, ref int y, ref int z, int axis, int value)
        {
            if (axis == 0) x = value;
            else if (axis == 1) y = value;
            else z = value;
        }
    }

    /// <summary>
    /// Bakes one face's label as a grid of tiny quads, one per lit font cell. The face-plane basis
    /// (<paramref name="axis"/>/<paramref name="sign"/>) is derived the same way an outside camera
    /// looking straight at the face would be (world-up reference, right = up x normal), so every
    /// face reads upright and non-mirrored regardless of which side of the cube it is on.
    /// </summary>
    private static void AddLabel(List<float> data, int axis, int sign, string text)
    {
        var normal = AxisVector(axis) * sign;
        var worldUp = MathF.Abs(normal.Z) > 0.99f ? new Vector3(0, 1, 0) : new Vector3(0, 0, 1);
        var right = Vector3.Normalize(Vector3.Cross(worldUp, normal));
        var up = Vector3.Cross(normal, right);

        var (widthPx, heightPx) = ViewCubeLabels.Measure(text);
        var pixel = LabelCellSize; // one scale for all faces, so letters are the same size everywhere
        var totalWidth = widthPx * pixel;
        var totalHeight = heightPx * pixel;
        var centerRegion = RegionId(
            axis == 0 ? sign : 0, axis == 1 ? sign : 0, axis == 2 ? sign : 0);

        // Halo first, then the ink over it: Draw() has no depth test, so whatever is submitted
        // later simply wins the pixel.
        var runs = ViewCubeLabels.Runs(text).ToList();
        foreach (var (color, grow) in new[] { (LabelHalo, LabelHaloExtent), (LabelInk, LabelStrokeBleed) })
        foreach (var (row, col, length) in runs)
        {
            // Cell (row, col) spans [col, col+1) across and [row, row+1) down from the top-left of
            // the text block; a run of length cells is one quad, not `length` of them.
            var u0 = (col - grow) * pixel - totalWidth / 2f;
            var u1 = (col + length + grow) * pixel - totalWidth / 2f;
            var v0 = totalHeight / 2f - (row + 1 + grow) * pixel;
            var v1 = totalHeight / 2f - (row - grow) * pixel;
            var p00 = normal + right * u0 + up * v0;
            var p10 = normal + right * u1 + up * v0;
            var p11 = normal + right * u1 + up * v1;
            var p01 = normal + right * u0 + up * v1;
            // CCW as seen from outside: right/up/normal form a camera-style basis (x,y,z-toward-
            // viewer), so this winding matches the CCW front-face convention used everywhere else.
            foreach (var p in new[] { p00, p10, p11, p00, p11, p01 })
            {
                data.Add(p.X); data.Add(p.Y); data.Add(p.Z);
                data.Add(color.X); data.Add(color.Y); data.Add(color.Z);
                data.Add(centerRegion);
            }
        }
    }

    private static Vector3 AxisVector(int axis) =>
        axis == 0 ? Vector3.UnitX : axis == 1 ? Vector3.UnitY : Vector3.UnitZ;

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
