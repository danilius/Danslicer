using System.Reflection;
using System.Text;

namespace Danslicer.Core.Diagnostics;

/// <summary>
/// The application log: one file per day under <see cref="Directory"/> (by default
/// %AppData%\Danslicer\logs), holding unhandled exceptions with their full stack and whatever
/// else the app chooses to record. Added 2026-09-08 after two crashes whose stacks had to be
/// dug out of the Windows event log. Files older than <see cref="RetainDays"/> are removed the
/// first time the log is written in a session, so it never grows unbounded.
/// </summary>
public static class CrashLog
{
    public const int RetainDays = 14;

    private static readonly object Gate = new();
    private static bool _pruned;
    private static string _directory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Danslicer", "logs");

    /// <summary>Where log files live. Tests point this at a scratch folder; a new folder is pruned afresh.</summary>
    public static string Directory
    {
        get => _directory;
        set
        {
            lock (Gate)
            {
                _directory = value;
                _pruned = false;
            }
        }
    }

    /// <summary>Today's log file.</summary>
    public static string CurrentPath => Path.Combine(Directory, $"danslicer-{DateTime.Now:yyyyMMdd}.log");

    /// <summary>Appends one timestamped line. Never throws: a log that crashes the app is worse than none.</summary>
    public static void Write(string message)
    {
        try
        {
            lock (Gate)
            {
                System.IO.Directory.CreateDirectory(Directory);
                if (!_pruned)
                {
                    _pruned = true;
                    Prune();
                }
                File.AppendAllText(CurrentPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // Nothing sensible to do: the log is the last resort, not a feature to fail on.
        }
    }

    /// <summary>Records an exception with its context, type, message and full stack, inner exceptions included.</summary>
    public static void WriteException(string context, Exception exception)
    {
        var sb = new StringBuilder();
        sb.Append(context).Append(": ").Append(Header());
        for (var e = exception; e is not null; e = e.InnerException)
        {
            sb.AppendLine();
            sb.Append(e.GetType().FullName).Append(": ").Append(e.Message);
            if (e.StackTrace is { } stack) sb.AppendLine().Append(stack);
            if (e.InnerException is not null) sb.AppendLine().Append("--- inner ---");
        }
        Write(sb.ToString());
    }

    /// <summary>App and runtime versions, so a log line can be matched to a build.</summary>
    public static string Header()
    {
        var version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "unknown";
        return $"Danslicer {version} on .NET {Environment.Version} ({Environment.OSVersion})";
    }

    private static void Prune()
    {
        var cutoff = DateTime.Now.AddDays(-RetainDays);
        foreach (var file in System.IO.Directory.EnumerateFiles(Directory, "danslicer-*.log"))
        {
            try
            {
                if (File.GetLastWriteTime(file) < cutoff) File.Delete(file);
            }
            catch
            {
                // A file we cannot delete is left for next time.
            }
        }
    }
}
