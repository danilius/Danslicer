namespace Danslicer.Render;

/// <summary>GLSL sources. A preamble selects GL 3.3 core or GL ES 3.0 at runtime.</summary>
internal static class Shaders
{
    public static string Preamble(bool gles) => gles
        ? "#version 300 es\nprecision highp float;\nprecision highp int;\n"
        : "#version 330 core\n";

    public const string MeshVertex = """
        layout(location = 0) in vec3 aPosition;
        layout(location = 1) in vec3 aNormal;

        uniform mat4 uModel;
        uniform mat4 uView;
        uniform mat4 uProjection;
        uniform mat4 uNormalMatrix;      // transpose(inverse(model * view)), see ShaderProgram.Set
        uniform mat4 uModelNormalMatrix; // transpose(inverse(model)), for world-space normals
        uniform float uShadow;           // 1 = project the mesh onto the plate as its shadow
        uniform vec3 uShadowDir;         // direction the shadow light travels (z < 0), world space
        uniform float uMirror;
        uniform float uShadowZ;          // world Z of the shadow surface

        out vec3 vViewNormal;
        out vec3 vWorldNormal;
        out vec3 vWorldPosition;
        out vec3 vViewPosition;

        void main()
        {
            vec4 world = uModel * vec4(aPosition, 1.0);
            vec3 viewNormal = normalize(mat3(uNormalMatrix) * aNormal);
            vec3 worldNormal = normalize(mat3(uModelNormalMatrix) * aNormal);
            if (uShadow > 0.5)
            {
                // Every vertex slides along the light until it meets the shadow plane, so the
                // mesh collapses to its lit footprint; the surface faces up whatever the source.
                world.xyz += uShadowDir * ((uShadowZ - world.z) / uShadowDir.z);
                worldNormal = vec3(0.0, 0.0, 1.0);
                viewNormal = normalize(mat3(uView) * worldNormal);
            }
            vec3 sourcePosition = world.xyz;
            if (uMirror > 0.5)
            {
                world.z = -world.z - 0.1;
                worldNormal.z = -worldNormal.z;
                viewNormal = normalize(mat3(uView) * worldNormal);
            }
            vec4 view = uView * world;
            vWorldPosition = sourcePosition;
            vViewPosition = view.xyz;
            vViewNormal = viewNormal;
            vWorldNormal = worldNormal;
            gl_Position = uProjection * view;
        }
        """;

