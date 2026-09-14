using System.Numerics;
using Danslicer.App.Input;

namespace Danslicer.Tests;

public sealed class SixAxisSessionTests
{
    private sealed class Device : ISixAxisInput
    {
        public bool IsConnected { get; private set; }
        public int Connections, Disposals, Drains;
        public SixAxisMotion Motion;
        public bool Available = true;
        public bool FailPoll;
        public bool TryConnect() { Connections++; return IsConnected = Available; }
        public SixAxisMotion Poll()
        {
            if (FailPoll) { IsConnected = false; return default; }
            return Motion;
        }
        public IReadOnlyList<SixAxisButtonPress> DrainButtonPresses() { Drains++; return []; }
        public void Dispose() { Disposals++; IsConnected = false; }
    }

    private sealed class Clock : TimeProvider
    {
        public long Milliseconds;
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => Milliseconds;
    }

    [Fact]
    public void AbsentDriverRetriesAtBoundedRateAndRecoversWithoutActivation()
    {
        var clock = new Clock();
        var device = new Device { Available = false };
        var session = new SixAxisSession(() => device, clock);
        var owner = new object();
        session.Attach(owner);
        Assert.Null(session.Acquire(owner));
        for (var i = 1; i < 2000; i++)
        {
            clock.Milliseconds = i;
            Assert.Null(session.Acquire(owner));
        }
        Assert.Equal(1, device.Connections);
        device.Available = true;
        clock.Milliseconds = 2000;
        Assert.Same(device, session.Acquire(owner));
        session.Poll(owner, 0);
        device.Motion = new(new(10, 0, 0), Vector3.Zero);
        Assert.Equal(device.Motion, session.Poll(owner, 0));
        session.Detach(owner);
    }

    [Fact]
    public void PollFailureReplacesDeviceAndWaitsForNeutralBeforeResuming()
    {
        var clock = new Clock();
        var original = new Device();
        var replacement = new Device { Motion = new(new(10, 0, 0), Vector3.Zero) };
        var created = 0;
        var session = new SixAxisSession(() => created++ == 0 ? original : replacement, clock);
        var owner = new object();
        session.Attach(owner);
        session.Acquire(owner);
        session.Poll(owner, 0);
        original.FailPoll = true;
        Assert.True(session.Poll(owner, 0).IsZero);
        clock.Milliseconds = 2000;
        Assert.Same(replacement, session.Acquire(owner));
        Assert.Equal(1, original.Disposals);
        Assert.True(session.Poll(owner, 0).IsZero);
        replacement.Motion = default;
        session.Poll(owner, 0);
        replacement.Motion = new(new(20, 0, 0), Vector3.Zero);
        Assert.Equal(replacement.Motion, session.Poll(owner, 0));
        session.Detach(owner);
        clock.Milliseconds = 2001;
        session.Attach(owner);
        Assert.Same(replacement, session.Acquire(owner)); // Final detach resets retry delay.
        session.Detach(owner);
    }

    [Fact]
    public void MotionRateTracksElapsedTimeAndCapsLongStalls()
    {
        var clock = new Clock();
        var timing = new SixAxisMotionTiming(clock);
        Assert.Equal(1f, timing.NextScale());
        var totalScale = 0f;
        foreach (var interval in new[] { 10, 20, 12, 18, 30 })
        {
            clock.Milliseconds += interval;
            totalScale += timing.NextScale();
        }
        Assert.Equal(6f, totalScale, 5); // Same distance as six regular 15 ms ticks.
        clock.Milliseconds += 1000;
        Assert.Equal(1f, timing.NextScale());
        timing.Reset();
        clock.Milliseconds += 5000;
        Assert.Equal(1f, timing.NextScale());
    }

    [Fact]
    public void ClosingEditorKeepsConnectionAndRejectsHeldRotationUntilNeutral()
    {
        var device = new Device();
        var session = new SixAxisSession(() => device);
        var main = new object(); var editor = new object();
        session.Attach(main); session.Attach(editor);
        Assert.Same(device, session.Acquire(main));
        Assert.True(session.Poll(main, 0.01f).IsZero);
        device.Motion = new(Vector3.Zero, new(0, 100, 0));
        Assert.Equal(device.Motion, session.Poll(main, 0.01f));
        session.Release(main);
        Assert.Same(device, session.Acquire(editor));
        Assert.True(session.Poll(main, 0.01f).IsZero);
        Assert.True(session.Poll(editor, 0.01f).IsZero);
        device.Motion = default;
        session.Poll(editor, 0.01f);
        device.Motion = new(Vector3.Zero, new(0, 100, 0));
        Assert.Equal(device.Motion, session.Poll(editor, 0.01f));
        session.Detach(editor); // Close while the last reading is still rotation.
        Assert.Same(device, session.Acquire(main));
        for (var i = 0; i < 1000; i++) Assert.True(session.Poll(main, 0.01f).IsZero);
        device.Motion = new(new(0.001f), new(0.001f)); // Within configured neutral deadzone.
        Assert.True(session.Poll(main, 0.01f).IsZero);
        device.Motion = new(Vector3.Zero, new(0, 20, 0));
        for (var i = 0; i < 100; i++) Assert.Equal(device.Motion, session.Poll(main, 0.01f));
        Assert.Equal(1, device.Connections);
        Assert.Equal(0, device.Disposals);
        session.Detach(main);
        Assert.Equal(1, device.Disposals);
    }

    [Fact]
    public void RepeatedActivationDoesNotInterruptHeldInputAndOldDetachDoesNotClearNewOwner()
    {
        var device = new Device(); var session = new SixAxisSession(() => device);
        var main = new object(); var editor = new object();
        session.Attach(main); session.Attach(editor);
        session.Acquire(editor); session.Poll(editor, 0);
        session.Acquire(main); session.Poll(main, 0);
        session.Detach(editor);
        device.Motion = new(new(5, 0, 0), Vector3.Zero);
        session.Acquire(main);
        Assert.Equal(device.Motion, session.Poll(main, 0));
        Assert.Equal(1, device.Connections);
        session.Detach(main);
    }
}
