using System.Diagnostics;
using Danslicer.Core.Supports.Generation;
namespace Danslicer.Core;

/// <summary>Execution-local generation context, including deep geometry calls. Restored on every exit.</summary>
internal sealed class SupportGenerationMonitor : IDisposable
{
    private static readonly AsyncLocal<SupportGenerationMonitor?> Current = new();
    private readonly SupportGenerationMonitor? _previous;
    private readonly CancellationToken _token;
    private readonly IProgress<SupportGenerationProgress>? _progress;
    private long _lastReport;
    private double _fraction;
    private string? _stage;
    private int _total;
    private SupportGenerationMonitor(CancellationToken token, IProgress<SupportGenerationProgress>? progress)
    {
        _previous = Current.Value;
        _token = token.CanBeCanceled ? token : _previous?._token ?? default;
        _progress = progress ?? _previous?._progress;
        _token.ThrowIfCancellationRequested();
        Current.Value = this;
    }
    public static IDisposable Begin(CancellationToken token, IProgress<SupportGenerationProgress>? progress)
    {
        token.ThrowIfCancellationRequested();
        return new SupportGenerationMonitor(token, progress);
    }
    public static void Check() => Current.Value?._token.ThrowIfCancellationRequested();
    public static void Report(double fraction, string stage, int completed, int total)
    {
        Check();
        if (Current.Value is not { } context) return;
        fraction = Math.Clamp(fraction, context._fraction, 1);
        var now = Stopwatch.GetTimestamp();
        if (stage == context._stage && total == context._total && completed != total &&
            Stopwatch.GetElapsedTime(context._lastReport, now).TotalMilliseconds < 50) return;
        context._fraction = fraction; context._lastReport = now; context._stage = stage; context._total = total;
        context._progress?.Report(new(fraction, stage, completed, total));
        Check();
    }
    public void Dispose() => Current.Value = _previous;
}
