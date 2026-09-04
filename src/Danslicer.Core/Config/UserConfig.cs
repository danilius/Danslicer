using System.Text.Json;
using System.Text.Json.Serialization;
using Danslicer.Core.Printers;
using Danslicer.Core.Slicing;
using Danslicer.Core.Supports;
using Danslicer.Core.Supports.Generation;
using Danslicer.Core.Supports.Routing;

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

/// <summary>Which viewport render pipeline draws the scene.</summary>
public enum RenderPathMode
{
    /// <summary>The original forward renderer, kept as the fallback and user option.</summary>
    Classic,
    /// <summary>G-buffer pipeline with composite lighting, cavity, outlines and FXAA.</summary>
    Deferred,
}

/// <summary>Surface shading used by the deferred composite pass.</summary>
public enum ViewportShadingMode
{
    Studio,
    MatCapClay,
    MatCapMetal,
    MatCapPearl,
}

/// <summary>Viewport display tuning.</summary>
public sealed class ViewportConfig
{
    /// <summary>Overhang tint threshold, degrees from the vertical wall.</summary>
    public float OverhangAngleDegrees { get; set; } = 45f;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    // Deferred by user decision D2 (2026-09-04) after on-screen verification; the renderer
    // still latches back to Classic for the session if the pipeline fails on a machine.
    public RenderPathMode RenderPath { get; set; } = RenderPathMode.Deferred;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ViewportShadingMode Shading { get; set; } = ViewportShadingMode.Studio;

    /// <summary>Screen-space ridge/valley shading (deferred path only).</summary>
    public bool CavityEnabled { get; set; } = true;

    /// <summary>Brightening applied to ridges, 0 disables.</summary>
    public float CavityRidgeStrength { get; set; } = 0.35f;

    /// <summary>Darkening applied to valleys, 0 disables.</summary>
    public float CavityValleyStrength { get; set; } = 0.7f;

    /// <summary>Cavity sample offset in physical pixels.</summary>
    public float CavityRadiusPixels { get; set; } = 1.5f;

    /// <summary>Object outlines from ID and depth discontinuities (deferred path only).</summary>
    public bool OutlinesEnabled { get; set; } = true;

    /// <summary>Outline blend strength, 0 to 1.</summary>
    public float OutlineStrength { get; set; } = 0.75f;

    /// <summary>Anti-aliasing on the final deferred image.</summary>
    public bool FxaaEnabled { get; set; } = true;

    /// <summary>Wireframe overlay on visible objects (works on both render paths).</summary>
    public bool WireframeEnabled { get; set; }

    /// <summary>The corner view cube (design 6.2).</summary>
    public bool ViewCubeEnabled { get; set; } = true;

    /// <summary>Build-plate opacity when the camera is below it: 0 invisible, 1 fully opaque.</summary>
    public float PlateOpacityFromBelow { get; set; } = 0.3f;

    /// <summary>First overhang checker colour, "#RRGGBB".</summary>
    public string OverhangColorA { get; set; } = "#FACC26";

    /// <summary>Second overhang checker colour, "#RRGGBB".</summary>
    public string OverhangColorB { get; set; } = "#E61F1A";

    /// <summary>Edge length of the overhang checker squares, millimetres.</summary>
    public float OverhangCheckerSizeMm { get; set; } = 2f;

    /// <summary>Last procedural shape selected in the support preset editor.</summary>
    public string SupportPresetPreviewSample { get; set; } = "Overhang table / bridge";

    /// <summary>Viewport-only support presentation. This never changes slice geometry.</summary>
    public SupportDisplayConfig SupportDisplay { get; set; } = new();

    internal void Normalize()
    {
        if (!Enum.IsDefined(RenderPath)) RenderPath = RenderPathMode.Deferred;
        if (!Enum.IsDefined(Shading)) Shading = ViewportShadingMode.Studio;
        CavityRidgeStrength = Clamp(CavityRidgeStrength, 0f, 4f, 0.35f);
        CavityValleyStrength = Clamp(CavityValleyStrength, 0f, 4f, 0.7f);
        CavityRadiusPixels = Clamp(CavityRadiusPixels, 0.5f, 8f, 1.5f);
        OutlineStrength = Clamp(OutlineStrength, 0f, 1f, 0.75f);
    }

    private static float Clamp(float value, float min, float max, float fallback) =>
        float.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;
}

