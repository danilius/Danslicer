using System.Text.Json;
using System.Text.Json.Serialization;
using Danslicer.Core.Supports;

namespace Danslicer.Core.Config;

/// <summary>
/// SpaceMouse navigation tuning. Sensitivities are multipliers on the app's base step sizes, so
/// 1.0 reproduces the untuned behaviour; inversion flags flip individual motion axes.
/// </summary>
public sealed class SpaceMouseConfig
{
    public float OrbitSensitivity { get; set; } = 1f;
    public float PanSensitivity { get; set; } = 1f;
    public float ZoomSensitivity { get; set; } = 1f;
    public bool InvertOrbitYaw { get; set; }
    public bool InvertOrbitPitch { get; set; }
    public bool InvertPanX { get; set; }
    public bool InvertPanY { get; set; }
    public bool InvertZoom { get; set; }
    /// <summary>Fraction of full deflection below which motion is ignored.</summary>
    public float Deadzone { get; set; } = 0.001f;
}

/// <summary>Viewport display tuning.</summary>
public sealed class ViewportConfig
{
    /// <summary>Overhang tint threshold, degrees from the vertical wall.</summary>
    public float OverhangAngleDegrees { get; set; } = 45f;

    /// <summary>Build-plate opacity when the camera is below it: 0 invisible, 1 fully opaque.</summary>
    public float PlateOpacityFromBelow { get; set; } = 0.3f;

    /// <summary>First overhang checker colour, "#RRGGBB".</summary>
    public string OverhangColorA { get; set; } = "#FACC26";

    /// <summary>Second overhang checker colour, "#RRGGBB".</summary>
    public string OverhangColorB { get; set; } = "#E61F1A";

    /// <summary>Edge length of the overhang checker squares, millimetres.</summary>
    public float OverhangCheckerSizeMm { get; set; } = 2f;
}

public enum PlacementMode
{
    AutoDrop,
    RaiseAbovePlate,
    Off,
}

/// <summary>Automatic vertical placement applied after object transform commits.</summary>
public sealed class PlacementConfig
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public PlacementMode Mode { get; set; } = PlacementMode.AutoDrop;
    public float HeightMm { get; set; }
}

/// <summary>Basic support generation geometry and placement settings, in millimetres/degrees.</summary>
public sealed record SupportConfig
{
    public float TipDiameter { get; set; } = 0.4f;
    public float ConeLength { get; set; } = 2f;
    public float BallDiameter { get; set; }
    public float PenetrationDepth { get; set; }

    public float TrunkDiameter { get; set; } = 1.2f;
    public float BranchDiameter { get; set; } = 1.2f;
    public float MemberAngleDegrees { get; set; } = 45f;
    public float TipMemberLength { get; set; } = 2f;
    public float MaxBranchLength { get; set; } = 8f;
    public bool PreferExistingTrunks { get; set; } = true;
    public float ExistingTrunkBranchRange { get; set; } = 8f;
    public float MiniSupportDiameter { get; set; } = 0.6f;
    public float MiniSupportTipDiameter { get; set; } = 0.25f;
    public float MiniSupportConeLength { get; set; } = 1f;
    public float MiniSupportMaxLength { get; set; } = 5f;
    public int MiniSupportMaxFanPerBranchEnd { get; set; } = 4;
    public float BaseGridPitch { get; set; } = 20f;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public SupportBaseShape BaseShape { get; set; } = SupportBaseShape.Disc;
    public float BaseDiameter { get; set; } = 4f;
    public float BaseHeight { get; set; } = 0.8f;
    public float BaseConeHeight { get; set; } = 2f;

    public float Spacing { get; set; } = 2.5f;
    public float IslandSpacingMm { get; set; } = 0.5f;
    public float OverhangAngleDegrees { get; set; } = 45f;
    public float MinIslandAreaMm2 { get; set; } = 0.1f;

