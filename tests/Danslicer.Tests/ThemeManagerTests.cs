using Danslicer.App.Configuration;
using Danslicer.Core.Config;

namespace Danslicer.Tests;

public sealed class ThemeManagerTests
{
    [Fact]
    public void EverySelectableThemeResolvesToAnEmbeddedPalette()
    {
        Assert.Equal(ThemeManager.ClassicPaletteUri, ThemeManager.PaletteUri(AppTheme.Classic));
        Assert.Equal(ThemeManager.ForgePaletteUri, ThemeManager.PaletteUri(AppTheme.Forge));
        Assert.EndsWith(".axaml", ThemeManager.ForgeStylesUri);
    }

    [Fact]
    public void UnknownThemeFallsBackToClassic()
    {
        var unknown = (AppTheme)12345;
        Assert.Equal(AppTheme.Classic, ThemeManager.Normalize(unknown));
        Assert.Equal(ThemeManager.ClassicPaletteUri, ThemeManager.PaletteUri(unknown));
    }
}
