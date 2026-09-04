using CommunityToolkit.Mvvm.Input;
using Danslicer.Core;
using Danslicer.Core.Config;
using Danslicer.Core.Slicing;
using Danslicer.Core.Utilities;

namespace Danslicer.App.ViewModels;

/// <summary>Shared resin preset selector/editor used by Preferences and the Slicing panel.</summary>
public sealed class ResinPresetViewModel : ViewModelBase
{
    private enum NameOperation { None, SaveAs, Rename }

    private readonly UserConfig _config;
    private readonly Document _document;
    private readonly Action _save;
    private IReadOnlyList<ResinPreset> _options = [];
    private IReadOnlyList<string> _displayNames = [];
    private int _selectedIndex = -1;
    private bool _isNameEditorVisible;
    private string _nameDraft = "";
    private string _validationMessage = "";
    private NameOperation _nameOperation;

    public ResinPresetViewModel(UserConfig config, Document document, Action save)
    {
        _config = config;
        _document = document;
        _save = save;
        BottomLayers = Field("Bottom layers", UnitKind.Scalar, "0",
            (s, v) => s with { BottomLayers = Math.Max(0, (int)Math.Round(v)) });
        BottomExposure = Field("Bottom exposure", UnitKind.Scalar, "0.##",
            (s, v) => s with { BottomExposure = MathF.Max(0, (float)v) }, "s");
        Exposure = Field("Exposure", UnitKind.Scalar, "0.##",
            (s, v) => s with { Exposure = MathF.Max(0, (float)v) }, "s");
        LightOffDelay = Field("Light-off delay", UnitKind.Scalar, "0.##",
            (s, v) => s with { LightOffDelay = MathF.Max(0, (float)v) }, "s");
        LiftHeight = Field("Lift height", UnitKind.Length, "0.##",
            (s, v) => s with { LiftHeight = MathF.Max(0, (float)v) });
        LiftSpeed = Field("Lift speed", UnitKind.Scalar, "0.#",
            (s, v) => s with { LiftSpeed = MathF.Max(float.Epsilon, (float)v) }, "mm/min");
        RetractSpeed = Field("Retract speed", UnitKind.Scalar, "0.#",
            (s, v) => s with { RetractSpeed = MathF.Max(float.Epsilon, (float)v) }, "mm/min");
        BottomLiftHeight = Field("Bottom lift height", UnitKind.Length, "0.##",
            (s, v) => s with { BottomLiftHeight = MathF.Max(0, (float)v) });
        BottomLiftSpeed = Field("Bottom lift speed", UnitKind.Scalar, "0.#",
            (s, v) => s with { BottomLiftSpeed = MathF.Max(float.Epsilon, (float)v) }, "mm/min");
        Fields =
        [
            BottomLayers, BottomExposure, Exposure, LightOffDelay, LiftHeight,
            LiftSpeed, RetractSpeed, BottomLiftHeight, BottomLiftSpeed,
        ];

        SaveCommand = new RelayCommand(Save, HasSelection);
        BeginSaveAsCommand = new RelayCommand(BeginSaveAs);
        BeginRenameCommand = new RelayCommand(BeginRename, HasSelection);
        DeleteCommand = new RelayCommand(Delete, () => HasSelection() && _options.Count > 1);
        ConfirmNameCommand = new RelayCommand(ConfirmName);
        CancelNameCommand = new RelayCommand(CancelName);
        Refresh();
    }

    public NumericField BottomLayers { get; }
    public NumericField BottomExposure { get; }
    public NumericField Exposure { get; }
    public NumericField LightOffDelay { get; }
    public NumericField LiftHeight { get; }
    public NumericField LiftSpeed { get; }
    public NumericField RetractSpeed { get; }
    public NumericField BottomLiftHeight { get; }
    public NumericField BottomLiftSpeed { get; }
    public IReadOnlyList<NumericField> Fields { get; }

    public IRelayCommand SaveCommand { get; }
    public IRelayCommand BeginSaveAsCommand { get; }
    public IRelayCommand BeginRenameCommand { get; }
    public IRelayCommand DeleteCommand { get; }
    public IRelayCommand ConfirmNameCommand { get; }
    public IRelayCommand CancelNameCommand { get; }

