namespace Danslicer.Render;

/// <summary>
/// GLSL for the deferred path. The geometry fragment keeps the classic shader's tinting
/// (backface, overhang checker, build-volume warning) byte-for-byte so the composited image matches
/// the classic look wherever the new effects are switched off; the studio lighting constants in the
/// composite pass are the classic ones for the same reason.
/// </summary>
internal static class DeferredShaders
{
    /// <summary>Writes albedo + normal + ID; shares the classic mesh vertex shader.</summary>
    public const string GBufferFragment = """
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
        uniform float uWarnOutsideBuildVolume; // 1 = tint geometry outside printable XYZ
        uniform vec3 uBuildVolume;             // centred X/Y extents, Z travel from zero
        uniform float uOverhangCos;    // cos of the overhang angle from straight down; 2 disables
        uniform vec3 uOverhangColorA;  // checker colour on even cells
        uniform vec3 uOverhangColorB;  // checker colour on odd cells
        uniform float uOverhangCell;   // checker cell edge, mm
        uniform vec3 uId;              // 24-bit draw id packed into RGB
        uniform float uSelected;       // 1 = selection outline colour applies at this draw's edges
        uniform float uClipEnabled;
        uniform float uClipLowerZ;
        uniform float uClipUpperZ;
        uniform float uShadow;         // 1 = this draw is a plate shadow (see the vertex stage)

        layout(location = 0) out vec4 gAlbedo; // rgb base colour, a = 1 marks lit geometry
        layout(location = 1) out vec4 gNormal; // view-space normal * 0.5 + 0.5
        layout(location = 2) out vec4 gId;     // rgb id, a = selected flag

        void main()
        {
            if (uClipEnabled > 0.5 &&
                (vWorldPosition.z < uClipLowerZ || vWorldPosition.z > uClipUpperZ)) discard;

            vec3 n = normalize(vViewNormal);
            // A shadow is one flat surface whatever the winding of the triangles that made it.
            bool back = !gl_FrontFacing && uShadow < 0.5;
            if (back) n = -n;

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

            // Clip cut-edge highlight, identical to the classic shader: baked into albedo so the
            // composite lights it like any other surface colour.
            if (uClipEnabled > 0.5)
            {
                float distanceToCut = min(abs(vWorldPosition.z - uClipLowerZ),
                                          abs(vWorldPosition.z - uClipUpperZ));
                float cutBand = max(fwidth(vWorldPosition.z) * 1.5, 0.002);
                float cut = 1.0 - smoothstep(0.0, cutBand, distanceToCut);
                color = mix(color, vec3(1.0, 0.55, 0.16), cut * 0.8);
            }

            gAlbedo = vec4(color, 1.0);
            gNormal = vec4(n * 0.5 + 0.5, uPlateMaterial > 0.5 ? 0.0 : 1.0);
            gId = vec4(uId, uSelected);
        }
        """;

    public const string FullscreenVertex = """
        layout(location = 0) in vec2 aPosition;

        out vec2 vUv;

        void main()
        {
            vUv = aPosition * 0.5 + 0.5;
            gl_Position = vec4(aPosition, 0.0, 1.0);
        }
        """;

