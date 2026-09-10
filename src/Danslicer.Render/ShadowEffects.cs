using System.Numerics;
using Danslicer.Core.Config;
using Danslicer.Core.Geometry;

namespace Danslicer.Render;

/// <summary>Independent of AO/cavity. Strength bounds retain readable illumination in full shadow.</summary>
public sealed record ShadowEffects
{
    public ModelShadowMode Mode { get; init; } = ModelShadowMode.Working;
    public float Strength { get; init; } = 0.22f;
    public float SoftnessMm { get; init; } = 0.6f;
    public static ShadowEffects FromConfig(ViewportConfig config)
    {
        var mode = Enum.IsDefined(config.ModelShadows) ? config.ModelShadows : ModelShadowMode.Working;
        var presentation = mode == ModelShadowMode.Presentation;
        return new()
        {
            Mode = mode,
            Strength = Clamp(presentation ? config.PresentationShadowStrength : config.WorkingShadowStrength, 0, 0.7f, presentation ? 0.5f : 0.22f),
            SoftnessMm = Clamp(presentation ? config.PresentationShadowSoftnessMm : config.WorkingShadowSoftnessMm, 0, 4, presentation ? 1.2f : 0.6f),
        };
    }
    private static float Clamp(float value, float min, float max, float fallback) =>
        float.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;
}

/// <summary>Fit only scene geometry, never the printer volume; this preserves contact detail.</summary>
public readonly record struct ShadowProjection(Matrix4x4 Matrix, Vector3 LightDirection, float WorldUnitsPerTexel)
{
    public const int Resolution = 2048;
    public static ShadowProjection Fit(Aabb bounds, Camera camera)
    {
        Matrix4x4.Invert(camera.View, out var inverseView);
        var light = Vector3.Normalize(Vector3.TransformNormal(Vector3.Normalize(new Vector3(0.45f, 0.55f, 0.7f)), inverseView));
        var radius = MathF.Max(bounds.Radius, 2f) * 1.08f;
        var centre = bounds.Center;
        var up = MathF.Abs(Vector3.Dot(light, Vector3.UnitZ)) > 0.95f ? Vector3.UnitY : Vector3.UnitZ;
        var view = Matrix4x4.CreateLookAt(centre + light * radius * 3, centre, up);
        var projection = Matrix4x4.CreateOrthographic(radius * 2, radius * 2, radius, radius * 5);
        return new(view * projection, light, radius * 2 / Resolution);
    }
}