    internal void Normalize()
    {
        TipDiameter = Positive(TipDiameter, 0.4f);
        ConeLength = Positive(ConeLength, 2f);
        BallDiameter = NonNegative(BallDiameter);
        PenetrationDepth = NonNegative(PenetrationDepth);
        TrunkDiameter = Positive(TrunkDiameter, 1.2f);
        BranchDiameter = Positive(BranchDiameter, 1.2f);
        MemberAngleDegrees = float.IsFinite(MemberAngleDegrees)
            ? Math.Clamp(MemberAngleDegrees, 1f, 89f) : 45f;
        TipMemberLength = Positive(TipMemberLength, 2f);
        MaxBranchLength = Positive(MaxBranchLength, 8f);
        ExistingTrunkBranchRange = Positive(ExistingTrunkBranchRange, 8f);
        MiniSupportDiameter = Positive(MiniSupportDiameter, 0.6f);
        MiniSupportTipDiameter = Positive(MiniSupportTipDiameter, 0.25f);
        MiniSupportConeLength = Positive(MiniSupportConeLength, 1f);
        MiniSupportMaxLength = Positive(MiniSupportMaxLength, 5f);
        MiniSupportMaxFanPerBranchEnd = Math.Max(1, MiniSupportMaxFanPerBranchEnd);
        BaseGridPitch = Positive(BaseGridPitch, 20f);
        if (!Enum.IsDefined(BaseShape)) BaseShape = SupportBaseShape.Disc;
        BaseDiameter = Positive(BaseDiameter, 4f);
        BaseHeight = NonNegative(BaseHeight);
        BaseConeHeight = NonNegative(BaseConeHeight);
        Spacing = Positive(Spacing, 2.5f);
        IslandSpacingMm = Positive(IslandSpacingMm, 0.5f);
        OverhangAngleDegrees = float.IsFinite(OverhangAngleDegrees)
            ? Math.Clamp(OverhangAngleDegrees, 0f, 90f) : 45f;
        MinIslandAreaMm2 = NonNegative(MinIslandAreaMm2);
    }

    private static float Positive(float value, float fallback) =>
        float.IsFinite(value) && value > 0 ? value : fallback;

    private static float NonNegative(float value) =>
        float.IsFinite(value) ? MathF.Max(0, value) : 0;
}

/// <summary>Saved placement of one window, in screen pixels.</summary>
public sealed class WindowStateConfig
{
    public int X { get; set; }
    public int Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public bool Maximized { get; set; }
    public double LeftPanelWidth { get; set; }
    public double RightPanelWidth { get; set; }
}

/// <summary>
/// User configuration persisted as JSON in the user profile. Unknown properties in the file are
/// ignored and missing ones keep their defaults, so the file survives version changes in both
/// directions. A corrupt or unreadable file yields defaults rather than an error: configuration
/// must never stop the app from starting.
/// </summary>
public sealed class UserConfig
{
    public SpaceMouseConfig SpaceMouse { get; set; } = new();
    public ViewportConfig Viewport { get; set; } = new();
    public PlacementConfig Placement { get; set; } = new();
    public SupportConfig Supports { get; set; } = new();

    /// <summary>Window placements keyed by a stable window name ("main", "preferences").</summary>
    public Dictionary<string, WindowStateConfig> Windows { get; set; } = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Danslicer", "config.json");

    public static UserConfig Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return new UserConfig();
            var config = JsonSerializer.Deserialize<UserConfig>(File.ReadAllText(path), JsonOptions)
                         ?? new UserConfig();
            // Explicit nulls from hand-edited or older files are treated like missing sections.
            config.SpaceMouse ??= new SpaceMouseConfig();
            config.Viewport ??= new ViewportConfig();
            config.Placement ??= new PlacementConfig();
            config.Supports ??= new SupportConfig();
            config.Supports.Normalize();
            config.Placement.HeightMm = float.IsFinite(config.Placement.HeightMm)
                ? MathF.Max(0, config.Placement.HeightMm)
                : 0;
            // Legacy Drop ignored its stored Raise height. In the toggle model it migrates to
            // enabled with zero offset; Raise and Off preserve their editable offset.
            if (config.Placement.Mode == PlacementMode.AutoDrop)
                config.Placement.HeightMm = 0;
            config.Windows ??= new Dictionary<string, WindowStateConfig>();
            return config;
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            return new UserConfig();
        }
    }

    public void Save(string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions));
    }
}
