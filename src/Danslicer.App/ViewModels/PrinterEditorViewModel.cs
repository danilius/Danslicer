using CommunityToolkit.Mvvm.Input;
using Danslicer.Core.Config;
using Danslicer.Core.Printers;
using Danslicer.Core.Utilities;

namespace Danslicer.App.ViewModels;

/// <summary>Persisted printer collection editor used by the dedicated preset window.</summary>
public sealed class PrinterEditorViewModel : ViewModelBase
{
    private readonly UserConfig _config;
    private readonly Action _save;
    private int _selectedIndex;
    private string _nameDraft = "";
    private string _validationMessage = "";

    public PrinterEditorViewModel(UserConfig config, Action save)
    {
        _config = config;
        _save = save;
        AddCommand = new RelayCommand(Add);
        DuplicateCommand = new RelayCommand(Duplicate, HasSelection);
        RenameCommand = new RelayCommand(Rename, HasSelection);
        DeleteCommand = new RelayCommand(Delete, () => SelectedPrinter is { IsBuiltIn: false });

        DisplayWidth = Field("Display width", "0.###", UnitKind.Length,
            (printer, value) => printer with { DisplayWidthMm = Positive(value, printer.DisplayWidthMm) });
        DisplayHeight = Field("Display height", "0.###", UnitKind.Length,
            (printer, value) => printer with { DisplayHeightMm = Positive(value, printer.DisplayHeightMm) });
        PrintWidth = Field("Print width", "0.###", UnitKind.Length,
            (printer, value) => printer with { PrintWidthMm = Math.Min(printer.DisplayWidthMm, Positive(value, printer.BuildVolume.X)) });
        PrintHeight = Field("Print depth", "0.###", UnitKind.Length,
            (printer, value) => printer with { PrintHeightMm = Math.Min(printer.DisplayHeightMm, Positive(value, printer.BuildVolume.Y)) });
        ZTravel = Field("Z travel", "0.###", UnitKind.Length,
            (printer, value) => printer with { ZTravelMm = Positive(value, printer.ZTravelMm) });
        ResolutionX = Field("Resolution X", "0", UnitKind.Scalar,
            (printer, value) => printer with { ResolutionX = PositiveInt(value, printer.ResolutionX) }, "px");
        ResolutionY = Field("Resolution Y", "0", UnitKind.Scalar,
            (printer, value) => printer with { ResolutionY = PositiveInt(value, printer.ResolutionY) }, "px");
        FormatVersion = Field("Format version", "0", UnitKind.Scalar,
            (printer, value) => printer with { FormatVersion = PositiveUInt(value, printer.FormatVersion) });
        Fields = [DisplayWidth, DisplayHeight, PrintWidth, PrintHeight, ZTravel, ResolutionX, ResolutionY, FormatVersion];
        Refresh();
    }

