using Danslicer.Core.Config;

namespace Danslicer.Tests;

public sealed class UserConfigTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "danslicer-config-tests", Guid.NewGuid().ToString("N"));
    private string PathFor(string name) => Path.Combine(_dir, name);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void RoundTripsAllSpaceMouseSettings()
    {
        var config = new UserConfig
        {
            SpaceMouse = new SpaceMouseConfig
            {
                OrbitSensitivity = 0.25f,
                PanSensitivity = 0.5f,
                ZoomSensitivity = 2f,
                InvertOrbitYaw = true,
                InvertOrbitPitch = true,
                InvertPanX = true,
                InvertPanY = true,
                InvertZoom = true,
                Deadzone = 0.02f,
            },
        };
        var path = PathFor("config.json");

        config.Save(path);
        var loaded = UserConfig.Load(path);

        Assert.Equal(0.25f, loaded.SpaceMouse.OrbitSensitivity);
        Assert.Equal(0.5f, loaded.SpaceMouse.PanSensitivity);
        Assert.Equal(2f, loaded.SpaceMouse.ZoomSensitivity);
        Assert.True(loaded.SpaceMouse.InvertOrbitYaw);
        Assert.True(loaded.SpaceMouse.InvertOrbitPitch);
        Assert.True(loaded.SpaceMouse.InvertPanX);
        Assert.True(loaded.SpaceMouse.InvertPanY);
        Assert.True(loaded.SpaceMouse.InvertZoom);
        Assert.Equal(0.02f, loaded.SpaceMouse.Deadzone);
    }

    [Fact]
    public void MissingFileYieldsDefaults()
    {
        var loaded = UserConfig.Load(PathFor("nowhere.json"));
        Assert.Equal(1f, loaded.SpaceMouse.OrbitSensitivity);
        Assert.False(loaded.SpaceMouse.InvertZoom);
    }

    [Fact]
    public void CorruptFileYieldsDefaults()
    {
        var path = PathFor("corrupt.json");
        Directory.CreateDirectory(_dir);
        File.WriteAllText(path, "{ not json at all");
        var loaded = UserConfig.Load(path);
        Assert.Equal(1f, loaded.SpaceMouse.PanSensitivity);
    }

    [Fact]
    public void UnknownPropertiesAndMissingSectionsAreTolerated()
    {
        var path = PathFor("partial.json");
        Directory.CreateDirectory(_dir);
        File.WriteAllText(path, """{ "FutureSection": { "x": 1 }, "SpaceMouse": { "ZoomSensitivity": 0.1 } }""");
        var loaded = UserConfig.Load(path);
        Assert.Equal(0.1f, loaded.SpaceMouse.ZoomSensitivity);
        Assert.Equal(1f, loaded.SpaceMouse.OrbitSensitivity); // untouched default
    }
}
