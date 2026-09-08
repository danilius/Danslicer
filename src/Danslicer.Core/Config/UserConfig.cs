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

/// <summary>Viewport-only rendering method for horizontal clip cross-sections.</summary>
public enum ClipCapStyle
{
    Sliced,
    Painted,
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

    /// <summary>Close visible horizontal clip cuts with viewport-only faces.</summary>
    public bool CapInterior { get; set; } = true;

    /// <summary>
    /// Sliced builds exact CPU cap geometry (<see cref="Danslicer.Core.Slicing.ClipCapBuilder"/>).
    /// Painted is a deferred-only screen-space technique that skips that CPU work entirely; on the
    /// Classic render path it has no screen-space equivalent, so it falls back to Sliced there
    /// (see <see cref="ClipCapPolicy"/>) rather than leaving the model uncapped.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ClipCapStyle CapStyle { get; set; } = ClipCapStyle.Painted;

    /// <summary>The corner view cube (design 6.2).</summary>
    public bool ViewCubeEnabled { get; set; } = true;

    /// <summary>
    /// On-screen size of the view cube, in DIP pixels before DPI scaling. Bounds mirror
    /// <c>ViewCube.MinSizePixels</c>/<c>MaxSizePixels</c> in Danslicer.Render, and this default
    /// mirrors its <c>DefaultSizePixels</c> (120), which Core cannot
    /// reference directly — keep the two in sync if either changes.
    /// </summary>
    public int ViewCubeSizePixels { get; set; } = 120;

    /// <summary>
    /// Build-plate opacity once the view grazes the plate or looks up from under it: 0 invisible,
    /// 1 fully opaque (no fade). The ramp between this and solid lives in <c>PlateFade</c>.
    /// </summary>
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
        if (!Enum.IsDefined(CapStyle)) CapStyle = ClipCapStyle.Painted;
        CavityRidgeStrength = Clamp(CavityRidgeStrength, 0f, 4f, 0.35f);
        CavityValleyStrength = Clamp(CavityValleyStrength, 0f, 4f, 0.7f);
        CavityRadiusPixels = Clamp(CavityRadiusPixels, 0.5f, 8f, 1.5f);
        OutlineStrength = Clamp(OutlineStrength, 0f, 1f, 0.75f);
        ViewCubeSizePixels = Math.Clamp(ViewCubeSizePixels, 48, 192);
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
    public bool ShowBranches { get; init; } = true;
    public bool ShowTrunks { get; init; } = true;
    public bool ShowBases { get; init; } = true;
    public bool ShowBracing { get; init; } = true;

    /// <summary>
    /// Draw elements the user hid individually (Support mode's H) as if they were visible. Not a
    /// setting and never persisted: <see cref="Danslicer.Core.Supports.SupportDisplayPolicy.ForWorkspace"/>
    /// turns it on for Layout, where a model and its supports are one object being arranged and
    /// a Support-mode working aid must not leave the arrangement looking wrong. The graph's own
    /// Hidden flags are untouched, so returning to Support mode restores exactly what was hidden.
    /// </summary>
    [JsonIgnore]
    public bool ShowHiddenElements { get; init; }

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

/// <summary>How the braces between one pair of trunks climb (SUPPORT-GEOMETRY-SPEC "Bracing").</summary>
public enum BracingPattern
{
    /// <summary>Each brace leaves the trunk the previous one arrived at, so the pair reads as a zigzag.</summary>
    Zigzag = 0,
    /// <summary>Every brace leaves the same trunk and leans the same way.</summary>
    Diagonal = 1,
}

/// <summary>Basic support generation geometry and placement settings, in millimetres/degrees.</summary>
public sealed record SupportConfig
{
    public float TipDiameter { get; set; } = 0.4f;
    public float ConeLength { get; set; } = 2f;
    public float BallDiameter { get; set; }
    public float PenetrationDepth { get; set; }
    public float TipNormalLeadInMm { get; set; } = 0.3f;

