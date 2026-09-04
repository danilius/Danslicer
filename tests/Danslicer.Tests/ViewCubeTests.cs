using System.Numerics;
using Danslicer.Core.Config;
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

    // ----- Configurable size (min/max/default and a size in between) -----

    [Theory]
    [InlineData(ViewCube.MinSizePixels)]
    [InlineData(ViewCube.DefaultSizePixels)]
    [InlineData(140)]
    [InlineData(ViewCube.MaxSizePixels)]
    public void RectGrowsWithTheConfiguredSizeAndStaysInsideTheFramebuffer(int sizePixels)
    {
        const int width = 1000, height = 800;
        var (rx, ry, size) = ViewCube.Rect(width, height, 1.0, sizePixels);

        Assert.Equal(sizePixels, size);
        Assert.InRange(rx, 0, width - size);
        Assert.InRange(ry, 0, height - size);
    }

    [Theory]
    [InlineData(ViewCube.MinSizePixels)]
    [InlineData(ViewCube.DefaultSizePixels)]
    [InlineData(ViewCube.MaxSizePixels)]
    public void HitTestingStaysCorrectAtEverySize(int sizePixels)
    {
        const int width = 1000, height = 800;
        var (rx, ry, size) = ViewCube.Rect(width, height, 1.0, sizePixels);
        var centreX = rx + size / 2f;
        var centreY = height - 1 - (ry + size / 2f);

        var region = ViewCube.HitRegion(centreX, centreY, width, height, 1.0,
            LookFrom(new(0, -10, 0)), sizePixels);
        Assert.Equal((0 + 1) * 9 + (-1 + 1) * 3 + (0 + 1), region); // -Y face, i.e. Front

        // A pixel just outside the (bigger or smaller) rect still misses.
        Assert.Equal(-1, ViewCube.HitRegion(rx - 1, centreY, width, height, 1.0,
            LookFrom(new(0, -10, 0)), sizePixels));
    }

    [Theory]
    [InlineData(ViewCube.MinSizePixels)]
    [InlineData(ViewCube.DefaultSizePixels)]
    [InlineData(ViewCube.MaxSizePixels)]
    public void EachFaceMapsToItsSnapViewRegardlessOfCubeSize(int sizePixels)
    {
        const int width = 1000, height = 800;
        var (rx, ry, size) = ViewCube.Rect(width, height, 1.0, sizePixels);
        var centreX = rx + size / 2f;
        var centreY = height - 1 - (ry + size / 2f);

        // Front (-Y): centre-of-rect hit while looking from -Y.
        var region = ViewCube.HitRegion(centreX, centreY, width, height, 1.0,
            LookFrom(new(0, -10, 0)), sizePixels);
        var (yaw, pitch) = ViewCube.ViewAngles(region, currentYawDegrees: 0f);
        Assert.Equal(-90f, yaw, 3);
        Assert.Equal(0f, pitch, 3);

        // Top (+Z): centre-of-rect hit while looking from +Z (a Y-up look-at, since Z-up is
        // degenerate when looking straight down the Z axis).
        var lookFromTop = Matrix4x4.CreateLookAt(new Vector3(0, 0, 10), Vector3.Zero, Vector3.UnitY);
        region = ViewCube.HitRegion(centreX, centreY, width, height, 1.0, lookFromTop, sizePixels);
        (yaw, pitch) = ViewCube.ViewAngles(region, currentYawDegrees: 17f);
        Assert.Equal(17f, yaw, 3); // pole keeps the current yaw
        Assert.Equal(90f, pitch, 3);
    }

    [Fact]
    public void ViewCubeSizeConfigDefaultsAndRoundTripsAndClampsOutOfRangeValues()
    {
        Assert.Equal(96, new ViewportConfig().ViewCubeSizePixels);

        var dir = Path.Combine(Path.GetTempPath(), "danslicer-viewcube-size-tests",
            Guid.NewGuid().ToString("N"));
        var path = Path.Combine(dir, "config.json");
        try
        {
            var config = new UserConfig { Viewport = new ViewportConfig { ViewCubeSizePixels = 140 } };
            config.Save(path);
            Assert.Equal(140, UserConfig.Load(path).Viewport.ViewCubeSizePixels);

            config = new UserConfig { Viewport = new ViewportConfig { ViewCubeSizePixels = 4 } };
            config.Save(path);
            Assert.Equal(ViewCube.MinSizePixels, UserConfig.Load(path).Viewport.ViewCubeSizePixels);

            config = new UserConfig { Viewport = new ViewportConfig { ViewCubeSizePixels = 5000 } };
            config.Save(path);
            Assert.Equal(ViewCube.MaxSizePixels, UserConfig.Load(path).Viewport.ViewCubeSizePixels);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
        }
    }
}
