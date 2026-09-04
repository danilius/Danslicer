using Danslicer.App.ViewModels;
using Danslicer.Core.Config;

namespace Danslicer.Tests;

public sealed class ConfigViewModelTests
{
    [Fact]
    public void GridEditsPersistAcrossPresetSwitchesWithoutSavingOtherFields()
    {
        var config = new UserConfig();
        config.Supports = new SupportConfig { TipDiameter = 0.4f, UseBaseGrid = true, BaseGridPitch = 6f };
        Assert.True(config.SaveSupportPresetAs("Grid A"));
        config.Supports = new SupportConfig { TipDiameter = 0.8f, UseBaseGrid = true, BaseGridPitch = 12f };
        Assert.True(config.SaveSupportPresetAs("Grid B"));
        Assert.True(config.ApplySupportPreset("Grid A"));
        var saves = 0;
        var viewModel = new ConfigViewModel(config, () => saves++);

        viewModel.SupportTipDiameter = 0.55f;
        viewModel.SupportUseBaseGrid = false;
        viewModel.SupportBaseGridPitch = 9f;

        var savedA = config.FindSupportPreset("Grid A")!;
        Assert.Equal(0.4f, savedA.Settings.TipDiameter);
        Assert.False(savedA.Settings.UseBaseGrid);
        Assert.Equal(9f, savedA.Settings.BaseGridPitch);
        Assert.EndsWith(" *", viewModel.SupportPresetDisplayNames[viewModel.SelectedSupportPresetIndex]);

        viewModel.SelectedSupportPresetIndex = config.SupportPresets.IndexOf(config.FindSupportPreset("Grid B")!);
        viewModel.SelectedSupportPresetIndex = config.SupportPresets.IndexOf(savedA);

        Assert.Equal(0.4f, viewModel.SupportTipDiameter);
        Assert.False(viewModel.SupportUseBaseGrid);
        Assert.Equal(9f, viewModel.SupportBaseGridPitch);
        Assert.Equal(5, saves);
    }

    [Fact]
    public void GridEditOfBuiltInCreatesAndSelectsUserCopy()
    {
        var config = new UserConfig();
        var builtIn = config.FindSupportPreset(UserConfig.CadCleanSupportPresetName)!;
        var viewModel = new ConfigViewModel(config, () => { });

        viewModel.SupportUseBaseGrid = false;

        Assert.Equal(3, config.SupportPresets.Count);
        Assert.True(builtIn.Settings.UseBaseGrid);
        Assert.Equal("CAD clean copy", config.ActiveSupportPresetName);
        Assert.False(config.FindSupportPreset("CAD clean copy")!.Settings.UseBaseGrid);
        Assert.Equal(config.SupportPresets.IndexOf(config.FindSupportPreset("CAD clean copy")!),
            viewModel.SelectedSupportPresetIndex);
        Assert.DoesNotContain(" *", viewModel.SupportPresetDisplayNames[viewModel.SelectedSupportPresetIndex]);
    }

    [Fact]
    public void ExplicitSaveOfBuiltInUsesTheSameCopyOnEditPolicy()
    {
        var config = new UserConfig();
        var builtIn = config.FindSupportPreset(UserConfig.CadCleanSupportPresetName)!;
        config.Supports.TipDiameter = 0.7f;

        Assert.True(config.SaveSupportPreset(UserConfig.CadCleanSupportPresetName));

        Assert.Equal(0.4f, builtIn.Settings.TipDiameter);
        Assert.Equal("CAD clean copy", config.ActiveSupportPresetName);
        Assert.Equal(0.7f, config.FindSupportPreset("CAD clean copy")!.Settings.TipDiameter);
    }

    [Fact]
    public void PresetEditorGridEditsRemainTransactional()
    {
        var editingSettings = new SupportConfig();
        var changes = 0;
        var viewModel = new ConfigViewModel(editingSettings, () => changes++);

        viewModel.SupportUseBaseGrid = false;
        viewModel.SupportBaseGridPitch = 11f;
        viewModel.SupportMinMemberSeparationMm = 0.8f;
        viewModel.SupportIndependentManualSupports = true;

        Assert.False(editingSettings.UseBaseGrid);
        Assert.Equal(11f, editingSettings.BaseGridPitch);
        Assert.Equal(0.8f, editingSettings.MinMemberSeparationMm);
        Assert.True(editingSettings.IndependentManualSupports);
        Assert.Equal(4, changes);
        Assert.False(viewModel.ShowSupportPresetControls);
    }
}
