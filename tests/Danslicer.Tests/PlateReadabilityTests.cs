using System.Numerics;
using Danslicer.Core.Config;
using Danslicer.Render;

namespace Danslicer.Tests;

public sealed class PlateReadabilityTests
{
    [Fact]
    public void SlabIsClosedOutwardAndStaysBelowPrintablePlane()
    {
        var mesh = GpuMesh.CreatePlate(192, 120);
        Assert.Equal(new Vector3(-96, -60, -2), mesh.Bounds.Min);
        Assert.Equal(new Vector3(96, 60, 0), mesh.Bounds.Max);
        var edges = new Dictionary<(int, int), int>();
        for (var t = 0; t < mesh.TriangleCount; t++)
        {
            mesh.GetTriangle(t, out var a, out var b, out var c);
            Assert.True(Vector3.Dot(mesh.FaceNormals[t], (a + b + c) / 3 - new Vector3(0, 0, -1)) > 0);
            for (var i = 0; i < 3; i++)
            {
                int u = mesh.Indices[t * 3 + i], v = mesh.Indices[t * 3 + (i + 1) % 3];
                edges[(u, v)] = edges.GetValueOrDefault((u, v)) + 1;
            }
        }
        foreach (var (edge, count) in edges)
        {
            Assert.Equal(1, count);
            Assert.Equal(1, edges.GetValueOrDefault((edge.Item2, edge.Item1)));
        }
    }

    [Fact]
    public void SurfaceAndShadowTransitionsAreContinuousAndBelowStaysAbsentOnZoom()
    {
        var camera = new Camera();
        float prior = 0, shadow = 0;
        for (float pitch = -30; pitch <= 40; pitch += 0.1f)
        {
            camera.SetView(-60, pitch);
            var opacity = PlateFade.SurfaceOpacityFor(camera);
            var nextShadow = PlateFade.ShadowStrengthFor(camera);
            Assert.InRange(opacity, prior - 0.00001f, prior + 0.02f);
            Assert.InRange(nextShadow, shadow - 0.00001f, shadow + 0.02f);
            prior = opacity; shadow = nextShadow;
        }
        Assert.Equal(1, prior); Assert.Equal(1, shadow);
        camera.SetView(-60, -10);
        foreach (var distance in new[] { 5f, 50f, 400f })
        {
            camera.Distance = distance;
            Assert.Equal(0, PlateFade.SurfaceOpacityFor(camera));
        }
        camera.SetView(-60, 30); camera.Distance = 10;
        prior = 0;
        for (float eyeZ = -0.1f; eyeZ <= 2.1f; eyeZ += 0.01f)
        {
            camera.Target = new(0, 0, eyeZ - 5);
            var opacity = PlateFade.SurfaceOpacityFor(camera);
            Assert.InRange(opacity, prior - 0.00001f, prior + 0.01f);
            prior = opacity;
        }
    }

    [Fact]
    public void ReadabilitySettingsRoundTripAndClampWithoutChangingOtherSettings()
    {
        var directory = Path.Combine(Path.GetTempPath(), "Danslicer-readability-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "config.json");
            var config = new UserConfig();
            config.Viewport.AmbientOcclusionEnabled = false;
            config.Viewport.AmbientOcclusionStrength = 9;
            config.Viewport.AmbientOcclusionRadiusMm = -1;
            config.Viewport.PlateReflectionsEnabled = false;
            config.Viewport.PlateReflectionStrength = 9;
            config.Viewport.CapInterior = false;
            config.Save(path);
            var loaded = UserConfig.Load(path).Viewport;
            Assert.False(loaded.AmbientOcclusionEnabled);
            Assert.False(loaded.PlateReflectionsEnabled);
            Assert.False(loaded.CapInterior);
            Assert.Equal(0.6f, loaded.AmbientOcclusionStrength);
            Assert.Equal(0.1f, loaded.AmbientOcclusionRadiusMm);
            Assert.Equal(0.3f, loaded.PlateReflectionStrength);
            var snapshot = DeferredEffects.FromConfig(new ViewportConfig { AmbientOcclusionStrength = float.NaN, AmbientOcclusionRadiusMm = float.PositiveInfinity });
            Assert.Equal(0.35f, snapshot.AmbientOcclusionStrength);
            Assert.Equal(2f, snapshot.AmbientOcclusionRadiusMm);
        }
        finally { Directory.Delete(directory, true); }
    }
}
