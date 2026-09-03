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

        out vec3 vViewNormal;
        out vec3 vWorldNormal;
        out vec3 vWorldPosition;
        out vec3 vViewPosition;

        void main()
        {
            vec4 world = uModel * vec4(aPosition, 1.0);
            vec4 view = uView * world;
            vWorldPosition = world.xyz;
            vViewPosition = view.xyz;
            vViewNormal = normalize(mat3(uNormalMatrix) * aNormal);
            vWorldNormal = normalize(mat3(uModelNormalMatrix) * aNormal);
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

        uniform vec3 uColor;
        uniform float uBackfaceTint;   // 1 = tint back faces to reveal inverted normals
        uniform float uOpacity;
        uniform float uWarnBelowPlate; // 1 = tint geometry below Z = 0
        uniform float uOverhangCos;    // cos of the overhang angle from straight down; 2 disables

        out vec4 fragColor;

        float wrap(float ndotl) { float d = ndotl * 0.5 + 0.5; return d * d; }

        void main()
        {
            vec3 n = normalize(vViewNormal);
            bool back = !gl_FrontFacing;
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
            float spec = pow(max(dot(n, h), 0.0), 48.0) * 0.18;

            // Fresnel-style edge lift separates silhouettes from what lies behind them.
            float edge = pow(1.0 - max(dot(n, v), 0.0), 3.0) * 0.12;

            vec3 color = uColor;
            if (back) color = mix(color, vec3(0.85, 0.30, 0.55), uBackfaceTint * 0.6);

            // Overhang tint: surfaces facing downward within the threshold of straight down.
            // Severity runs yellow at the threshold to red on flat undersides, with a soft edge.
            // A 2 mm world-space checker alternates tint strength so overhangs stay readable on
            // any base colour, selection orange included.
            if (uOverhangCos < 1.5)
            {
                float down = dot(normalize(vWorldNormal), vec3(0.0, 0.0, -1.0));
                float over = smoothstep(uOverhangCos - 0.06, uOverhangCos + 0.02, down);
                if (over > 0.0)
                {
                    float severity = clamp((down - uOverhangCos) / max(1.0 - uOverhangCos, 1e-3), 0.0, 1.0);
                    vec3 warn = mix(vec3(0.98, 0.80, 0.15), vec3(0.90, 0.12, 0.10), severity);
                    vec3 cells = floor(vWorldPosition / 2.0);
                    float checker = mod(cells.x + cells.y + cells.z, 2.0);
                    color = mix(color, warn, over * mix(0.40, 0.90, checker));
                }
            }

            if (uWarnBelowPlate > 0.5 && vWorldPosition.z < -0.001) color = mix(color, vec3(0.95, 0.15, 0.10), 0.6);

            vec3 lit = color * (diffuse + 0.08) + vec3(spec) + vec3(edge);
            fragColor = vec4(lit, uOpacity);
        }
        """;

    public const string LineVertex = """
        layout(location = 0) in vec3 aPosition;
        layout(location = 1) in vec4 aColor;

        uniform mat4 uViewProjection;

        out vec4 vColor;

        void main()
        {
            vColor = aColor;
            gl_Position = uViewProjection * vec4(aPosition, 1.0);
        }
        """;

    public const string LineFragment = """
        in vec4 vColor;
        out vec4 fragColor;
        void main() { fragColor = vColor; }
        """;
}
