using Danslicer.Core.Config;

namespace Danslicer.App.Configuration;

/// <summary>
/// The process-wide user configuration, loaded once at startup. Consumers read values live
/// (the viewport reads SpaceMouse settings every poll tick), so edits apply immediately;
/// <see cref="Save"/> persists them.
/// </summary>
public static class AppConfig
{
    public static UserConfig Current { get; } = UserConfig.Load(UserConfig.DefaultPath);

    public static void Save() => Current.Save(UserConfig.DefaultPath);
}
