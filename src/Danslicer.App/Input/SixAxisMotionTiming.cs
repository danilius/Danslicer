namespace Danslicer.App.Input;

/// <summary>Preserves the tuned 15 ms motion rate despite dispatcher jitter.</summary>
internal sealed class SixAxisMotionTiming(TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;
    private long? _lastTick;
    internal double LastElapsedMs { get; private set; }

    public float NextScale()
    {
        var now = _clock.GetTimestamp();
        var elapsedMs = _lastTick is { } last ? _clock.GetElapsedTime(last, now).TotalMilliseconds : 15;
        _lastTick = now;
        LastElapsedMs = elapsedMs;
        // After a missed run of frames, resume with one normal step, not a catch-up jump.
        // Ordinary frame intervals still integrate at the same rate on 30/60/120 Hz displays.
        return elapsedMs > 45 ? 1f : (float)Math.Max(elapsedMs / 15, 0);
    }

    public void Reset() => _lastTick = null;
}
