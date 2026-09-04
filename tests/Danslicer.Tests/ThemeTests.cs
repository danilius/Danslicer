using System.Text.RegularExpressions;
using Danslicer.App.Themes;
using Danslicer.Core.Config;

namespace Danslicer.Tests;

/// <summary>
/// Covers the stylesheet-proposal plumbing that does not need a live Avalonia Application:
/// token-key parity across the theme .axaml files (a live theme swap that quietly drops a key
/// would only show up as a missing DynamicResource at runtime), the catalog's own bookkeeping,
/// and UserConfig.Theme persistence/normalization. Actually loading a theme dictionary via
/// AvaloniaXamlLoader needs an Application instance this test project does not spin up
/// (no Avalonia.Headless reference) — that path is exercised by running the app.
/// </summary>
public sealed class ThemeTests : IDisposable
{
    private static readonly string ThemesDir = FindThemesDirectory();
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "danslicer-theme-tests", Guid.NewGuid().ToString("N"));
    private string PathFor(string name) => Path.Combine(_dir, name);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private static string FindThemesDirectory()
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 12 && dir is not null; i++, dir = Path.GetDirectoryName(dir))
        {
            var candidate = Path.Combine(dir, "src", "Danslicer.App", "Themes");
            if (Directory.Exists(candidate)) return candidate;
        }
        throw new DirectoryNotFoundException(
            "Could not locate src/Danslicer.App/Themes from the test output directory.");
    }

    private static IReadOnlySet<string> ExtractKeys(string fileName)
    {
        var text = File.ReadAllText(Path.Combine(ThemesDir, fileName));
        return Regex.Matches(text, "x:Key=\"([A-Za-z0-9]+)\"")
            .Select(m => m.Groups[1].Value)
            .ToHashSet();
    }

    [Theory]
    [InlineData("ClassicTheme.axaml")]
    [InlineData("CarbideTheme.axaml")]
    [InlineData("SlateTheme.axaml")]
    public void EveryThemeFileExists(string fileName) =>
        Assert.True(File.Exists(Path.Combine(ThemesDir, fileName)), fileName);

    [Fact]
    public void EveryThemeDefinesTheSameAppTokens()
    {
        string[] requiredAppTokens =
        [
            "AppSurface", "AppSurfaceAlt", "AppPanel", "AppHeader", "AppBorder",
            "AppTextPrimary", "AppTextMuted", "AppTextFaint", "AppAccent", "AppPopupBackground",
        ];
        var classic = ExtractKeys("ClassicTheme.axaml");
        foreach (var token in requiredAppTokens)
            Assert.Contains(token, classic);

        var carbide = ExtractKeys("CarbideTheme.axaml");
        var slate = ExtractKeys("SlateTheme.axaml");
        Assert.Equal(classic, carbide);
        Assert.Equal(classic, slate);
    }

    [Fact]
    public void EveryThemeMergesTheSharedIconSet()
    {
        foreach (var fileName in new[] { "ClassicTheme.axaml", "CarbideTheme.axaml", "SlateTheme.axaml" })
        {
            var text = File.ReadAllText(Path.Combine(ThemesDir, fileName));
            Assert.Contains("avares://Danslicer.App/Themes/IconSet.axaml", text);
        }
    }

    [Fact]
    public void IconSetKeysAreConsistentAcrossThemesViaTheSharedInclude()
    {
        // Every theme merges IconSet.axaml rather than redefining icons itself, so the icon
        // keys belong to IconSet.axaml only — assert they are NOT duplicated locally (that
        // would silently shadow the shared set and defeat the "define icons once" design).
        var iconKeys = ExtractKeys("IconSet.axaml");
        Assert.NotEmpty(iconKeys);
        foreach (var fileName in new[] { "ClassicTheme.axaml", "CarbideTheme.axaml", "SlateTheme.axaml" })
        {
            var themeKeys = ExtractKeys(fileName);
            Assert.Empty(iconKeys.Intersect(themeKeys));
        }
    }

    [Fact]
    public void CatalogListsAllThreeThemesWithClassicDefault()
    {
        Assert.Equal(["Classic", "Carbide", "Slate"], ThemeCatalog.Names);
        Assert.Equal("Classic", ThemeCatalog.DefaultTheme);
        Assert.True(ThemeCatalog.IsKnown("Classic"));
        Assert.True(ThemeCatalog.IsKnown("carbide")); // case-insensitive
        Assert.False(ThemeCatalog.IsKnown("NotAThinTheme"));
        Assert.False(ThemeCatalog.IsKnown(null));
    }

    [Fact]
    public void DefaultUserConfigThemeIsClassic() =>
        Assert.Equal("Classic", new UserConfig().Theme);

    [Fact]
    public void ThemeRoundTripsThroughSaveAndLoad()
    {
        var config = new UserConfig { Theme = "Carbide" };
        var path = PathFor("theme.json");

        config.Save(path);
        var loaded = UserConfig.Load(path);

        Assert.Equal("Carbide", loaded.Theme);
    }

    [Fact]
    public void MissingOrBlankThemeFallsBackToClassicOnLoad()
    {
        var path = PathFor("legacy.json");
        Directory.CreateDirectory(_dir);
        File.WriteAllText(path, """{ "Viewport": { "OverhangAngleDegrees": 30 } }""");

        Assert.Equal("Classic", UserConfig.Load(path).Theme);

        var blankPath = PathFor("blank.json");
        File.WriteAllText(blankPath, """{ "Theme": "   " }""");
        Assert.Equal("Classic", UserConfig.Load(blankPath).Theme);
    }

    [Fact]
    public void UnknownThemeNamePassesThroughCoreButFallsBackInTheCatalog()
    {
        // Core has no theme catalog to validate names against (App -> Core would be a cycle),
        // so an unrecognised name from a hand-edited or future-version config file survives
        // UserConfig.Load unchanged; ThemeCatalog.Apply is what falls back to Classic for it.
        var path = PathFor("unknown.json");
        Directory.CreateDirectory(_dir);
        File.WriteAllText(path, """{ "Theme": "SomeFutureTheme" }""");

        Assert.Equal("SomeFutureTheme", UserConfig.Load(path).Theme);
        Assert.False(ThemeCatalog.IsKnown(UserConfig.Load(path).Theme));
    }
}