    /// <summary>
    /// Lighting, cavity and outlines from the G-buffer. Background pixels (albedo alpha 0) pass
    /// through untouched, so the clear colour survives compositing exactly as in the classic path.
    /// </summary>
    public const string CompositeFragment = ShadowShaders.Sampling + """
        in vec2 vUv;

        // highp: the ES default for sampler2D is lowp, far too coarse for depth reconstruction.
        uniform highp sampler2D uAlbedo;
        uniform highp sampler2D uNormalTex;
        uniform highp sampler2D uIdTex;
        uniform highp sampler2D uDepthTex;
        uniform highp sampler2D uMatCap;
        uniform mat4 uInvProjection;
        uniform mat4 uInvView;         // world-Z reconstruction for the waterline contour
        uniform vec2 uTexel;           // 1 / render target size
        uniform float uWaterlineEnabled;
        uniform float uWaterlineZ;
        uniform int uShadingMode;      // 0 = studio lighting, 1 = MatCap lookup
        uniform float uPlateEffectVisibility;
        uniform float uAoStrength;
        uniform float uAoRadiusMm;
        uniform float uCavityRidge;    // 0 disables ridges
        uniform float uCavityValley;   // 0 disables valleys
        uniform float uCavityRadius;   // sample offset, pixels
        uniform float uOutlineStrength; // 0 disables outlines
        uniform vec3 uOutlineColor;
        uniform vec3 uSelectColor;

        out vec4 fragColor;

        float wrap(float ndotl) { float d = ndotl * 0.5 + 0.5; return d * d; }

        // Depth-buffer value to view space; works for perspective and orthographic alike.
        vec3 viewPos(vec2 uv, float depth)
        {
            vec4 clip = vec4(uv * 2.0 - 1.0, depth * 2.0 - 1.0, 1.0);
            vec4 v = uInvProjection * clip;
            return v.xyz / v.w;
        }

        vec3 sampleNormal(vec2 uv) { return texture(uNormalTex, uv).xyz * 2.0 - 1.0; }

        void main()
        {
            vec4 albedo = texture(uAlbedo, vUv);
            float depth = texture(uDepthTex, vUv).r;
            vec3 color = albedo.rgb;

            if (albedo.a > 0.5)
            {
                vec3 n = normalize(sampleNormal(vUv));
                vec3 p = viewPos(vUv, depth);

                bool plateMaterial = texture(uNormalTex, vUv).a < 0.5;
                float plateEffect = plateMaterial ? uPlateEffectVisibility : 1.0;
                if (uShadingMode == 0 || plateMaterial)
                {
                    // The classic studio rig, reproduced from the G-buffer.
                    vec3 v = normalize(-p);
                    vec3 key  = normalize(vec3( 0.45,  0.55,  0.70));
                    vec3 fill = normalize(vec3(-0.70,  0.10,  0.45));
                    vec3 rim  = normalize(vec3( 0.20, -0.60, -0.75));

                    float diffuse =
                        wrap(dot(n, key))  * 0.75 +
                        wrap(dot(n, fill)) * 0.30 +
                        max(dot(n, rim), 0.0) * 0.20;

                    vec3 h = normalize(key + v);
                    bool plate = plateMaterial;
                    float spec = pow(max(dot(n, h), 0.0), plate ? 10.0 : 48.0) * (plate ? 0.06 : 0.18);
                    float edgeLift = pow(1.0 - max(dot(n, v), 0.0), 3.0) * 0.12;

                    color = color * (diffuse + 0.08) + vec3(spec) + vec3(edgeLift);
                }
                else
                {
                    // MatCaps are authored around mid-grey; the 2x restores full range.
                    color = color * texture(uMatCap, n.xy * 0.5 + 0.5).rgb * 2.0;
                }

                if (!plateMaterial)
                    color *= modelShadow((uInvView * vec4(p, 1.0)).xyz, normalize(mat3(uInvView) * n));

                if (uAoStrength > 0.0)
                {
                    // View-space radius is in millimetres, stable under zoom and either projection.
                    float pixelMm = max(length(viewPos(vUv + vec2(uTexel.x, 0.0), depth) - p), 0.0001);
                    float radiusPx = clamp(uAoRadiusMm / pixelMm, 1.0, 96.0);
                    float occlusion = 0.0;
                    for (int i = 0; i < 16; i++)
                    {
                        float angle = float(i) * 2.39996323;
                        float radius = sqrt((float(i) + 0.5) / 16.0);
                        vec2 uv = vUv + vec2(cos(angle), sin(angle)) * radius * radiusPx * uTexel;
                        if (any(lessThan(uv, vec2(0.0))) || any(greaterThan(uv, vec2(1.0)))) continue;
                        float sd = texture(uDepthTex, uv).r;
                        if (sd >= 0.999999) continue;
                        vec3 delta = viewPos(uv, sd) - p;
                        float distanceMm = length(delta);
                        if (distanceMm < 0.01 || distanceMm > uAoRadiusMm) continue;
                        // Reject coplanar neighbours; no ID rejection, so neighbouring supports
                        // can shade a model/contact. Background and distant layers never occlude.
                        float horizon = max(dot(n, delta / distanceMm) - 0.08, 0.0);
                        float falloff = 1.0 - smoothstep(uAoRadiusMm * 0.25, uAoRadiusMm, distanceMm);
                        occlusion += horizon * falloff;
                    }
                    color *= 1.0 - min(occlusion * (3.0 / 16.0), 1.0) * uAoStrength * plateEffect;
                }

                if (uCavityRidge + uCavityValley > 0.0)
                {
                    vec2 dx = vec2(uTexel.x, 0.0) * uCavityRadius;
                    vec2 dy = vec2(0.0, uTexel.y) * uCavityRadius;
                    // Neighbours across a large depth gap belong to other surfaces; their normals
                    // are pulled back to the centre so silhouettes get no cavity halo.
                    float limit = max(0.5, abs(p.z) * 0.02);
                    vec3 nn[4];
                    vec2 offs[4];
                    offs[0] = dx; offs[1] = -dx; offs[2] = dy; offs[3] = -dy;
                    for (int i = 0; i < 4; i++)
                    {
                        vec2 uv = vUv + offs[i];
                        float w = step(abs(viewPos(uv, texture(uDepthTex, uv).r).z - p.z), limit);
                        vec3 sn = sampleNormal(uv);
                        // Background texels decode to the zero vector; normalizing one is NaN.
                        nn[i] = (w > 0.5 && dot(sn, sn) > 1e-4) ? normalize(sn) : n;
                    }
                    float curvature = (nn[0].x - nn[1].x) + (nn[2].y - nn[3].y);
                    float ridge = clamp(curvature, 0.0, 1.0) * uCavityRidge;
                    float valley = clamp(-curvature, 0.0, 1.0) * uCavityValley;
                    color *= clamp(1.0 + (ridge - valley) * plateEffect, 0.0, 2.0);
                }
            }

            if (uOutlineStrength > 0.0)
            {
                vec4 idC = texture(uIdTex, vUv);
                float zC = viewPos(vUv, depth).z;
                float edge = 0.0;
                float selected = 0.0;
                vec2 offs[4];
                offs[0] = vec2(uTexel.x, 0.0); offs[1] = -offs[0];
                offs[2] = vec2(0.0, uTexel.y); offs[3] = -offs[2];
                for (int i = 0; i < 4; i++)
                {
                    vec2 uv = vUv + offs[i];
                    vec4 idN = texture(uIdTex, uv);
                    float zN = viewPos(uv, texture(uDepthTex, uv).r).z;
                    // The line lands on the nearer surface only, keeping outlines one pixel wide.
                    bool inFront = zC >= zN - 0.001;
                    bool idEdge = distance(idN.rgb, idC.rgb) > 0.001;
                    bool depthEdge = (zC - zN) > max(1.0, abs(zC) * 0.04);
                    if (inFront && (idEdge || depthEdge))
                    {
                        edge = 1.0;
                        selected = max(selected, max(idC.a, idN.a));
                    }
                }
                if (edge > 0.0 && albedo.a > 0.5 && texture(uNormalTex, vUv).a > 0.5)
                    color = mix(color, selected > 0.5 ? uSelectColor : uOutlineColor, uOutlineStrength);
            }

            // Hover waterline, last so it stays visible over every other effect, as in the classic
            // shader. World Z is reconstructed from depth; the derivative-sized band keeps the
            // contour approximately constant in screen pixels (classic constants).
            if (uWaterlineEnabled > 0.5 && albedo.a > 0.5)
            {
                float worldZ = (uInvView * vec4(viewPos(vUv, depth), 1.0)).z;
                float band = clamp(fwidth(worldZ) * 1.75, 0.008, 0.25);
                float contour = 1.0 - smoothstep(band * 0.35, band, abs(worldZ - uWaterlineZ));
                color = mix(color, vec3(0.04, 0.96, 0.92), contour * 0.96);
            }

            fragColor = vec4(color, 1.0);
        }
        """;

