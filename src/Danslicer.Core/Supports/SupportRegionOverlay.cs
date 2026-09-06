using System.Numerics;
using Danslicer.Core.Geometry;

namespace Danslicer.Core.Supports;

/// <summary>
/// Builds the translucent face overlay that shows a painted support region (DESIGN 8.3: "Regions
/// are shown as a translucent colour overlay in the viewport. Keep-clean regions use a distinct
/// colour"). Pure geometry: it copies the selected triangles into a world-space mesh, which the
/// viewport draws as a depth-overlay aux mesh on either render path — no shader, no per-face
/// attribute, and testable without a GL context.
/// </summary>
public static class SupportRegionOverlay
{
    /// <summary>Region tint: a cool teal, deliberately far from the overhang checker's yellow/red
    /// and from the support colours, so a painted region cannot be mistaken for an overhang
    /// warning or for a support.</summary>
    public static readonly Vector3 RegionColor = new(0.18f, 0.71f, 0.77f);

    /// <summary>Keep-clean tint: magenta. Reads as "not here" against the teal and stays legible
    /// on the grey model.</summary>
    public static readonly Vector3 KeepCleanColor = new(0.88f, 0.28f, 0.62f);

    /// <summary>Translucent enough to read the surface through, solid enough to see at a glance.</summary>
    public const float Opacity = 0.38f;

    /// <summary>
    /// World-space mesh of the given faces of <paramref name="mesh"/>, or null when the set is
    /// empty or names no valid face. Out-of-range indices are skipped rather than throwing: a
    /// region painted on one mesh can outlive an edit that shortens it, and a stale index should
    /// not take the viewport down.
    /// </summary>
    public static Mesh? Build(Mesh mesh, Matrix4x4 transform, IReadOnlySet<int> faces)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(faces);
        if (faces.Count == 0 || mesh.TriangleCount == 0) return null;

        var vertices = new List<Vector3>(faces.Count * 3);
        foreach (var face in faces)
        {
            if (face < 0 || face >= mesh.TriangleCount) continue;
            for (var corner = 0; corner < 3; corner++)
                vertices.Add(Vector3.Transform(
                    mesh.Positions[mesh.Indices[face * 3 + corner]], transform));
        }
        return vertices.Count == 0 ? null : Mesh.FromTriangleSoup([.. vertices]);
    }

    /// <summary>
    /// Both layers for one object, region first so keep-clean draws over it — a face marked both
    /// painted and keep-clean is keep-clean, which is what generation does with it too.
    /// </summary>
    public static IReadOnlyList<(Mesh Mesh, Vector3 Color)> Build(Mesh mesh, Matrix4x4 transform,
        ObjectSupportRegions regions)
    {
        ArgumentNullException.ThrowIfNull(regions);
        var layers = new List<(Mesh, Vector3)>(2);
        if (Build(mesh, transform, regions.Faces) is { } region) layers.Add((region, RegionColor));
        if (Build(mesh, transform, regions.KeepCleanFaces) is { } clean)
            layers.Add((clean, KeepCleanColor));
        return layers;
    }
}
