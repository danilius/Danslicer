using System.Numerics;
using Danslicer.Core.Geometry;
using Danslicer.Core.Supports;
using Danslicer.Core.Supports.Generation;
using Xunit;

namespace Danslicer.Tests;

/// <summary>
/// Detect Islands uses the strict definition (user, 2026-09-09): an island is a place where
/// printing starts off the plate, and nothing else. A slanted plate has one, at its lowest
/// edge, however many layers its surface advances through.
/// </summary>
public class IslandDefinitionTests
{
    /// <summary>A 20 × 10 × 1 mm slab tilted by <paramref name="degrees"/> about Y, its lowest edge at <paramref name="bottomZ"/>.</summary>
    private static Mesh TiltedSlab(float degrees, float bottomZ)
    {
        var box = Meshes.Box(20, 10, 1);
        var rotation = Matrix4x4.CreateRotationY(degrees * MathF.PI / 180f);
        var positions = box.Positions.Select(p => Vector3.Transform(p, rotation)).ToArray();
        var minZ = positions.Min(p => p.Z);
        for (var i = 0; i < positions.Length; i++) positions[i].Z += bottomZ - minZ;
        return new Mesh(positions, box.Indices.ToArray());
    }

    private static IReadOnlyList<DetectedIsland> Detect(Mesh mesh) =>
        IslandDetection.FindUnsupported(mesh, null, layerHeightMm: 0.05f,
            minIslandAreaMm2: 0.1f, plateZ: 0f, overhangAngleDegrees: 45f);

    [Fact]
    public void ARaisedSlantedSlabHasExactlyOneIslandAtItsLowestEdge()
    {
        var mesh = TiltedSlab(30f, bottomZ: 3f);
        var lowest = mesh.Positions.Min(p => p.Z);

        var islands = Detect(mesh);

        var island = Assert.Single(islands);
        Assert.InRange(island.Position.Z, lowest, lowest + 0.2f);
    }

    [Fact]
    public void ASlantedSlabOnThePlateHasNoIsland()
    {
        Assert.Empty(Detect(TiltedSlab(30f, bottomZ: 0f)));
    }

    [Fact]
    public void TwoLegsStartingOffPlateAreTwoIslands()
    {
        // An inverted V: two slabs leaning away from each other, both lowest edges raised.
        var left = TiltedSlab(30f, bottomZ: 2f);
        var right = TiltedSlab(-30f, bottomZ: 2f);
        var shifted = new Mesh(right.Positions.Select(p => p + new Vector3(30, 0, 0)).ToArray(), right.Indices.ToArray());

        var islands = Detect(Meshes.Merge(left, shifted));

        Assert.Equal(2, islands.Count);
    }

    [Fact]
    public void OverhangsGrowingOutOfSupportedMaterialAreNotIslands()
    {
        // A table top over its legs and a cantilever arm out of its post both start attached.
        Assert.Empty(Detect(Meshes.Table(top: 20, topThickness: 2, leg: 2, height: 10)));
        Assert.Empty(Detect(Meshes.Cantilever()));
    }

    [Fact]
    public void AFloatingBoxIsStillAnIsland()
    {
        Assert.Single(Detect(Meshes.FloatingBox(10, 10, 10, z: 5)));
    }
}
