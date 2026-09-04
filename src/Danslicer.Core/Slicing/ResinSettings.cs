namespace Danslicer.Core.Slicing;

/// <summary>
/// Cure and peel behaviour supplied by a resin recipe. Lengths are millimetres, times are
/// seconds, and speeds are millimetres per minute.
/// </summary>
public sealed record ResinSettings
{
    public int BottomLayers { get; init; } = 5;
    public float BottomExposure { get; init; } = 30f;
    public float Exposure { get; init; } = 2f;
    /// <summary>Wait before cure, often called light-off delay.</summary>
    public float LightOffDelay { get; init; } = 2.5f;
    public float LiftHeight { get; init; } = 8f;
    public float LiftSpeed { get; init; } = 120f;
    public float RetractSpeed { get; init; } = 180f;
    public float BottomLiftHeight { get; init; } = 8f;
    public float BottomLiftSpeed { get; init; } = 120f;

    public static ResinSettings Default { get; } = new();

    public float ExposureForLayer(int index) => index < BottomLayers ? BottomExposure : Exposure;
    public float LiftHeightForLayer(int index) => index < BottomLayers ? BottomLiftHeight : LiftHeight;
    public float LiftSpeedForLayer(int index) => index < BottomLayers ? BottomLiftSpeed : LiftSpeed;

    /// <summary>Rough print time in seconds for the given layer count.</summary>
    public double EstimatePrintTime(int layerCount)
    {
        double total = 0;
        for (var i = 0; i < layerCount; i++)
        {
            var lift = LiftHeightForLayer(i);
            var liftSpeed = Math.Max(LiftSpeedForLayer(i), 1f) / 60.0;
            var retract = Math.Max(RetractSpeed, 1f) / 60.0;
            total += ExposureForLayer(i) + LightOffDelay + lift / liftSpeed + lift / retract;
        }
        return total;
    }

    public ResinSettings Normalize() => this with
    {
        BottomLayers = Math.Max(0, BottomLayers),
        BottomExposure = NonNegative(BottomExposure, Default.BottomExposure),
        Exposure = NonNegative(Exposure, Default.Exposure),
        LightOffDelay = NonNegative(LightOffDelay, Default.LightOffDelay),
        LiftHeight = NonNegative(LiftHeight, Default.LiftHeight),
        LiftSpeed = Positive(LiftSpeed, Default.LiftSpeed),
        RetractSpeed = Positive(RetractSpeed, Default.RetractSpeed),
        BottomLiftHeight = NonNegative(BottomLiftHeight, Default.BottomLiftHeight),
        BottomLiftSpeed = Positive(BottomLiftSpeed, Default.BottomLiftSpeed),
    };

    private static float Positive(float value, float fallback) =>
        float.IsFinite(value) && value > 0 ? value : fallback;

    private static float NonNegative(float value, float fallback) =>
        float.IsFinite(value) && value >= 0 ? value : fallback;
}

/// <summary>A named, versioned resin recipe suitable for user config and project embedding.</summary>
public sealed record ResinPreset
{
    public const int CurrentVersion = 1;
    public const string DefaultId = "default-resin";

    public int Version { get; init; } = CurrentVersion;
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Name { get; init; } = "Resin";
    public ResinSettings Settings { get; init; } = ResinSettings.Default;

    public static ResinPreset Default => new()
    {
        Id = DefaultId,
        Name = "Default",
        Settings = ResinSettings.Default,
    };

    public ResinPreset Normalize()
    {
        var id = string.IsNullOrWhiteSpace(Id) ? Guid.NewGuid().ToString("N") : Id.Trim();
        return this with
        {
            Version = Math.Max(1, Version),
            Id = id,
            Name = string.IsNullOrWhiteSpace(Name) ? "Unnamed resin" : Name.Trim(),
            Settings = (Settings ?? ResinSettings.Default).Normalize(),
        };
    }
}
