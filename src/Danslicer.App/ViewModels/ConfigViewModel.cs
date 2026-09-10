using System.Runtime.CompilerServices;
using CommunityToolkit.Mvvm.Input;
using Danslicer.App.Configuration;
using Danslicer.Core;
using Danslicer.Core.Config;
using Danslicer.Core.Supports;
using Danslicer.Core.Supports.Rafts;
using Danslicer.Core.Supports.Generation;
using Danslicer.Core.Supports.Routing;

namespace Danslicer.App.ViewModels;

/// <summary>
/// Binds the config window to <see cref="AppConfig.Current"/>. Every set persists immediately;
/// consumers such as the viewport read the live config each frame or poll tick, so changes apply
/// while the window is open — that is what makes SpaceMouse tuning workable.
/// </summary>
public sealed class ConfigViewModel : ViewModelBase
{
    private enum PresetNameOperation { None, SaveAs, Rename }

    private readonly UserConfig _config;
    private readonly Action _saveConfig;
    private bool _previewing;
    private bool _serializingPreview;
    private readonly Document? _document;
    private readonly SupportConfig? _supportOverride;
    private readonly bool _persistChanges;
    private readonly Action? _supportChanged;
    private SpaceMouseConfig SpaceMouse => _config.SpaceMouse;
    private ViewportConfig Viewport => _config.Viewport;
    private SupportConfig Supports => _supportOverride ?? _config.Supports;
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

    public ConfigViewModel() : this(AppConfig.Current, AppConfig.Save,
        null, persistChanges: true, null, null)
    {
    }

    public ConfigViewModel(Document document) : this(AppConfig.Current, AppConfig.Save,
        null, persistChanges: true, null, document)
    {
    }

    internal ConfigViewModel(SupportConfig supportSettings, Action supportChanged)
        : this(AppConfig.Current, AppConfig.Save,
            supportSettings, persistChanges: false, supportChanged, null)
    {
    }

    internal ConfigViewModel(UserConfig config, Action saveConfig)
        : this(config, saveConfig, null, persistChanges: true, null, null)
    {
    }

