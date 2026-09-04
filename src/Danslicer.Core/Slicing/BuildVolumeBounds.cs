using System.Numerics;
using Danslicer.Core.Geometry;

namespace Danslicer.Core.Slicing;

[Flags]
public enum BuildVolumeViolationAxes
{
    None = 0,
    X = 1,
    Y = 2,
    Z = 4,
}

/// <summary>
/// CPU-side build-volume classification mirrored by the viewport mesh shaders. The LCD is
/// centred on X/Y, while printable Z runs from the plate at zero through the printer travel.
/// </summary>
public static class BuildVolumeBounds
{
    public const float ToleranceMm = 0.001f;

    public static BuildVolumeViolationAxes CheckPoint(Vector3 point, Vector3 buildVolume,
        float toleranceMm = ToleranceMm)
    {
        var axes = BuildVolumeViolationAxes.None;
        if (MathF.Abs(point.X) > buildVolume.X * 0.5f + toleranceMm)
            axes |= BuildVolumeViolationAxes.X;
        if (MathF.Abs(point.Y) > buildVolume.Y * 0.5f + toleranceMm)
            axes |= BuildVolumeViolationAxes.Y;
        if (point.Z < -toleranceMm || point.Z > buildVolume.Z + toleranceMm)
            axes |= BuildVolumeViolationAxes.Z;
        return axes;
    }

    public static BuildVolumeViolationAxes Check(Aabb bounds, Vector3 buildVolume,
        float toleranceMm = ToleranceMm)
    {
        if (bounds.IsEmpty) return BuildVolumeViolationAxes.None;

        var axes = BuildVolumeViolationAxes.None;
        var halfX = buildVolume.X * 0.5f;
        var halfY = buildVolume.Y * 0.5f;
        if (bounds.Min.X < -halfX - toleranceMm || bounds.Max.X > halfX + toleranceMm)
            axes |= BuildVolumeViolationAxes.X;
        if (bounds.Min.Y < -halfY - toleranceMm || bounds.Max.Y > halfY + toleranceMm)
            axes |= BuildVolumeViolationAxes.Y;
        if (bounds.Min.Z < -toleranceMm || bounds.Max.Z > buildVolume.Z + toleranceMm)
            axes |= BuildVolumeViolationAxes.Z;
        return axes;
    }

    public static string FormatAxes(BuildVolumeViolationAxes axes)
    {
        var names = new List<string>(3);
        if ((axes & BuildVolumeViolationAxes.X) != 0) names.Add("X");
        if ((axes & BuildVolumeViolationAxes.Y) != 0) names.Add("Y");
        if ((axes & BuildVolumeViolationAxes.Z) != 0) names.Add("Z");
        return names.Count switch
        {
            0 => "none",
            1 => names[0],
            2 => $"{names[0]} and {names[1]}",
            _ => $"{names[0]}, {names[1]} and {names[2]}",
        };
    }

    public static string CroppedWarning(BuildVolumeViolationAxes axes) =>
        $"Warning: content outside the build area on {FormatAxes(axes)} was cropped.";
}