    public float TrunkDiameter { get; set; } = 1.2f;
    public float BranchDiameter { get; set; } = 1.2f;
    public float MemberAngleDegrees { get; set; } = 45f;
    public float TipMemberLength { get; set; } = 2f;
    public float MaxBranchLength { get; set; } = 8f;
    public bool PreferExistingTrunks { get; set; } = true;
    public float ExistingTrunkBranchRange { get; set; } = 8f;
    /// <summary>
    /// When true, manual placements route against models only and neither collide with nor reuse
    /// existing supports. Automatic generation is unaffected.
    /// </summary>
    public bool IndependentManualSupports { get; set; }
    /// <summary>
    /// Guided placement (line, polygon, edge) ignores the supports already in the document: it
    /// neither skips a tip near an existing one nor routes around existing members (user
    /// decision 2026-09-07: a second edge next to a supported one placed two tips). Off makes
    /// guided placement existing-aware, keeping <see cref="GuidedExistingClearanceMm"/> from
    /// existing tips and routing around existing supports.
    /// </summary>
    public bool GuidedIgnoreExistingSupports { get; set; } = true;
    /// <summary>Existing-aware guided placement: no guided tip lands closer than this to an existing tip.</summary>
    public float GuidedExistingClearanceMm { get; set; } = 2.5f;
    /// <summary>Densify (D): tips inserted between each pair of neighbouring selected tips.</summary>
    public int GuidedDensifyInsertions { get; set; } = 1;
    /// <summary>Thin (Shift+D): keep one tip in this many along each run of selected tips.</summary>
    public int GuidedThinKeepEvery { get; set; } = 2;

    // Parenting (J): SUPPORT-GEOMETRY-SPEC "Parenting". Zero means "use the Members value".
    /// <summary>How far a tip may reach to join a trunk when parented; 0 = <see cref="MaxBranchLength"/>.</summary>
    public float ParentingMaxBranchLength { get; set; }
    /// <summary>Steepest branch allowed when parented; 0 = <see cref="MemberAngleDegrees"/>.</summary>
    public float ParentingMaxBranchAngle { get; set; }
    /// <summary>How far around a tip the router looks for a trunk to join; 0 = <see cref="ExistingTrunkBranchRange"/>.</summary>
    public float ParentingTrunkRange { get; set; }
    /// <summary>A trunk left carrying fewer tips than this is re-routed once more with double range.</summary>
    public int ParentingMinTipsPerTrunk { get; set; } = 1;
    /// <summary>Re-route rounds with different seeds; the round with the fewest trunks wins.</summary>
    public int ParentingRounds { get; set; } = 3;
    /// <summary>
    /// How far the member leaving a cone may bend from the cone's axis when parented, degrees;
    /// 0 = <see cref="MemberAngleDegrees"/>. A cone on a leaning wall points outward, so a join
    /// sideways along the edge needs more than the member angle (user screen test 2026-09-08).
    /// </summary>
    public float ParentingMaxConeBend { get; set; }
    /// <summary>Branches one trunk may carry when parented; 0 = the growth rule's default (6).</summary>
    public int ParentingMaxBranchesPerTrunk { get; set; }
    /// <summary>
    /// Parenting builds a hierarchical tree — tips pair into junctions, junctions pair again,
    /// one trunk carries the lot (user direction 2026-09-08 from a reference image). Off, it
    /// joins each tip straight onto a trunk with the tree router instead.
    /// </summary>
    public bool ParentingHierarchical { get; set; } = true;
    /// <summary>
    /// Auto-parenting (user directive 2026-09-08): after any placement — T, a guided commit,
    /// densify — the new tips and the tips of existing supports within the trunk search range
    /// of a new tip are parented at once, as part of the placement's undo step. Off, supports
    /// stay single until J.
    /// </summary>
    public bool AutoParenting { get; set; } = true;
    // Bracing (K): SUPPORT-GEOMETRY-SPEC "Bracing" (user-approved 2026-09-09). Zero means "use
    // the Members value" where one exists.
    /// <summary>Brace after generation and after every parenting, inside that command's undo step.</summary>
    public bool AutoBracing { get; set; } = true;
    /// <summary>How the braces of one pair of trunks climb: alternating sides, or all one way.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public BracingPattern BracingPattern { get; set; } = BracingPattern.Zigzag;
    /// <summary>Member diameter of every brace; 0 = <see cref="BranchDiameter"/>.</summary>
    public float BracingDiameter { get; set; }
    /// <summary>The most a brace may lean from vertical, degrees; every rung is laid at exactly this lean.</summary>
    public float BracingAngleDegrees { get; set; } = 45f;
    /// <summary>Vertical pitch between the braces of one pair of trunks; 0 = continuous, each brace starts where the last ended.</summary>
    public float BracingSpacingMm { get; set; }
    /// <summary>No brace foot below this height above the plate; 0 = <see cref="MinBranchAttachHeightMm"/>.</summary>
    public float BracingLowestHeightMm { get; set; }
    /// <summary>Only trunks rising at least this far above the plate are braced.</summary>
    public float BracingMinSupportHeightMm { get; set; } = 20f;
    /// <summary>Largest horizontal gap between two trunk axes that a brace may span.</summary>
    public float BracingNeighbourDistanceMm { get; set; } = 10f;
    /// <summary>How many other trunks one trunk may be braced to.</summary>
    public int BracingMaxPartners { get; set; } = 3;
    /// <summary>
    /// A branch continuing a trunk upward within this lean from vertical counts as part of the
    /// trunk for bracing (user drawing 2026-09-09: braces climb the near-vertical members
    /// parenting leaves above a short trunk).
    /// </summary>
    public float BracingMaxStemLeanDegrees { get; set; } = 30f;
    /// <summary>
    /// Stems whose trunk surfaces are closer than this are one bundle for bracing: no braces
    /// inside it, and the row ties to its outer member (user decision 2026-09-09, from a cluster
    /// of five trunks). A gap between surfaces, so a field at the tip spacing is not a cluster.
    /// </summary>
    public float BracingClusterGapMm { get; set; } = 1f;
    /// <summary>Minimum gap between non-incident member surfaces; zero disables the constraint.</summary>
    public float MinMemberSeparationMm { get; set; }
    /// <summary>
    /// No branch joins a trunk, and no junction is made, below this height above the plate
    /// (user decision 2026-09-08: branches may connect at almost any height, but not near the
    /// bottom; 10 mm for now).
    /// </summary>
    public float MinBranchAttachHeightMm { get; set; } = 10f;
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

