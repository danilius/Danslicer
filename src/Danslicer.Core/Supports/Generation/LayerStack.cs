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
        var h = (double)layerHeight;
        if (prepared.MaxZ <= 1e-9) return result;

        var layerCount = (int)Math.Ceiling(prepared.MaxZ / h - 1e-6);
        if (layerCount <= 0) return result;

        var buckets = MeshSlicer.BucketTriangles(prepared, h, layerCount);
        var segments = new List<MeshSlicer.Segment>();
        for (int i = 0; i < layerCount; i++)
        {
            var z = (i + 0.5) * h;
            segments.Clear();
            MeshSlicer.CollectSegments(prepared, buckets[i], z, segments);
            var polygons = MeshSlicer.Finish(MeshSlicer.ChainSegments(segments), 0);
            result.Add(new SliceLayer(i, (float)z, polygons));
        }
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
