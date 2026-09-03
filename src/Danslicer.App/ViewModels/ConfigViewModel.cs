using System.Runtime.CompilerServices;
using Danslicer.App.Configuration;
using Danslicer.Core.Config;
using Danslicer.Core.Supports;

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
    private SupportConfig Supports => AppConfig.Current.Supports;

    public IReadOnlyList<SupportBaseShape> SupportBaseShapes { get; } =
        Enum.GetValues<SupportBaseShape>();

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

    // Supports

    public float SupportTipDiameter
    {
        get => Supports.TipDiameter;
        set => Update(() => Supports.TipDiameter = Clamp(value, 0.01f, 100f, 0.4f));
    }

    public float SupportConeLength
    {
        get => Supports.ConeLength;
        set => Update(() => Supports.ConeLength = Clamp(value, 0.01f, 100f, 2f));
    }

    public float SupportBallDiameter
    {
        get => Supports.BallDiameter;
        set => Update(() => Supports.BallDiameter = Clamp(value, 0f, 100f, 0f));
    }

    public float SupportPenetrationDepth
    {
        get => Supports.PenetrationDepth;
        set => Update(() => Supports.PenetrationDepth = Clamp(value, 0f, 100f, 0f));
    }

    public float SupportTrunkDiameter
    {
        get => Supports.TrunkDiameter;
        set => Update(() => Supports.TrunkDiameter = Clamp(value, 0.01f, 100f, 1.2f));
    }

    public float SupportBranchDiameter
    {
        get => Supports.BranchDiameter;
        set => Update(() => Supports.BranchDiameter = Clamp(value, 0.01f, 100f, 1.2f));
    }

    public float SupportMemberAngleDegrees
    {
        get => Supports.MemberAngleDegrees;
        set => Update(() => Supports.MemberAngleDegrees = Clamp(value, 1f, 89f, 45f));
    }

    public float SupportTipMemberLength
    {
        get => Supports.TipMemberLength;
        set => Update(() => Supports.TipMemberLength = Clamp(value, 0.01f, 100f, 2f));
    }

    public float SupportMaxBranchLength
    {
        get => Supports.MaxBranchLength;
        set => Update(() => Supports.MaxBranchLength = Clamp(value, 0.01f, 1000f, 8f));
    }

    public float SupportBaseGridPitch
    {
        get => Supports.BaseGridPitch;
        set => Update(() => Supports.BaseGridPitch = Clamp(value, 0.01f, 1000f, 20f));
    }

    public SupportBaseShape SupportBaseShapeValue
    {
        get => Supports.BaseShape;
        set => Update(() => Supports.BaseShape = Enum.IsDefined(value) ? value : SupportBaseShape.Disc);
    }

    public float SupportBaseDiameter
    {
        get => Supports.BaseDiameter;
        set => Update(() => Supports.BaseDiameter = Clamp(value, 0.01f, 100f, 4f));
    }

    public float SupportBaseHeight
    {
        get => Supports.BaseHeight;
        set => Update(() => Supports.BaseHeight = Clamp(value, 0f, 100f, 0.8f));
    }

    public float SupportBaseConeHeight
    {
        get => Supports.BaseConeHeight;
        set => Update(() => Supports.BaseConeHeight = Clamp(value, 0f, 100f, 2f));
    }

    public float SupportSpacing
    {
        get => Supports.Spacing;
        set => Update(() => Supports.Spacing = Clamp(value, 0.01f, 1000f, 2.5f));
    }

    public float SupportIslandSpacingMm
    {
        get => Supports.IslandSpacingMm;
        set => Update(() => Supports.IslandSpacingMm = Clamp(value, 0.01f, 1000f, 0.5f));
    }

    public float SupportOverhangAngleDegrees
    {
        get => Supports.OverhangAngleDegrees;
        set => Update(() => Supports.OverhangAngleDegrees = Clamp(value, 0f, 90f, 45f));
    }

    public float SupportMinIslandAreaMm2
    {
        get => Supports.MinIslandAreaMm2;
        set => Update(() => Supports.MinIslandAreaMm2 = Clamp(value, 0f, 1_000_000f, 0.1f));
    }

    private static float Clamp(float value, float minimum, float maximum, float fallback) =>
        float.IsFinite(value) ? Math.Clamp(value, minimum, maximum) : fallback;

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
