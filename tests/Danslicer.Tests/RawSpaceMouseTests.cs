using System.Buffers.Binary;
using System.Numerics;
using System.Runtime.InteropServices;
using Danslicer.App.Input;

namespace Danslicer.Tests;

public sealed class RawSpaceMouseTests(Xunit.Abstractions.ITestOutputHelper output)
{
    private sealed class Clock : TimeProvider
    {
        public long Milliseconds;
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => Milliseconds;
    }

    private static byte[] Axes(byte kind, params short[] values)
    {
        var bytes = new byte[1 + values.Length * 2];
        bytes[0] = kind;
        for (var i = 0; i < values.Length; i++) BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(1 + i * 2), values[i]);
        return bytes;
    }

    [Fact]
    public void SplitAndCombinedReportsGiveSameSignedScreenAxes()
    {
        var split = new RawSpaceMouseState();
        var combined = new RawSpaceMouseState();
        split.Receive(1, 0xC62B, Axes(1, 350, -175, 70));
        split.Receive(1, 0xC62B, Axes(2, -70, 175, -350));
        combined.Receive(1, 0xC62B, Axes(1, 350, -175, 70, -70, 175, -350));
        Assert.Equal(new SixAxisMotion(new(1, -0.2f, -0.5f), new(-0.2f, 1, 0.5f)), split.Poll());
        Assert.Equal(split.Poll(), combined.Poll());
    }

    [Fact]
    public void BatchedReportsCoalesceToLatestValuesWithoutReplayingBacklog()
    {
        var state = new RawSpaceMouseState();
        var reports = Axes(1, 100, 0, 0).Concat(Axes(2, 0, 0, 100)).Concat(Axes(1, 200, 0, 0)).ToArray();
        Assert.True(state.ReceiveBatch(1, 0xC62B, reports, 7, 3));
        Assert.Equal(3, state.MotionReports);
        Assert.Equal(200f / 350, state.Poll().Translation.X);
        Assert.Equal(-100f / 350, state.Poll().Rotation.Y);
        Assert.Equal(state.Poll(), state.Poll());
        state.Receive(1, 0xC62B, Axes(1, 0, 0, 0, 0, 0, 0));
        Assert.True(state.Poll().IsZero);
    }

    [Fact]
    public void AcceptedBatchWakesOnceWithCompleteAxesAndStopsWakeToo()
    {
        var wakes = 0;
        var clock = new Clock();
        var state = new RawSpaceMouseState(clock, () => wakes++);
        var reports = Axes(1, 100, 0, 0).Concat(Axes(2, 0, 0, 100)).ToArray();
        state.ReceiveBatch(1, 0xC62B, reports, 7, 2);
        Assert.Equal(1, wakes);
        Assert.NotEqual(Vector3.Zero, state.Poll().Translation);
        Assert.NotEqual(Vector3.Zero, state.Poll().Rotation);
        state.ReceiveBatch(1, 0xC62B, Axes(1, 0, 0, 0, 0, 0, 0), 13, 1);
        Assert.Equal(2, wakes);
        Assert.True(state.Poll().IsZero);
        state.ReceiveBatch(1, 0xC62B, new byte[] { 3, 2 }, 2, 1);
        Assert.Equal(3, wakes);
        state.ReceiveBatch(1, 0xC62B, reports, 7, 3); // Truncated batch.
        state.ReceiveBatch(1, 0xC62B, new byte[] { 99, 0 }, 2, 1);
        Assert.Equal(3, wakes);
        state.SetForeground(false);
        state.ReceiveBatch(1, 0xC62B, reports, 7, 2);
        Assert.Equal(3, wakes);
    }

    [Fact]
    public void TruncatedAndOverflowingPacketsAreIgnoredWithoutChangingMotion()
    {
        var state = new RawSpaceMouseState();
        state.Receive(1, 0xC62B, Axes(1, 350, 0, 0));
        var motion = state.Poll();
        foreach (var bad in new byte[][] { [], [1], [2, 0, 0], [1, 0, 0, 0, 0, 0, 0, 0], [3], [0x1c, 0], [99, 0, 0] })
            Assert.False(state.Receive(1, 0xC62B, bad));
        Assert.False(state.ReceiveBatch(1, 0xC62B, new byte[7], uint.MaxValue, uint.MaxValue));
        Assert.False(state.ReceiveBatch(1, 0xC62B, new byte[7], 0, 1));
        Assert.False(state.ReceiveBatch(1, 0xC62B, new byte[7], 7, 2));
        Assert.Equal(motion, state.Poll());
        Assert.Equal(10, state.RejectedReports);
    }

    [Fact]
    public void TranslationAndRotationExpireIndependentlyAndButtonsCannotKeepMotionAlive()
    {
        var clock = new Clock();
        var state = new RawSpaceMouseState(clock);
        state.Receive(1, 0xC62B, Axes(1, 350, 0, 0, 0, 0, 350));
        clock.Milliseconds = 100;
        state.Receive(1, 0xC62B, Axes(2, 0, 0, 175));
        clock.Milliseconds = 151;
        state.Receive(1, 0xC62B, new byte[] { 3, 2 });
        Assert.Equal(new SixAxisMotion(Vector3.Zero, new(0, -0.5f, 0)), state.Poll());
        clock.Milliseconds = 251;
        Assert.True(state.Poll().IsZero);
        state.Receive(1, 0xC62B, Axes(1, 175, 0, 0));
        Assert.Equal(new SixAxisMotion(new(0.5f, 0, 0), Vector3.Zero), state.Poll());
    }

    [Fact]
    public void DevicesDoNotMixAxesAndRemovalClearsMotionAndQueuedButtons()
    {
        var state = new RawSpaceMouseState();
        state.Receive(1, 0xC62B, Axes(1, 350, 0, 0));
        state.Receive(2, 0xC632, Axes(2, 0, 0, 175));
        Assert.Equal(new SixAxisMotion(Vector3.Zero, new(0, -0.5f, 0)), state.Poll());
        state.Receive(1, 0xC62B, Axes(1, 0, 0, 0));
        Assert.Equal(-0.5f, state.Poll().Rotation.Y);
        state.Receive(2, 0xC632, new byte[] { 3, 2 });
        state.RemoveDevice(2);
        Assert.True(state.Poll().IsZero);
        Assert.Empty(state.DrainButtonPresses());
        state.Receive(2, 0xC632, Axes(1, 70, 0, 0)); // A reused handle is a fresh device.
        Assert.Equal(new SixAxisMotion(new(0.2f, 0, 0), Vector3.Zero), state.Poll());
        state.Receive(2, 0xC632, new byte[] { 3, 2 });
        Assert.Equal(SixAxisButton.Fit, Assert.Single(state.DrainButtonPresses()).Button);
    }

    [Fact]
    public void BackgroundMotionAndButtonsAreDiscardedAndHeldCapWaitsForReleaseOnReturn()
    {
        var clock = new Clock();
        var state = new RawSpaceMouseState(clock);
        state.Receive(1, 0xC62B, Axes(1, 350, 0, 0, 0, 0, 0));
        state.Receive(1, 0xC62B, new byte[] { 3, 2 });
        state.SetForeground(false);
        state.Receive(1, 0xC62B, Axes(1, 100, 0, 0));
        state.Receive(1, 0xC62B, new byte[] { 3, 2 });
        Assert.True(state.Poll().IsZero);
        Assert.Empty(state.DrainButtonPresses());
        state.SetForeground(true);
        state.Receive(1, 0xC62B, Axes(1, 100, 0, 0, 0, 0, 0));
        Assert.True(state.Poll().IsZero);
        clock.Milliseconds = 1000; // Silence must not masquerade as a release.
        Assert.True(state.Poll().IsZero);
        state.Receive(1, 0xC62B, Axes(1, 100, 0, 0, 0, 0, 0));
        Assert.True(state.Poll().IsZero);
        state.Receive(1, 0xC62B, Axes(1, 0, 0, 0, 0, 0, 0));
        Assert.True(state.Poll().IsZero);
        state.Receive(1, 0xC62B, Axes(1, 175, 0, 0, 0, 0, 0));
        Assert.Equal(0.5f, state.Poll().Translation.X);
    }

    [Fact]
    public void IdleFocusChangeDoesNotSwallowFirstGesture()
    {
        var state = new RawSpaceMouseState();
        state.SetForeground(false);
        state.SetForeground(true);
        state.Receive(1, 0xC62B, Axes(1, 175, 0, 0));
        Assert.Equal(0.5f, state.Poll().Translation.X);
    }

    [Theory]
    [InlineData(0xC62B, 1, SixAxisButton.Fit)]
    [InlineData(0xC632, 26, SixAxisButton.RotationLock)]
    [InlineData(0xC627, 10, SixAxisButton.Fit)]
    [InlineData(0xC625, 14, SixAxisButton.Fit)]
    public void LegacyButtonsUseDeviceMapAndFireOnlyOnDown(uint product, int bit, SixAxisButton expected)
    {
        var state = new RawSpaceMouseState();
        var report = new byte[5]; report[0] = 3;
        BinaryPrimitives.WriteUInt32LittleEndian(report.AsSpan(1), 1u << bit);
        state.Receive(1, product, report);
        state.Receive(1, product, report);
        Assert.Equal(expected, Assert.Single(state.DrainButtonPresses()).Button);
        state.Receive(1, product, new byte[] { 3, 0, 0, 0, 0 });
        state.Receive(1, product, report);
        Assert.Equal(expected, Assert.Single(state.DrainButtonPresses()).Button);
    }

    [Fact]
    public void NumberedButtonsTrackShortAndLongPressesIndependently()
    {
        var state = new RawSpaceMouseState();
        state.Receive(1, 0xC633, Axes(0x1c, 2, 3, 0, 0, 0, 0));
        state.Receive(1, 0xC633, Axes(0x1c, 2, 3, 3, 0, 0, 0));
        Assert.Equal(new[] { SixAxisButton.Fit, SixAxisButton.ViewTop }, state.DrainButtonPresses().Select(p => p.Button));
        state.Receive(1, 0xC633, Axes(0x1d, 3, 0, 0, 0, 0, 0));
        state.Receive(1, 0xC633, Axes(0x1d, 3, 0, 0, 0, 0, 0));
        Assert.Equal(SixAxisButton.ViewBottom, Assert.Single(state.DrainButtonPresses()).Button);
        state.Receive(1, 0xC633, Axes(0x1c, 0, 0, 0, 0, 0, 0));
        state.Receive(1, 0xC633, Axes(0x1c, 2, 0, 0, 0, 0, 0));
        Assert.Equal(SixAxisButton.Fit, Assert.Single(state.DrainButtonPresses()).Button);
    }

    [Fact]
    public void PaddedBitmaskDoesNotTreatPaddingAsExtraButtons()
    {
        var state = new RawSpaceMouseState();
        state.Receive(1, 0xC62B, new byte[] { 3, 2, 0, 0, 0, 255, 255, 255, 255, 0, 0, 0, 0 });
        Assert.Equal(SixAxisButton.Fit, Assert.Single(state.DrainButtonPresses()).Button);
    }

    [Fact]
    public void SignedExtremesDoNotOverflowDuringAxisConversion()
    {
        var state = new RawSpaceMouseState();
        state.Receive(1, 0xC62B, Axes(1, short.MinValue, short.MaxValue, short.MinValue));
        Assert.Equal(new Vector3(-32768f, 32768f, 32767f) / 350, state.Poll().Translation);
    }

    [Fact]
    public void NativeReceiverRegistersOnlyMultiAxisAndSurvivesBadInputAndRecreation()
    {
        if (!OperatingSystem.IsWindows()) return;
        List<string> messages = [];
        using var input = new RawInputSpaceMouse(messages.Add);
        Assert.True(input.TryConnect(), string.Join(Environment.NewLine, messages));
        Assert.True(input.TryConnect());
        var registrations = new Registration[64];
        uint count = (uint)registrations.Length;
        var read = GetRegisteredRawInputDevices(registrations, ref count, (uint)Marshal.SizeOf<Registration>());
        Assert.NotEqual(uint.MaxValue, read);
        var registration = Assert.Single(registrations.Take((int)read), d => d.Page == 1 && d.Usage == 8);
        Assert.NotEqual(0, registration.Window);
        Assert.Equal(0x100u, registration.Flags & 0x100u); // Windows omits DEVNOTIFY from readback.
        SendMessageW(registration.Window, 0xFF, 0, 0); // Invalid RAWINPUT handle exercises callback containment.
        Assert.True(input.IsConnected);
        Assert.Contains(messages, message => message.Contains("Raw Input input:"));
        input.Dispose();
        Assert.False(input.IsConnected);
        count = (uint)registrations.Length;
        read = GetRegisteredRawInputDevices(registrations, ref count, (uint)Marshal.SizeOf<Registration>());
        Assert.NotEqual(uint.MaxValue, read);
        Assert.DoesNotContain(registrations.Take((int)read), d => d.Page == 1 && d.Usage == 8);
        Assert.True(input.TryConnect(), string.Join(Environment.NewLine, messages));
        output.WriteLine(string.Join(Environment.NewLine, messages));
    }

    [Theory]
    [InlineData(2, 0x046D, 0xC62B, 1, 8, true)]
    [InlineData(2, 0x256F, 0xC632, 1, 8, true)]
    [InlineData(2, 0x046D, 0x1234, 1, 8, false)]
    [InlineData(2, 0x1234, 0xC62B, 1, 8, false)]
    [InlineData(2, 0x256F, 0xC632, 1, 6, false)]
    [InlineData(1, 0x256F, 0xC632, 1, 8, false)]
    public void FiltersOutUnrelatedHidAndKeyboardDevices(uint type, uint vendor, uint product, ushort page, ushort usage, bool expected)
    {
        if (OperatingSystem.IsWindows())
            Assert.Equal(expected, RawInputSpaceMouse.IsSpaceMouse(type, vendor, product, page, usage));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Registration { public ushort Page, Usage; public uint Flags; public nint Window; }
    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetRegisteredRawInputDevices([Out] Registration[] devices, ref uint count, uint size);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint SendMessageW(nint window, uint message, nuint wParam, nint lParam);
}
