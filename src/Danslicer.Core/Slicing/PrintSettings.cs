namespace Danslicer.Core.Slicing;

/// <summary>
/// Exposure and motion settings for a print. Lengths in millimetres, times in seconds,
/// speeds in millimetres per minute (the unit printers and resin vendors quote).
/// </summary>
public sealed record PrintSettings
{
    public float LayerHeight { get; init; } = 0.05f;
    public int BottomLayers { get; init; } = 5;
    public float BottomExposure { get; init; } = 30f;
    public float Exposure { get; init; } = 2.0f;
    /// <summary>Wait before cure, often called light-off delay.</summary>
    public float LightOffDelay { get; init; } = 2.5f;
    public float LiftHeight { get; init; } = 8f;
    public float LiftSpeed { get; init; } = 120f;
    public float RetractSpeed { get; init; } = 180f;
    public float BottomLiftHeight { get; init; } = 8f;
    public float BottomLiftSpeed { get; init; } = 120f;
    /// <summary>Anti-aliased layer edges. The Photon Workshop format stores 16 grey levels.</summary>
    public bool AntiAliasing { get; init; } = true;
    /// <summary>Contour offset applied to every layer, negative shrinks. Compensates for light bleed.</summary>
    public float XyCompensation { get; init; } = 0f;

    public static PrintSettings Default { get; } = new();

    public float ExposureForLayer(int index) => index < BottomLayers ? BottomExposure : Exposure;
    public float LiftHeightForLayer(int index) => index < BottomLayers ? BottomLiftHeight : LiftHeight;
    public float LiftSpeedForLayer(int index) => index < BottomLayers ? BottomLiftSpeed : LiftSpeed;

    /// <summary>Rough print time in seconds for the given layer count.</summary>
    public double EstimatePrintTime(int layerCount)
    {
        double total = 0;
        for (int i = 0; i < layerCount; i++)
        {
            var lift = LiftHeightForLayer(i);
            var liftSpeed = Math.Max(LiftSpeedForLayer(i), 1f) / 60.0;
            var retract = Math.Max(RetractSpeed, 1f) / 60.0;
            total += ExposureForLayer(i) + LightOffDelay + lift / liftSpeed + lift / retract;
        }
        return total;
    }
}
