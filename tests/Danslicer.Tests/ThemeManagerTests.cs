using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
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

    [Fact]
    public void EmbeddedPalettesResolveRequiredTokens()
    {
        Danslicer.App.Program.BuildAvaloniaApp().SetupWithoutStarting();
        var application = Assert.IsType<Danslicer.App.App>(Application.Current);
        foreach (var theme in Enum.GetValues<AppTheme>())
        {
            ThemeManager.Apply(theme, application);
            Assert.True(application.TryFindResource("DsBackgroundBrush", out var background));
            Assert.True(application.TryFindResource("DsAccentBrush", out var accent));
            Assert.True(application.TryFindResource("DsIconObjects", out var objectsIcon));
            Assert.IsAssignableFrom<IBrush>(background);
            Assert.IsAssignableFrom<IBrush>(accent);
            Assert.IsType<StreamGeometry>(objectsIcon);

            var forgeStyles = application.Styles.OfType<StyleInclude>().SingleOrDefault(
                style => style.Source?.ToString() == ThemeManager.ForgeStylesUri);
            if (theme == AppTheme.Forge)
                Assert.NotNull(forgeStyles?.Loaded);
            else
                Assert.Null(forgeStyles);
        }
    }
}