    /// <summary>Compact FXAA (the widely used 5-tap console variant); needs bilinear input.</summary>
    public const string FxaaFragment = """
        in vec2 vUv;

        uniform highp sampler2D uScene;
        uniform vec2 uTexel;

        out vec4 fragColor;

        void main()
        {
            const float spanMax = 8.0;
            const float reduceMul = 1.0 / 8.0;
            const float reduceMin = 1.0 / 128.0;

            vec3 rgbNW = texture(uScene, vUv + vec2(-1.0, -1.0) * uTexel).rgb;
            vec3 rgbNE = texture(uScene, vUv + vec2( 1.0, -1.0) * uTexel).rgb;
            vec3 rgbSW = texture(uScene, vUv + vec2(-1.0,  1.0) * uTexel).rgb;
            vec3 rgbSE = texture(uScene, vUv + vec2( 1.0,  1.0) * uTexel).rgb;
            vec3 rgbM  = texture(uScene, vUv).rgb;

            vec3 luma = vec3(0.299, 0.587, 0.114);
            float lumaNW = dot(rgbNW, luma);
            float lumaNE = dot(rgbNE, luma);
            float lumaSW = dot(rgbSW, luma);
            float lumaSE = dot(rgbSE, luma);
            float lumaM  = dot(rgbM,  luma);

            float lumaMin = min(lumaM, min(min(lumaNW, lumaNE), min(lumaSW, lumaSE)));
            float lumaMax = max(lumaM, max(max(lumaNW, lumaNE), max(lumaSW, lumaSE)));

            vec2 dir = vec2(
                -((lumaNW + lumaNE) - (lumaSW + lumaSE)),
                 ((lumaNW + lumaSW) - (lumaNE + lumaSE)));

            float dirReduce = max((lumaNW + lumaNE + lumaSW + lumaSE) * 0.25 * reduceMul, reduceMin);
            float rcpDirMin = 1.0 / (min(abs(dir.x), abs(dir.y)) + dirReduce);
            dir = clamp(dir * rcpDirMin, vec2(-spanMax), vec2(spanMax)) * uTexel;

            vec3 rgbA = 0.5 * (
                texture(uScene, vUv + dir * (1.0 / 3.0 - 0.5)).rgb +
                texture(uScene, vUv + dir * (2.0 / 3.0 - 0.5)).rgb);
            vec3 rgbB = rgbA * 0.5 + 0.25 * (
                texture(uScene, vUv + dir * -0.5).rgb +
                texture(uScene, vUv + dir *  0.5).rgb);

            float lumaB = dot(rgbB, luma);
            fragColor = vec4(lumaB < lumaMin || lumaB > lumaMax ? rgbA : rgbB, 1.0);
        }
        """;

    /// <summary>Straight copy for the FXAA-off path (blit would demand matching host formats).</summary>
    public const string PassthroughFragment = """
        in vec2 vUv;

        uniform highp sampler2D uScene;

        out vec4 fragColor;

        void main() { fragColor = vec4(texture(uScene, vUv).rgb, 1.0); }
        """;
}
