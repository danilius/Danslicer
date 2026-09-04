using Danslicer.Core.Config;
using Danslicer.Render;

namespace Danslicer.Tests;

public sealed class RenderPathConfigTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "danslicer-renderpath-tests", Guid.NewGuid().ToString("N"));
    private string PathFor(string name) => Path.Combine(_dir, name);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void DefaultRenderPathIsDeferred()
    {
        var viewport = new ViewportConfig();
        Assert.Equal(RenderPathMode.Deferred, viewport.RenderPath);
        Assert.Equal(ViewportShadingMode.Studio, viewport.Shading);
        Assert.True(viewport.CavityEnabled);
        Assert.True(viewport.OutlinesEnabled);
        Assert.True(viewport.FxaaEnabled);
    }

    [Fact]
    public void RenderPathSettingsRoundTrip()
    {
        var config = new UserConfig
        {
            Viewport = new ViewportConfig
            {
                RenderPath = RenderPathMode.Deferred,
                Shading = ViewportShadingMode.MatCapMetal,
                CavityEnabled = false,
                CavityRidgeStrength = 0.9f,
                CavityValleyStrength = 1.2f,
                CavityRadiusPixels = 3f,
                OutlinesEnabled = false,
                OutlineStrength = 0.4f,
                FxaaEnabled = false,
            },
        };
        var path = PathFor("renderpath.json");

        config.Save(path);
        var loaded = UserConfig.Load(path).Viewport;

        Assert.Equal(RenderPathMode.Deferred, loaded.RenderPath);
        Assert.Equal(ViewportShadingMode.MatCapMetal, loaded.Shading);
        Assert.False(loaded.CavityEnabled);
        Assert.Equal(0.9f, loaded.CavityRidgeStrength);
        Assert.Equal(1.2f, loaded.CavityValleyStrength);
        Assert.Equal(3f, loaded.CavityRadiusPixels);
        Assert.False(loaded.OutlinesEnabled);
        Assert.Equal(0.4f, loaded.OutlineStrength);
        Assert.False(loaded.FxaaEnabled);
    }

    [Fact]
    public void MissingRenderPathFieldsGetTheDeferredDefault()
    {
        var path = PathFor("legacy.json");
        Directory.CreateDirectory(_dir);
        File.WriteAllText(path, """{ "Viewport": { "OverhangAngleDegrees": 30 } }""");

        var loaded = UserConfig.Load(path).Viewport;

        Assert.Equal(RenderPathMode.Deferred, loaded.RenderPath);
        Assert.Equal(ViewportShadingMode.Studio, loaded.Shading);
        Assert.True(loaded.FxaaEnabled);
    }

    [Fact]
    public void OutOfRangeEffectValuesAreClampedOnLoad()
    {
        var path = PathFor("wild.json");
        Directory.CreateDirectory(_dir);
        File.WriteAllText(path, """
            { "Viewport": {
                "CavityRidgeStrength": 100,
                "CavityValleyStrength": -3,
                "CavityRadiusPixels": 0,
                "OutlineStrength": 7
            } }
            """);

        var loaded = UserConfig.Load(path).Viewport;

        Assert.Equal(4f, loaded.CavityRidgeStrength);
        Assert.Equal(0f, loaded.CavityValleyStrength);
        Assert.Equal(0.5f, loaded.CavityRadiusPixels);
        Assert.Equal(1f, loaded.OutlineStrength);
    }

    [Fact]
    public void DeferredEffectsSnapshotMapsAndClampsConfig()
    {
        var effects = DeferredEffects.FromConfig(new ViewportConfig
        {
            Shading = ViewportShadingMode.MatCapPearl,
            CavityEnabled = false,
            CavityRidgeStrength = float.NaN,
            CavityValleyStrength = 9f,
            CavityRadiusPixels = 100f,
            OutlinesEnabled = true,
            OutlineStrength = -2f,
            FxaaEnabled = false,
        });

        Assert.Equal(ViewportShadingMode.MatCapPearl, effects.Shading);
        Assert.False(effects.CavityEnabled);
        Assert.Equal(0.35f, effects.CavityRidgeStrength); // NaN falls back to the default
        Assert.Equal(4f, effects.CavityValleyStrength);
        Assert.Equal(8f, effects.CavityRadiusPixels);
        Assert.True(effects.OutlinesEnabled);
        Assert.Equal(0f, effects.OutlineStrength);
        Assert.False(effects.FxaaEnabled);
    }

    [Fact]
    public void DefaultEffectsMatchDefaultConfig()
    {
        Assert.Equal(DeferredEffects.FromConfig(new ViewportConfig()), DeferredEffects.Default);
    }
}
