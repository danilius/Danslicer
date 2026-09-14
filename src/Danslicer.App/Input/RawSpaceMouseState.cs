using System.Buffers.Binary;
using System.Numerics;

namespace Danslicer.App.Input;

/// <summary>Managed HID decoder and latest-sample store. All calls use the receiver's UI thread.</summary>
internal sealed class RawSpaceMouseState(TimeProvider? timeProvider = null, Action? inputAvailable = null)
{
    // Nominal device range, not a clamp: driver profiles can scale past this value.
    internal const float NominalRange = 350f;
    internal const double StaleAfterMs = 150;
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;
    private readonly Dictionary<nint, Device> _devices = [];
    private readonly Queue<SixAxisButtonPress> _presses = new();
    private nint _motionDevice;
    private bool _foreground = true;
    private bool _waitingForNeutral;
    public long MotionReports { get; private set; }
    public long RejectedReports { get; private set; }

    private sealed class Device
    {
        public Vector3 Translation, Rotation;
        public long? TranslationTime, RotationTime;
        public uint Buttons;
        public HashSet<int> ShortButtons = [], LongButtons = [];
    }

    public void SetForeground(bool foreground)
    {
        if (_foreground == foreground) return;
        var waitForNeutral = _waitingForNeutral || (!foreground &&
            _devices.Values.Any(d => d.Translation != Vector3.Zero || d.Rotation != Vector3.Zero));
        _foreground = foreground;
        Reset();
        _waitingForNeutral = waitForNeutral;
    }

    public void Reset()
    {
        _devices.Clear();
        _presses.Clear();
        _motionDevice = 0;
        _waitingForNeutral = false;
    }

    public void RemoveDevice(nint handle)
    {
        _devices.Remove(handle);
        _presses.Clear();
        if (_motionDevice == handle) { _motionDevice = 0; _waitingForNeutral = false; }
    }

    // RAWHID may contain several reports. Validate the complete batch before slicing it.
    public bool ReceiveBatch(nint handle, uint product, ReadOnlySpan<byte> payload, uint size, uint count)
    {
        if (size == 0 || count == 0 || (ulong)size * count > (ulong)payload.Length)
        {
            RejectedReports++;
            return false;
        }
        var accepted = false;
        for (var i = 0; i < (int)count; i++)
            accepted |= Receive(handle, product, payload.Slice(i * (int)size, (int)size));
        // Wake the compositor once after the complete batch, including a neutral stop or button.
        // RequestAnimationFrame alone can be throttled while the viewport has nothing to draw.
        if (accepted) inputAvailable?.Invoke();
        return accepted;
    }

    public bool Receive(nint handle, uint product, ReadOnlySpan<byte> report)
    {
        if (!_foreground || handle == 0) return false;
        if (report.IsEmpty) return Reject();
        var kind = report[0];
        var valid = kind switch
        {
            1 => report.Length == 7 || report.Length == 13,
            2 => report.Length >= 7,
            3 => report.Length >= 2, // Windows may pad reports to the collection's maximum size.
            0x1c or 0x1d => report.Length >= 3 && report.Length <= 13 && report.Length % 2 == 1,
            _ => false,
        };
        if (!valid) return Reject();
        if (!_devices.TryGetValue(handle, out var device)) _devices[handle] = device = new();
        var now = _clock.GetTimestamp();
        switch (kind)
        {
            case 1:
                device.Translation = ReadAxes(report[1..]);
                device.TranslationTime = now;
                if (report.Length == 13)
                {
                    device.Rotation = ReadAxes(report[7..]);
                    device.RotationTime = now;
                }
                MotionReports++;
                SelectMotionDevice(handle, device.Translation != Vector3.Zero ||
                    (report.Length == 13 && device.Rotation != Vector3.Zero));
                break;
            case 2:
                device.Rotation = ReadAxes(report[1..]);
                device.RotationTime = now;
                MotionReports++;
                SelectMotionDevice(handle, device.Rotation != Vector3.Zero);
                break;
            case 3:
                uint bits = 0;
                for (var i = 1; i < Math.Min(report.Length, 5); i++) bits |= (uint)report[i] << ((i - 1) * 8);
                var pressed = bits & ~device.Buttons;
                device.Buttons = bits;
                for (var bit = 0; bit < 32; bit++)
                    if ((pressed & (1u << bit)) != 0) Enqueue(SpaceMouseButtons.FromBit(bit, product));
                break;
            default:
                var previous = kind == 0x1c ? device.ShortButtons : device.LongButtons;
                HashSet<int> current = [];
                for (var i = 1; i < report.Length; i += 2)
                {
                    int code = BinaryPrimitives.ReadUInt16LittleEndian(report[i..]);
                    if (code == 0) continue;
                    if (kind == 0x1d) code = SpaceMouseButtons.FromLongPress(code);
                    if (current.Add(code) && !previous.Contains(code)) Enqueue(code);
                }
                if (kind == 0x1c) device.ShortButtons = current;
                else device.LongButtons = current;
                break;
        }
        return true;
    }

    private bool Reject() { RejectedReports++; return false; }

    private void SelectMotionDevice(nint handle, bool hasMotion)
    {
        // An idle second controller must not cancel the one currently in use.
        if (_motionDevice == 0 || hasMotion)
            _motionDevice = handle;
    }

    private static Vector3 ReadAxes(ReadOnlySpan<byte> axes)
    {
        float x = BinaryPrimitives.ReadInt16LittleEndian(axes);
        float y = BinaryPrimitives.ReadInt16LittleEndian(axes[2..]);
        float z = BinaryPrimitives.ReadInt16LittleEndian(axes[4..]);
        // HID uses tabletop axes. The viewport uses X right, Y up, Z toward the user.
        return new Vector3(x, -z, y) / NominalRange;
    }

    public SixAxisMotion Poll()
    {
        if (!_foreground || !_devices.TryGetValue(_motionDevice, out var device)) return default;
        var now = _clock.GetTimestamp();
        bool Fresh(long? timestamp) => timestamp is { } t && _clock.GetElapsedTime(t, now).TotalMilliseconds < StaleAfterMs;
        var translationFresh = Fresh(device.TranslationTime);
        var rotationFresh = Fresh(device.RotationTime);
        var motion = new SixAxisMotion(translationFresh ? device.Translation : Vector3.Zero,
            rotationFresh ? device.Rotation : Vector3.Zero);
        if (_waitingForNeutral)
        {
            // A timeout is not evidence of release. Wait for both fresh zero reports on return.
            if (translationFresh && rotationFresh && motion.IsZero) _waitingForNeutral = false;
            return default;
        }
        return motion;
    }

    private void Enqueue(int code)
    {
        if (code == 0) return;
        if (_presses.Count == 64) _presses.Dequeue();
        _presses.Enqueue(new(SpaceMouseButtons.Map(code), code));
    }

    public IReadOnlyList<SixAxisButtonPress> DrainButtonPresses()
    {
        if (_presses.Count == 0) return Array.Empty<SixAxisButtonPress>();
        var presses = _presses.ToArray();
        _presses.Clear();
        return presses;
    }
}
