using Clipper2Lib;

namespace Danslicer.Core.Slicing;

/// <summary>
/// Deterministically triangulates Clipper polygon sets, including holes and nested islands.
/// Input winding follows the slicer convention: positive outers and negative holes.
/// </summary>
public static class PolygonTriangulator
{
    /// <summary>Returns one three-point path per triangle, or an empty set for no polygons.</summary>
    public static Paths64 Triangulate(Paths64 polygons)
    {
        ArgumentNullException.ThrowIfNull(polygons);
        if (polygons.Count == 0) return new Paths64();

        // Normalize overlaps and winding before triangulation. Clipper's constrained Delaunay
        // implementation handles holes without another dependency and is deterministic for a
        // stable integer input sequence.
        var normalized = Clipper.Union(polygons, FillRule.NonZero);
        if (normalized.Count == 0) return new Paths64();

        var result = Clipper.Triangulate(normalized, out var triangles, useDelaunay: true);
        if (result != TriangulateResult.success)
            throw new InvalidOperationException($"Polygon triangulation failed: {result}.");
        return triangles;
    }
}
