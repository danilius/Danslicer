using System.Numerics;
using Clipper2Lib;
using Danslicer.Core.Slicing;

namespace Danslicer.Core.Supports;

/// <summary>
/// Analytic cross-sections of support elements for slicing. Segments are capsules: a cylinder
/// between the two node positions with hemispherical caps, so joints are watertight without
/// tessellation. A cone tip is a frustum of linearly interpolated radius along the neck, plus an
/// optional contact ball (sphere sections). A horizontal plane cuts the cylinder in an ellipse
/// (a circle when vertical) and each cap/ball in a circle; the union of those, polygonised at
/// fixed angular resolution in Clipper units, is exact to well under a printer pixel.
/// </summary>
public static class SupportSliceGeometry
{
    /// <summary>Vertices per full contour. 64 keeps radial error below 0.2% of the radius.</summary>
    public const int ContourVertices = 64;

    /// <summary>
    /// All support cross-sections at height <paramref name="z"/>, in Clipper units, ready to union
    /// with the model's layer polygons. Disabled elements are skipped; hidden ones still slice.
    /// Capsule-shaped tips take the historical path unchanged.
    /// </summary>
    public static Paths64 SectionsAt(SupportGraph graph, double z) =>
        SectionsAt(graph, z, includeSegment: null, includeNode: null);

    /// <summary>
    /// Filtered viewport variant. The predicates select visible element categories before their
    /// analytic sections are generated; null preserves the print-slicing behaviour above.
    /// </summary>
    public static Paths64 SectionsAt(SupportGraph graph, double z,
        Func<SupportSegment, bool>? includeSegment, Func<SupportNode, bool>? includeNode)
    {
        var paths = new Paths64();
        foreach (var segment in graph.Segments)
        {
            if (segment.Disabled || includeSegment is not null && !includeSegment(segment)) continue;
            var a = graph.GetNode(segment.NodeA);
            var b = graph.GetNode(segment.NodeB);
            if (a.Disabled || b.Disabled) continue;
            if (TryConeTip(a, b, out var tip, out var other))
                ConeTipSection(tip, other, TipJunctionDiameter(graph, segment) * 0.5, z, paths);
            else
                CapsuleSection(a.Position, b.Position, segment.Diameter * 0.5, z, paths);
        }
        foreach (var node in graph.Nodes)
        {
            if (node.Disabled || includeNode is not null && !includeNode(node) ||
                node.Type != SupportNodeType.Tip) continue;
            if (node.TipShape != SupportTipShape.Cone || node.BallDiameter <= 0) continue;
            SphereSection(node.ContactBallCenter, node.BallDiameter * 0.5, z, paths);
        }
        foreach (var node in graph.Nodes)
        {
            if (node.Disabled || includeNode is not null && !includeNode(node) ||
                node.Type != SupportNodeType.Base) continue;
            if (node.BaseShape == SupportBaseShape.None) continue;
            BaseSection(node, MaxIncidentDiameter(graph, node), z, paths);
        }
        return paths;
    }

    /// <summary>The widest member meeting a node; the top radius of a DiscCone base's cone.</summary>
    private static float MaxIncidentDiameter(SupportGraph graph, SupportNode node)
    {
        var diameter = 0f;
        foreach (var segment in graph.SegmentsAt(node.Id))
            if (!segment.Disabled && segment.Diameter > diameter) diameter = segment.Diameter;
        return diameter;
    }

    /// <summary>
    /// Disc (a vertical cylinder of BaseDiameter × BaseHeight rising from the node) and, for
    /// DiscCone, a frustum from the disc diameter to the member diameter over BaseConeHeight.
    /// </summary>
    public static void BaseSection(SupportNode baseNode, float memberDiameter, double z, Paths64 output)
    {
        var origin = baseNode.Position;
        var discTop = origin + Vector3.UnitZ * baseNode.BaseHeight;
        var discRadius = baseNode.BaseDiameter * 0.5;
        ConeSection(origin, discTop, discRadius, discRadius, z, output);
        if (baseNode.BaseShape != SupportBaseShape.DiscCone) return;
        var coneTop = discTop + Vector3.UnitZ * baseNode.BaseConeHeight;
        ConeSection(discTop, coneTop, discRadius, memberDiameter * 0.5, z, output);
    }

