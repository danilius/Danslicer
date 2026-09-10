using System.Collections.Concurrent;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Runtime.Versioning;

namespace Danslicer.App.Input;

/// <summary>
/// SpaceMouse backend over the 3DxWare COM interface (<c>TDxInput.Device</c>), available on Windows
/// when the 3Dconnexion driver is installed. Late-bound through <c>dynamic</c> so no interop
/// assembly is needed and machines without the driver simply fail to connect. Must be created and
/// polled on an STA thread (Avalonia's UI thread qualifies).
///
/// Motion is polled; buttons arrive as <c>_IKeyboardEvents</c> COM events on the STA message pump
/// and are queued for <see cref="DrainButtonPresses"/>. Key codes follow the driver's V3DKey
/// numbering (Menu 1, Fit 2, Top 3 ... Rotation 27), verified against the installed type library.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class TdxSpaceMouse : ISixAxisInput
{
    private dynamic? _device;
    private dynamic? _sensor;
    private dynamic? _keyboard;
    private IConnectionPoint? _keyboardConnection;
    private int _keyboardCookie;
    private KeyboardSink? _keyboardSink;
    private readonly ConcurrentQueue<SixAxisButtonPress> _pressed = new();

    public bool IsConnected { get; private set; }

    /// <summary>True when the button event hook is live; motion can work without it.</summary>
    public bool ButtonsConnected { get; private set; }

    /// <summary>
    /// The driver's own sensor refresh interval in seconds (<c>ISensor.Period</c>), read once at
    /// connect for diagnostics; <c>null</c> when the driver would not report it. A healthy device
    /// reports a few milliseconds; a large value means the driver itself is throttling us.
    /// </summary>
    public double? DriverPeriodSeconds { get; private set; }

    public bool TryConnect()
    {
        if (IsConnected) return true;
        try
        {
            var type = Type.GetTypeFromProgID("TDxInput.Device");
            if (type is null) return false;
            _device = Activator.CreateInstance(type);
            if (_device is null) return false;
            _device.Connect();
            _sensor = _device.Sensor;
            IsConnected = true;
            try { DriverPeriodSeconds = (double)_sensor!.Period; } catch { DriverPeriodSeconds = null; }
            ConnectKeyboard();
            return true;
        }
        catch
        {
            Dispose();
            return false;
        }
    }

    public SixAxisMotion Poll()
    {
        if (!IsConnected || _sensor is null) return default;
        object? t = null;
        object? r = null;
        try
        {
            // Each property read hands back a fresh COM object (Vector3D / AngleAxis). Release
            // them here rather than leaving them to the finalizer: STA wrappers released from the
            // finalizer thread must marshal back onto the UI thread, and at ~66 polls a second
            // that backlog is enough to stall the driver's updates into half-second steps.
            dynamic td = t = _sensor.Translation;
            dynamic rd = r = _sensor.Rotation;
            // Rotation is an axis-angle pair; the angle carries the deflection magnitude.
            return new SixAxisMotion(
                new Vector3((float)td.X, (float)td.Y, (float)td.Z),
                new Vector3((float)rd.X, (float)rd.Y, (float)rd.Z) * (float)rd.Angle);
        }
        catch
        {
            // Driver went away mid-session: report idle and stay quiet.
            IsConnected = false;
            return default;
        }
        finally
        {
            if (t is not null) Marshal.ReleaseComObject(t);
            if (r is not null) Marshal.ReleaseComObject(r);
        }
    }

    public IReadOnlyList<SixAxisButtonPress> DrainButtonPresses()
    {
        if (_pressed.IsEmpty) return Array.Empty<SixAxisButtonPress>();
        var list = new List<SixAxisButtonPress>();
        while (_pressed.TryDequeue(out var press)) list.Add(press);
        return list;
    }

    /// <summary>Maps a driver V3DKey code to a logical button.</summary>
    public static SixAxisButton MapButton(int rawCode) => rawCode switch
    {
        1 => SixAxisButton.Menu,
        2 => SixAxisButton.Fit,
        3 => SixAxisButton.ViewTop,
        4 => SixAxisButton.ViewLeft,
        5 => SixAxisButton.ViewRight,
        6 => SixAxisButton.ViewFront,
        7 => SixAxisButton.ViewBottom,
        8 => SixAxisButton.ViewBack,
        9 => SixAxisButton.RollClockwise,
        10 => SixAxisButton.RollCounterClockwise,
        11 => SixAxisButton.Iso1,
        12 => SixAxisButton.Iso2,
        13 => SixAxisButton.Key1,
        14 => SixAxisButton.Key2,
        15 => SixAxisButton.Key3,
        16 => SixAxisButton.Key4,
        23 => SixAxisButton.Escape,
        24 => SixAxisButton.Alt,
        25 => SixAxisButton.Shift,
        26 => SixAxisButton.Ctrl,
        27 => SixAxisButton.RotationLock,
        _ => SixAxisButton.Unknown,
    };

    private void ConnectKeyboard()
    {
        try
        {
            _keyboard = _device!.Keyboard;
            if (_keyboard is not IConnectionPointContainer container) return;
            var iid = typeof(IKeyboardEvents).GUID;
            container.FindConnectionPoint(ref iid, out var connection);
            if (connection is null) return;
            _keyboardSink = new KeyboardSink(this);
            connection.Advise(_keyboardSink, out _keyboardCookie);
            _keyboardConnection = connection;
            ButtonsConnected = true;
        }
        catch
        {
            // Buttons are an extra; motion still works without them.
            ButtonsConnected = false;
        }
    }

    private void OnKeyDown(int rawCode) => _pressed.Enqueue(new SixAxisButtonPress(MapButton(rawCode), rawCode));

    public void Dispose()
    {
        try { _keyboardConnection?.Unadvise(_keyboardCookie); } catch { /* driver already gone */ }
        _keyboardConnection = null;
        _keyboardSink = null;
        _keyboard = null;
        ButtonsConnected = false;
        try { _device?.Disconnect(); } catch { /* driver already gone */ }
        _sensor = null;
        _device = null;
        IsConnected = false;
    }

    /// <summary>
    /// The driver's keyboard event dispinterface. Declared locally (GUID and DISPIDs read from the
    /// installed type library) so no interop assembly is needed.
    /// </summary>
    [ComImport, Guid("6B6BB0A8-4491-40CF-B1A9-C15A801FE151"),
     InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
    private interface IKeyboardEvents
    {
        [DispId(1)] void KeyDown(int keyCode);
        [DispId(2)] void KeyUp(int keyCode);
    }

    [ComVisible(true)]
    private sealed class KeyboardSink(TdxSpaceMouse owner) : IKeyboardEvents
    {
        public void KeyDown(int keyCode) => owner.OnKeyDown(keyCode);

        public void KeyUp(int keyCode)
        {
            // Presses act on key-down; releases carry no binding yet.
        }
    }
}
