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
        public bool TryConnect() { Connections++; return IsConnected = true; }
        public SixAxisMotion Poll() => Motion;
        public IReadOnlyList<SixAxisButtonPress> DrainButtonPresses() { Drains++; return []; }
        public void Dispose() { Disposals++; IsConnected = false; }
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
