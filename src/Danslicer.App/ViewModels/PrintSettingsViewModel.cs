using CommunityToolkit.Mvvm.ComponentModel;
using Danslicer.Core;
using Danslicer.Core.Slicing;
using Danslicer.Core.Utilities;

namespace Danslicer.App.ViewModels;

/// <summary>Expression-field bindings over <see cref="Document.PrintSettings"/>.</summary>
public sealed partial class PrintSettingsViewModel : ObservableObject
{
    private readonly Document _document;

    public NumericField LayerHeight { get; }
    public NumericField XyCompensation { get; }

    public IReadOnlyList<NumericField> Fields { get; }

    public PrintSettingsViewModel(Document document)
    {
        _document = document;
        LayerHeight = Field("Layer height", UnitKind.Length, "0.###", v => S with { LayerHeight = (float)v });
        XyCompensation = Field("XY compensation", UnitKind.Length, "0.###", v => S with { XyCompensation = (float)v });
        Fields = [LayerHeight, XyCompensation];
        Refresh();
    }

    private PrintSettings S => _document.PrintSettings;

    public bool AntiAliasing
    {
        get => S.AntiAliasing;
        set
        {
            if (S.AntiAliasing == value) return;
            _document.PrintSettings = S with { AntiAliasing = value };
            OnPropertyChanged();
        }
    }

    private NumericField Field(string label, UnitKind kind, string format, Func<double, PrintSettings> edit, string? suffix = null)
    {
        NumericField? field = null;
        field = new NumericField(label, kind, format, v =>
        {
            _document.PrintSettings = edit(v);
            Refresh();
        }, suffix);
        return field;
    }

    public void Refresh()
    {
        var s = S;
        LayerHeight.SetValue(s.LayerHeight);
        XyCompensation.SetValue(s.XyCompensation);
        OnPropertyChanged(nameof(AntiAliasing));
    }
}
