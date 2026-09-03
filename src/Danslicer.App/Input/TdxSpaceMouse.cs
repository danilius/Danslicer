using System.Numerics;
using System.Runtime.Versioning;

namespace Danslicer.App.Input;

/// <summary>
/// SpaceMouse backend over the 3DxWare COM interface (<c>TDxInput.Device</c>), available on Windows
/// when the 3Dconnexion driver is installed. Late-bound through <c>dynamic</c> so no interop
/// assembly is needed and machines without the driver simply fail to connect. Must be created and
/// polled on an STA thread (Avalonia's UI thread qualifies).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class TdxSpaceMouse : ISixAxisInput
{
    private dynamic? _device;
    private dynamic? _sensor;

    public bool IsConnected { get; private set; }

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
        try
        {
            dynamic t = _sensor.Translation;
            dynamic r = _sensor.Rotation;
            // Rotation is an axis-angle pair; the angle carries the deflection magnitude.
            var motion = new SixAxisMotion(
                new Vector3((float)t.X, (float)t.Y, (float)t.Z),
                new Vector3((float)r.X, (float)r.Y, (float)r.Z) * (float)r.Angle);
            return motion;
        }
        catch
        {
            // Driver went away mid-session: report idle and stay quiet.
            IsConnected = false;
            return default;
        }
    }

    public void Dispose()
    {
        try { _device?.Disconnect(); } catch { /* driver already gone */ }
        _sensor = null;
        _device = null;
        IsConnected = false;
    }
}