    public IReadOnlyList<PrinterDefinition> Items => _config.Printers;
    public PrinterDefinition? SelectedPrinter =>
        _selectedIndex >= 0 && _selectedIndex < _config.Printers.Count
            ? _config.Printers[_selectedIndex]
            : null;

    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            if (value == _selectedIndex) return;
            _selectedIndex = value;
            Refresh();
        }
    }

    public bool HasSelectedPrinter => SelectedPrinter is not null;
    public bool IsBuiltInSelected => SelectedPrinter?.IsBuiltIn == true;
    public string CompatibilityNote => SelectedPrinter?.CompatibilityNote ?? "";
    public string FormatStatus
    {
        get
        {
            if (SelectedPrinter is not { } printer) return "";
            try
            {
                Core.IO.NativePrintWriter.ValidatePrinter(printer);
                return $"Native {printer.NativeFormat} export: .{printer.FileExtension}, version {printer.FormatVersion}. "
                    + Core.IO.NativePrintWriter.Limitations(printer);
            }
            catch (Exception ex) when (ex is NotSupportedException or InvalidOperationException)
            {
                return ex.Message;
            }
        }
    }

    public string NameDraft
    {
        get => _nameDraft;
        set
        {
            if (value == _nameDraft) return;
            _nameDraft = value;
            ValidationMessage = "";
            OnPropertyChanged();
        }
    }

    public string MachineName
    {
        get => SelectedPrinter?.MachineName ?? "";
        set => Edit(printer => printer with { MachineName = value });
    }

    public string FileExtension
    {
        get => SelectedPrinter?.FileExtension ?? "";
        set => Edit(printer => printer with { FileExtension = value });
    }

    public bool MirrorX
    {
        get => SelectedPrinter?.MirrorX == true;
        set => Edit(printer => printer with { MirrorX = value });
    }

    public bool MirrorY
    {
        get => SelectedPrinter?.MirrorY == true;
        set => Edit(printer => printer with { MirrorY = value });
    }

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

    public NumericField DisplayWidth { get; }
    public NumericField DisplayHeight { get; }
    public NumericField PrintWidth { get; }
    public NumericField PrintHeight { get; }
    public NumericField ZTravel { get; }
    public NumericField ResolutionX { get; }
    public NumericField ResolutionY { get; }
    public NumericField FormatVersion { get; }
    public IReadOnlyList<NumericField> Fields { get; }

    public IRelayCommand AddCommand { get; }
    public IRelayCommand DuplicateCommand { get; }
    public IRelayCommand RenameCommand { get; }
    public IRelayCommand DeleteCommand { get; }

    public event Action? Changed;

    private NumericField Field(string label, string format, UnitKind unit,
        Func<PrinterDefinition, double, PrinterDefinition> apply, string? suffix = null)
    {
        NumericField? field = null;
        field = new NumericField(label, unit, format, value =>
        {
            // Reject invalid dimensions before nullable usable-size defaults become an explicit copy.
            if (double.IsFinite(value) && value > 0)
                Edit(printer => apply(printer, value));
            RefreshFields();
        }, suffix);
        return field;
    }

    private void Add()
    {
        var printer = _config.AddPrinter(PrinterDefinition.PhotonMonoX.CreateUserCopy(
            UniqueName("New printer")));
        SelectAndSave(printer.Id);
    }

    private void Duplicate()
    {
        if (SelectedPrinter is not { } selected) return;
        var copy = _config.AddPrinter(selected.CreateUserCopy(UniqueName($"{selected.Name} copy")));
        SelectAndSave(copy.Id);
    }

    private void Rename()
    {
        if (SelectedPrinter is not { } selected) return;
        var name = NameDraft.Trim();
        if (name.Length == 0)
        {
            ValidationMessage = "Enter a printer name.";
            return;
        }
        if (_config.Printers.Any(printer => printer.Id != selected.Id &&
            string.Equals(printer.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            ValidationMessage = "That printer name is already in use.";
            return;
        }
        Edit(printer => printer with { Name = name });
    }

    private void Delete()
    {
        if (SelectedPrinter is not { IsBuiltIn: false } selected || !_config.DeletePrinter(selected.Id)) return;
        _selectedIndex = Math.Clamp(_selectedIndex, 0, _config.Printers.Count - 1);
        PersistAndRefresh();
    }

    private void Edit(Func<PrinterDefinition, PrinterDefinition> apply)
    {
        if (SelectedPrinter is not { } selected) return;
        var edited = apply(selected).Normalize();
        if (edited == selected) return;
        string selectedId;
        if (selected.IsBuiltIn)
        {
            var copyName = edited.Name == selected.Name ? UniqueName($"{selected.Name} copy") : edited.Name;
            selectedId = _config.AddPrinter(edited.CreateUserCopy(copyName)).Id;
        }
        else
        {
            if (!_config.ReplacePrinter(edited)) return;
            selectedId = edited.Id;
        }
        SelectAndSave(selectedId);
    }

    private void SelectAndSave(string id)
    {
        _selectedIndex = _config.Printers.FindIndex(printer => printer.Id == id);
        PersistAndRefresh();
    }

    private void PersistAndRefresh()
    {
        _save();
        Refresh();
        Changed?.Invoke();
    }

    private void Refresh()
    {
        OnPropertyChanged(nameof(Items));
        OnPropertyChanged(nameof(SelectedIndex));
        OnPropertyChanged(nameof(SelectedPrinter));
        OnPropertyChanged(nameof(HasSelectedPrinter));
        OnPropertyChanged(nameof(IsBuiltInSelected));
        OnPropertyChanged(nameof(CompatibilityNote));
        OnPropertyChanged(nameof(FormatStatus));
        OnPropertyChanged(nameof(MachineName));
        OnPropertyChanged(nameof(FileExtension));
        OnPropertyChanged(nameof(MirrorX));
        OnPropertyChanged(nameof(MirrorY));
        NameDraft = SelectedPrinter?.Name ?? "";
        ValidationMessage = "";
        RefreshFields();
        DuplicateCommand.NotifyCanExecuteChanged();
        RenameCommand.NotifyCanExecuteChanged();
        DeleteCommand.NotifyCanExecuteChanged();
    }

    private void RefreshFields()
    {
        if (SelectedPrinter is not { } printer) return;
        DisplayWidth.SetValue(printer.DisplayWidthMm);
        DisplayHeight.SetValue(printer.DisplayHeightMm);
        PrintWidth.SetValue(printer.BuildVolume.X);
        PrintHeight.SetValue(printer.BuildVolume.Y);
        ZTravel.SetValue(printer.ZTravelMm);
        ResolutionX.SetValue(printer.ResolutionX);
        ResolutionY.SetValue(printer.ResolutionY);
        FormatVersion.SetValue(printer.FormatVersion);
    }

    private bool HasSelection() => SelectedPrinter is not null;

    private string UniqueName(string baseName)
    {
        if (!_config.Printers.Any(printer =>
            string.Equals(printer.Name, baseName, StringComparison.OrdinalIgnoreCase))) return baseName;
        for (var suffix = 2; ; suffix++)
        {
            var candidate = $"{baseName} {suffix}";
            if (!_config.Printers.Any(printer =>
                string.Equals(printer.Name, candidate, StringComparison.OrdinalIgnoreCase))) return candidate;
        }
    }

    private static float Positive(double value, float fallback) =>
        double.IsFinite(value) && value > 0 && value <= float.MaxValue ? (float)value : fallback;

    private static int PositiveInt(double value, int fallback) =>
        double.IsFinite(value) && value >= 1 && value <= int.MaxValue
            ? Math.Max(1, (int)Math.Round(value))
            : fallback;

    private static uint PositiveUInt(double value, uint fallback) =>
        double.IsFinite(value) && value >= 1 && value <= uint.MaxValue
            ? Math.Max(1u, (uint)Math.Round(value))
            : fallback;
}
