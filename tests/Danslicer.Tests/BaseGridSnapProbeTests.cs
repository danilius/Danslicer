using System.Numerics;
using Danslicer.Core;
using Danslicer.Core.Config;
using Danslicer.Core.Geometry;
using Danslicer.Core.Scene;
using Danslicer.Core.Supports;
using Xunit;

namespace Danslicer.Tests;

/// <summary>
/// User report 2026-09-09: "snap to grid for supports does not seem to be working anymore".
/// These pin down what the Core does: a manual placement with the base grid on lands its
/// base on a grid point, with or without a base disc.
/// </summary>
public class BaseGridSnapProbeTests
{
    private static (Document Document, SceneObject Object) Slab(SupportConfig settings)
    {
        var document = new Document { PlacementMode = PlacementMode.Off };
        document.SupportSettings = settings;
        var obj = new SceneObject("slab", new Mesh(
            [new(-30, -30, 20), new(30, -30, 20), new(-30, 30, 20)], [0, 1, 2]));
        document.AddObject(obj);
        document.Select(obj);
        return (document, obj);
    }

    private static void AssertOnGrid(SupportNode foot, float pitch)
    {
        Assert.Equal(0f, MathF.Abs(foot.Position.X / pitch - MathF.Round(foot.Position.X / pitch)) * pitch, 3);
        Assert.Equal(0f, MathF.Abs(foot.Position.Y / pitch - MathF.Round(foot.Position.Y / pitch)) * pitch, 3);
    }

    [Theory]
    [InlineData(SupportBaseShape.Disc)]
    [InlineData(SupportBaseShape.None)]
    public void AManualSupportsBaseSnapsToTheGrid(SupportBaseShape baseShape)
    {
        var settings = new SupportConfig
        {
            UseBaseGrid = true, BaseGridPitch = 6f, BaseShape = baseShape, AutoBracing = false, AutoParenting = false,
        };
        var (document, obj) = Slab(settings);

        Assert.True(document.AddManualSupport(obj, new Vector3(7.3f, -8.1f, 20), -Vector3.UnitZ));

        var foot = Assert.Single(document.Supports.Nodes, n => n.Type == SupportNodeType.Base);
        AssertOnGrid(foot, 6f);
        Assert.NotEqual(7.3f, foot.Position.X, 3);
    }

    [Fact]
    public void WithTheGridOffTheBaseStaysUnderTheContact()
    {
        var settings = new SupportConfig { UseBaseGrid = false, BaseGridPitch = 6f, AutoBracing = false, AutoParenting = false };
        var (document, obj) = Slab(settings);

        Assert.True(document.AddManualSupport(obj, new Vector3(7.3f, -8.1f, 20), -Vector3.UnitZ));

        var foot = Assert.Single(document.Supports.Nodes, n => n.Type == SupportNodeType.Base);
        Assert.Equal(7.3f, foot.Position.X, 2);
        Assert.Equal(-8.1f, foot.Position.Y, 2);
    }
}