    internal static bool TryConeTip(SupportNode a, SupportNode b, out SupportNode tip, out SupportNode other)
    {
        if (a.Type == SupportNodeType.Tip && a.TipShape == SupportTipShape.Cone && a.ConeLength > 0
            && b.Type != SupportNodeType.Tip)
        {
            tip = a;
            other = b;
            return true;
        }
        if (b.Type == SupportNodeType.Tip && b.TipShape == SupportTipShape.Cone && b.ConeLength > 0
            && a.Type != SupportNodeType.Tip)
        {
            tip = b;
            other = a;
            return true;
        }
        tip = a;
        other = b;
        return false;
    }

    /// <summary>
    /// Diameter that a tapered tip must reach at its junction. Tip/mini members carry the
    /// taper-rule diameter on the graph for routing clearance; their printable wide end instead
    /// matches the branch or trunk that carries them. A direct tip-to-base member has no separate
    /// parent and therefore keeps its own diameter.
    /// </summary>
    internal static float TipJunctionDiameter(SupportGraph graph, SupportSegment tipSegment)
    {
        var a = graph.GetNode(tipSegment.NodeA);
        var junctionId = a.Type == SupportNodeType.Tip ? tipSegment.NodeB : tipSegment.NodeA;
        var diameter = 0f;
        foreach (var incident in graph.SegmentsAt(junctionId))
        {
            if (incident.Id == tipSegment.Id ||
                incident.Type is not (SupportSegmentType.Branch or SupportSegmentType.Trunk))
                continue;
            diameter = MathF.Max(diameter, incident.Diameter);
        }
        return diameter > 0 ? diameter : tipSegment.Diameter;
    }

    /// <summary>
    /// One cone frustum from the contact radius to the parent-member (ball) radius over the
    /// whole tip member, its base ring at the ball's centre. Embedding moves the narrow end past
    /// the surface while leaving the base fixed, so the original contact plane cuts a slightly
    /// wider part of the frustum.
    /// </summary>
    public static void ConeTipSection(SupportNode tip, SupportNode other, double junctionRadius,
        double z, Paths64 output)
    {
        var rContact = Math.Max(tip.TipDiameter * 0.5, 0.0);
        var sections = TipBodyGeometry.Sections(tip, other, (float)junctionRadius,
            embedContact: true);
        if (sections.Count == 0)
        {
            CapsuleSection(tip.Position, other.Position, junctionRadius, z, output);
            return;
        }

        foreach (var section in sections)
            ConeSection(section.Start, section.End, section.StartRadius,
                section.EndRadius, z, output);
        if (tip.BallDiameter <= 0 && rContact > 0)
            SphereSection(tip.Position, rContact, z, output);
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
        EllipseSection(centre, axis, radius, output);
    }

    /// <summary>
    /// Frustum of linearly interpolated radius from <paramref name="a"/> to <paramref name="b"/>.
    /// A horizontal cut is a circle (vertical axis) or an ellipse (leaning), same as a cylinder.
    /// </summary>
    public static void ConeSection(Vector3 a, Vector3 b, double radiusA, double radiusB, double z,
        Paths64 output)
    {
        if (radiusA < 0 || radiusB < 0) return;
        var (lo, hi, rLo, rHi) = a.Z <= b.Z
            ? (a, b, radiusA, radiusB)
            : (b, a, radiusB, radiusA);
        var dz = hi.Z - lo.Z;
        if (z < lo.Z || z > hi.Z || dz < 1e-9)
            return;

        var t = (z - lo.Z) / dz;
        var centre = Vector3.Lerp(lo, hi, (float)t);
        var radius = rLo + (rHi - rLo) * t;
        if (radius <= 0) return;

        var axis = Vector3.Normalize(hi - lo);
        EllipseSection(centre, axis, radius, output);
    }

    /// <summary>
    /// Circle of radius <c>sqrt(r² − dz²)</c> about <paramref name="centre"/>, or nothing if the
    /// plane misses the sphere.
    /// </summary>
    public static void SphereSection(Vector3 centre, double radius, double z, Paths64 output)
    {
        if (radius <= 0) return;
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

    private static void EllipseSection(Vector3 centre, Vector3 axis, double radius, Paths64 output)
    {
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
}
