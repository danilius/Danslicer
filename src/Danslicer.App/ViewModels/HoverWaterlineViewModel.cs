using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Danslicer.App.ViewModels;

/// <summary>
/// Session-only state for the Support-mode hover contour. Pointer hit-testing owns the sampled
/// world Z; this model owns mode/toggle gating and the status-bar representation.
/// </summary>
public sealed class HoverWaterlineViewModel : ObservableObject
{
    private bool _enabled = true;
    private bool _supportModeActive;
    private float? _hoverZ;

    public event Action? Changed;

    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (!SetProperty(ref _enabled, value)) return;
            if (!value) _hoverZ = null;
            NotifyDerivedState();
        }
    }

    public bool SupportModeActive
    {
        get => _supportModeActive;
        set
        {
            if (!SetProperty(ref _supportModeActive, value)) return;
            if (!value) _hoverZ = null;
            NotifyDerivedState();
        }
    }

    public bool IsActive => _enabled && _supportModeActive && _hoverZ.HasValue;

    public float? WorldZ => IsActive ? _hoverZ : null;

    public string? StatusText => WorldZ is { } z
        ? string.Create(CultureInfo.InvariantCulture, $"Waterline Z: {z:0.###} mm")
        : null;

    public void UpdateHover(float? worldZ)
    {
        float? next = _enabled && _supportModeActive && worldZ is { } z && float.IsFinite(z)
            ? z
            : null;
        if (_hoverZ == next) return;
        _hoverZ = next;
        NotifyDerivedState();
    }

    public void Clear() => UpdateHover(null);

    private void NotifyDerivedState()
    {
        OnPropertyChanged(nameof(IsActive));
        OnPropertyChanged(nameof(WorldZ));
        OnPropertyChanged(nameof(StatusText));
        Changed?.Invoke();
    }
}
