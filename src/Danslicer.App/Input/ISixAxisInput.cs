using System.Numerics;

namespace Danslicer.App.Input;

/// <summary>
/// One reading from a six-axis device. Axes follow the 3Dconnexion convention seen from the user:
/// translation X right, Y up, Z toward the user; rotation is the axis-angle product, so tilting the
/// cap forward is +X, twisting it is +Y, rolling it sideways is +Z. Units are driver-scaled and
/// roughly -1..1 per axis at full deflection; zero means the cap is at rest.
/// </summary>
public readonly record struct SixAxisMotion(Vector3 Translation, Vector3 Rotation)
{
    public bool IsZero => Translation == Vector3.Zero && Rotation == Vector3.Zero;
}

/// <summary>
/// Logical buttons of a six-axis device, named after the 3Dconnexion V3DKey standard set. A
/// SpaceMouse Pro carries Menu, Fit, T, R, F, Roll+, 1-4, Esc, Alt, Shift, Ctrl and Rotation;
/// T/R/F send Top/Right/Front and their driver-modified variants send Bottom/Left/Back.
/// </summary>
public enum SixAxisButton
{
    Unknown,
    Menu,
    Fit,
    ViewTop,
    ViewLeft,
    ViewRight,
    ViewFront,
    ViewBottom,
    ViewBack,
    RollClockwise,
    RollCounterClockwise,
    Iso1,
    Iso2,
    Key1,
    Key2,
    Key3,
    Key4,
    Escape,
    Alt,
    Shift,
    Ctrl,
    RotationLock,
}

/// <summary>A button press, keeping the raw device code so unmapped buttons can be reported.</summary>
public readonly record struct SixAxisButtonPress(SixAxisButton Button, int RawCode);

/// <summary>
/// A six-axis input device, polled by the viewport at frame rate. Implementations must be cheap to
/// poll and must return zeros rather than throw when the device goes away.
/// </summary>
public interface ISixAxisInput : IDisposable
{
    bool IsConnected { get; }

    /// <summary>Attempts to connect. Safe to call when the driver or device is absent.</summary>
    bool TryConnect();

    /// <summary>Current deflection, or zeros when idle or disconnected.</summary>
    SixAxisMotion Poll();

    /// <summary>
    /// Removes and returns the button presses received since the last call, oldest first. Empty
    /// when idle, disconnected, or the backend cannot deliver buttons.
    /// </summary>
    IReadOnlyList<SixAxisButtonPress> DrainButtonPresses();
}