public enum SupportDisplayMode
{
    Full,
    ContactPoints,
    Lines,
    Tips,
    Transparent,
}

/// <summary>
/// Persisted support viewport presentation. Element switches intentionally apply only to the
/// Full and Transparent modes; the focused Contact points, Lines and Tips modes have fixed scope.
/// </summary>
public sealed record SupportDisplayConfig
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public SupportDisplayMode Mode { get; init; } = SupportDisplayMode.Full;
    public bool ShowContactPointsInTransparent { get; init; } = true;
    public bool ShowTips { get; init; } = true;
    public bool ShowMiniSupports { get; init; } = true;
    public bool ShowBranches { get; init; } = true;
    public bool ShowTrunks { get; init; } = true;
    public bool ShowBases { get; init; } = true;
    public bool ShowBracing { get; init; } = true;

    internal SupportDisplayConfig Normalize() => Enum.IsDefined(Mode)
        ? this
        : this with { Mode = SupportDisplayMode.Full };
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
    public float MiniSupportMaxAngleDegrees { get; set; } = 75f;
    public int MiniSupportMaxFanPerBranchEnd { get; set; } = 4;
    /// <summary>
    /// Maximum distance between regular contacts for density-based mini-tip clustering.
    /// The 1.25 mm default is half the default 2.5 mm placement spacing.
    /// </summary>
    public float MiniSupportClusterDistance { get; set; } = 1.25f;
    public bool RefusedTipsFallBackToMini { get; set; }
    public float MiniIslandMaxAreaMm2 { get; set; } = 0.1f;
    public bool UseBaseGrid { get; set; } = true;
    public float BaseGridPitch { get; set; } = 6f;

    public bool ReinforceEnabled { get; set; }
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ReinforceSeedSelector ReinforceSeedSelector { get; set; } =
        ReinforceSeedSelector.LowestPointOfObject;
    public int ReinforceCount { get; set; } = 3;
    public float ReinforceRingRadius { get; set; } = 2f;
    public float ReinforceRingDiameterMultiplier { get; set; } = 1.25f;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public SupportBaseShape BaseShape { get; set; } = SupportBaseShape.Disc;
    public float BaseDiameter { get; set; } = 4f;
    public float BaseHeight { get; set; } = 0.8f;
    public float BaseConeHeight { get; set; } = 2f;

    public float Spacing { get; set; } = 2.5f;
    public float IslandSpacingMm { get; set; } = 0.5f;
    public float OverhangAngleDegrees { get; set; } = 45f;
    public float MinIslandAreaMm2 { get; set; } = 0.1f;

    /// <summary>
    /// Maximum angle from straight down for a contact to remain eligible (see
    /// <see cref="ContactFaceFilter"/>). 0° = only perfectly horizontal undersides; 90° (default)
    /// = every downward-facing face, i.e. today's behaviour.
    /// </summary>
    public float MaxContactFaceAngleDegrees { get; set; } = 90f;

    /// <summary>
    /// When true, additionally requires an unobstructed straight-down line of sight from the
    /// contact to the plate (see <see cref="ContactFaceFilter"/>). Off by default.
    /// </summary>
    public bool RequireContactSeesPlate { get; set; }

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
        MiniSupportMaxAngleDegrees = float.IsFinite(MiniSupportMaxAngleDegrees)
            ? Math.Clamp(MiniSupportMaxAngleDegrees, 1f, 89f) : 75f;
        MiniSupportMaxFanPerBranchEnd = Math.Max(1, MiniSupportMaxFanPerBranchEnd);
        MiniSupportClusterDistance = Positive(MiniSupportClusterDistance, 1.25f);
        BaseGridPitch = Positive(BaseGridPitch, 6f);
        if (!Enum.IsDefined(ReinforceSeedSelector))
            ReinforceSeedSelector = ReinforceSeedSelector.LowestPointOfObject;
        ReinforceCount = Math.Max(1, ReinforceCount);
        ReinforceRingRadius = Positive(ReinforceRingRadius, 2f);
        ReinforceRingDiameterMultiplier = Positive(ReinforceRingDiameterMultiplier, 1.25f);
        if (!Enum.IsDefined(BaseShape)) BaseShape = SupportBaseShape.Disc;
        BaseDiameter = Positive(BaseDiameter, 4f);
        BaseHeight = NonNegative(BaseHeight);
        BaseConeHeight = NonNegative(BaseConeHeight);
        Spacing = Positive(Spacing, 2.5f);
        IslandSpacingMm = Positive(IslandSpacingMm, 0.5f);
        OverhangAngleDegrees = float.IsFinite(OverhangAngleDegrees)
            ? Math.Clamp(OverhangAngleDegrees, 0f, 90f) : 45f;
        MinIslandAreaMm2 = NonNegative(MinIslandAreaMm2);
        MaxContactFaceAngleDegrees = float.IsFinite(MaxContactFaceAngleDegrees)
            ? Math.Clamp(MaxContactFaceAngleDegrees, 0f, 90f) : 90f;
        var miniContactRadius = MiniSupportTipDiameter * 0.5f;
        var miniContactArea = MathF.PI * miniContactRadius * miniContactRadius;
        MiniIslandMaxAreaMm2 = MathF.Min(MinIslandAreaMm2,
            MathF.Max(miniContactArea, NonNegative(MiniIslandMaxAreaMm2)));
    }

    private static float Positive(float value, float fallback) =>
        float.IsFinite(value) && value > 0 ? value : fallback;

    private static float NonNegative(float value) =>
        float.IsFinite(value) ? MathF.Max(0, value) : 0;
}

