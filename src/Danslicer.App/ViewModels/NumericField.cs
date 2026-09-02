using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Danslicer.Core.Utilities;

namespace Danslicer.App.ViewModels;

/// <summary>
/// A numeric property field that accepts expressions with units ("10 + 2.5cm", "45deg").
/// The view sets <see cref="Text"/> on commit; the model refreshes it via <see cref="SetValue"/>.
/// </summary>
public sealed partial class NumericField : ObservableObject
{
    private readonly UnitKind _kind;
    private readonly string _format;
    private readonly Action<double> _apply;
    private double _value;
    private string _text = "";

    public NumericField(string label, UnitKind kind, string format, Action<double> apply)
    {
        Label = label;
        _kind = kind;
        _format = format;
        _apply = apply;
    }

    public string Label { get; }

    public string Suffix => _kind switch
    {
        UnitKind.Length => "mm",
        UnitKind.Angle => "°",
        _ => "",
    };

    public string Text
    {
        get => _text;
        set
        {
            if (value == _text) return;
            if (ExpressionParser.TryEvaluate(value, _kind, out var parsed))
            {
                _apply(parsed);
                // The model round-trips the value through SetValue; if it did not (no change), restore formatting.
                if (Math.Abs(parsed - _value) < 1e-9) SetValue(_value);
            }
            else
            {
                // Reject: re-publish the old text so the box snaps back.
                OnPropertyChanged(nameof(Text));
            }
        }
    }

    public void SetValue(double value)
    {
        if (Math.Abs(value) < 1e-9) value = 0; // avoid "-0"
        _value = value;
        var text = value.ToString(_format, CultureInfo.InvariantCulture);
        if (text != _text)
        {
            _text = text;
            OnPropertyChanged(nameof(Text));
        }
        else
        {
            // Force the view to refresh even when identical, e.g. after a rejected edit.
            OnPropertyChanged(nameof(Text));
        }
    }
}
