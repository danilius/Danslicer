using Danslicer.App.ViewModels;
using Danslicer.Core.Config;

namespace Danslicer.Tests;

public sealed class NumericPreviewTests
{
    [Fact]
    public void ViewPreviewDoesNotSaveAndUnrelatedSaveSeesCommittedValue()
    {
        var config = new UserConfig();
        var original = config.Viewport.AmbientOcclusionStrength;
        var saved = new List<float>();
        var vm = new ConfigViewModel(config, () => saved.Add(config.Viewport.AmbientOcclusionStrength));
        var events = 0;
        vm.ViewportSaved += () => events++;
        var preview = vm.BeginNumericPreview(nameof(vm.AmbientOcclusionStrength));
        preview.Update(0.12);
        preview.Update(0.22);
        Assert.Equal(0.22f, config.Viewport.AmbientOcclusionStrength);
        Assert.Empty(saved);
        Assert.Equal(0, events);
        vm.PlateReflectionStrength = 0.2f;
        Assert.Equal(original, Assert.Single(saved));
        Assert.Equal(0.22f, config.Viewport.AmbientOcclusionStrength);
        preview.Commit(0.22);
        Assert.Equal(new[] { original, 0.22f }, saved);
        Assert.Equal(2, events);
        preview.Update(0.5); preview.Cancel(); preview.Commit(0.5);
        Assert.Equal(0.22f, config.Viewport.AmbientOcclusionStrength);
        Assert.Equal(2, saved.Count);
    }

    [Fact]
    public void SupportPreviewDoesNotApplyToSelectionAndCancelRestoresWithoutSaving()
    {
        var config = new UserConfig();
        var original = config.Supports.TipDiameter;
        var saves = 0; var applications = 0;
        var vm = new ConfigViewModel(config, () => saves++);
        vm.Saved += () => applications++;
        var preview = vm.BeginNumericPreview(nameof(vm.SupportTipDiameter));
        preview.Update(1.5);
        Assert.Equal(1.5f, config.Supports.TipDiameter);
        Assert.Equal(0, saves); Assert.Equal(0, applications);
        preview.Cancel();
        Assert.Equal(original, config.Supports.TipDiameter);
        Assert.Equal(0, saves); Assert.Equal(0, applications);
        preview = vm.BeginNumericPreview(nameof(vm.SupportTipDiameter));
        preview.Update(1.2); preview.Commit(1.2);
        Assert.Equal(1, saves); Assert.Equal(1, applications);
    }

    [Fact]
    public void PreviewReturningToOriginalDoesNotSave()
    {
        var config = new UserConfig(); var saves = 0;
        var vm = new ConfigViewModel(config, () => saves++);
        var original = vm.WorkingShadowStrength;
        var preview = vm.BeginNumericPreview(nameof(vm.WorkingShadowStrength));
        preview.Update(0.5); preview.Update(original); preview.Commit(original);
        Assert.Equal(original, vm.WorkingShadowStrength);
        Assert.Equal(0, saves);
    }
}
