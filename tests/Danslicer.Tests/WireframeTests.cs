using Danslicer.Core.Config;
using Danslicer.Render;

namespace Danslicer.Tests;

public sealed class WireframeTests
{
    [Fact]
    public void EdgeIndicesEmitThreeLinesPerTriangleOverThePerCornerLayout()
    {
        var indices = GpuMesh.EdgeIndices(2);

        Assert.Equal(12, indices.Length);
        // Triangle 0 occupies vertices 0..2, triangle 1 vertices 3..5.
        Assert.Equal(new uint[] { 0, 1, 1, 2, 2, 0, 3, 4, 4, 5, 5, 3 }, indices);
    }

    [Fact]
    public void WireframeAndViewCubeDefaultsAndRoundTrip()
    {
        var viewport = new ViewportConfig();
        Assert.False(viewport.WireframeEnabled);
        Assert.True(viewport.ViewCubeEnabled);

        var dir = Path.Combine(Path.GetTempPath(), "danslicer-wireframe-tests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(dir, "config.json");
        try
        {
            var config = new UserConfig
            {
                Viewport = new ViewportConfig { WireframeEnabled = true, ViewCubeEnabled = false },
            };
            config.Save(path);
            var loaded = UserConfig.Load(path).Viewport;
            Assert.True(loaded.WireframeEnabled);
            Assert.False(loaded.ViewCubeEnabled);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
        }
    }
}