    /// <summary>
    /// Row pitch of the painted-region grid, in Z. Rows are the axis the user cares about most,
    /// so this is separate from <see cref="Spacing"/> and from the horizontal pitch. Only used
    /// where a support region has been painted.
    /// </summary>
    public float RegionGridVerticalPitchMm { get; set; } = 2.5f;

    /// <summary>Spacing along each painted-region row, measured along the surface.</summary>
    public float RegionGridHorizontalPitchMm { get; set; } = 2.5f;
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
        TipNormalLeadInMm = NonNegativeOrFallback(TipNormalLeadInMm, 0.3f);
        TrunkDiameter = Positive(TrunkDiameter, 1.2f);
        BranchDiameter = Positive(BranchDiameter, 1.2f);
        MemberAngleDegrees = float.IsFinite(MemberAngleDegrees)
            ? Math.Clamp(MemberAngleDegrees, 1f, 89f) : 45f;
        TipMemberLength = Positive(TipMemberLength, 2f);
        MaxBranchLength = Positive(MaxBranchLength, 8f);
        ExistingTrunkBranchRange = Positive(ExistingTrunkBranchRange, 8f);
        MinMemberSeparationMm = NonNegative(MinMemberSeparationMm);
        MinBranchAttachHeightMm = float.IsFinite(MinBranchAttachHeightMm) ? Math.Clamp(MinBranchAttachHeightMm, 0f, 500f) : 10f;
        GuidedExistingClearanceMm = Positive(GuidedExistingClearanceMm, 2.5f);
        GuidedDensifyInsertions = Math.Clamp(GuidedDensifyInsertions, 1, 10);
        GuidedThinKeepEvery = Math.Clamp(GuidedThinKeepEvery, 2, 10);
        ParentingMaxBranchLength = NonNegative(ParentingMaxBranchLength);
        ParentingMaxBranchAngle = float.IsFinite(ParentingMaxBranchAngle)
            ? Math.Clamp(ParentingMaxBranchAngle, 0f, 89f) : 0f;
        ParentingTrunkRange = NonNegative(ParentingTrunkRange);
        ParentingMinTipsPerTrunk = Math.Clamp(ParentingMinTipsPerTrunk, 1, 20);
        ParentingRounds = Math.Clamp(ParentingRounds, 1, 10);
        ParentingMaxConeBend = float.IsFinite(ParentingMaxConeBend)
            ? Math.Clamp(ParentingMaxConeBend, 0f, 180f) : 0f;
        ParentingMaxBranchesPerTrunk = Math.Clamp(ParentingMaxBranchesPerTrunk, 0, 200);
        if (!Enum.IsDefined(BracingPattern)) BracingPattern = BracingPattern.Zigzag;
        BracingDiameter = NonNegative(BracingDiameter);
        BracingAngleDegrees = float.IsFinite(BracingAngleDegrees) ? Math.Clamp(BracingAngleDegrees, 1f, 89f) : 45f;
        BracingSpacingMm = NonNegative(BracingSpacingMm);
        BracingLowestHeightMm = NonNegative(BracingLowestHeightMm);
        BracingMinSupportHeightMm = NonNegative(BracingMinSupportHeightMm);
        BracingNeighbourDistanceMm = Positive(BracingNeighbourDistanceMm, 10f);
        BracingMaxPartners = Math.Clamp(BracingMaxPartners, 1, 20);
        BracingMaxStemLeanDegrees = float.IsFinite(BracingMaxStemLeanDegrees) ? Math.Clamp(BracingMaxStemLeanDegrees, 0f, 89f) : 30f;
        BracingClusterGapMm = NonNegative(BracingClusterGapMm);
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
        RegionGridVerticalPitchMm = Positive(RegionGridVerticalPitchMm, 2.5f);
        RegionGridHorizontalPitchMm = Positive(RegionGridHorizontalPitchMm, 2.5f);
        OverhangAngleDegrees = float.IsFinite(OverhangAngleDegrees)
            ? Math.Clamp(OverhangAngleDegrees, 0f, 90f) : 45f;
        MinIslandAreaMm2 = NonNegative(MinIslandAreaMm2);
        MaxContactFaceAngleDegrees = float.IsFinite(MaxContactFaceAngleDegrees)
            ? Math.Clamp(MaxContactFaceAngleDegrees, 0f, 90f) : 90f;
    }

