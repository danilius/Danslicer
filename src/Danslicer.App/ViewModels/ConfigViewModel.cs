using System.Runtime.CompilerServices;
using Danslicer.App.Configuration;
using Danslicer.Core.Config;

namespace Danslicer.App.ViewModels;

/// <summary>
/// Binds the config window to <see cref="AppConfig.Current"/>. Every set persists immediately;
/// consumers such as the viewport read the live config each frame or poll tick, so changes apply
/// while the window is open — that is what makes SpaceMouse tuning workable.
/// </summary>
public sealed class ConfigViewModel : ViewModelBase
{
    private SpaceMouseConfig SpaceMouse => AppConfig.Current.SpaceMouse;
    private ViewportConfig Viewport => AppConfig.Current.Viewport;

    /// <summary>Raised after every persisted change, so hosts can refresh what they draw.</summary>
    public event Action? Saved;

    private void Update(Action apply, [CallerMemberName] string? property = null)
    {
        apply();
        AppConfig.Save();
        OnPropertyChanged(property);
        Saved?.Invoke();
    }

    // Viewport

    public float OverhangAngleDegrees
    {
        get => Viewport.OverhangAngleDegrees;
        set => Update(() => Viewport.OverhangAngleDegrees = Math.Clamp(value, 10f, 89f));
    }

    public float PlateOpacityFromBelow
    {
        get => Viewport.PlateOpacityFromBelow;
        set => Update(() => Viewport.PlateOpacityFromBelow = Math.Clamp(value, 0f, 1f));
    }

    public Avalonia.Media.Color OverhangColorA
    {
        get => ToColor(Viewport.OverhangColorA, Avalonia.Media.Color.FromRgb(0xFA, 0xCC, 0x26));
        set => Update(() => Viewport.OverhangColorA = ToHex(value));
    }

    public Avalonia.Media.Color OverhangColorB
    {
        get => ToColor(Viewport.OverhangColorB, Avalonia.Media.Color.FromRgb(0xE6, 0x1F, 0x1A));
        set => Update(() => Viewport.OverhangColorB = ToHex(value));
    }

    private static Avalonia.Media.Color ToColor(string? hex, Avalonia.Media.Color fallback)
    {
        var v = AppConfig.ParseColor(hex, new System.Numerics.Vector3(fallback.R, fallback.G, fallback.B) / 255f);
        return Avalonia.Media.Color.FromRgb((byte)(v.X * 255f + 0.5f), (byte)(v.Y * 255f + 0.5f), (byte)(v.Z * 255f + 0.5f));
    }

    private static string ToHex(Avalonia.Media.Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    public float OverhangCheckerSizeMm
    {
        get => Viewport.OverhangCheckerSizeMm;
        set => Update(() => Viewport.OverhangCheckerSizeMm = Math.Clamp(value, 0.5f, 20f));
    }

    // SpaceMouse

    public float SpaceMouseOrbitSensitivity
    {
        get => SpaceMouse.OrbitSensitivity;
        set => Update(() => SpaceMouse.OrbitSensitivity = Math.Clamp(value, 0.02f, 4f));
    }

    public float SpaceMousePanSensitivity
    {
        get => SpaceMouse.PanSensitivity;
        set => Update(() => SpaceMouse.PanSensitivity = Math.Clamp(value, 0.02f, 4f));
    }

    public float SpaceMouseZoomSensitivity
    {
        get => SpaceMouse.ZoomSensitivity;
        set => Update(() => SpaceMouse.ZoomSensitivity = Math.Clamp(value, 0.02f, 4f));
    }

    public bool SpaceMouseInvertOrbitYaw
    {
        get => SpaceMouse.InvertOrbitYaw;
        set => Update(() => SpaceMouse.InvertOrbitYaw = value);
    }

    public bool SpaceMouseInvertOrbitPitch
    {
        get => SpaceMouse.InvertOrbitPitch;
        set => Update(() => SpaceMouse.InvertOrbitPitch = value);
    }

    public bool SpaceMouseInvertPanX
    {
        get => SpaceMouse.InvertPanX;
        set => Update(() => SpaceMouse.InvertPanX = value);
    }

    public bool SpaceMouseInvertPanY
    {
        get => SpaceMouse.InvertPanY;
        set => Update(() => SpaceMouse.InvertPanY = value);
    }

    public bool SpaceMouseInvertZoom
    {
        get => SpaceMouse.InvertZoom;
        set => Update(() => SpaceMouse.InvertZoom = value);
    }

    public float SpaceMouseDeadzone
    {
        get => SpaceMouse.Deadzone;
        set => Update(() => SpaceMouse.Deadzone = Math.Clamp(value, 0f, 0.2f));
    }
}