    public IReadOnlyList<string> DisplayNames
    {
        get => _displayNames;
        private set { _displayNames = value; OnPropertyChanged(); }
    }

    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            if (value == _selectedIndex) return;
            _selectedIndex = value;
            OnPropertyChanged();
            if (value < 0 || value >= _options.Count) return;
            _document.ApplyResinPreset(_options[value]);
            CancelName();
            Refresh();
            Changed?.Invoke();
        }
    }

    public bool IsNameEditorVisible
    {
        get => _isNameEditorVisible;
        private set { _isNameEditorVisible = value; OnPropertyChanged(); }
    }

    public string NameDraft
    {
        get => _nameDraft;
        set { _nameDraft = value; ValidationMessage = ""; OnPropertyChanged(); }
    }

    public string NameEditorAction => _nameOperation == NameOperation.Rename ? "Rename" : "Save copy";

    public string ValidationMessage
    {
        get => _validationMessage;
        private set
        {
            _validationMessage = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasValidationMessage));
        }
    }

    public bool HasValidationMessage => ValidationMessage.Length > 0;
    public event Action? Changed;

    public void Refresh()
    {
        var current = _document.ResinPreset;
        var options = _config.ResinPresets.ToList();
        var index = options.FindIndex(preset =>
            string.Equals(preset.Id, current.Id, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            options.Add(current);
            index = options.Count - 1;
        }
        else if (options[index] != current)
        {
            _document.ResinPreset = options[index];
        }

        _options = options;
        DisplayNames = options.Select(preset =>
        {
            var projectOnly = _config.FindResinPreset(preset.Id) is null ? " (project)" : "";
            var modified = preset.Id == _document.ResinPreset.Id &&
                           preset.Settings != _document.ResinSettings ? " *" : "";
            return preset.Name + projectOnly + modified;
        }).ToArray();
        _selectedIndex = index;
        OnPropertyChanged(nameof(SelectedIndex));
        RefreshFields();
        NotifyCommands();
    }

    private NumericField Field(string label, UnitKind kind, string format,
        Func<ResinSettings, double, ResinSettings> edit, string? suffix = null)
    {
        NumericField? field = null;
        field = new NumericField(label, kind, format, value =>
        {
            _document.ResinSettings = edit(_document.ResinSettings, value).Normalize();
            _document.NotifyTransientChange();
            Refresh();
            Changed?.Invoke();
        }, suffix);
        return field;
    }

    private void RefreshFields()
    {
        var s = _document.ResinSettings;
        BottomLayers.SetValue(s.BottomLayers);
        BottomExposure.SetValue(s.BottomExposure);
        Exposure.SetValue(s.Exposure);
        LightOffDelay.SetValue(s.LightOffDelay);
        LiftHeight.SetValue(s.LiftHeight);
        LiftSpeed.SetValue(s.LiftSpeed);
        RetractSpeed.SetValue(s.RetractSpeed);
        BottomLiftHeight.SetValue(s.BottomLiftHeight);
        BottomLiftSpeed.SetValue(s.BottomLiftSpeed);
    }

    private bool HasSelection() => _selectedIndex >= 0 && _selectedIndex < _options.Count;

    private void Save()
    {
        if (!HasSelection()) return;
        var selected = _options[_selectedIndex];
        if (_config.FindResinPreset(selected.Id) is null)
        {
            BeginName(NameOperation.SaveAs, UniqueName(selected.Name));
            return;
        }
        if (!_config.SaveResinPreset(selected.Id, _document.ResinSettings)) return;
        _document.ResinPreset = _config.FindResinPreset(selected.Id)!;
        PersistAndRefresh();
    }

    private void BeginSaveAs()
    {
        var baseName = HasSelection() ? _options[_selectedIndex].Name : "Resin";
        BeginName(NameOperation.SaveAs, UniqueName(baseName));
    }

    private string UniqueName(string baseName)
    {
        var candidate = $"{baseName} copy";
        for (var suffix = 2; _config.FindResinPresetByName(candidate) is not null; suffix++)
            candidate = $"{baseName} copy {suffix}";
        return candidate;
    }

    private void BeginRename()
    {
        if (!HasSelection()) return;
        var selected = _options[_selectedIndex];
        if (_config.FindResinPreset(selected.Id) is null) return;
        BeginName(NameOperation.Rename, selected.Name);
    }

    private void BeginName(NameOperation operation, string draft)
    {
        _nameOperation = operation;
        OnPropertyChanged(nameof(NameEditorAction));
        NameDraft = draft;
        IsNameEditorVisible = true;
    }

    private void ConfirmName()
    {
        var name = NameDraft.Trim();
        if (name.Length == 0)
        {
            ValidationMessage = "Enter a preset name.";
            return;
        }

        ResinPreset? selected = null;
        var succeeded = false;
        if (_nameOperation == NameOperation.SaveAs)
        {
            selected = _config.SaveResinPresetAs(name, _document.ResinSettings);
            succeeded = selected is not null;
        }
        else if (_nameOperation == NameOperation.Rename && HasSelection())
        {
            var id = _options[_selectedIndex].Id;
            succeeded = _config.RenameResinPreset(id, name);
            selected = _config.FindResinPreset(id);
        }

        if (!succeeded || selected is null)
        {
            ValidationMessage = "That preset name is already in use.";
            return;
        }
        _document.ResinPreset = selected;
        CancelName();
        PersistAndRefresh();
    }

    private void CancelName()
    {
        _nameOperation = NameOperation.None;
        IsNameEditorVisible = false;
        ValidationMessage = "";
    }

    private void Delete()
    {
        if (!HasSelection() || _options.Count <= 1) return;
        var selected = _options[_selectedIndex];
        if (_config.FindResinPreset(selected.Id) is null || !_config.DeleteResinPreset(selected.Id)) return;
        var replacement = _config.ResinPresets.FirstOrDefault() ?? ResinPreset.Default;
        _document.ApplyResinPreset(replacement);
        CancelName();
        PersistAndRefresh();
    }

    private void PersistAndRefresh()
    {
        _save();
        Refresh();
        Changed?.Invoke();
    }

    private void NotifyCommands()
    {
        SaveCommand.NotifyCanExecuteChanged();
        BeginRenameCommand.NotifyCanExecuteChanged();
        DeleteCommand.NotifyCanExecuteChanged();
    }
}
