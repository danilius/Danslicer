using Danslicer.Core;
using Danslicer.Core.Config;
using Danslicer.Core.Printers;
using Danslicer.Core.Slicing;
using Danslicer.Core.Supports;
using Danslicer.Core.Supports.Generation;
using Danslicer.Core.Supports.Routing;

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
                CapInterior = false,
                CapStyle = ClipCapStyle.Painted,
                SupportDisplay = new SupportDisplayConfig
                {
                    Mode = SupportDisplayMode.Transparent,
                    ShowContactPointsInTransparent = false,
                    ShowTips = false,
                    ShowMiniSupports = false,
                    ShowBranches = false,
                    ShowTrunks = false,
                    ShowBases = false,
                    ShowBracing = false,
                },
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
        Assert.False(loaded.Viewport.CapInterior);
        Assert.Equal(ClipCapStyle.Painted, loaded.Viewport.CapStyle);
        Assert.Equal(SupportDisplayMode.Transparent, loaded.Viewport.SupportDisplay.Mode);
        Assert.False(loaded.Viewport.SupportDisplay.ShowContactPointsInTransparent);
        Assert.False(loaded.Viewport.SupportDisplay.ShowTips);
        Assert.False(loaded.Viewport.SupportDisplay.ShowMiniSupports);
        Assert.False(loaded.Viewport.SupportDisplay.ShowBranches);
        Assert.False(loaded.Viewport.SupportDisplay.ShowTrunks);
        Assert.False(loaded.Viewport.SupportDisplay.ShowBases);
        Assert.False(loaded.Viewport.SupportDisplay.ShowBracing);
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
        Assert.Equal(SupportDisplayMode.Full, loaded.Viewport.SupportDisplay.Mode);
        Assert.True(loaded.Viewport.CapInterior);
        Assert.Equal(ClipCapStyle.Sliced, loaded.Viewport.CapStyle);
        Assert.True(loaded.Viewport.SupportDisplay.ShowTips);
        Assert.True(loaded.Viewport.SupportDisplay.ShowMiniSupports);
        Assert.True(loaded.Viewport.SupportDisplay.ShowBranches);
        Assert.True(loaded.Viewport.SupportDisplay.ShowTrunks);
        Assert.True(loaded.Viewport.SupportDisplay.ShowBases);
        Assert.True(loaded.Viewport.SupportDisplay.ShowBracing);
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
    public void PrinterDefinitionsRoundTripEveryConsumedField()
    {
        var config = new UserConfig();
        var custom = PrinterDefinition.PhotonMonoX.CreateUserCopy("Workshop custom") with
        {
            MachineName = "Workshop machine",
            FileExtension = "pwma",
            DisplayWidthMm = 130.5f,
            DisplayHeightMm = 81.25f,
            ZTravelMm = 190,
            ResolutionX = 2560,
            ResolutionY = 1620,
            MirrorX = false,
            MirrorY = true,
            FormatVersion = 517,
        };
        config.AddPrinter(custom);
        var path = PathFor("printers.json");

        config.Save(path);
        var loaded = UserConfig.Load(path);

        Assert.Equal(custom, loaded.FindPrinter(custom.Id));
        Assert.True(loaded.FindPrinter(PrinterDefinition.PhotonMonoXId)!.IsBuiltIn);
        var json = File.ReadAllText(path);
        Assert.DoesNotContain("BuildVolume", json);
        Assert.DoesNotContain("PixelPitch", json);
    }

    [Fact]
    public void MissingOrModifiedBuiltInPrinterIsRecreatedOnLoad()
    {
        var path = PathFor("missing-printer.json");
        Directory.CreateDirectory(_dir);
        File.WriteAllText(path, """
            {
              "Printers": [
                {
                  "Id": "anycubic-photon-mono-x",
                  "IsBuiltIn": false,
                  "Name": "Changed built-in",
                  "MachineName": "Wrong",
                  "FileExtension": "bad",
                  "DisplayWidthMm": 1,
                  "DisplayHeightMm": 1,
                  "ZTravelMm": 1,
                  "ResolutionX": 1,
                  "ResolutionY": 1,
                  "MirrorX": false,
                  "MirrorY": true,
                  "FormatVersion": 1
                }
              ]
            }
            """);

        var loaded = UserConfig.Load(path);

        Assert.Equal(PrinterDefinition.PhotonMonoX,
            loaded.FindPrinter(PrinterDefinition.PhotonMonoXId));
    }

    [Fact]
    public void ResinPresetsRoundTripApplyAndLeavePerPrintSettingsAlone()
    {
        var config = new UserConfig();
        var print = PrintSettings.Default with
            { LayerHeight = 0.025f, AntiAliasing = false, XyCompensation = -0.04f };
        var resin = ResinSettings.Default with
        {
            BottomLayers = 8, BottomExposure = 35, Exposure = 2.7f,
            LightOffDelay = 1.1f, LiftHeight = 9, LiftSpeed = 88,
            RetractSpeed = 144, BottomLiftHeight = 11, BottomLiftSpeed = 72,
        };
        var saved = config.SaveResinPresetAs("Tough grey", resin);
        var path = PathFor("resin-presets.json");

        config.Save(path);
        var loaded = UserConfig.Load(path);
        var roundTrip = loaded.FindResinPreset(saved!.Id);

        Assert.Equal(ResinPreset.CurrentVersion, roundTrip!.Version);
        Assert.Equal(resin, roundTrip.Settings);
        var document = new Document { PrintSettings = print };
        document.ApplyResinPreset(roundTrip);
        Assert.Equal(print, document.PrintSettings);
        Assert.Equal(resin, document.ResinSettings);
        Assert.NotSame(roundTrip.Settings, document.ResinSettings);
    }

    [Fact]
    public void MissingDefaultResinPresetIsRecreatedOnLoad()
    {
        var path = PathFor("missing-default-resin.json");
        Directory.CreateDirectory(_dir);
        File.WriteAllText(path, """
            {
              "ResinPresets": [
                { "Version": 1, "Id": "custom", "Name": "Custom", "Settings": { "Exposure": 3.1 } }
              ]
            }
            """);

        var loaded = UserConfig.Load(path);

        Assert.Equal(ResinPreset.Default, loaded.FindResinPreset(ResinPreset.DefaultId));
        Assert.Equal(3.1f, loaded.FindResinPreset("custom")!.Settings.Exposure);
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
                PreferExistingTrunks = false, ExistingTrunkBranchRange = 9f,
                MiniSupportDiameter = 0.7f, MiniSupportTipDiameter = 0.3f,
                MiniSupportConeLength = 1.2f, MiniSupportMaxLength = 6f,
                MiniSupportMaxAngleDegrees = 72f, MiniSupportMaxFanPerBranchEnd = 5,
                MiniSupportClusterDistance = 1.4f, FineFeatureMaxAreaMm2 = 1.8f,
                FineFeatureMinisFallBackToRegular = false,
                RefusedTipsFallBackToMini = true, MiniIslandMaxAreaMm2 = 0.2f,
                UseBaseGrid = false, BaseGridPitch = 18f,
                ReinforceEnabled = true,
                ReinforceSeedSelector = ReinforceSeedSelector.CriticalTips,
                ReinforceCount = 5, ReinforceRingRadius = 4.5f,
                ReinforceRingDiameterMultiplier = 1.6f,
                BaseShape = SupportBaseShape.DiscCone, BaseDiameter = 6f, BaseHeight = 1.1f,
                BaseConeHeight = 2.8f, Spacing = 3.2f, IslandSpacingMm = 0.7f, OverhangAngleDegrees = 51f,
                MinIslandAreaMm2 = 0.9f,
                MaxContactFaceAngleDegrees = 33f, RequireContactSeesPlate = true,
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
        Assert.False(supports.PreferExistingTrunks);
        Assert.Equal(9f, supports.ExistingTrunkBranchRange);
        Assert.Equal(0.7f, supports.MiniSupportDiameter);
        Assert.Equal(0.3f, supports.MiniSupportTipDiameter);
        Assert.Equal(1.2f, supports.MiniSupportConeLength);
        Assert.Equal(6f, supports.MiniSupportMaxLength);
        Assert.Equal(72f, supports.MiniSupportMaxAngleDegrees);
        Assert.Equal(5, supports.MiniSupportMaxFanPerBranchEnd);
        Assert.Equal(1.4f, supports.MiniSupportClusterDistance);
        Assert.Equal(1.8f, supports.FineFeatureMaxAreaMm2);
        Assert.False(supports.FineFeatureMinisFallBackToRegular);
        Assert.True(supports.RefusedTipsFallBackToMini);
        Assert.Equal(0.2f, supports.MiniIslandMaxAreaMm2);
        Assert.False(supports.UseBaseGrid);
        Assert.Equal(18f, supports.BaseGridPitch);
        Assert.True(supports.ReinforceEnabled);
        Assert.Equal(ReinforceSeedSelector.CriticalTips, supports.ReinforceSeedSelector);
        Assert.Equal(5, supports.ReinforceCount);
        Assert.Equal(4.5f, supports.ReinforceRingRadius);
        Assert.Equal(1.6f, supports.ReinforceRingDiameterMultiplier);
        Assert.Equal(SupportBaseShape.DiscCone, supports.BaseShape);
        Assert.Equal(6f, supports.BaseDiameter);
        Assert.Equal(1.1f, supports.BaseHeight);
        Assert.Equal(2.8f, supports.BaseConeHeight);
        Assert.Equal(3.2f, supports.Spacing);
        Assert.Equal(0.7f, supports.IslandSpacingMm);
        Assert.Equal(51f, supports.OverhangAngleDegrees);
        Assert.Equal(0.9f, supports.MinIslandAreaMm2);
        Assert.Equal(33f, supports.MaxContactFaceAngleDegrees);
        Assert.True(supports.RequireContactSeesPlate);
    }

    [Fact]
    public void ContactFaceSettingsRoundTripThroughSupportPresets()
    {
        var config = new UserConfig();
        config.Supports.MaxContactFaceAngleDegrees = 30f;
        config.Supports.RequireContactSeesPlate = true;
        Assert.True(config.SaveSupportPresetAs("Downward only"));

        var path = PathFor("contact-face-preset.json");
        config.Save(path);
        var loaded = UserConfig.Load(path);

        var preset = loaded.FindSupportPreset("Downward only");
        Assert.NotNull(preset);
        Assert.Equal(30f, preset!.Settings.MaxContactFaceAngleDegrees);
        Assert.True(preset.Settings.RequireContactSeesPlate);

        // Default (unset) config keeps 90°/off so existing users see no change.
        var defaults = new SupportConfig();
        Assert.Equal(90f, defaults.MaxContactFaceAngleDegrees);
        Assert.False(defaults.RequireContactSeesPlate);
    }

    [Fact]
    public void SupportPresetsRoundTripAsVersionedIndependentSnapshots()
    {
        var config = new UserConfig();
        config.Supports.TipDiameter = 0.23f;
        config.Supports.UseBaseGrid = false;
        config.Supports.MiniSupportMaxFanPerBranchEnd = 7;
        config.Supports.FineFeatureMaxAreaMm2 = 1.7f;
        config.Supports.FineFeatureMinisFallBackToRegular = false;
        Assert.True(config.SaveSupportPresetAs("Delicate teeth"));
        var path = PathFor("support-presets.json");

        config.Save(path);
        var loaded = UserConfig.Load(path);

        var preset = Assert.Single(loaded.SupportPresets,
            candidate => candidate.Name == "Delicate teeth");
        Assert.Equal(SupportPreset.CurrentVersion, preset.Version);
        Assert.Equal(0.23f, preset.Settings.TipDiameter);
        Assert.False(preset.Settings.UseBaseGrid);
        Assert.Equal(7, preset.Settings.MiniSupportMaxFanPerBranchEnd);
        Assert.Equal(1.7f, preset.Settings.FineFeatureMaxAreaMm2);
        Assert.False(preset.Settings.FineFeatureMinisFallBackToRegular);
        Assert.Equal("Delicate teeth", loaded.ActiveSupportPresetName);
        Assert.NotSame(loaded.Supports, preset.Settings);
    }

    [Fact]
    public void ApplyingSupportPresetReplacesTheWholeLiveBundleWithACopy()
    {
        var config = new UserConfig();
        config.Supports = new SupportConfig
        {
            TipDiameter = 0.72f, ConeLength = 3.4f, TrunkDiameter = 2.1f,
            PreferExistingTrunks = false, UseBaseGrid = false, BaseGridPitch = 13f,
            RefusedTipsFallBackToMini = true, MiniIslandMaxAreaMm2 = 0.08f,
            BaseShape = SupportBaseShape.DiscCone, Spacing = 1.7f,
        };
        Assert.True(config.SaveSupportPresetAs("Heavy"));
        var snapshot = config.FindSupportPreset("Heavy")!.Settings;
        config.Supports = new SupportConfig { TipDiameter = 0.11f, BaseGridPitch = 99f };

        Assert.True(config.ApplySupportPreset("heavy"));

        Assert.Equal(snapshot, config.Supports);
        Assert.NotSame(snapshot, config.Supports);
        config.Supports.TipDiameter = 9f;
        Assert.Equal(0.72f, snapshot.TipDiameter);
    }

    [Fact]
    public void SupportPresetsCanBeSavedRenamedAndDeleted()
    {
        var config = new UserConfig();
        config.Supports.TipDiameter = 0.31f;
        Assert.True(config.SaveSupportPresetAs("Working copy"));
        config.Supports.TipDiameter = 0.44f;

        Assert.True(config.SaveSupportPreset("working COPY"));
        Assert.Equal(0.44f, config.FindSupportPreset("Working copy")!.Settings.TipDiameter);
        Assert.True(config.RenameSupportPreset("Working copy", "Fine detail"));
        Assert.Equal("Fine detail", config.ActiveSupportPresetName);
        Assert.False(config.RenameSupportPreset("Fine detail", UserConfig.CadCleanSupportPresetName));
        Assert.True(config.DeleteSupportPreset("fine detail"));
        Assert.Null(config.FindSupportPreset("Fine detail"));
        Assert.Equal(UserConfig.CadCleanSupportPresetName, config.ActiveSupportPresetName);
    }

    [Fact]
    public void MissingBuiltInSupportPresetsAreRecreatedOnLoad()
    {
        var path = PathFor("missing-built-ins.json");
        Directory.CreateDirectory(_dir);
        File.WriteAllText(path, """
            {
              "SupportPresets": [
                { "Version": 1, "Name": "Custom", "Settings": { "TipDiameter": 0.3 } }
              ],
              "ActiveSupportPresetName": "missing"
            }
            """);

        var loaded = UserConfig.Load(path);

        Assert.NotNull(loaded.FindSupportPreset(UserConfig.CadCleanSupportPresetName));
        Assert.NotNull(loaded.FindSupportPreset(UserConfig.OrganicDenseSupportPresetName));
        Assert.NotNull(loaded.FindSupportPreset("Custom"));
        Assert.Equal(UserConfig.CadCleanSupportPresetName, loaded.ActiveSupportPresetName);
        Assert.Equal(new SupportConfig(),
            loaded.FindSupportPreset(UserConfig.CadCleanSupportPresetName)!.Settings);
        Assert.Equal(new SupportConfig(),
            loaded.FindSupportPreset(UserConfig.OrganicDenseSupportPresetName)!.Settings);
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
        Assert.True(supports.PreferExistingTrunks);
        Assert.Equal(8f, supports.ExistingTrunkBranchRange);
        Assert.Equal(0.6f, supports.MiniSupportDiameter);
        Assert.Equal(0.25f, supports.MiniSupportTipDiameter);
        Assert.Equal(1f, supports.MiniSupportConeLength);
        Assert.Equal(5f, supports.MiniSupportMaxLength);
        Assert.Equal(75f, supports.MiniSupportMaxAngleDegrees);
        Assert.Equal(4, supports.MiniSupportMaxFanPerBranchEnd);
        Assert.Equal(1f, supports.FineFeatureMaxAreaMm2);
        Assert.True(supports.FineFeatureMinisFallBackToRegular);
        Assert.False(supports.RefusedTipsFallBackToMini);
        Assert.Equal(0.1f, supports.MiniIslandMaxAreaMm2);
        Assert.True(supports.UseBaseGrid);
        Assert.Equal(6f, supports.BaseGridPitch);
        Assert.False(supports.ReinforceEnabled);
        Assert.Equal(ReinforceSeedSelector.LowestPointOfObject, supports.ReinforceSeedSelector);
        Assert.Equal(3, supports.ReinforceCount);
        Assert.Equal(2f, supports.ReinforceRingRadius);
        Assert.Equal(1.25f, supports.ReinforceRingDiameterMultiplier);
        Assert.Equal(6f, new TreeRoutingOptions().BaseGridPitch);
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
    public void ExplicitNullSupportDisplayIsTreatedAsMissing()
    {
        var path = PathFor("null-support-display.json");
        Directory.CreateDirectory(_dir);
        File.WriteAllText(path, """{ "Viewport": { "SupportDisplay": null } }""");

        var loaded = UserConfig.Load(path);

        Assert.Equal(SupportDisplayMode.Full, loaded.Viewport.SupportDisplay.Mode);
        Assert.True(loaded.Viewport.SupportDisplay.ShowContactPointsInTransparent);
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
