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
    public NumericField BottomLayers { get; }
    public NumericField BottomExposure { get; }
    public NumericField Exposure { get; }
    public NumericField LightOffDelay { get; }
    public NumericField LiftHeight { get; }
    public NumericField LiftSpeed { get; }
    public NumericField RetractSpeed { get; }
    public NumericField BottomLiftHeight { get; }
    public NumericField BottomLiftSpeed { get; }
    public NumericField XyCompensation { get; }

    public IReadOnlyList<NumericField> Fields { get; }

    public PrintSettingsViewModel(Document document)
    {
        _document = document;
        LayerHeight = Field("Layer height", UnitKind.Length, "0.###", v => S with { LayerHeight = (float)v });
        BottomLayers = Field("Bottom layers", UnitKind.Scalar, "0", v => S with { BottomLayers = Math.Max(0, (int)Math.Round(v)) });
        BottomExposure = Field("Bottom exposure", UnitKind.Scalar, "0.##", v => S with { BottomExposure = (float)v }, "s");
        Exposure = Field("Exposure", UnitKind.Scalar, "0.##", v => S with { Exposure = (float)v }, "s");
        LightOffDelay = Field("Light-off delay", UnitKind.Scalar, "0.##", v => S with { LightOffDelay = (float)v }, "s");
        LiftHeight = Field("Lift height", UnitKind.Length, "0.##", v => S with { LiftHeight = (float)v });
        LiftSpeed = Field("Lift speed", UnitKind.Scalar, "0.#", v => S with { LiftSpeed = (float)v }, "mm/min");
        RetractSpeed = Field("Retract speed", UnitKind.Scalar, "0.#", v => S with { RetractSpeed = (float)v }, "mm/min");
        BottomLiftHeight = Field("Bottom lift height", UnitKind.Length, "0.##", v => S with { BottomLiftHeight = (float)v });
        BottomLiftSpeed = Field("Bottom lift speed", UnitKind.Scalar, "0.#", v => S with { BottomLiftSpeed = (float)v }, "mm/min");
        XyCompensation = Field("XY compensation", UnitKind.Length, "0.###", v => S with { XyCompensation = (float)v });
        Fields = new[]
        {
            LayerHeight, BottomLayers, BottomExposure, Exposure, LightOffDelay,
            LiftHeight, LiftSpeed, RetractSpeed, BottomLiftHeight, BottomLiftSpeed, XyCompensation,
        };
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
        BottomLayers.SetValue(s.BottomLayers);
        BottomExposure.SetValue(s.BottomExposure);
        Exposure.SetValue(s.Exposure);
        LightOffDelay.SetValue(s.LightOffDelay);
        LiftHeight.SetValue(s.LiftHeight);
        LiftSpeed.SetValue(s.LiftSpeed);
        RetractSpeed.SetValue(s.RetractSpeed);
        BottomLiftHeight.SetValue(s.BottomLiftHeight);
        BottomLiftSpeed.SetValue(s.BottomLiftSpeed);
        XyCompensation.SetValue(s.XyCompensation);
        OnPropertyChanged(nameof(AntiAliasing));
    }
}
