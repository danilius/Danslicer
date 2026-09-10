namespace Danslicer.App.Configuration;

/// <summary>UI-thread only. An unrelated configuration save serializes committed values,
/// then puts the live preview back without notifications, jobs or additional writes.</summary>
internal static class PreviewPersistence
{
    private static readonly List<(Action Restore, Action Preview)> Entries = [];
    private static bool _saving;
    public static Action Register(Action restore, Action preview)
    {
        var entry = (restore, preview);
        Entries.Add(entry);
        return () => Entries.Remove(entry);
    }
    public static void Save(Action save)
    {
        if (_saving) { save(); return; }
        _saving = true;
        var entries = Entries.ToArray();
        try
        {
            foreach (var entry in entries) entry.Restore();
            save();
        }
        finally
        {
            foreach (var entry in entries) entry.Preview();
            _saving = false;
        }
    }
}
