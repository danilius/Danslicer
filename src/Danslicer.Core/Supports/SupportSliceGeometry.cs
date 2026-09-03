using System.Numerics;
using Clipper2Lib;
using Danslicer.Core.Slicing;

namespace Danslicer.Core.Supports;

/// <summary>
/// Analytic cross-sections of support elements for slicing. Segments are capsules: a cylinder
/// between the two node positions with hemispherical caps, so joints are watertight without
/// tessellation. A horizontal plane cuts the cylinder in an ellipse (a circle when vertical) and
/// each cap in a circle; the union of those, polygonised at fixed angular resolution in Clipper
/// units, is exact to well under a printer pixel.
/// </summary>
public static class SupportSliceGeometry
{
    /// <summary>Vertices per full contour. 64 keeps radial error below 0.2% of the radius.</summary>
    public const int ContourVertices = 64;

    /// <summary>
    /// All support cross-sections at height <paramref name="z"/>, in Clipper units, ready to union
    /// with the model's layer polygons. Disabled elements are skipped; hidden ones still slice.
    /// </summary>
    public static Paths64 SectionsAt(SupportGraph graph, double z)
    {
        var paths = new Paths64();
        foreach (var segment in graph.Segments)
        {
            if (segment.Disabled) continue;
            var a = graph.GetNode(segment.NodeA);
            var b = graph.GetNode(segment.NodeB);
            if (a.Disabled || b.Disabled) continue;
            CapsuleSection(a.Position, b.Position, segment.Diameter * 0.5, z, paths);
        }
        return paths;
    }

    /// <summary>
    /// Cross-section of one capsule (cylinder from <paramref name="a"/> to <paramref name="b"/> of
    /// radius <paramref name="radius"/>, spherical caps at both ends) with the plane at
    /// <paramref name="z"/>. Appends zero, one or more counter-clockwise contours.
    /// </summary>
    public static void CapsuleSection(Vector3 a, Vector3 b, double radius, double z, Paths64 output)
    {
        if (radius <= 0) return;

        // Spherical caps: circle of radius sqrt(r^2 - dz^2) about each end point.
        SphereSection(a, radius, z, output);
        SphereSection(b, radius, z, output);

        // Cylinder body between the endpoint planes.
        var (lo, hi) = a.Z <= b.Z ? ((Vector3 Low, Vector3 High))(a, b) : (b, a);
        var dz = hi.Z - lo.Z;
        if (z < lo.Z || z > hi.Z || dz < 1e-9)
            return; // outside the body span, or a horizontal member: caps carry the section

        var t = (z - lo.Z) / dz;
        var centre = Vector3.Lerp(lo, hi, (float)t);

        var axis = Vector3.Normalize(hi - lo);
        // The plane cuts the cylinder in an ellipse: semi-minor r perpendicular to the lean
        // direction, semi-major r / cos(lean) along it, where cos(lean) = axis dot Z.
        var cosLean = MathF.Abs(axis.Z);
        var horizontal = new Vector2(axis.X, axis.Y);
        var major = horizontal.LengthSquared() > 1e-12f ? Vector2.Normalize(horizontal) : Vector2.UnitX;
        var minor = new Vector2(-major.Y, major.X);
        var semiMajor = radius / Math.Max(cosLean, 1e-3f);

        var path = new Path64(ContourVertices);
        for (int i = 0; i < ContourVertices; i++)
        {
            var angle = i * (2 * Math.PI / ContourVertices);
            var u = Math.Cos(angle) * semiMajor;
            var v = Math.Sin(angle) * radius;
            var x = centre.X + major.X * u + minor.X * v;
            var y = centre.Y + major.Y * u + minor.Y * v;
            path.Add(new Point64(
                (long)Math.Round(x * MeshSlicer.UnitsPerMm),
                (long)Math.Round(y * MeshSlicer.UnitsPerMm)));
        }
        output.Add(path);
    }

    private static void SphereSection(Vector3 centre, double radius, double z, Paths64 output)
    {
        var dz = z - centre.Z;
        var r2 = radius * radius - dz * dz;
        if (r2 <= 0) return;
        var r = Math.Sqrt(r2);

        var path = new Path64(ContourVertices);
        for (int i = 0; i < ContourVertices; i++)
        {
            var angle = i * (2 * Math.PI / ContourVertices);
            path.Add(new Point64(
                (long)Math.Round((centre.X + Math.Cos(angle) * r) * MeshSlicer.UnitsPerMm),
                (long)Math.Round((centre.Y + Math.Sin(angle) * r) * MeshSlicer.UnitsPerMm)));
        }
        output.Add(path);
    }
}
