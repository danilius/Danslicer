using System.Numerics;
using Clipper2Lib;
using Danslicer.Core.Geometry;
using Danslicer.Core.Slicing;

namespace Danslicer.Core.Supports.Generation;

internal readonly record struct SliceLayer(int Index, float Z, Paths64 Polygons);

/// <summary>
/// Horizontal contours of a world-space mesh at layer mid-heights, via <see cref="MeshSlicer"/>'s
/// public API. Shared by island placement and print checks. Empty layers are included so indices
/// match the slicer's layer numbering.
/// </summary>
internal static class LayerStack
{
    public static List<SliceLayer> Slice(Mesh mesh, float layerHeight)
    {
        var result = new List<SliceLayer>();
        if (layerHeight <= 1e-6f) return result;

        var prepared = new MeshSlicer.PreparedMesh(mesh, Matrix4x4.Identity);
        var polygons = MeshSlicer.LayerPolygons(prepared, layerHeight);
        var h = (double)layerHeight;
        for (int i = 0; i < polygons.Count; i++)
            result.Add(new SliceLayer(i, (float)((i + 0.5) * h), polygons[i]));
        return result;
    }

    public static Path64 OrientedPositive(Path64 path)
    {
        if (Clipper.Area(path) >= 0) return path;
        var copy = new Path64(path);
        copy.Reverse();
        return copy;
    }
}