    /// <summary>
    /// Studio lighting: fixed key, fill and rim lights in view space so that every surface has a lit and
    /// a shaded side regardless of view direction. Half-Lambert wrap keeps shadow sides readable.
    /// </summary>
    public const string MeshFragment = """
        in vec3 vViewNormal;
        in vec3 vWorldNormal;
        in vec3 vWorldPosition;
        in vec3 vViewPosition;

        uniform float uPlateMaterial;
        uniform float uMirror;
        uniform float uReflectionStrength;
        uniform highp sampler2D uReflectionTex;
        uniform vec2 uReflectionTexel;
        uniform vec2 uViewportSize;
        uniform vec3 uColor;
        uniform float uBackfaceTint;   // 1 = tint back faces to reveal inverted normals
        uniform float uOpacity;
        uniform float uWarnOutsideBuildVolume; // 1 = tint geometry outside printable XYZ
        uniform vec3 uBuildVolume;             // centred X/Y extents, Z travel from zero
        uniform float uOverhangCos;    // cos of the overhang angle from straight down; 2 disables
        uniform vec3 uOverhangColorA;  // checker colour on even cells
        uniform vec3 uOverhangColorB;  // checker colour on odd cells
        uniform float uOverhangCell;   // checker cell edge, mm
        uniform float uClipEnabled;
        uniform float uClipLowerZ;
        uniform float uClipUpperZ;
        uniform float uShadow;         // 1 = this draw is a plate shadow (see the vertex stage)
        uniform float uWaterlineEnabled;
        uniform float uWaterlineZ;

        out vec4 fragColor;

        float wrap(float ndotl) { float d = ndotl * 0.5 + 0.5; return d * d; }

        void main()
        {
            if (uClipEnabled > 0.5 &&
                (vWorldPosition.z < uClipLowerZ || vWorldPosition.z > uClipUpperZ)) discard;

            vec3 n = normalize(vViewNormal);
            // A shadow is one flat surface whatever the winding of the triangles that made it.
            bool back = (!gl_FrontFacing != (uMirror > 0.5)) && uShadow < 0.5;
            if (back) n = -n;
            vec3 v = normalize(-vViewPosition);

            // Light directions (towards the light) in view space.
            vec3 key  = normalize(vec3( 0.45,  0.55,  0.70));
            vec3 fill = normalize(vec3(-0.70,  0.10,  0.45));
            vec3 rim  = normalize(vec3( 0.20, -0.60, -0.75));

            float diffuse =
                wrap(dot(n, key))  * 0.75 +
                wrap(dot(n, fill)) * 0.30 +
                max(dot(n, rim), 0.0) * 0.20;

            vec3 h = normalize(key + v);
            float spec = pow(max(dot(n, h), 0.0), uPlateMaterial > 0.5 ? 10.0 : 48.0) * (uPlateMaterial > 0.5 ? 0.06 : 0.18);

            // Fresnel-style edge lift separates silhouettes from what lies behind them.
            float edge = pow(1.0 - max(dot(n, v), 0.0), 3.0) * 0.12;

            vec3 color = uColor;
            if (uMirror > 0.5 && vWorldPosition.z < 0.0) discard;
            if (uPlateMaterial > 0.5)
            {
                // Broad satin highlight with a tiny world-locked brushed variation.
                float grain = sin(vWorldPosition.x * 38.0 + sin(vWorldPosition.y * 0.7)) * 0.004;
                color += vec3(grain);
                if (vWorldNormal.z > 0.9 && uReflectionStrength > 0.0)
                {
                    vec2 uv = gl_FragCoord.xy / uViewportSize;
                    vec4 reflection = vec4(0.0);
                    for (int x = -1; x <= 1; x++)
                    for (int y = -1; y <= 1; y++)
                    {
                        float w = (x == 0 ? 2.0 : 1.0) * (y == 0 ? 2.0 : 1.0);
                        vec2 tap = uv + vec2(float(x), float(y)) * uReflectionTexel * 2.0;
                        if (all(greaterThanEqual(tap, vec2(0.0))) && all(lessThanEqual(tap, vec2(1.0))))
                            reflection += texture(uReflectionTex, tap) * w / 16.0;
                    }
                    // Premultiplied clear-black samples soften silhouettes without a dark border.
                    color = color * (1.0 - reflection.a * uReflectionStrength) + reflection.rgb * uReflectionStrength;
                }
            }
            if (back) color = mix(color, vec3(0.85, 0.30, 0.55), uBackfaceTint * 0.6);

            // Overhang tint: surfaces facing downward within the threshold of straight down show
            // a solid two-colour world-space checker, so overhangs read clearly on any base
            // colour at any lighting. Colours and cell size come from user configuration; only
            // the threshold edge is softened.
            if (uOverhangCos < 1.5)
            {
                float down = dot(normalize(vWorldNormal), vec3(0.0, 0.0, -1.0));
                float over = smoothstep(uOverhangCos - 0.06, uOverhangCos + 0.02, down);
                if (over > 0.0)
                {
                    vec3 cells = floor(vWorldPosition / max(uOverhangCell, 0.1));
                    float checker = mod(cells.x + cells.y + cells.z, 2.0);
                    vec3 warn = mix(uOverhangColorA, uOverhangColorB, checker);
                    color = mix(color, warn, over);
                }
            }

            bool outsideBuildVolume =
                abs(vWorldPosition.x) > uBuildVolume.x * 0.5 + 0.001 ||
                abs(vWorldPosition.y) > uBuildVolume.y * 0.5 + 0.001 ||
                vWorldPosition.z < -0.001 || vWorldPosition.z > uBuildVolume.z + 0.001;
            if (uWarnOutsideBuildVolume > 0.5 && outsideBuildVolume)
                color = mix(color, vec3(0.95, 0.15, 0.10), 0.6);

            if (uClipEnabled > 0.5)
            {
                float distanceToCut = min(abs(vWorldPosition.z - uClipLowerZ),
                                          abs(vWorldPosition.z - uClipUpperZ));
                float cutBand = max(fwidth(vWorldPosition.z) * 1.5, 0.002);
                float cut = 1.0 - smoothstep(0.0, cutBand, distanceToCut);
                color = mix(color, vec3(1.0, 0.55, 0.16), cut * 0.8);
            }

            vec3 lit = color * (diffuse + 0.08) + vec3(spec) + vec3(edge);

            // A derivative-sized band stays approximately constant in screen pixels as the
            // camera moves. Tight world-space clamps keep grazing and nearly-horizontal faces
            // useful without turning the contour into a broad wash.
            if (uWaterlineEnabled > 0.5)
            {
                float band = clamp(fwidth(vWorldPosition.z) * 1.75, 0.008, 0.25);
                float contour = 1.0 - smoothstep(band * 0.35, band, abs(vWorldPosition.z - uWaterlineZ));
                lit = mix(lit, vec3(0.04, 0.96, 0.92), contour * 0.96);
            }
            fragColor = vec4(lit, uOpacity);
        }
        """;

