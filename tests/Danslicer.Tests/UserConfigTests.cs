using Danslicer.Core.Config;
using Danslicer.Core.Supports;

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
    public void RoundTripsAllSupportSettings()
    {
        var config = new UserConfig
        {
            Supports = new SupportConfig
            {
                TipDiameter = 0.55f, ConeLength = 2.5f, BallDiameter = 0.3f,
                PenetrationDepth = 0.15f, TrunkDiameter = 1.8f, BranchDiameter = 1.4f,
                MemberAngleDegrees = 38f, TipMemberLength = 3f, MaxBranchLength = 11f,
                BaseShape = SupportBaseShape.DiscCone, BaseDiameter = 6f, BaseHeight = 1.1f,
                BaseConeHeight = 2.8f, Spacing = 3.2f, IslandSpacingMm = 0.7f, OverhangAngleDegrees = 51f,
                MinIslandAreaMm2 = 0.9f,
            },
        };
        var path = PathFor("supports.json");

        config.Save(path);
        var supports = UserConfig.Load(path).Supports;

        Assert.Equal(0.55f, supports.TipDiameter);
        Assert.Equal(2.5f, supports.ConeLength);
        Assert.Equal(0.3f, supports.BallDiameter);
        Assert.Equal(0.15f, supports.PenetrationDepth);
        Assert.Equal(1.8f, supports.TrunkDiameter);
        Assert.Equal(1.4f, supports.BranchDiameter);
        Assert.Equal(38f, supports.MemberAngleDegrees);
        Assert.Equal(3f, supports.TipMemberLength);
        Assert.Equal(11f, supports.MaxBranchLength);
        Assert.Equal(SupportBaseShape.DiscCone, supports.BaseShape);
        Assert.Equal(6f, supports.BaseDiameter);
        Assert.Equal(1.1f, supports.BaseHeight);
        Assert.Equal(2.8f, supports.BaseConeHeight);
        Assert.Equal(3.2f, supports.Spacing);
        Assert.Equal(0.7f, supports.IslandSpacingMm);
        Assert.Equal(51f, supports.OverhangAngleDegrees);
        Assert.Equal(0.9f, supports.MinIslandAreaMm2);
    }

    [Fact]
    public void FreshSupportSettingsMatchGenerationDefaults()
    {
        var supports = new UserConfig().Supports;

        Assert.Equal(0.4f, supports.TipDiameter);
        Assert.Equal(2f, supports.ConeLength);
        Assert.Equal(0f, supports.BallDiameter);
        Assert.Equal(0f, supports.PenetrationDepth);
        Assert.Equal(1.2f, supports.TrunkDiameter);
        Assert.Equal(1.2f, supports.BranchDiameter);
        Assert.Equal(45f, supports.MemberAngleDegrees);
        Assert.Equal(2f, supports.TipMemberLength);
        Assert.Equal(8f, supports.MaxBranchLength);
        Assert.Equal(SupportBaseShape.Disc, supports.BaseShape);
        Assert.Equal(4f, supports.BaseDiameter);
        Assert.Equal(0.8f, supports.BaseHeight);
        Assert.Equal(2f, supports.BaseConeHeight);
        Assert.Equal(2.5f, supports.Spacing);
        Assert.Equal(0.5f, supports.IslandSpacingMm);
        Assert.Equal(45f, supports.OverhangAngleDegrees);
        Assert.Equal(0.1f, supports.MinIslandAreaMm2);
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
    public void ExplicitNullSupportSectionIsTreatedAsMissing()
    {
        var path = PathFor("null-support-section.json");
        Directory.CreateDirectory(_dir);
        File.WriteAllText(path, """{ "Supports": null }""");

        var loaded = UserConfig.Load(path);

        Assert.Equal(0.4f, loaded.Supports.TipDiameter);
        Assert.Equal(SupportBaseShape.Disc, loaded.Supports.BaseShape);
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
