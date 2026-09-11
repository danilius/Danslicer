using System.Globalization;
using Danslicer.Core.Slicing;

namespace Danslicer.App.ViewModels;

/// <summary>Presentation of completed slice metadata; never starts geometry work.</summary>
public static class SliceEstimate
{
    public const string Assumptions = "Theoretical resin: sliced model, support and raft union inside the build area; excludes tank fill, waste and shrinkage. AA grey levels are coverage, not measured cure volume. Time: bottom/normal exposures + lift/retract at configured speeds + light-off delay as an additional wait per layer. No transition schedule is supported. Firmware delay interpretation, acceleration, homing and finishing overhead are unknown.";

    public static string Volume(SliceResult? slice) => slice is { LayerCount: > 0 }
        && float.IsFinite(slice.VolumeMl) && slice.VolumeMl >= 0
        ? slice.VolumeMl.ToString("0.00", CultureInfo.CurrentCulture) + " mL" : "—";

    public static string Duration(SliceResult? slice)
    {
        if (slice is not { LayerCount: > 0 }) return "—";
        var seconds = slice.EstimatedSeconds;
        if (!double.IsFinite(seconds) || seconds < 0 || seconds > TimeSpan.MaxValue.TotalSeconds - 60) return "Unavailable";
        // Round up to a minute: second-level precision would imply firmware knowledge we lack.
        var minutes = (long)Math.Ceiling(seconds / 60);
        return minutes == 0 ? "0 min" : minutes < 60 ? $"≈ {minutes} min" : $"≈ {minutes / 60} h {minutes % 60:00} min";
    }
}