    private ConfigViewModel(UserConfig config, Action saveConfig,
        SupportConfig? supportOverride, bool persistChanges,
        Action? supportChanged, Document? document)
    {
        _config = config;
        _document = document;
        _saveConfig = () => PreviewPersistence.Save(saveConfig);
        _supportOverride = supportOverride;
        _persistChanges = persistChanges;
        _supportChanged = supportChanged;
        Keymap = new KeymapViewModel(_config);
        Keymap.Changed += () => Saved?.Invoke();
        Printers = new PrinterEditorViewModel(_config, _saveConfig);
        Printers.Changed += () => Saved?.Invoke();
        Resins = new ResinPresetViewModel(_config, document ?? new Document(), _saveConfig);
        Resins.Changed += () => Saved?.Invoke();
        SaveSupportPresetCommand = new RelayCommand(SaveSupportPreset, HasSelectedSupportPreset);
        BeginSaveSupportPresetAsCommand = new RelayCommand(BeginSaveSupportPresetAs);
        BeginRenameSupportPresetCommand = new RelayCommand(
            BeginRenameSupportPreset, HasSelectedSupportPreset);
        DeleteSupportPresetCommand = new RelayCommand(
            DeleteSupportPreset, () => HasSelectedSupportPreset() && _config.SupportPresets.Count > 1);
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

    public IReadOnlyList<BracingPattern> BracingPatterns { get; } =
        Enum.GetValues<BracingPattern>();

    public IReadOnlyList<RaftType> RaftTypes { get; } = Enum.GetValues<RaftType>();

    public IReadOnlyList<ReinforceSeedSelector> ReinforceSeedSelectors { get; } =
        Enum.GetValues<ReinforceSeedSelector>();

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
            if (value < 0 || value >= _config.SupportPresets.Count) return;
            if (!_config.ApplySupportPreset(_config.SupportPresets[value].Name)) return;
            _saveConfig();
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

    private void Update(Action apply, bool saveGridToPreset = false,
        [CallerMemberName] string? property = null)
    {
        apply();
        if (_previewing)
        {
            if (!_serializingPreview) OnPropertyChanged(property);
            return; // Future-operation settings: never mutate generated supports during a drag.
        }
        if (_persistChanges)
        {
            if (saveGridToPreset) _config.SaveActiveSupportPresetGrid();
            _saveConfig();
        }
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
        SelectedSupportPresetIndex < _config.SupportPresets.Count;

    private void RefreshSupportPresetOptions()
    {
        var config = _config;
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
        var config = _config;
        config.SaveSupportPreset(config.SupportPresets[SelectedSupportPresetIndex].Name);
        _saveConfig();
        RefreshSupportPresetOptions();
        Saved?.Invoke();
    }

    private void BeginSaveSupportPresetAs()
    {
        var baseName = HasSelectedSupportPreset()
            ? _config.SupportPresets[SelectedSupportPresetIndex].Name
            : "Support preset";
        var candidate = $"{baseName} copy";
        for (var suffix = 2; _config.FindSupportPreset(candidate) is not null; suffix++)
            candidate = $"{baseName} copy {suffix}";
        BeginSupportPresetNameEdit(PresetNameOperation.SaveAs, candidate);
    }

    private void BeginRenameSupportPreset()
    {
        if (!HasSelectedSupportPreset()) return;
        BeginSupportPresetNameEdit(PresetNameOperation.Rename,
            _config.SupportPresets[SelectedSupportPresetIndex].Name);
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

        var config = _config;
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

        _saveConfig();
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
        if (!HasSelectedSupportPreset() || _config.SupportPresets.Count <= 1) return;
        var config = _config;
        config.DeleteSupportPreset(config.SupportPresets[SelectedSupportPresetIndex].Name);
        _saveConfig();
        CancelSupportPresetName();
        RefreshSupportPresetOptions();
        Saved?.Invoke();
    }

    private void UpdateSupportDisplay(Func<SupportDisplayConfig, SupportDisplayConfig> apply,
        [CallerMemberName] string? property = null)
    {
        Viewport.SupportDisplay = apply(Viewport.SupportDisplay);
        _saveConfig();
        OnPropertyChanged(property);
        OnPropertyChanged(nameof(SupportDisplay));
        OnPropertyChanged(nameof(IsSupportElementVisibilityAvailable));
        OnPropertyChanged(nameof(IsTransparentSupportDisplay));
        Saved?.Invoke();
    }

    // Viewport

    // Display-only saves must not invoke Saved: that event also applies support geometry.
    public event Action? ViewportSaved;
    private void UpdateViewport(Action apply, [CallerMemberName] string? property = null)
    {
        apply();
        if (_serializingPreview) return;
        if (_previewing)
        {
            OnPropertyChanged(property);
            ViewportPreviewed?.Invoke();
            return;
        }
        if (_persistChanges) _saveConfig();
        OnPropertyChanged(property);
        ViewportSaved?.Invoke();
    }

    public event Action? ViewportPreviewed;

    /// <summary>Explicit numeric property selected by the host; existing setter validation is
    /// retained, but preview bypasses persistence and support-application events.</summary>
    public NumericPreview BeginNumericPreview(string property)
    {
        var member = GetType().GetProperty(property) ?? throw new ArgumentException(property);
        if (member.PropertyType != typeof(float) && member.PropertyType != typeof(double) && member.PropertyType != typeof(int))
            throw new ArgumentException("Preview requires a numeric property", nameof(property));
        var original = member.GetValue(this)!;
        var latest = original;
        // New rafts remain an explicit Add action. Existing selected rafts can preview their
        // parameters; outline+mesh rebuilding measured up to 83 ms for 600 feet, so coalesce.
        var raftObjects = property.StartsWith("SupportRaft", StringComparison.Ordinal) && _supportOverride is null
            ? _document?.Selection.Where(o => o.Raft is not null).Select(o => (Object: o, Original: o.Raft)).ToArray()
            : null;
        var raftTimer = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        RaftParameters? pendingRaft = null;
        var raftApplied = false;
        raftTimer.Tick += (_, _) =>
        {
            raftTimer.Stop();
            if (pendingRaft is null || raftObjects is null) return;
            foreach (var item in raftObjects) item.Object.Raft = pendingRaft;
            pendingRaft = null; raftApplied = true;
            _document!.NotifyTransientChange();
        };
        void Set(object value, bool silent = false)
        {
            _previewing = true; _serializingPreview = silent;
            try { member.SetValue(this, value); }
            finally { _previewing = false; _serializingPreview = false; }
        }
        var unregister = PreviewPersistence.Register(() => Set(original, true), () => Set(latest, true));
        return new NumericPreview(value =>
        {
            latest = Convert.ChangeType(value, member.PropertyType);
            Set(latest);
            if (raftObjects is { Length: > 0 })
            {
                pendingRaft = Supports.ToRaftParameters();
                if (!raftTimer.IsEnabled) raftTimer.Start();
            }
        }, () =>
        {
            raftTimer.Stop(); pendingRaft = null;
            unregister(); Set(original);
            if (raftApplied && raftObjects is not null)
            {
                foreach (var item in raftObjects) item.Object.Raft = item.Original;
                _document!.NotifyTransientChange();
            }
        }, value =>
        {
            var final = Convert.ChangeType(value, member.PropertyType);
            if (!final.Equals(original)) member.SetValue(this, final);
        });
    }

    public IReadOnlyList<string> ModelShadowModes { get; } = new[] { "Off", "Working", "Presentation" };
    public int ModelShadowModeIndex
    {
        get => (int)Viewport.ModelShadows;
        set { if (value is >= 0 and <= 2) UpdateViewport(() => Viewport.ModelShadows = (ModelShadowMode)value); }
    }
    public bool AmbientOcclusionEnabled { get => Viewport.AmbientOcclusionEnabled; set => UpdateViewport(() => Viewport.AmbientOcclusionEnabled = value); }
    public bool PlateReflectionsEnabled { get => Viewport.PlateReflectionsEnabled; set => UpdateViewport(() => Viewport.PlateReflectionsEnabled = value); }
    public bool PlateShadowsEnabled { get => Viewport.PlateShadowsEnabled; set => UpdateViewport(() => Viewport.PlateShadowsEnabled = value); }
    public bool CavityEnabled { get => Viewport.CavityEnabled; set => UpdateViewport(() => Viewport.CavityEnabled = value); }
    public bool ViewCubeEnabled { get => Viewport.ViewCubeEnabled; set => UpdateViewport(() => Viewport.ViewCubeEnabled = value); }
    public float WorkingShadowStrength { get => Viewport.WorkingShadowStrength; set => UpdateViewport(() => Viewport.WorkingShadowStrength = Math.Clamp(value, 0f, 0.7f)); }
    public float PresentationShadowStrength { get => Viewport.PresentationShadowStrength; set => UpdateViewport(() => Viewport.PresentationShadowStrength = Math.Clamp(value, 0f, 0.7f)); }
    public float WorkingShadowSoftnessMm { get => Viewport.WorkingShadowSoftnessMm; set => UpdateViewport(() => Viewport.WorkingShadowSoftnessMm = Math.Clamp(value, 0f, 4f)); }
    public float PresentationShadowSoftnessMm { get => Viewport.PresentationShadowSoftnessMm; set => UpdateViewport(() => Viewport.PresentationShadowSoftnessMm = Math.Clamp(value, 0f, 4f)); }
    public float CavityRidgeStrength { get => Viewport.CavityRidgeStrength; set => UpdateViewport(() => Viewport.CavityRidgeStrength = Math.Clamp(value, 0f, 4f)); }
    public float CavityValleyStrength { get => Viewport.CavityValleyStrength; set => UpdateViewport(() => Viewport.CavityValleyStrength = Math.Clamp(value, 0f, 4f)); }
    public float CavityRadiusPixels { get => Viewport.CavityRadiusPixels; set => UpdateViewport(() => Viewport.CavityRadiusPixels = Math.Clamp(value, 0.5f, 8f)); }


    public float OverhangAngleDegrees
    {
        get => Viewport.OverhangAngleDegrees;
        set => UpdateViewport(() => Viewport.OverhangAngleDegrees = Math.Clamp(value, 10f, 89f));
    }

    public float AmbientOcclusionStrength
    {
        get => Viewport.AmbientOcclusionStrength;
        set => UpdateViewport(() => Viewport.AmbientOcclusionStrength = Math.Clamp(value, 0, 0.6f));
    }
    public float AmbientOcclusionRadiusMm
    {
        get => Viewport.AmbientOcclusionRadiusMm;
        set => UpdateViewport(() => Viewport.AmbientOcclusionRadiusMm = Math.Clamp(value, 0.1f, 10));
    }
    public float PlateReflectionStrength
    {
        get => Viewport.PlateReflectionStrength;
        set => UpdateViewport(() => Viewport.PlateReflectionStrength = Math.Clamp(value, 0, 0.3f));
    }

    public float PlateOpacityFromBelow
    {
        get => Viewport.PlateOpacityFromBelow;
        set => UpdateViewport(() => Viewport.PlateOpacityFromBelow = Math.Clamp(value, 0f, 1f));
    }

    /// <summary>On-screen size of the corner view cube, in DIP pixels. Bounds match
    /// <c>ViewCube.MinSizePixels</c>/<c>MaxSizePixels</c> and <c>ViewportConfig.Normalize</c>.</summary>
    public int ViewCubeSizePixels
    {
        get => Viewport.ViewCubeSizePixels;
        set => UpdateViewport(() => Viewport.ViewCubeSizePixels = Math.Clamp(value, 48, 192));
    }

    public int SupportGizmoSizePixels
    {
        get => Viewport.SupportGizmoSizePixels;
        set => UpdateViewport(() => Viewport.SupportGizmoSizePixels = Math.Clamp(value, 24, 400));
    }

    public float SupportGizmoLineWidth
    {
        get => Viewport.SupportGizmoLineWidth;
        set => UpdateViewport(() => Viewport.SupportGizmoLineWidth = Math.Clamp(value, 1f, 12f));
    }

    public Avalonia.Media.Color OverhangColorA
    {
        get => ToColor(Viewport.OverhangColorA, Avalonia.Media.Color.FromRgb(0xFA, 0xCC, 0x26));
        set => UpdateViewport(() => Viewport.OverhangColorA = ToHex(value));
    }

    public Avalonia.Media.Color OverhangColorB
    {
        get => ToColor(Viewport.OverhangColorB, Avalonia.Media.Color.FromRgb(0xE6, 0x1F, 0x1A));
        set => UpdateViewport(() => Viewport.OverhangColorB = ToHex(value));
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
        set => UpdateViewport(() => Viewport.OverhangCheckerSizeMm = Math.Clamp(value, 0.5f, 20f));
    }

    // External tools

    public string UvtoolsExecutablePath
    {
        get => _config.UvtoolsExecutablePath;
        set
        {
            var path = value ?? "";
            if (path == _config.UvtoolsExecutablePath) return;
            Update(() => _config.UvtoolsExecutablePath = path);
            OnPropertyChanged(nameof(IsUvtoolsExecutablePathValid));
            OnPropertyChanged(nameof(UvtoolsExecutablePathValidationMessage));
        }
    }

    public bool IsUvtoolsExecutablePathValid =>
        !string.IsNullOrWhiteSpace(UvtoolsExecutablePath) && File.Exists(UvtoolsExecutablePath);

    public string UvtoolsExecutablePathValidationMessage =>
        string.IsNullOrWhiteSpace(UvtoolsExecutablePath)
            ? "Not configured."
            : IsUvtoolsExecutablePathValid
                ? "Executable found."
                : "File not found.";

    // Appearance

    /// <summary>Theme names for the Preferences selector, in display order.</summary>
    public IReadOnlyList<string> ThemeNames { get; } = Danslicer.App.Themes.ThemeCatalog.Names;

    public int SelectedThemeIndex
    {
        get
        {
            var index = ThemeNames.ToList().IndexOf(_config.Theme);
            return index >= 0 ? index : 0;
        }
        set
        {
            if (value < 0 || value >= ThemeNames.Count) return;
            var name = ThemeNames[value];
            if (string.Equals(_config.Theme, name, StringComparison.OrdinalIgnoreCase)) return;
            Update(() =>
            {
                _config.Theme = name;
                Danslicer.App.Themes.ThemeCatalog.Apply(name);
            });
        }
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

    public bool ShowSupportRafts
    {
        get => SupportDisplay.ShowRafts;
        set => UpdateSupportDisplay(display => display with { ShowRafts = value });
    }

    // Rafts (SUPPORT-GEOMETRY-SPEC "Rafts"): what Add raft snapshots onto the object.

    public RaftType SupportRaftType
    {
        get => Supports.RaftType;
        set => Update(() => Supports.RaftType = Enum.IsDefined(value) ? value : RaftType.Plate);
    }

    public float SupportRaftThickness
    {
        get => Supports.RaftThickness;
        set => Update(() => Supports.RaftThickness = Clamp(value, 0.05f, 20f, 1f));
    }

    public float SupportRaftEdgeAngleDegrees
    {
        get => Supports.RaftEdgeAngleDegrees;
        set => Update(() => Supports.RaftEdgeAngleDegrees = Clamp(value, 5f, 90f, 45f));
    }

    public float SupportRaftDiscDiameter
    {
        get => Supports.RaftDiscDiameter;
        set => Update(() => Supports.RaftDiscDiameter = Clamp(value, 0.1f, 100f, 5f));
    }

    public float SupportRaftBarWidth
    {
        get => Supports.RaftBarWidth;
        set => Update(() => Supports.RaftBarWidth = Clamp(value, 0.1f, 100f, 4f));
    }

    public float SupportRaftMaxBarLength
    {
        get => Supports.RaftMaxBarLength;
        set => Update(() => Supports.RaftMaxBarLength = Clamp(value, 0f, 500f, 15f));
    }

    public float SupportRaftMargin
    {
        get => Supports.RaftMargin;
        set => Update(() => Supports.RaftMargin = Clamp(value, 0f, 100f, 2f));
    }

    public float SupportRaftBridgingDistance
    {
        get => Supports.RaftBridgingDistance;
        set => Update(() => Supports.RaftBridgingDistance = Clamp(value, 0f, 500f, 8f));
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

    public float SupportTipNormalLeadInMm
    {
        get => Supports.TipNormalLeadInMm;
        set => Update(() => Supports.TipNormalLeadInMm = Clamp(value, 0f, 100f, 0.3f));
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

    public bool SupportIndependentManualSupports
    {
        get => Supports.IndependentManualSupports;
        set => Update(() => Supports.IndependentManualSupports = value);
    }

    public bool SupportGuidedIgnoreExistingSupports
    {
        get => Supports.GuidedIgnoreExistingSupports;
        set => Update(() => Supports.GuidedIgnoreExistingSupports = value);
    }

    public float SupportGuidedExistingClearanceMm
    {
        get => Supports.GuidedExistingClearanceMm;
        set => Update(() => Supports.GuidedExistingClearanceMm = Clamp(value, 0.1f, 100f, 2.5f));
    }

    public int SupportGuidedDensifyInsertions
    {
        get => Supports.GuidedDensifyInsertions;
        set => Update(() => Supports.GuidedDensifyInsertions = Math.Clamp(value, 1, 10));
    }

    public int SupportGuidedThinKeepEvery
    {
        get => Supports.GuidedThinKeepEvery;
        set => Update(() => Supports.GuidedThinKeepEvery = Math.Clamp(value, 2, 10));
    }

    public float SupportParentingMaxBranchLength
    {
        get => Supports.ParentingMaxBranchLength;
        set => Update(() => Supports.ParentingMaxBranchLength = Clamp(value, 0f, 1000f, 0f));
    }

    public float SupportParentingMaxBranchAngle
    {
        get => Supports.ParentingMaxBranchAngle;
        set => Update(() => Supports.ParentingMaxBranchAngle = Clamp(value, 0f, 89f, 0f));
    }

    public float SupportParentingTrunkRange
    {
        get => Supports.ParentingTrunkRange;
        set => Update(() => Supports.ParentingTrunkRange = Clamp(value, 0f, 1000f, 0f));
    }

    public int SupportParentingMinTipsPerTrunk
    {
        get => Supports.ParentingMinTipsPerTrunk;
        set => Update(() => Supports.ParentingMinTipsPerTrunk = Math.Clamp(value, 1, 20));
    }

    public int SupportParentingRounds
    {
        get => Supports.ParentingRounds;
        set => Update(() => Supports.ParentingRounds = Math.Clamp(value, 1, 10));
    }

    public float SupportParentingMaxConeBend
    {
        get => Supports.ParentingMaxConeBend;
        set => Update(() => Supports.ParentingMaxConeBend = Clamp(value, 0f, 180f, 0f));
    }

    public int SupportParentingMaxBranchesPerTrunk
    {
        get => Supports.ParentingMaxBranchesPerTrunk;
        set => Update(() => Supports.ParentingMaxBranchesPerTrunk = Math.Clamp(value, 0, 200));
    }

    public bool SupportParentingHierarchical
    {
        get => Supports.ParentingHierarchical;
        set => Update(() => Supports.ParentingHierarchical = value);
    }

    public bool SupportAutoParenting
    {
        get => Supports.AutoParenting;
        set => Update(() => Supports.AutoParenting = value);
    }

    // Bracing (K): SUPPORT-GEOMETRY-SPEC "Bracing".
    public bool SupportAutoBracing
    {
        get => Supports.AutoBracing;
        set => Update(() => Supports.AutoBracing = value);
    }

    public BracingPattern SupportBracingPattern
    {
        get => Supports.BracingPattern;
        set => Update(() => Supports.BracingPattern = Enum.IsDefined(value) ? value : BracingPattern.Zigzag);
    }

    public float SupportBracingDiameter
    {
        get => Supports.BracingDiameter;
        set => Update(() => Supports.BracingDiameter = Clamp(value, 0f, 20f, 0f));
    }

    public float SupportBracingAngleDegrees
    {
        get => Supports.BracingAngleDegrees;
        set => Update(() => Supports.BracingAngleDegrees = Clamp(value, 1f, 89f, 45f));
    }

    public float SupportBracingSpacingMm
    {
        get => Supports.BracingSpacingMm;
        set => Update(() => Supports.BracingSpacingMm = Clamp(value, 0f, 500f, 0f));
    }

    public float SupportBracingLowestHeightMm
    {
        get => Supports.BracingLowestHeightMm;
        set => Update(() => Supports.BracingLowestHeightMm = Clamp(value, 0f, 500f, 0f));
    }

    public float SupportBracingMinSupportHeightMm
    {
        get => Supports.BracingMinSupportHeightMm;
        set => Update(() => Supports.BracingMinSupportHeightMm = Clamp(value, 0f, 500f, 20f));
    }

    public float SupportBracingNeighbourDistanceMm
    {
        get => Supports.BracingNeighbourDistanceMm;
        set => Update(() => Supports.BracingNeighbourDistanceMm = Clamp(value, 0.5f, 500f, 10f));
    }

    public int SupportBracingMaxPartners
    {
        get => Supports.BracingMaxPartners;
        set => Update(() => Supports.BracingMaxPartners = Math.Clamp(value, 1, 20));
    }

    public float SupportBracingMaxStemLeanDegrees
    {
        get => Supports.BracingMaxStemLeanDegrees;
        set => Update(() => Supports.BracingMaxStemLeanDegrees = Clamp(value, 0f, 89f, 30f));
    }

    public float SupportBracingClusterGapMm
    {
        get => Supports.BracingClusterGapMm;
        set => Update(() => Supports.BracingClusterGapMm = Clamp(value, 0f, 100f, 1f));
    }

    public float SupportMinBranchAttachHeightMm
    {
        get => Supports.MinBranchAttachHeightMm;
        set => Update(() => Supports.MinBranchAttachHeightMm = Clamp(value, 0f, 500f, 10f));
    }

    public float SupportExistingTrunkBranchRange
    {
        get => Supports.ExistingTrunkBranchRange;
        set => Update(() => Supports.ExistingTrunkBranchRange = Clamp(value, 0.01f, 1000f, 8f));
    }

    public float SupportMinMemberSeparationMm
    {
        get => Supports.MinMemberSeparationMm;
        set => Update(() => Supports.MinMemberSeparationMm = Clamp(value, 0f, 100f, 0f));
    }

    public float SupportBaseGridPitch
    {
        get => Supports.BaseGridPitch;
        set
        {
            var normalized = Clamp(value, 0.01f, 1000f, 6f);
            if (Supports.BaseGridPitch == normalized) return;
            Update(() => Supports.BaseGridPitch = normalized, saveGridToPreset: true);
        }
    }

    public bool SupportUseBaseGrid
    {
        get => Supports.UseBaseGrid;
        set
        {
            if (Supports.UseBaseGrid == value) return;
            Update(() => Supports.UseBaseGrid = value, saveGridToPreset: true);
        }
    }

    public bool SupportReinforceEnabled
    {
        get => Supports.ReinforceEnabled;
        set => Update(() => Supports.ReinforceEnabled = value);
    }

    public ReinforceSeedSelector SupportReinforceSeedSelector
    {
        get => Supports.ReinforceSeedSelector;
        set => Update(() => Supports.ReinforceSeedSelector = Enum.IsDefined(value)
            ? value : ReinforceSeedSelector.LowestPointOfObject);
    }

    public int SupportReinforceCount
    {
        get => Supports.ReinforceCount;
        set => Update(() => Supports.ReinforceCount = Math.Clamp(value, 1, 100));
    }

    public float SupportReinforceRingRadius
    {
        get => Supports.ReinforceRingRadius;
        set => Update(() => Supports.ReinforceRingRadius = Clamp(value, 0.01f, 1000f, 2f));
    }

    public float SupportReinforceRingDiameterMultiplier
    {
        get => Supports.ReinforceRingDiameterMultiplier;
        set => Update(() => Supports.ReinforceRingDiameterMultiplier =
            Clamp(value, 0.01f, 100f, 1.25f));
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

    /// <summary>Row pitch of the painted-region grid, in Z — the axis that matters most on a
    /// painted wall, so it is its own setting rather than sharing <see cref="SupportSpacing"/>.</summary>
    public float SupportRegionGridVerticalPitchMm
    {
        get => Supports.RegionGridVerticalPitchMm;
        set => Update(() => Supports.RegionGridVerticalPitchMm = Clamp(value, 0.01f, 1000f, 2.5f));
    }

    /// <summary>Spacing along each painted-region row, measured along the surface.</summary>
    public float SupportRegionGridHorizontalPitchMm
    {
        get => Supports.RegionGridHorizontalPitchMm;
        set => Update(() => Supports.RegionGridHorizontalPitchMm = Clamp(value, 0.01f, 1000f, 2.5f));
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

    public float SupportMaxContactFaceAngleDegrees
    {
        get => Supports.MaxContactFaceAngleDegrees;
        set => Update(() => Supports.MaxContactFaceAngleDegrees = Clamp(value, 0f, 90f, 90f));
    }

    public bool SupportRequireContactSeesPlate
    {
        get => Supports.RequireContactSeesPlate;
        set => Update(() => Supports.RequireContactSeesPlate = value);
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
