using Danslicer.App.ViewModels;

namespace Danslicer.Tests;

public sealed class HoverWaterlineViewModelTests
{
    [Fact]
    public void DefaultsOnButRequiresSupportModeAndAFiniteSurfaceHit()
    {
        var model = new HoverWaterlineViewModel();

        Assert.True(model.Enabled);
        model.UpdateHover(12.3456f);
        Assert.False(model.IsActive);

        model.SupportModeActive = true;
        model.UpdateHover(float.NaN);
        Assert.False(model.IsActive);

        model.UpdateHover(12.3456f);
        Assert.True(model.IsActive);
        Assert.Equal(12.3456f, model.WorldZ);
        Assert.Equal("Waterline Z: 12.346 mm", model.StatusText);
    }

    [Fact]
    public void PointerExitToggleAndModeExitClearTheHover()
    {
        var model = new HoverWaterlineViewModel { SupportModeActive = true };
        model.UpdateHover(4.5f);

        model.Clear();
        Assert.Null(model.WorldZ);

        model.UpdateHover(5.5f);
        model.Enabled = false;
        Assert.Null(model.WorldZ);

        model.Enabled = true;
        model.UpdateHover(6.5f);
        model.SupportModeActive = false;
        Assert.Null(model.WorldZ);
        Assert.Null(model.StatusText);
    }
}
