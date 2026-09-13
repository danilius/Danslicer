using System.Numerics;
using Danslicer.Core.Config;
using Danslicer.Core.Geometry;
using Danslicer.Render;

namespace Danslicer.Tests;

public sealed class ShadowEffectsTests
{
    [Fact]
    public void LegacyConfigDefaultsToSubtleShadowsAndInvalidValuesRemainBounded()
    {
        var defaults = ShadowEffects.FromConfig(new ViewportConfig());
        Assert.Equal(ModelShadowMode.Working, defaults.Mode);
        Assert.Equal(0.22f, defaults.Strength);
        Assert.Equal(0.6f, defaults.SoftnessMm);
        var invalid = ShadowEffects.FromConfig(new ViewportConfig { ModelShadows = (ModelShadowMode)99, WorkingShadowStrength = float.NaN, WorkingShadowSoftnessMm = float.PositiveInfinity });
        Assert.Equal(defaults, invalid);
        var strong = ShadowEffects.FromConfig(new ViewportConfig { ModelShadows = ModelShadowMode.Presentation, PresentationShadowStrength = 9, PresentationShadowSoftnessMm = -1 });
        Assert.Equal(0.7f, strong.Strength);
        Assert.Equal(0, strong.SoftnessMm);
    }

    [Theory]
    [InlineData(ModelShadowMode.Off)]
    [InlineData(ModelShadowMode.Working)]
    [InlineData(ModelShadowMode.Presentation)]
    public void BothPresetsRoundTripIndependentlyOfAoAndCap(ModelShadowMode mode)
    {
        var path = Path.Combine(Path.GetTempPath(), "Danslicer-shadow-" + Guid.NewGuid() + ".json");
        try
        {
            var config = new UserConfig();
            config.Viewport.ModelShadows = mode;
            config.Viewport.WorkingShadowStrength = 0.3f;
            config.Viewport.WorkingShadowSoftnessMm = 0.8f;
            config.Viewport.PresentationShadowStrength = 0.6f;
            config.Viewport.PresentationShadowSoftnessMm = 2f;
            config.Viewport.AmbientOcclusionEnabled = false;
            config.Viewport.CapInterior = false;
            config.Save(path);
            var loaded = UserConfig.Load(path).Viewport;
            Assert.Equal(mode, loaded.ModelShadows);
            Assert.Equal(0.3f, loaded.WorkingShadowStrength);
            Assert.Equal(0.8f, loaded.WorkingShadowSoftnessMm);
            Assert.Equal(0.6f, loaded.PresentationShadowStrength);
            Assert.Equal(2f, loaded.PresentationShadowSoftnessMm);
            Assert.False(loaded.AmbientOcclusionEnabled);
            Assert.False(loaded.CapInterior);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData(-90)]
    [InlineData(-35)]
    [InlineData(28)]
    [InlineData(90)]
    public void LightProjectionContainsAllSceneCornersWhenOrbiting(float pitch)
    {
        var camera = new Camera(); camera.SetView(-65, pitch);
        var bounds = new Aabb(new(-20, -30, 12), new(50, 10, 85));
        var fit = ShadowProjection.Fit(bounds, camera);
        Assert.InRange(fit.WorldUnitsPerTexel, 0.001f, 1f);
        foreach (var x in new[] { bounds.Min.X, bounds.Max.X })
        foreach (var y in new[] { bounds.Min.Y, bounds.Max.Y })
        foreach (var z in new[] { bounds.Min.Z, bounds.Max.Z })
        {
            var p = Vector3.Transform(new(x, y, z), fit.Matrix);
            Assert.InRange(p.X, -1, 1); Assert.InRange(p.Y, -1, 1); Assert.InRange(p.Z, 0, 1);
        }
        var viewLight = Vector3.Normalize(Vector3.TransformNormal(fit.LightDirection, camera.View));
        Assert.True(Vector3.Distance(viewLight, Vector3.Normalize(new(0.45f, 0.55f, 0.7f))) < 0.0001f);
    }
}
