using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Danslicer.App.Themes;

/// <summary>
/// Named, runtime-selectable chrome palettes. Each name maps to a <see cref="ResourceDictionary"/>
/// (ClassicTheme.axaml, CarbideTheme.axaml, SlateTheme.axaml, TechyTheme.axaml, BlenderTheme.axaml)
/// that defines the shared "App*" tokens
/// plus IconSet.axaml's icon geometries plus a handful of FluentTheme resource-key overrides — see
/// PLAN.md for the full token list and the rationale for overriding FluentTheme this way.
/// </summary>
public static class ThemeCatalog
{
    public const string Classic = "Classic";
    public const string Carbide = "Carbide";
    public const string Slate = "Slate";
    public const string Techy = "Techy";
    public const string Blender = "Blender";

    /// <summary>Today's look; stays the default so existing users see no change on upgrade.</summary>
    public const string DefaultTheme = Classic;

    private static readonly IReadOnlyDictionary<string, Uri> ThemeUris =
        new Dictionary<string, Uri>(StringComparer.OrdinalIgnoreCase)
        {
            [Classic] = new Uri("avares://Danslicer.App/Themes/ClassicTheme.axaml"),
            [Carbide] = new Uri("avares://Danslicer.App/Themes/CarbideTheme.axaml"),
            [Slate] = new Uri("avares://Danslicer.App/Themes/SlateTheme.axaml"),
            [Techy] = new Uri("avares://Danslicer.App/Themes/TechyTheme.axaml"),
            [Blender] = new Uri("avares://Danslicer.App/Themes/BlenderTheme.axaml"),
        };

    /// <summary>Display order for the Preferences selector.</summary>
    public static IReadOnlyList<string> Names { get; } = [Classic, Carbide, Slate, Techy, Blender];

    public static bool IsKnown(string? name) => name is not null && ThemeUris.ContainsKey(name);

    /// <summary>The dedicated slot swapped by <see cref="Apply"/>, so re-applying never leaks
    /// a stale dictionary or duplicates one.</summary>
    private static ResourceDictionary? _active;

    /// <summary>
    /// Loads the named theme (falling back to <see cref="DefaultTheme"/> for an unknown name) and
    /// swaps it into Application.Resources.MergedDictionaries in place of whatever this method
    /// applied last. Safe to call before any window exists (startup) or at any time after
    /// (Preferences live-apply).
    /// </summary>
    public static void Apply(string? name)
    {
        var app = Application.Current;
        if (app is null) return;

        var uri = ThemeUris.TryGetValue(name ?? "", out var found) ? found : ThemeUris[DefaultTheme];
        var dictionary = (ResourceDictionary)AvaloniaXamlLoader.Load(uri);

        if (_active is not null) app.Resources.MergedDictionaries.Remove(_active);
        app.Resources.MergedDictionaries.Add(dictionary);
        _active = dictionary;
    }
}
