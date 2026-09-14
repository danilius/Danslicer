using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;

namespace Danslicer.App.Input;

/// <summary>Opt-in detailed trace. Disk writes stay off the input/render thread.</summary>
internal static class SpaceMouseDiagnostics
{
    private static readonly long Start = Stopwatch.GetTimestamp();
    private static readonly BlockingCollection<string>? Queue;
    public static bool Enabled => Queue is not null;
    private static long _dropped;

    static SpaceMouseDiagnostics()
    {
        var path = Environment.GetEnvironmentVariable("DANSLICER_SPACEMOUSE_LOG");
        if (string.IsNullOrWhiteSpace(path)) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            var writer = new StreamWriter(path, append: false, System.Text.Encoding.UTF8, 65536);
            Queue = new BlockingCollection<string>(20000);
            var worker = Task.Factory.StartNew(() =>
            {
                using (writer)
                {
                    writer.WriteLine($"START utc={DateTime.UtcNow:O} pid={Environment.ProcessId}");
                    var pending = 0;
                    while (!Queue.IsCompleted)
                    {
                        if (Queue.TryTake(out var line, 500)) { writer.WriteLine(line); pending++; }
                        else { writer.Flush(); pending = 0; }
                        if (pending >= 256) { writer.Flush(); pending = 0; }
                    }
                    writer.WriteLine($"END utc={DateTime.UtcNow:O} dropped={Interlocked.Read(ref _dropped)}");
                }
            }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            AppDomain.CurrentDomain.ProcessExit += (_, _) =>
            {
                Queue.CompleteAdding();
                try { worker.Wait(TimeSpan.FromSeconds(3)); } catch { }
            };
            Console.Error.WriteLine($"SpaceMouse detailed diagnostics: {path}");
        }
        catch (Exception ex) { Console.Error.WriteLine($"SpaceMouse diagnostics unavailable: {ex.Message}"); }
    }

    public static void Write(string kind, FormattableString data)
    {
        if (Queue is null || Queue.IsAddingCompleted) return;
        var line = Stopwatch.GetElapsedTime(Start).TotalMilliseconds.ToString("F3", CultureInfo.InvariantCulture)
            + " " + kind + " " + data.ToString(CultureInfo.InvariantCulture);
        try { if (!Queue.TryAdd(line)) Interlocked.Increment(ref _dropped); }
        catch (InvalidOperationException) { } // Process exit raced the last callback.
    }
}
