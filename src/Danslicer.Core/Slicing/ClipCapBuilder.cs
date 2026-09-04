using System.Numerics;
using System.Runtime.InteropServices;
using Clipper2Lib;
using Danslicer.Core.Geometry;

namespace Danslicer.Core.Slicing;

public enum ClipCapFace
{
    Lower,
    Upper,
}

/// <summary>Builds world-space viewport-only meshes that close horizontal clip sections.</summary>
public static class ClipCapBuilder
{
    /// <summary>
    /// Inset from the shader clip plane. This is far below slicing resolution but large enough
    /// to survive float conversion and remain inside the visible slab.
    /// </summary>
    public const float PlaneInsetMm = 0.0001f;

    public static Mesh? Build(Mesh mesh, Matrix4x4 world, double planeZ, ClipCapFace face) =>
        Build(new MeshSlicer.PreparedMesh(mesh, world), planeZ, face);

    public static Mesh? Build(MeshSlicer.PreparedMesh mesh, double planeZ, ClipCapFace face) =>
        Build(MeshSlicer.PolygonsAt(mesh, planeZ), planeZ, face);

    public static Mesh? Build(Paths64 polygons, double planeZ, ClipCapFace face)
    {
        ArgumentNullException.ThrowIfNull(polygons);
        var triangles = PolygonTriangulator.Triangulate(polygons);
        if (triangles.Count == 0) return null;

        var positive = face == ClipCapFace.Upper;
        var z = (float)planeZ + (positive ? -PlaneInsetMm : PlaneInsetMm);
        var soup = new List<Vector3>(triangles.Count * 3);
        foreach (var triangle in triangles)
        {
            if (triangle.Count != 3) continue;
            var cross = CrossZ(triangle[0], triangle[1], triangle[2]);
            if (cross == 0) continue;
            Append(triangle[0]);
            if ((cross > 0) == positive)
            {
                Append(triangle[1]);
                Append(triangle[2]);
            }
            else
            {
                Append(triangle[2]);
                Append(triangle[1]);
            }
        }
        return soup.Count == 0 ? null : Mesh.FromTriangleSoup(CollectionsMarshal.AsSpan(soup));

        void Append(Point64 point) => soup.Add(new Vector3(
            (float)(point.X / MeshSlicer.UnitsPerMm),
            (float)(point.Y / MeshSlicer.UnitsPerMm), z));
    }

    private static Int128 CrossZ(Point64 a, Point64 b, Point64 c) =>
        (Int128)(b.X - a.X) * (c.Y - a.Y) - (Int128)(b.Y - a.Y) * (c.X - a.X);
}
