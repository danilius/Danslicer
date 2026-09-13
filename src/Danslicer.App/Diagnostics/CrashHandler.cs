using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Danslicer.App.Views;
using Danslicer.Core.Diagnostics;

namespace Danslicer.App.Diagnostics;

/// <summary>
/// Catches every route an exception can take out of the app and writes it to
/// <see cref="CrashLog"/> before anything else happens (added 2026-09-08 after two crashes
/// whose stacks had to be recovered from the Windows event log):
/// <list type="bullet">
/// <item>UI-thread exceptions from event handlers — a key press, a button — are logged, the
/// app keeps running, and a dialog offers to save the project under a new name, because the
/// document may be half-changed by the command that failed.</item>
/// <item>Unobserved task exceptions from background work (generation, detection) are logged
/// and marked observed so they do not take the process down later.</item>
/// <item>Anything else unhandled is logged on its way out; the process still dies, but the
/// stack is on disk.</item>
/// </list>
/// </summary>
public static class CrashHandler
{
    private static bool _dialogOpen;

    public static void Install()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            CrashLog.WriteException("Unhandled (process terminating)",
                e.ExceptionObject as Exception ?? new Exception(e.ExceptionObject?.ToString() ?? "unknown"));
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            CrashLog.WriteException("Unobserved task exception", e.Exception);
            e.SetObserved();
        };
        Dispatcher.UIThread.UnhandledException += (_, e) =>
        {
            CrashLog.WriteException("UI thread exception", e.Exception);
            e.Handled = true;
            Dispatcher.UIThread.Post(() => ShowDialog(e.Exception));
        };
        CrashLog.Write($"Started: {CrashLog.Header()}");
    }

    private static async void ShowDialog(Exception exception)
    {
        if (_dialogOpen) return;
        var owner = (Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        if (owner is null) return;
        _dialogOpen = true;
        try
        {
            var saveAs = await CrashDialog.ShowAsync(owner, exception);
            if (saveAs && owner is MainWindow main) main.RequestSaveProjectAs();
        }
        catch (Exception ex)
        {
            CrashLog.WriteException("Crash dialog failed", ex);
        }
        finally
        {
            _dialogOpen = false;
        }
    }
}
