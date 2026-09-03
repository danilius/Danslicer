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
            Viewport = new ViewportConfig
            {
                OverhangAngleDegrees = 30f,
                PlateOpacityFromBelow = 0.6f,
                OverhangColorA = "#112233",
                OverhangColorB = "#445566",
                OverhangCheckerSizeMm = 5f,
            },
            Placement = new PlacementConfig
            {
                Mode = PlacementMode.RaiseAbovePlate,
                HeightMm = 8.5f,
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
        Assert.Equal(30f, loaded.Viewport.OverhangAngleDegrees);
        Assert.Equal(0.6f, loaded.Viewport.PlateOpacityFromBelow);
        Assert.Equal("#112233", loaded.Viewport.OverhangColorA);
        Assert.Equal("#445566", loaded.Viewport.OverhangColorB);
        Assert.Equal(5f, loaded.Viewport.OverhangCheckerSizeMm);
        Assert.Equal(PlacementMode.RaiseAbovePlate, loaded.Placement.Mode);
        Assert.Equal(8.5f, loaded.Placement.HeightMm);
    }

    [Fact]
    public void WindowPlacementsRoundTrip()
    {
        var config = new UserConfig();
        config.Windows["main"] = new WindowStateConfig
        {
            X = -8, Y = 120, Width = 1400.5, Height = 900, Maximized = true,
            LeftPanelWidth = 245, RightPanelWidth = 365,
        };
        var path = PathFor("windows.json");

        config.Save(path);
        var loaded = UserConfig.Load(path);

        var main = loaded.Windows["main"];
        Assert.Equal(-8, main.X);
        Assert.Equal(120, main.Y);
        Assert.Equal(1400.5, main.Width);
        Assert.Equal(900, main.Height);
        Assert.True(main.Maximized);
        Assert.Equal(245, main.LeftPanelWidth);
        Assert.Equal(365, main.RightPanelWidth);
    }

    [Fact]
    public void MissingFileYieldsDefaults()
    {
        var loaded = UserConfig.Load(PathFor("nowhere.json"));
        Assert.Equal(1f, loaded.SpaceMouse.OrbitSensitivity);
        Assert.False(loaded.SpaceMouse.InvertZoom);
        Assert.Equal(PlacementMode.AutoDrop, loaded.Placement.Mode);
        Assert.Equal(0f, loaded.Placement.HeightMm);
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

    [Fact]
    public void ExplicitNullPlacementSectionIsTreatedAsMissing()
    {
        var path = PathFor("null-section.json");
        Directory.CreateDirectory(_dir);
        File.WriteAllText(path, """{ "Placement": null }""");

        var loaded = UserConfig.Load(path);

        Assert.Equal(PlacementMode.AutoDrop, loaded.Placement.Mode);
        Assert.Equal(0f, loaded.Placement.HeightMm);
    }

    [Fact]
    public void LegacyPlacementModesMigrateToToggleAndOffsetSemantics()
    {
        Directory.CreateDirectory(_dir);
        var dropPath = PathFor("old-drop.json");
        File.WriteAllText(dropPath, """{ "Placement": { "Mode": "AutoDrop", "HeightMm": 9 } }""");
        var offPath = PathFor("old-off.json");
        File.WriteAllText(offPath, """{ "Placement": { "Mode": "Off", "HeightMm": 9 } }""");

        var drop = UserConfig.Load(dropPath);
        var off = UserConfig.Load(offPath);

        Assert.Equal(PlacementMode.AutoDrop, drop.Placement.Mode);
        Assert.Equal(0f, drop.Placement.HeightMm);
        Assert.Equal(PlacementMode.Off, off.Placement.Mode);
        Assert.Equal(9f, off.Placement.HeightMm);
    }
}