/// <summary>
/// A named snapshot of the complete support-generation settings bundle. The record version is
/// independent of the user-config schema so recipes and richer profile metadata can be added
/// later without changing the meaning of existing snapshots.
/// </summary>
public sealed record SupportPreset
{
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;
    public string Name { get; set; } = "";
    public SupportConfig Settings { get; set; } = new();
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
    public const string CadCleanSupportPresetName = "CAD clean";
    public const string OrganicDenseSupportPresetName = "Organic dense";

    public SpaceMouseConfig SpaceMouse { get; set; } = new();
    public ViewportConfig Viewport { get; set; } = new();
    public PlacementConfig Placement { get; set; } = new();
    public SupportConfig Supports { get; set; } = new();
    public List<PrinterDefinition> Printers { get; set; } = CreateBuiltInPrinters();
    public List<ResinPreset> ResinPresets { get; set; } = CreateBuiltInResinPresets();
    public List<SupportPreset> SupportPresets { get; set; } = CreateBuiltInSupportPresets();
    public string ActiveSupportPresetName { get; set; } = CadCleanSupportPresetName;

    /// <summary>
    /// Window-level shortcut overrides keyed by stable action id. Defaults live in the App layer;
    /// keeping only differences here makes a fresh keymap empty and lets new defaults flow through.
    /// </summary>
    public Dictionary<string, string> KeymapOverrides { get; set; } = new();

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
            config.Viewport.SupportDisplay =
                (config.Viewport.SupportDisplay ?? new SupportDisplayConfig()).Normalize();
            config.Viewport.Normalize();
            config.Placement ??= new PlacementConfig();
            config.Supports ??= new SupportConfig();
            config.Supports.Normalize();
            config.NormalizePrinters();
            config.NormalizeResinPresets();
            config.NormalizeSupportPresets();
            config.Placement.HeightMm = float.IsFinite(config.Placement.HeightMm)
                ? MathF.Max(0, config.Placement.HeightMm)
                : 0;
            // Legacy Drop ignored its stored Raise height. In the toggle model it migrates to
            // enabled with zero offset; Raise and Off preserve their editable offset.
            if (config.Placement.Mode == PlacementMode.AutoDrop)
                config.Placement.HeightMm = 0;
            config.Windows ??= new Dictionary<string, WindowStateConfig>();
            config.KeymapOverrides ??= new Dictionary<string, string>();
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

    public PrinterDefinition? FindPrinter(string id) => Printers.FirstOrDefault(
        printer => string.Equals(printer.Id, id, StringComparison.OrdinalIgnoreCase));

    public PrinterDefinition AddPrinter(PrinterDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var normalized = definition.Normalize();
        if (FindPrinter(normalized.Id) is not null)
            normalized = normalized.CreateUserCopy(normalized.Name);
        Printers.Add(normalized);
        return normalized;
    }

    public bool ReplacePrinter(PrinterDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var index = Printers.FindIndex(printer =>
            string.Equals(printer.Id, definition.Id, StringComparison.OrdinalIgnoreCase));
        if (index < 0 || Printers[index].IsBuiltIn) return false;
        Printers[index] = definition.Normalize() with { IsBuiltIn = false };
        return true;
    }

