using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Danslicer.Core;
using Danslicer.Core.Geometry;
using Danslicer.Core.Utilities;

namespace Danslicer.App.ViewModels;

/// <summary>Session-only lower/upper layer range with expression-capable numeric fields.</summary>
public sealed class LayerRangeClipViewModel : ObservableObject
{
    private const double Epsilon = 1e-6;
    private double _minimumZ;
    private double _maximumZ = 1;
    private double _lowerZ;
    private double _upperZ = 1;
    private bool _active;

    public LayerRangeClipViewModel()
    {
        LowerField = new NumericField("Lower", UnitKind.Length, "0.###", value => LowerZ = value);
        UpperField = new NumericField("Upper", UnitKind.Length, "0.###", value => UpperZ = value);
        ResetCommand = new RelayCommand(Reset);
        RefreshFields();
    }

    public event Action? Changed;

    public double MinimumZ => _minimumZ;
    public double MaximumZ => _maximumZ;

    public double LowerZ
    {
        get => _lowerZ;
        set => SetRange(Math.Clamp(value, _minimumZ, _upperZ), _upperZ);
    }

    public double UpperZ
    {
        get => _upperZ;
        set => SetRange(_lowerZ, Math.Clamp(value, _lowerZ, _maximumZ));
    }

    public bool Active
    {
        get => _active;
        set
        {
            if (!SetProperty(ref _active, value)) return;
            Changed?.Invoke();
        }
    }

    public NumericField LowerField { get; }
    public NumericField UpperField { get; }
    public ICommand ResetCommand { get; }

    public ViewportClipRange Range => new((float)_minimumZ, (float)_maximumZ,
        (float)_lowerZ, (float)_upperZ, _active);

    /// <summary>
    /// Tracks model-transform changes while keeping a handle attached to an old endpoint attached
    /// to the corresponding new endpoint. Interior values remain world-space layer heights.
    /// </summary>
    public void RefreshBounds(Aabb bounds, double fallbackMaximum, bool reset = false)
    {
        double minimum = bounds.IsEmpty ? 0 : bounds.Min.Z;
        double maximum = bounds.IsEmpty ? Math.Max(1, fallbackMaximum) : bounds.Max.Z;
        if (!double.IsFinite(minimum) || !double.IsFinite(maximum)) return;
        if (maximum - minimum < 0.001)
        {
            minimum -= 0.5;
            maximum += 0.5;
        }

        var lowerAtEdge = Math.Abs(_lowerZ - _minimumZ) <= Epsilon;
        var upperAtEdge = Math.Abs(_upperZ - _maximumZ) <= Epsilon;
        var oldLower = _lowerZ;
        var oldUpper = _upperZ;
        var boundsChanged = Math.Abs(_minimumZ - minimum) > Epsilon ||
            Math.Abs(_maximumZ - maximum) > Epsilon;
        if (!boundsChanged && !reset) return;

        _minimumZ = minimum;
        _maximumZ = maximum;
        OnPropertyChanged(nameof(MinimumZ));
        OnPropertyChanged(nameof(MaximumZ));
        var lower = reset || lowerAtEdge ? minimum : Math.Clamp(oldLower, minimum, maximum);
        var upper = reset || upperAtEdge ? maximum : Math.Clamp(oldUpper, minimum, maximum);
        if (lower > upper) lower = upper;
        SetRange(lower, upper, forceNotify: true);
    }

    public void Reset() => SetRange(_minimumZ, _maximumZ);

    private void SetRange(double lower, double upper, bool forceNotify = false)
    {
        // Slider maths can land a hair under zero, which the fields then show as "-0".
        if (Math.Abs(lower) < Epsilon) lower = 0;
        if (Math.Abs(upper) < Epsilon) upper = 0;
        var lowerChanged = Math.Abs(_lowerZ - lower) > Epsilon;
        var upperChanged = Math.Abs(_upperZ - upper) > Epsilon;
        if (!lowerChanged && !upperChanged && !forceNotify)
        {
            RefreshFields();
            return;
        }

        _lowerZ = lower;
        _upperZ = upper;
        if (lowerChanged || forceNotify) OnPropertyChanged(nameof(LowerZ));
        if (upperChanged || forceNotify) OnPropertyChanged(nameof(UpperZ));
        RefreshFields();
        Changed?.Invoke();
    }

    private void RefreshFields()
    {
        LowerField.SetValue(_lowerZ);
        UpperField.SetValue(_upperZ);
    }
}
