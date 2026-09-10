using Danslicer.Core.Config;

namespace Danslicer.Render;

/// <summary>
/// Per-frame effect settings for the deferred path, snapshotted from configuration so the renderer
/// never reads mutable config mid-frame. <see cref="FromConfig"/> clamps defensively: the renderer
/// must stay sane even if a caller skips config normalization.
/// </summary>
public sealed record DeferredEffects
{
    public ViewportShadingMode Shading { get; init; } = ViewportShadingMode.Studio;
    public bool AmbientOcclusionEnabled { get; init; } = true;
    public float AmbientOcclusionStrength { get; init; } = 0.35f;
    public float AmbientOcclusionRadiusMm { get; init; } = 2f;
    public bool CavityEnabled { get; init; } = true;
    public float CavityRidgeStrength { get; init; } = 0.35f;
    public float CavityValleyStrength { get; init; } = 0.7f;
    public float CavityRadiusPixels { get; init; } = 1.5f;
    public bool OutlinesEnabled { get; init; } = true;
    public float OutlineStrength { get; init; } = 0.75f;
    public bool FxaaEnabled { get; init; } = true;

    public static DeferredEffects Default { get; } = new();

    public static DeferredEffects FromConfig(ViewportConfig config) => new()
    {
        Shading = Enum.IsDefined(config.Shading) ? config.Shading : ViewportShadingMode.Studio,
        CavityEnabled = config.CavityEnabled,
        AmbientOcclusionEnabled = config.AmbientOcclusionEnabled,
        AmbientOcclusionStrength = Clamp(config.AmbientOcclusionStrength, 0f, 0.6f, 0.35f),
        AmbientOcclusionRadiusMm = Clamp(config.AmbientOcclusionRadiusMm, 0.1f, 10f, 2f),
        CavityRidgeStrength = Clamp(config.CavityRidgeStrength, 0f, 4f, 0.35f),
        CavityValleyStrength = Clamp(config.CavityValleyStrength, 0f, 4f, 0.7f),
        CavityRadiusPixels = Clamp(config.CavityRadiusPixels, 0.5f, 8f, 1.5f),
        OutlinesEnabled = config.OutlinesEnabled,
        OutlineStrength = Clamp(config.OutlineStrength, 0f, 1f, 0.75f),
        FxaaEnabled = config.FxaaEnabled,
    };

    private static float Clamp(float value, float min, float max, float fallback) =>
        float.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;
}