    public bool DeletePrinter(string id)
    {
        var printer = FindPrinter(id);
        if (printer is null || printer.IsBuiltIn) return false;
        return Printers.Remove(printer);
    }

    public ResinPreset? FindResinPreset(string id) => ResinPresets.FirstOrDefault(
        preset => string.Equals(preset.Id, id, StringComparison.OrdinalIgnoreCase));

    public ResinPreset? FindResinPresetByName(string name) => ResinPresets.FirstOrDefault(
        preset => string.Equals(preset.Name, name, StringComparison.OrdinalIgnoreCase));

    public bool SaveResinPreset(string id, ResinSettings settings)
    {
        var preset = FindResinPreset(id);
        if (preset is null) return false;
        ResinPresets[ResinPresets.IndexOf(preset)] = preset with
        {
            Settings = settings.Normalize(),
        };
        return true;
    }

    public ResinPreset? SaveResinPresetAs(string name, ResinSettings settings)
    {
        var normalizedName = name.Trim();
        if (normalizedName.Length == 0 || FindResinPresetByName(normalizedName) is not null)
            return null;
        var preset = new ResinPreset
        {
            Name = normalizedName,
            Settings = settings.Normalize(),
        };
        ResinPresets.Add(preset);
        return preset;
    }

    public bool RenameResinPreset(string id, string name)
    {
        var preset = FindResinPreset(id);
        var normalizedName = name.Trim();
        if (preset is null || normalizedName.Length == 0 || ResinPresets.Any(other =>
                other.Id != preset.Id &&
                string.Equals(other.Name, normalizedName, StringComparison.OrdinalIgnoreCase)))
            return false;
        ResinPresets[ResinPresets.IndexOf(preset)] = preset with { Name = normalizedName };
        return true;
    }

    public bool DeleteResinPreset(string id)
    {
        var preset = FindResinPreset(id);
        return preset is not null && ResinPresets.Remove(preset);
    }

