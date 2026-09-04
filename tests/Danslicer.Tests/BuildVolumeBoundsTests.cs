using System.Numerics;
using Danslicer.Core.Geometry;
using Danslicer.Core.Slicing;

namespace Danslicer.Tests;

public sealed class BuildVolumeBoundsTests
{
    private static readonly Vector3 Volume = new(20, 10, 30);

    [Theory]
    [InlineData(0, 0, 0, BuildVolumeViolationAxes.None)]
    [InlineData(10.001f, 0, 0, BuildVolumeViolationAxes.None)]
    [InlineData(10.002f, 0, 0, BuildVolumeViolationAxes.X)]
    [InlineData(0, -5.002f, 0, BuildVolumeViolationAxes.Y)]
    [InlineData(0, 0, -0.002f, BuildVolumeViolationAxes.Z)]
    [InlineData(0, 0, 30.002f, BuildVolumeViolationAxes.Z)]
    [InlineData(10.1f, -5.1f, 31, BuildVolumeViolationAxes.X | BuildVolumeViolationAxes.Y | BuildVolumeViolationAxes.Z)]
    public void PointClassificationMirrorsShaderRegion(float x, float y, float z,
        BuildVolumeViolationAxes expected)
    {
        Assert.Equal(expected, BuildVolumeBounds.CheckPoint(new Vector3(x, y, z), Volume));
    }

    [Fact]
    public void BoundsClassificationAndAxisTextCoverEverySide()
    {
        var bounds = new Aabb(new Vector3(-11, -6, -1), new Vector3(12, 4, 31));

        var axes = BuildVolumeBounds.Check(bounds, Volume);

        Assert.Equal(BuildVolumeViolationAxes.X | BuildVolumeViolationAxes.Y | BuildVolumeViolationAxes.Z, axes);
        Assert.Equal("X, Y and Z", BuildVolumeBounds.FormatAxes(axes));
        Assert.Equal("Warning: content outside the build area on X, Y and Z was cropped.",
            BuildVolumeBounds.CroppedWarning(axes));
    }
}
