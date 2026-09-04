using System.Numerics;
using Danslicer.Render;

namespace Danslicer.Tests;

public sealed class ViewCubeTests
{
    private static Matrix4x4 LookFrom(Vector3 eye) =>
        Matrix4x4.CreateLookAt(eye, Vector3.Zero, Vector3.UnitZ);

    [Fact]
    public void CentreOfTheCubeRectHitsTheFaceTowardTheCamera()
    {
        const int width = 1000, height = 800;
        var (rx, ry, size) = ViewCube.Rect(width, height, 1.0);
        var centreX = rx + size / 2f;
        var centreY = height - 1 - (ry + size / 2f); // pointer coords are top-left based

        // Looking from -Y: the -Y face points at the camera, region (0,-1,0).
        var region = ViewCube.HitRegion(centreX, centreY, width, height, 1.0, LookFrom(new(0, -10, 0)));
        Assert.Equal((0 + 1) * 9 + (-1 + 1) * 3 + (0 + 1), region);

        // Looking from +X: the +X face, region (1,0,0).
        region = ViewCube.HitRegion(centreX, centreY, width, height, 1.0, LookFrom(new(10, 0, 0)));
        Assert.Equal((1 + 1) * 9 + (0 + 1) * 3 + (0 + 1), region);
    }

    [Fact]
    public void PointsOutsideTheCornerRectMiss()
    {
        var view = LookFrom(new Vector3(0, -10, 0));
        Assert.Equal(-1, ViewCube.HitRegion(5, 5, 1000, 800, 1.0, view));
        Assert.Equal(-1, ViewCube.HitRegion(500, 400, 1000, 800, 1.0, view));
    }

    [Fact]
    public void ViewAnglesMatchTheNumpadViews()
    {
        // +X face = Right = SetView(0, 0); -Y face = Front = SetView(-90, 0).
        Assert.Equal((0f, 0f), ViewCube.ViewAngles((1 + 1) * 9 + 3 + 1, currentYawDegrees: 123f));
        var (yaw, pitch) = ViewCube.ViewAngles(9 + 0 + 1, currentYawDegrees: 123f);
        Assert.Equal(-90f, yaw, 3);
        Assert.Equal(0f, pitch, 3);
    }

    [Fact]
    public void PolesKeepTheCurrentYawAndCornersSplitTheAngles()
    {
        var top = (0 + 1) * 9 + (0 + 1) * 3 + (1 + 1);
        var (yaw, pitch) = ViewCube.ViewAngles(top, currentYawDegrees: 42f);
        Assert.Equal(42f, yaw, 3);
        Assert.Equal(90f, pitch, 3);

        var corner = (1 + 1) * 9 + (1 + 1) * 3 + (1 + 1); // (+1,+1,+1)
        (yaw, pitch) = ViewCube.ViewAngles(corner, currentYawDegrees: 0f);
        Assert.Equal(45f, yaw, 3);
        Assert.Equal(35.264f, pitch, 2);
    }
}