    public SupportPreset? FindSupportPreset(string name) => SupportPresets.FirstOrDefault(
        preset => string.Equals(preset.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Replaces the live settings with an independent copy of the named preset.</summary>
    public bool ApplySupportPreset(string name)
    {
        var preset = FindSupportPreset(name);
        if (preset is null) return false;
        Supports = preset.Settings with { };
        ActiveSupportPresetName = preset.Name;
        return true;
    }

    /// <summary>Overwrites an existing preset from the live settings.</summary>
    public bool SaveSupportPreset(string name)
    {
        var preset = FindSupportPreset(name);
        if (preset is null) return false;
        if (IsBuiltInSupportPreset(preset))
            return SaveSupportPresetAs(UniqueSupportPresetName($"{preset.Name} copy"));
        preset.Settings = Supports with { };
        ActiveSupportPresetName = preset.Name;
        return true;
    }

    /// <summary>
    /// Persists only the live grid fields into the active preset. Built-ins stay unchanged and
    /// instead produce an active user copy; unrelated live edits remain unsaved.
    /// </summary>
    public bool SaveActiveSupportPresetGrid()
    {
        var preset = FindSupportPreset(ActiveSupportPresetName);
        if (preset is null) return false;
        var saved = preset.Settings with
        {
            UseBaseGrid = Supports.UseBaseGrid,
            BaseGridPitch = Supports.BaseGridPitch,
        };
        if (IsBuiltInSupportPreset(preset))
        {
            var copyName = UniqueSupportPresetName($"{preset.Name} copy");
            SupportPresets.Add(new SupportPreset { Name = copyName, Settings = saved });
            ActiveSupportPresetName = copyName;
        }
        else
        {
            preset.Settings = saved;
        }
        return true;
    }

    public bool SaveSupportPresetAs(string name)
    {
        var normalizedName = name.Trim();
        if (normalizedName.Length == 0 || FindSupportPreset(normalizedName) is not null)
            return false;
        SupportPresets.Add(new SupportPreset
        {
            Name = normalizedName,
            Settings = Supports with { },
        });
        ActiveSupportPresetName = normalizedName;
        return true;
    }

    public bool RenameSupportPreset(string oldName, string newName)
    {
        var preset = FindSupportPreset(oldName);
        var normalizedName = newName.Trim();
        if (preset is null || normalizedName.Length == 0 || SupportPresets.Any(other =>
                !ReferenceEquals(other, preset) &&
                string.Equals(other.Name, normalizedName, StringComparison.OrdinalIgnoreCase)))
            return false;
        preset.Name = normalizedName;
        if (string.Equals(ActiveSupportPresetName, oldName, StringComparison.OrdinalIgnoreCase))
            ActiveSupportPresetName = normalizedName;
        return true;
    }

    public bool DeleteSupportPreset(string name)
    {
        var preset = FindSupportPreset(name);
        if (preset is null) return false;
        SupportPresets.Remove(preset);
        if (string.Equals(ActiveSupportPresetName, preset.Name, StringComparison.OrdinalIgnoreCase))
            ActiveSupportPresetName = SupportPresets.FirstOrDefault()?.Name ?? "";
        return true;
    }

    private static bool IsBuiltInSupportPreset(SupportPreset preset) =>
        string.Equals(preset.Name, CadCleanSupportPresetName, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(preset.Name, OrganicDenseSupportPresetName, StringComparison.OrdinalIgnoreCase);

    private string UniqueSupportPresetName(string baseName)
    {
        if (FindSupportPreset(baseName) is null) return baseName;
        for (var suffix = 2; ; suffix++)
        {
            var candidate = $"{baseName} {suffix}";
            if (FindSupportPreset(candidate) is null) return candidate;
        }
    }

    private static List<SupportPreset> CreateBuiltInSupportPresets() =>
    [
        new() { Name = CadCleanSupportPresetName, Settings = new SupportConfig() },
        new() { Name = OrganicDenseSupportPresetName, Settings = new SupportConfig() },
    ];

    private static List<PrinterDefinition> CreateBuiltInPrinters() => [PrinterDefinition.PhotonMonoX];

    private static List<ResinPreset> CreateBuiltInResinPresets() => [ResinPreset.Default];

    private void NormalizePrinters()
    {
        Printers ??= [];
        var normalized = new List<PrinterDefinition>();
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var printer in Printers)
        {
            if (printer is null) continue;
            var item = printer.Normalize();
            if (item.Id == PrinterDefinition.PhotonMonoXId || !ids.Add(item.Id)) continue;
            normalized.Add(item with { IsBuiltIn = false });
        }
        normalized.Insert(0, PrinterDefinition.PhotonMonoX);
        Printers = normalized;
    }

    private void NormalizeResinPresets()
    {
        ResinPresets ??= [];
        var normalized = new List<ResinPreset>();
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in ResinPresets)
        {
            if (candidate is null) continue;
            var preset = candidate.Normalize();
            if (!ids.Add(preset.Id) || !names.Add(preset.Name)) continue;
            normalized.Add(preset);
        }
        var defaultIndex = normalized.FindIndex(preset =>
            string.Equals(preset.Id, ResinPreset.DefaultId, StringComparison.OrdinalIgnoreCase));
        if (defaultIndex < 0)
        {
            normalized.Insert(0, ResinPreset.Default);
        }
        else if (defaultIndex > 0)
        {
            var defaultPreset = normalized[defaultIndex];
            normalized.RemoveAt(defaultIndex);
            normalized.Insert(0, defaultPreset);
        }
        ResinPresets = normalized;
    }

    private void NormalizeSupportPresets()
    {
        SupportPresets ??= [];
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = SupportPresets.Count - 1; i >= 0; i--)
        {
            var preset = SupportPresets[i];
            if (preset is null || string.IsNullOrWhiteSpace(preset.Name))
            {
                SupportPresets.RemoveAt(i);
                continue;
            }
            preset.Name = preset.Name.Trim();
            if (!names.Add(preset.Name))
            {
                SupportPresets.RemoveAt(i);
                continue;
            }
            preset.Version = Math.Max(1, preset.Version);
            preset.Settings ??= new SupportConfig();
            preset.Settings.Normalize();
        }

        foreach (var builtIn in CreateBuiltInSupportPresets())
            if (FindSupportPreset(builtIn.Name) is null)
                SupportPresets.Add(builtIn);

        var active = FindSupportPreset(ActiveSupportPresetName);
        ActiveSupportPresetName = active?.Name ?? CadCleanSupportPresetName;
    }
}