    private static float Positive(float value, float fallback) =>
        float.IsFinite(value) && value > 0 ? value : fallback;

    private static float NonNegative(float value) =>
        float.IsFinite(value) ? MathF.Max(0, value) : 0;

    private static float NonNegativeOrFallback(float value, float fallback) =>
        float.IsFinite(value) ? MathF.Max(0, value) : fallback;
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

    /// <summary>Chrome palette name, one of Danslicer.App.Themes.ThemeCatalog.Names. Unknown or
    /// missing values fall back to the Classic default (see Normalize/ThemeCatalog.Apply).</summary>
    public string Theme { get; set; } = "Classic";

    public SpaceMouseConfig SpaceMouse { get; set; } = new();
    public ViewportConfig Viewport { get; set; } = new();
    public PlacementConfig Placement { get; set; } = new();
    public SupportConfig Supports { get; set; } = new();
    public List<PrinterDefinition> Printers { get; set; } = CreateBuiltInPrinters();
    public List<ResinPreset> ResinPresets { get; set; } = CreateBuiltInResinPresets();
    public List<SupportPreset> SupportPresets { get; set; } = CreateBuiltInSupportPresets();
    public string ActiveSupportPresetName { get; set; } = CadCleanSupportPresetName;

    /// <summary>
    /// User-selected UVtools executable. Danslicer launches it as a separate process and never
    /// links or bundles UVtools.
    /// </summary>
    public string UvtoolsExecutablePath { get; set; } = "";

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
            // An unrecognised name is left as-is here (Core has no theme catalog to validate
            // against); Danslicer.App.Themes.ThemeCatalog.Apply falls back to Classic for it.
            if (string.IsNullOrWhiteSpace(config.Theme)) config.Theme = "Classic";
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
            config.UvtoolsExecutablePath ??= "";
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
