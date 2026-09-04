using System.Runtime.CompilerServices;
using CommunityToolkit.Mvvm.Input;
using Danslicer.App.Configuration;
using Danslicer.Core;
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
    private enum PresetNameOperation { None, SaveAs, Rename }

    private readonly SupportConfig? _supportOverride;
    private readonly bool _persistChanges;
    private readonly Action? _supportChanged;
    private SpaceMouseConfig SpaceMouse => AppConfig.Current.SpaceMouse;
    private ViewportConfig Viewport => AppConfig.Current.Viewport;
    private SupportConfig Supports => _supportOverride ?? AppConfig.Current.Supports;
    private IReadOnlyList<string> _supportPresetDisplayNames = [];
    private int _selectedSupportPresetIndex = -1;
    private bool _isSupportPresetNameEditorVisible;
    private string _supportPresetNameDraft = "";
    private string _supportPresetValidationMessage = "";
    private PresetNameOperation _presetNameOperation;
    // The Support panel and the Preferences window bind SelectedSupportPresetIndex to the same
    // instance; index write-backs raised while the display-name list is being replaced must not
    // re-apply presets, or the two views recurse into each other (see ResinPresetViewModel).
    private bool _refreshingPresets;

    public ConfigViewModel() : this(null, persistChanges: true, null, null)
    {
    }

    public ConfigViewModel(Document document) : this(null, persistChanges: true, null, document)
    {
    }

    internal ConfigViewModel(SupportConfig supportSettings, Action supportChanged)
        : this(supportSettings, persistChanges: false, supportChanged, null)
    {
    }

    private ConfigViewModel(SupportConfig? supportOverride, bool persistChanges,
        Action? supportChanged, Document? document)
    {
        _supportOverride = supportOverride;
        _persistChanges = persistChanges;
        _supportChanged = supportChanged;
        Keymap = new KeymapViewModel(AppConfig.Current);
        Keymap.Changed += () => Saved?.Invoke();
        Printers = new PrinterEditorViewModel(AppConfig.Current, AppConfig.Save);
        Printers.Changed += () => Saved?.Invoke();
        Resins = new ResinPresetViewModel(AppConfig.Current, document ?? new Document(), AppConfig.Save);
        Resins.Changed += () => Saved?.Invoke();
        SaveSupportPresetCommand = new RelayCommand(SaveSupportPreset, HasSelectedSupportPreset);
        BeginSaveSupportPresetAsCommand = new RelayCommand(BeginSaveSupportPresetAs);
        BeginRenameSupportPresetCommand = new RelayCommand(
            BeginRenameSupportPreset, HasSelectedSupportPreset);
        DeleteSupportPresetCommand = new RelayCommand(
            DeleteSupportPreset, () => HasSelectedSupportPreset() && AppConfig.Current.SupportPresets.Count > 1);
        ConfirmSupportPresetNameCommand = new RelayCommand(ConfirmSupportPresetName);
        CancelSupportPresetNameCommand = new RelayCommand(CancelSupportPresetName);
        EditSupportPresetCommand = new RelayCommand(
            () => EditSupportPresetRequested?.Invoke(), HasSelectedSupportPreset);
        if (_persistChanges) RefreshSupportPresetOptions();
    }

    public SupportDisplayConfig SupportDisplay => Viewport.SupportDisplay;
    public KeymapViewModel Keymap { get; }
    public PrinterEditorViewModel Printers { get; }
    public ResinPresetViewModel Resins { get; }

    public IReadOnlyList<string> SupportDisplayModes { get; } =
        ["Full", "Contact points", "Lines", "Tips", "Transparent"];

    public IReadOnlyList<SupportBaseShape> SupportBaseShapes { get; } =
        Enum.GetValues<SupportBaseShape>();

    public IRelayCommand SaveSupportPresetCommand { get; }
    public IRelayCommand BeginSaveSupportPresetAsCommand { get; }
    public IRelayCommand BeginRenameSupportPresetCommand { get; }
    public IRelayCommand DeleteSupportPresetCommand { get; }
    public IRelayCommand ConfirmSupportPresetNameCommand { get; }
    public IRelayCommand CancelSupportPresetNameCommand { get; }
    public IRelayCommand EditSupportPresetCommand { get; }

    public bool ShowSupportPresetControls => _persistChanges;

    public IReadOnlyList<string> SupportPresetDisplayNames
    {
        get => _supportPresetDisplayNames;
        private set
        {
            _supportPresetDisplayNames = value;
            OnPropertyChanged();
        }
    }

    public int SelectedSupportPresetIndex
    {
        get => _selectedSupportPresetIndex;
        set
        {
            if (value == _selectedSupportPresetIndex) return;
            _selectedSupportPresetIndex = value;
            OnPropertyChanged();
            if (_refreshingPresets) return;
            if (value < 0 || value >= AppConfig.Current.SupportPresets.Count) return;
            if (!AppConfig.Current.ApplySupportPreset(AppConfig.Current.SupportPresets[value].Name)) return;
            AppConfig.Save();
            OnPropertyChanged(string.Empty);
            RefreshSupportPresetOptions();
            Saved?.Invoke();
        }
    }

    public bool IsSupportPresetNameEditorVisible
    {
        get => _isSupportPresetNameEditorVisible;
        private set
        {
            _isSupportPresetNameEditorVisible = value;
            OnPropertyChanged();
        }
    }

    public string SupportPresetNameDraft
    {
        get => _supportPresetNameDraft;
        set
        {
            _supportPresetNameDraft = value;
            SupportPresetValidationMessage = "";
            OnPropertyChanged();
        }
    }

    public string SupportPresetNameEditorAction =>
        _presetNameOperation == PresetNameOperation.Rename ? "Rename" : "Save copy";

    public string SupportPresetValidationMessage
    {
        get => _supportPresetValidationMessage;
        private set
        {
            _supportPresetValidationMessage = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasSupportPresetValidationMessage));
        }
    }

    public bool HasSupportPresetValidationMessage => SupportPresetValidationMessage.Length > 0;

    /// <summary>Raised after every persisted change, so hosts can refresh what they draw.</summary>
    public event Action? Saved;
    public event Action? EditSupportPresetRequested;

    private void Update(Action apply, [CallerMemberName] string? property = null)
    {
        apply();
        if (_persistChanges) AppConfig.Save();
        OnPropertyChanged(property);
        if (_persistChanges) RefreshSupportPresetOptions();
        _supportChanged?.Invoke();
        Saved?.Invoke();
    }

    internal void ApplyExternalSupportPresetChange()
    {
        OnPropertyChanged(string.Empty);
        RefreshSupportPresetOptions();
        Saved?.Invoke();
    }

    private bool HasSelectedSupportPreset() =>
        SelectedSupportPresetIndex >= 0 &&
        SelectedSupportPresetIndex < AppConfig.Current.SupportPresets.Count;

    private void RefreshSupportPresetOptions()
    {
        var config = AppConfig.Current;
        var active = config.FindSupportPreset(config.ActiveSupportPresetName);
        var modified = active is not null && active.Settings != config.Supports;
        var display = config.SupportPresets
            .Select(preset => preset.Name + (modified && ReferenceEquals(preset, active) ? " *" : ""))
            .ToArray();
        _refreshingPresets = true;
        try
        {
            if (!display.SequenceEqual(_supportPresetDisplayNames)) SupportPresetDisplayNames = display;
            _selectedSupportPresetIndex = active is null ? -1 : config.SupportPresets.IndexOf(active);
            OnPropertyChanged(nameof(SelectedSupportPresetIndex));
        }
        finally
        {
            _refreshingPresets = false;
        }
        SaveSupportPresetCommand.NotifyCanExecuteChanged();
        BeginRenameSupportPresetCommand.NotifyCanExecuteChanged();
        DeleteSupportPresetCommand.NotifyCanExecuteChanged();
        EditSupportPresetCommand.NotifyCanExecuteChanged();
    }

    private void SaveSupportPreset()
    {
        if (!HasSelectedSupportPreset()) return;
        var config = AppConfig.Current;
        config.SaveSupportPreset(config.SupportPresets[SelectedSupportPresetIndex].Name);
        AppConfig.Save();
        RefreshSupportPresetOptions();
        Saved?.Invoke();
    }

    private void BeginSaveSupportPresetAs()
    {
        var baseName = HasSelectedSupportPreset()
            ? AppConfig.Current.SupportPresets[SelectedSupportPresetIndex].Name
            : "Support preset";
        var candidate = $"{baseName} copy";
        for (var suffix = 2; AppConfig.Current.FindSupportPreset(candidate) is not null; suffix++)
            candidate = $"{baseName} copy {suffix}";
        BeginSupportPresetNameEdit(PresetNameOperation.SaveAs, candidate);
    }

    private void BeginRenameSupportPreset()
    {
        if (!HasSelectedSupportPreset()) return;
        BeginSupportPresetNameEdit(PresetNameOperation.Rename,
            AppConfig.Current.SupportPresets[SelectedSupportPresetIndex].Name);
    }

    private void BeginSupportPresetNameEdit(PresetNameOperation operation, string draft)
    {
        _presetNameOperation = operation;
        OnPropertyChanged(nameof(SupportPresetNameEditorAction));
        SupportPresetNameDraft = draft;
        IsSupportPresetNameEditorVisible = true;
    }

    private void ConfirmSupportPresetName()
    {
        var name = SupportPresetNameDraft.Trim();
        if (name.Length == 0)
        {
            SupportPresetValidationMessage = "Enter a preset name.";
            return;
        }

        var config = AppConfig.Current;
        var succeeded = _presetNameOperation switch
        {
            PresetNameOperation.SaveAs => config.SaveSupportPresetAs(name),
            PresetNameOperation.Rename when HasSelectedSupportPreset() => config.RenameSupportPreset(
                config.SupportPresets[SelectedSupportPresetIndex].Name, name),
            _ => false,
        };
        if (!succeeded)
        {
            SupportPresetValidationMessage = "That preset name is already in use.";
            return;
        }

        AppConfig.Save();
        CancelSupportPresetName();
        RefreshSupportPresetOptions();
        Saved?.Invoke();
    }

    private void CancelSupportPresetName()
    {
        _presetNameOperation = PresetNameOperation.None;
        IsSupportPresetNameEditorVisible = false;
        SupportPresetValidationMessage = "";
    }

    private void DeleteSupportPreset()
    {
        if (!HasSelectedSupportPreset() || AppConfig.Current.SupportPresets.Count <= 1) return;
        var config = AppConfig.Current;
        config.DeleteSupportPreset(config.SupportPresets[SelectedSupportPresetIndex].Name);
        AppConfig.Save();
        CancelSupportPresetName();
        RefreshSupportPresetOptions();
        Saved?.Invoke();
    }

    private void UpdateSupportDisplay(Func<SupportDisplayConfig, SupportDisplayConfig> apply,
        [CallerMemberName] string? property = null)
    {
        Viewport.SupportDisplay = apply(Viewport.SupportDisplay);
        AppConfig.Save();
        OnPropertyChanged(property);
        OnPropertyChanged(nameof(SupportDisplay));
        OnPropertyChanged(nameof(IsSupportElementVisibilityAvailable));
        OnPropertyChanged(nameof(IsTransparentSupportDisplay));
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

    public int SupportDisplayModeIndex
    {
        get => (int)SupportDisplay.Mode;
        set => UpdateSupportDisplay(display => display with
        {
            Mode = Enum.IsDefined((SupportDisplayMode)value)
                ? (SupportDisplayMode)value
                : SupportDisplayMode.Full,
        });
    }

    public bool IsSupportElementVisibilityAvailable =>
        SupportDisplay.Mode is SupportDisplayMode.Full or SupportDisplayMode.Transparent;

    public bool IsTransparentSupportDisplay =>
        SupportDisplay.Mode == SupportDisplayMode.Transparent;

    public bool ShowContactPointsInTransparent
    {
        get => SupportDisplay.ShowContactPointsInTransparent;
        set => UpdateSupportDisplay(display => display with
            { ShowContactPointsInTransparent = value });
    }

    public bool ShowSupportTips
    {
        get => SupportDisplay.ShowTips;
        set => UpdateSupportDisplay(display => display with { ShowTips = value });
    }

    public bool ShowMiniSupports
    {
        get => SupportDisplay.ShowMiniSupports;
        set => UpdateSupportDisplay(display => display with { ShowMiniSupports = value });
    }

    public bool ShowSupportBranches
    {
        get => SupportDisplay.ShowBranches;
        set => UpdateSupportDisplay(display => display with { ShowBranches = value });
    }

    public bool ShowSupportTrunks
    {
        get => SupportDisplay.ShowTrunks;
        set => UpdateSupportDisplay(display => display with { ShowTrunks = value });
    }

    public bool ShowSupportBases
    {
        get => SupportDisplay.ShowBases;
        set => UpdateSupportDisplay(display => display with { ShowBases = value });
    }

    public bool ShowSupportBracing
    {
        get => SupportDisplay.ShowBracing;
        set => UpdateSupportDisplay(display => display with { ShowBracing = value });
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

    public bool SupportPreferExistingTrunks
    {
        get => Supports.PreferExistingTrunks;
        set => Update(() => Supports.PreferExistingTrunks = value);
    }

    public float SupportExistingTrunkBranchRange
    {
        get => Supports.ExistingTrunkBranchRange;
        set => Update(() => Supports.ExistingTrunkBranchRange = Clamp(value, 0.01f, 1000f, 8f));
    }

    public float SupportMiniSupportDiameter
    {
        get => Supports.MiniSupportDiameter;
        set => Update(() => Supports.MiniSupportDiameter = Clamp(value, 0.01f, 100f, 0.6f));
    }

    public float SupportMiniSupportTipDiameter
    {
        get => Supports.MiniSupportTipDiameter;
        set => Update(() => Supports.MiniSupportTipDiameter = Clamp(value, 0.01f, 100f, 0.25f));
    }

    public float SupportMiniSupportConeLength
    {
        get => Supports.MiniSupportConeLength;
        set => Update(() => Supports.MiniSupportConeLength = Clamp(value, 0.01f, 100f, 1f));
    }

    public float SupportMiniSupportMaxLength
    {
        get => Supports.MiniSupportMaxLength;
        set => Update(() => Supports.MiniSupportMaxLength = Clamp(value, 0.01f, 1000f, 5f));
    }

    public float SupportMiniSupportMaxAngleDegrees
    {
        get => Supports.MiniSupportMaxAngleDegrees;
        set => Update(() => Supports.MiniSupportMaxAngleDegrees = Clamp(value, 1f, 89f, 75f));
    }

    public int SupportMiniSupportMaxFanPerBranchEnd
    {
        get => Supports.MiniSupportMaxFanPerBranchEnd;
        set => Update(() => Supports.MiniSupportMaxFanPerBranchEnd = Math.Clamp(value, 1, 100));
    }

    public bool SupportRefusedTipsFallBackToMini
    {
        get => Supports.RefusedTipsFallBackToMini;
        set => Update(() => Supports.RefusedTipsFallBackToMini = value);
    }

    public float SupportMiniIslandMaxAreaMm2
    {
        get => Supports.MiniIslandMaxAreaMm2;
        set => Update(() => Supports.MiniIslandMaxAreaMm2 =
            Clamp(value, 0f, 1_000_000f, 0.1f));
    }

    public float SupportBaseGridPitch
    {
        get => Supports.BaseGridPitch;
        set => Update(() => Supports.BaseGridPitch = Clamp(value, 0.01f, 1000f, 20f));
    }

    public bool SupportUseBaseGrid
    {
        get => Supports.UseBaseGrid;
        set => Update(() => Supports.UseBaseGrid = value);
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
