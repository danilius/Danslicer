using System.Numerics;
using Clipper2Lib;
using Danslicer.Core.Geometry;
using Danslicer.Core.Slicing;

namespace Danslicer.Tests;

/// <summary>
/// Additive <see cref="MeshSlicer.LayerPolygons"/> / <see cref="MeshSlicer.NewbornIslands"/>
/// helpers. Existing slicer tests are untouched; these assert the new members match the
/// contour loop they replaced in LayerStack.
/// </summary>
public sealed class MeshSlicerLayerHelperTests
{
    [Fact]
    public void CubeLayerPolygonsMatchMidHeightSquare()
    {
        var mesh = Meshes.Box(10, 10, 10);
        var prepared = new MeshSlicer.PreparedMesh(mesh, Matrix4x4.Identity);
        var layers = MeshSlicer.LayerPolygons(prepared, 0.05);
        Assert.Equal(200, layers.Count);

        var mid = layers[100];
        Assert.Single(mid);
        Assert.Equal(100.0, MeshSlicer.AreaMm2(mid), 3);
        Assert.True(Clipper.Area(mid[0]) > 0);
    }

    [Fact]
    public void NewbornIslandsTreatsFirstSolidLayerAsUnsupported()
    {
        var mesh = Meshes.FloatingBox(10, 10, 10, z: 5);
        var prepared = new MeshSlicer.PreparedMesh(mesh, Matrix4x4.Identity);
        var layers = MeshSlicer.LayerPolygons(prepared, 0.05);
        var inflate = 0.05 * Math.Tan(45.0 * Math.PI / 180.0) + 0.02;
        var newborn = MeshSlicer.NewbornIslands(layers, inflate);
        Assert.Equal(layers.Count, newborn.Count);

        var firstSolid = -1;
        for (int i = 0; i < layers.Count; i++)
        {
            if (layers[i].Count == 0) continue;
            firstSolid = i;
            break;
        }
        Assert.True(firstSolid > 0);
        Assert.Equal(100.0, MeshSlicer.AreaMm2(newborn[firstSolid]), 3);
        Assert.Equal(0.0, MeshSlicer.AreaMm2(newborn[firstSolid - 1]), 6);
    }

    [Fact]
    public void EmptyMeshProducesNoLayers()
    {
        var mesh = new Mesh(Array.Empty<Vector3>(), Array.Empty<int>());
        var prepared = new MeshSlicer.PreparedMesh(mesh, Matrix4x4.Identity);
        Assert.Empty(MeshSlicer.LayerPolygons(prepared, 0.05));
        Assert.Empty(MeshSlicer.NewbornIslands([], 0.1));
    }
}
