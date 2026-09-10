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
    private double _layerHeightMm = 0.05;
    private bool _active;
    private bool _isDragging;

    public LayerRangeClipViewModel()
    {
        // The boxes are layer numbers, not millimetres: a clip plane is a place in the print,
        // and "1288" is a thing you can find in the sliced preview in a way "64.412" is not.
        LowerField = new NumericField("Lower", UnitKind.Scalar, "0",
            value => LowerZ = ZOfLayer(value, _layerHeightMm), suffix: "");
        UpperField = new NumericField("Upper", UnitKind.Scalar, "0",
            value => UpperZ = ZOfLayer(value, _layerHeightMm), suffix: "");
        LowerMmField = new NumericField("Height", UnitKind.Length, "0.###", value => LowerZ = value, suffix: "mm");
        UpperMmField = new NumericField("Height", UnitKind.Length, "0.###", value => UpperZ = value, suffix: "mm");
        LowerField.EnableSessionPreview();
        UpperField.EnableSessionPreview();
        LowerMmField.EnableSessionPreview();
        UpperMmField.EnableSessionPreview();
        ResetCommand = new RelayCommand(Reset);
        RefreshFields();
    }

    /// <summary>
    /// The print's layer height, which turns a clip height into a layer number. Set from the
    /// document's print settings; a change re-labels the boxes without moving the planes.
    /// </summary>
    public double LayerHeightMm
    {
        get => _layerHeightMm;
        set
        {
            if (!double.IsFinite(value) || value <= 0 ||
                Math.Abs(_layerHeightMm - value) <= Epsilon) return;
            _layerHeightMm = value;
            OnPropertyChanged(); // Keeps the slider keyboard step aligned with print layer thickness.
            RefreshFields();
            OnPropertyChanged(nameof(LowerLayer));
            OnPropertyChanged(nameof(UpperLayer));
        }
    }

    /// <summary>
    /// The layer a height falls in, numbering the first printed layer above the plate as 1: layer
    /// n occupies (n-1)h to nh, so a plane at exactly nh is the top of layer n. Heights below the
    /// plate give zero or negative numbers rather than being hidden — a model dragged under the
    /// plate is the user's business, and the panel should say so plainly.
    /// </summary>
    /// <remarks>The tolerance is a ten-thousandth of a LAYER, not of a millimetre: heights
    /// arrive as float bounds, whose noise at 64 mm is larger than a millimetre epsilon would
    /// absorb, and rounding a plane sitting exactly on a layer boundary up to the next layer
    /// reads as an off-by-one to anyone comparing with the sliced preview.</remarks>
    public static int LayerAt(double z, double layerHeightMm) =>
        layerHeightMm <= 0 ? 0 : (int)Math.Ceiling(z / layerHeightMm - 1e-4);

    /// <summary>The height of the top of a layer: the inverse of <see cref="LayerAt"/>.</summary>
    public static double ZOfLayer(double layer, double layerHeightMm) =>
        layerHeightMm <= 0 ? 0 : layer * layerHeightMm;

    public int LowerLayer => LayerAt(_lowerZ, _layerHeightMm);
    public int UpperLayer => LayerAt(_upperZ, _layerHeightMm);

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

    /// <summary>True only while either range thumb is captured.</summary>
    public bool IsDragging
    {
        get => _isDragging;
        set => SetProperty(ref _isDragging, value);
    }

    public NumericField LowerField { get; }
    public NumericField UpperField { get; }
    public NumericField LowerMmField { get; }
    public NumericField UpperMmField { get; }
    public ICommand ResetCommand { get; }

    public ViewportClipRange Range => new((float)_minimumZ, (float)_maximumZ,
        (float)_lowerZ, (float)_upperZ, _active);

    /// <summary>
    /// Tracks model-transform changes while keeping a handle attached to an old endpoint attached
    /// to the corresponding new endpoint. Interior values remain world-space layer heights.
    /// </summary>
    public void RefreshBounds(Aabb bounds, double fallbackMaximum, bool reset = false)
    {
        // The plate is layer zero and the bottom of the print, so the range always starts there:
        // supports run down to the plate below any model, and a lower handle parked at the
        // model's underside hid their feet the moment the top handle moved (user, 2026-09-09).
        // Geometry under the plate is the user's business — the build-volume warning is what
        // complains about it — but a clip handle numbered in negative layers is just a broken
        // ruler. Only the top follows the models.
        const double minimum = 0;
        double maximum = bounds.IsEmpty ? Math.Max(1, fallbackMaximum) : Math.Max(0, bounds.Max.Z);
        if (!double.IsFinite(minimum) || !double.IsFinite(maximum)) return;
        if (maximum - minimum < 0.001)
        {
            // A degenerate range (a flat model, or one dragged entirely below the plate, whose
            // clamped bounds collapse onto zero) is opened out so the slider still has somewhere
            // to travel — upwards only, because the plate is still the floor.
            maximum = minimum + 1;
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
        if (lowerChanged || forceNotify)
        {
            OnPropertyChanged(nameof(LowerZ));
            OnPropertyChanged(nameof(LowerLayer));
        }
        if (upperChanged || forceNotify)
        {
            OnPropertyChanged(nameof(UpperZ));
            OnPropertyChanged(nameof(UpperLayer));
        }
        RefreshFields();
        Changed?.Invoke();
    }

    private void RefreshFields()
    {
        LowerMmField.SetValue(_lowerZ);
        UpperMmField.SetValue(_upperZ);
        LowerField.SetValue(LayerAt(_lowerZ, _layerHeightMm));
        UpperField.SetValue(LayerAt(_upperZ, _layerHeightMm));
    }
}
