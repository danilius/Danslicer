using System.Globalization;
using System.Numerics;
using Danslicer.Core.Config;

namespace Danslicer.App.Configuration;

/// <summary>
/// The process-wide user configuration, loaded once at startup. Consumers read values live
/// (the viewport reads SpaceMouse settings every poll tick), so edits apply immediately;
/// <see cref="Save"/> persists them.
/// </summary>
public static class AppConfig
{
    private static string _path = UserConfig.DefaultPath;
    private static UserConfig? _current;
    public static UserConfig Current => _current ??= UserConfig.Load(_path);
    public static string WorkspacePath => Path.Combine(Path.GetDirectoryName(_path)!, "workspace-ui.json");
    public static void UseIsolatedDirectory(string directory)
    {
        if (_current is not null) throw new InvalidOperationException("Configuration already loaded");
        _path = Path.Combine(directory, "config.json");
    }

    public static void Save() => Current.Save(_path);

    /// <summary>Parses "#RRGGBB" (leading '#' optional) into linear-ish RGB; fallback on junk.</summary>
    public static Vector3 ParseColor(string? hex, Vector3 fallback)
    {
        var s = hex?.TrimStart('#');
        if (s is not { Length: 6 } ||
            !int.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
            return fallback;
        return new Vector3(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f);
    }
}