    /// <summary>
    /// Wireframe overlay over the mesh VBO (attribute 0 only). The clip-space nudge pulls the
    /// lines a hair toward the camera so they win the depth test against their own surface.
    /// </summary>
    public const string WireVertex = """
        layout(location = 0) in vec3 aPosition;

        uniform mat4 uModel;
        uniform mat4 uView;
        uniform mat4 uProjection;

        out vec3 vWorldPosition;

        void main()
        {
            vec4 world = uModel * vec4(aPosition, 1.0);
            vWorldPosition = world.xyz;
            gl_Position = uProjection * uView * world;
            gl_Position.z -= gl_Position.w * 0.0006;
        }
        """;

    public const string WireFragment = """
        in vec3 vWorldPosition;
        uniform vec3 uColor;
        uniform float uClipEnabled;
        uniform float uClipLowerZ;
        uniform float uClipUpperZ;
        out vec4 fragColor;
        void main()
        {
            if (uClipEnabled > 0.5 &&
                (vWorldPosition.z < uClipLowerZ || vWorldPosition.z > uClipUpperZ)) discard;
            fragColor = vec4(uColor, 1.0);
        }
        """;

    public const string LineVertex = """
        layout(location = 0) in vec3 aPosition;
        layout(location = 1) in vec4 aColor;

        uniform mat4 uViewProjection;

        out vec4 vColor;
        out vec3 vWorldPosition;

        void main()
        {
            vColor = aColor;
            vWorldPosition = aPosition;
            gl_Position = uViewProjection * vec4(aPosition, 1.0);
        }
        """;

    /// <summary>
    /// Expands a segment to a pixel width on screen: both ends go to clip space, the screen
    /// direction between them gives the perpendicular, and the vertex steps sideways by its
    /// signed half-width in pixels. Shares <see cref="LineFragment"/>.
    /// </summary>
    public const string WideLineVertex = """
        layout(location = 0) in vec3 aPosition;
        layout(location = 1) in vec3 aOther;
        layout(location = 2) in float aSide;
        layout(location = 3) in vec4 aColor;

        uniform mat4 uViewProjection;
        uniform vec2 uViewport; // width, height in pixels

        out vec4 vColor;
        out vec3 vWorldPosition;

        void main()
        {
            vColor = aColor;
            vWorldPosition = aPosition;
            vec4 self = uViewProjection * vec4(aPosition, 1.0);
            vec4 other = uViewProjection * vec4(aOther, 1.0);
            vec2 selfScreen = self.xy / max(self.w, 1e-5) * uViewport * 0.5;
            vec2 otherScreen = other.xy / max(other.w, 1e-5) * uViewport * 0.5;
            vec2 dir = otherScreen - selfScreen;
            float len = length(dir);
            vec2 normal = len > 1e-4 ? vec2(-dir.y, dir.x) / len : vec2(0.0, 1.0);
            vec2 offset = normal * aSide / (uViewport * 0.5) * self.w;
            gl_Position = self + vec4(offset, 0.0, 0.0);
        }
        """;

    public const string LineFragment = """
        in vec4 vColor;
        in vec3 vWorldPosition;
        uniform float uClipEnabled;
        uniform float uClipLowerZ;
        uniform float uClipUpperZ;
        out vec4 fragColor;
        void main()
        {
            if (uClipEnabled > 0.5 &&
                (vWorldPosition.z < uClipLowerZ || vWorldPosition.z > uClipUpperZ)) discard;
            vec4 color = vColor;
            if (uClipEnabled > 0.5)
            {
                float distanceToCut = min(abs(vWorldPosition.z - uClipLowerZ),
                                          abs(vWorldPosition.z - uClipUpperZ));
                float cutBand = max(fwidth(vWorldPosition.z) * 1.5, 0.002);
                float cut = 1.0 - smoothstep(0.0, cutBand, distanceToCut);
                color.rgb = mix(color.rgb, vec3(1.0, 0.55, 0.16), cut * 0.8);
            }
            fragColor = color;
        }
        """;
}
