namespace Danslicer.Render;

internal static class ShadowShaders
{
    public const string Vertex = """
        layout(location = 0) in vec3 aPosition;
        uniform mat4 uModel;
        uniform mat4 uLightMatrix;
        out vec3 vWorld;
        void main() {
            vec4 world = uModel * vec4(aPosition, 1.0);
            vWorld = world.xyz;
            gl_Position = uLightMatrix * world;
        }
        """;
    public const string Fragment = """
        in vec3 vWorld;
        uniform float uClipEnabled;
        uniform float uClipLowerZ;
        uniform float uClipUpperZ;
        void main() {
            if (uClipEnabled > 0.5 && (vWorld.z < uClipLowerZ || vWorld.z > uClipUpperZ)) discard;
        }
        """;

    // Shared verbatim by forward and deferred receivers. No temporal noise or history trails.
    public const string Sampling = """
        uniform highp sampler2D uModelShadow;
        uniform mat4 uLightMatrix;
        uniform vec3 uLightDirection;
        uniform float uModelShadowStrength;
        uniform float uShadowSoftnessPixels;
        uniform float uShadowBiasMm;
        float modelShadow(vec3 world, vec3 normal) {
            if (uModelShadowStrength <= 0.0) return 1.0;
            vec4 projected = uLightMatrix * vec4(world + normal * uShadowBiasMm, 1.0);
            vec3 p = projected.xyz / projected.w * 0.5 + 0.5;
            // Compare at the receiver plane's depth at each tap, avoiding false self-shadow
            // on sloped flat faces when the soft filter spans many shadow-map texels.
            vec3 planeNormal = transpose(inverse(mat3(uLightMatrix))) * normal;
            vec2 depthGradient = abs(planeNormal.z) > 0.00001
                ? -planeNormal.xy / planeNormal.z : vec2(0.0);
            float facing = smoothstep(-0.05, 0.25, dot(normal, uLightDirection));
            if (facing <= 0.0) return 1.0;
            if (any(lessThan(p, vec3(0.0))) || any(greaterThan(p, vec3(1.0)))) return 1.0;
            float shade = 0.0;
            // A fixed sunflower disk avoids the visible parallel bands of a square PCF grid.
            for (int i = 0; i < 48; i++) {
                float angle = float(i) * 2.39996323;
                vec2 offset = sqrt((float(i) + 0.5) / 48.0) * vec2(cos(angle), sin(angle));
                vec2 uv = p.xy + offset * uShadowSoftnessPixels / 2048.0;
                if (any(lessThan(uv, vec2(0.0))) || any(greaterThan(uv, vec2(1.0)))) continue;
                uv = (floor(uv * 2048.0) + 0.5) / 2048.0;
                float blocker = texture(uModelShadow, uv).r;
                shade += smoothstep(0.00003, 0.00008, p.z + dot(depthGradient, uv - p.xy) - blocker);
            }
            return 1.0 - (shade / 48.0) * uModelShadowStrength * facing;
        }
        """;
}
