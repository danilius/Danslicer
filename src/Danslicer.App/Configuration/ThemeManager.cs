using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Markup.Xaml.Styling;
using Danslicer.Core.Config;

namespace Danslicer.App.Configuration;

/// <summary>Swaps the app palette and optional complete control-style proposal at runtime.</summary>
internal static class ThemeManager
{
    internal const string ClassicPaletteUri =
        "avares://Danslicer.App/Themes/ClassicPalette.axaml";
    internal const string ForgePaletteUri =
        "avares://Danslicer.App/Themes/ForgePalette.axaml";
    internal const string ForgeStylesUri =
        "avares://Danslicer.App/Themes/ForgeStyles.axaml";

    private static ResourceDictionary? _activePalette;
    private static StyleInclude? _activeStyles;

    internal static AppTheme CurrentTheme { get; private set; } = AppTheme.Classic;
    internal static event Action? ThemeChanged;

    internal static AppTheme Normalize(AppTheme theme) =>
        Enum.IsDefined(theme) ? theme : AppTheme.Classic;

    internal static string PaletteUri(AppTheme theme) => Normalize(theme) switch
    {
        AppTheme.Forge => ForgePaletteUri,
        _ => ClassicPaletteUri,
    };

    internal static void Apply(AppTheme theme, Application? application = null)
    {
        application ??= Application.Current;
        if (application is null) return;

        theme = Normalize(theme);
        CurrentTheme = theme;
        if (_activePalette is not null)
            application.Resources.MergedDictionaries.Remove(_activePalette);
        if (_activeStyles is not null)
            application.Styles.Remove(_activeStyles);

        _activePalette = (ResourceDictionary)AvaloniaXamlLoader.Load(new Uri(PaletteUri(theme)));
        application.Resources.MergedDictionaries.Add(_activePalette);
        _activeStyles = null;

        if (theme == AppTheme.Forge)
        {
            _activeStyles = new StyleInclude(new Uri("avares://Danslicer.App/"))
            {
                Source = new Uri(ForgeStylesUri),
            };
            application.Styles.Add(_activeStyles);
        }
        ThemeChanged?.Invoke();
    }
}
