using System.Numerics;
using Danslicer.Core.Geometry;

namespace Danslicer.Core.Supports;

/// <summary>Which colour bucket a render part belongs to. Mirrors the segment taxonomy.</summary>
public enum SupportRenderKind
{
    Tip,
    MiniSupport,
    Branch,
    Trunk,
    Bracing,
    Base,
}

/// <summary>
/// One derived render mesh plus the state the app needs to colour it. Parts group every segment
/// sharing (kind, selected, disabled) into a single mesh so the renderer draws a handful of
/// batches, not one per segment.
/// </summary>
public readonly record struct SupportRenderPart(Mesh Mesh, SupportRenderKind Kind, bool Selected, bool Disabled);

/// <summary>
/// Derives shaded render meshes from a support graph. Segments are tessellated as capsules —
/// the same analytic shape <see cref="SupportSliceGeometry"/> slices — so the viewport shows
/// exactly what will print. Hidden elements are skipped (they still slice); disabled elements
/// are emitted in their own parts so the app can fade them.
/// </summary>
public static class SupportRenderMesh
{
    /// <summary>Vertices per ring. 16 keeps a 1.2 mm pillar visually round at working zoom.</summary>
    public const int RadialSegments = 16;

    /// <summary>Latitude rings per hemispherical cap.</summary>
    public const int CapStacks = 4;

    /// <summary>Triangles emitted for one non-degenerate capsule, for capacity hints and tests.</summary>
    public static int TrianglesPerCapsule => 2 * RadialSegments + (2 * CapStacks - 1) * 2 * RadialSegments;

    /// <summary>Triangles emitted for one sphere (zero-length segment), for tests.</summary>
    public static int TrianglesPerSphere => 2 * RadialSegments + (2 * CapStacks - 2) * 2 * RadialSegments;

    /// <summary>Triangles emitted for one flat-capped frustum (or cylinder), for tests.</summary>
    public static int TrianglesPerFrustum => 4 * RadialSegments;

    /// <summary>
    /// Builds render meshes for every visible segment of <paramref name="graph"/>, grouped by
    /// (kind, selected, disabled). <paramref name="isSelected"/> may be null when nothing is.
    /// </summary>
    public static IReadOnlyList<SupportRenderPart> Build(SupportGraph graph,
        Func<Guid, bool>? isSelected = null,
        Func<SupportSegment, bool>? includeSegment = null,
        Func<SupportNode, bool>? includeBase = null)
    {
        var builders = new Dictionary<(SupportRenderKind Kind, bool Selected, bool Disabled), MeshBuilder>();

        foreach (var segment in graph.Segments)
        {
            if (segment.Hidden || !(includeSegment?.Invoke(segment) ?? true)) continue;
            var a = graph.GetNode(segment.NodeA);
            var b = graph.GetNode(segment.NodeB);
            if (a.Hidden || b.Hidden) continue;

            var kind = segment.Type switch
            {
                SupportSegmentType.Tip => SupportRenderKind.Tip,
                SupportSegmentType.MiniSupport => SupportRenderKind.MiniSupport,
                SupportSegmentType.Trunk => SupportRenderKind.Trunk,
                SupportSegmentType.Bracing => SupportRenderKind.Bracing,
                _ => SupportRenderKind.Branch,
            };
            var disabled = segment.Disabled || a.Disabled || b.Disabled;
            var key = (kind, isSelected?.Invoke(segment.Id) ?? false, disabled);
            if (!builders.TryGetValue(key, out var builder))
                builders[key] = builder = new MeshBuilder();

            if (SupportSliceGeometry.TryConeTip(a, b, out var tip, out var other))
                AppendConeTip(builder, tip, other, segment.Diameter * 0.5f,
                    SupportSliceGeometry.TipJunctionDiameter(graph, segment) * 0.5f);
            else
                AppendCapsule(builder, a.Position, b.Position, segment.Diameter * 0.5f);
        }

        foreach (var node in graph.Nodes)
        {
            if (node.Hidden || node.Type != SupportNodeType.Base ||
                !(includeBase?.Invoke(node) ?? true)) continue;
            if (node.BaseShape == SupportBaseShape.None) continue;
            var key = (SupportRenderKind.Base, isSelected?.Invoke(node.Id) ?? false, node.Disabled);
            if (!builders.TryGetValue(key, out var builder))
                builders[key] = builder = new MeshBuilder();
            AppendBase(builder, node, MaxVisibleIncidentDiameter(graph, node));
        }

        var parts = new List<SupportRenderPart>(builders.Count);
        foreach (var ((kind, selected, disabled), builder) in builders)
            parts.Add(new SupportRenderPart(builder.ToMesh(), kind, selected, disabled));
        return parts;
    }

    /// <summary>
    /// Builds one mesh containing only the SELECTED visible elements, for drawing as a highlight
    /// overlay on top of the full build. Selection changes then re-tessellate a handful of
    /// elements instead of the whole graph (the full rebuild froze the app for seconds on a
    /// generated forest). Returns null when nothing selected is visible.
    /// </summary>
    public static Mesh? BuildSelected(SupportGraph graph, Func<Guid, bool> isSelected,
        Func<SupportSegment, bool>? includeSegment = null,
        Func<SupportNode, bool>? includeBase = null)
    {
        var builder = new MeshBuilder();
        var any = false;
        foreach (var segment in graph.Segments)
        {
            if (segment.Hidden || !isSelected(segment.Id) ||
                !(includeSegment?.Invoke(segment) ?? true)) continue;
            var a = graph.GetNode(segment.NodeA);
            var b = graph.GetNode(segment.NodeB);
            if (a.Hidden || b.Hidden) continue;
            if (SupportSliceGeometry.TryConeTip(a, b, out var tip, out var other))
                AppendConeTip(builder, tip, other, segment.Diameter * 0.5f,
                    SupportSliceGeometry.TipJunctionDiameter(graph, segment) * 0.5f);
            else
                AppendCapsule(builder, a.Position, b.Position, segment.Diameter * 0.5f);
            any = true;
        }
        foreach (var node in graph.Nodes)
        {
            if (node.Hidden || node.Type != SupportNodeType.Base ||
                !(includeBase?.Invoke(node) ?? true)) continue;
            if (node.BaseShape == SupportBaseShape.None || !isSelected(node.Id)) continue;
            AppendBase(builder, node, MaxVisibleIncidentDiameter(graph, node));
            any = true;
        }
        return any ? builder.ToMesh() : null;
    }

    /// <summary>The widest visible member meeting a node; the top radius of a DiscCone base's cone.</summary>
    private static float MaxVisibleIncidentDiameter(SupportGraph graph, SupportNode node)
    {
        var diameter = 0f;
        foreach (var segment in graph.SegmentsAt(node.Id))
            if (!segment.Hidden && segment.Diameter > diameter) diameter = segment.Diameter;
        return diameter;
    }

    /// <summary>
    /// Renders a cone-shaped tip member the same way <see cref="SupportSliceGeometry.ConeTipSection"/>
    /// slices it: contact-to-neck taper, a flush transition to the parent-member diameter at the
    /// junction, and the contact ball (or a contact-radius sphere) at the tip.
    /// </summary>
    public static void AppendConeTip(MeshBuilder builder, SupportNode tip, SupportNode other,
        float neckRadius, float junctionRadius)
    {
        var contactRadius = MathF.Max(tip.TipDiameter * 0.5f, 0f);
        var axis = other.Position - tip.Position;
        var length = axis.Length();
        var coneLength = Math.Min(Math.Max(tip.ConeLength, 0f), length);
        if (length < 1e-6f || coneLength <= 0)
        {
            AppendCapsule(builder, tip.Position, other.Position, neckRadius);
            return;
        }

        var direction = axis / length;
        var coneBase = tip.Position + direction * coneLength;
        var hasRemainder = length - coneLength > 1e-4f;
        AppendTipBody(builder, tip.Position, coneBase, other.Position, contactRadius,
            neckRadius, junctionRadius, hasRemainder);
        if (tip.BallDiameter > 0)
            AppendSphere(builder, tip.ContactBallCenter, tip.BallDiameter * 0.5f);
        else if (contactRadius > 0)
            AppendSphere(builder, tip.Position, contactRadius);
    }

    private static void AppendTipBody(MeshBuilder builder, Vector3 tip, Vector3 coneBase,
        Vector3 junction, float contactRadius, float neckRadius, float junctionRadius,
        bool hasRemainder)
    {
        var axis = Vector3.Normalize(junction - tip);
        var (u, v) = OrthonormalFrame(axis);
        var rings = hasRemainder
            ? new[]
            {
                AddRing(builder, tip, u, v, MathF.Max(contactRadius, 0f)),
                AddRing(builder, coneBase, u, v, MathF.Max(neckRadius, 0f)),
                AddRing(builder, junction, u, v, MathF.Max(junctionRadius, 0f)),
            }
            : new[]
            {
                AddRing(builder, tip, u, v, MathF.Max(contactRadius, 0f)),
                AddRing(builder, junction, u, v, MathF.Max(junctionRadius, 0f)),
            };
        var tipPole = builder.AddVertex(tip);
        var junctionPole = builder.AddVertex(junction);
        StitchShell(builder, rings, tipPole, junctionPole);
    }

    /// <summary>
    /// Renders a base: the disc as a flat-capped cylinder rising BaseHeight from the node, and for
    /// DiscCone a frustum from the disc diameter to the member diameter over BaseConeHeight.
    /// </summary>
    public static void AppendBase(MeshBuilder builder, SupportNode baseNode, float memberDiameter)
    {
        var origin = baseNode.Position;
        var discTop = origin + Vector3.UnitZ * baseNode.BaseHeight;
        var discRadius = baseNode.BaseDiameter * 0.5f;
        AppendFrustum(builder, origin, discTop, discRadius, discRadius);
        if (baseNode.BaseShape != SupportBaseShape.DiscCone) return;
        var coneTop = discTop + Vector3.UnitZ * baseNode.BaseConeHeight;
        AppendFrustum(builder, discTop, coneTop, discRadius, memberDiameter * 0.5f);
    }

    /// <summary>
    /// Appends a closed frustum from <paramref name="a"/> (radius <paramref name="radiusA"/>) to
    /// <paramref name="b"/> (radius <paramref name="radiusB"/>) with flat fan caps at both ends.
    /// Equal radii give a cylinder.
    /// </summary>
    public static void AppendFrustum(MeshBuilder builder, Vector3 a, Vector3 b,
        float radiusA, float radiusB)
    {
        if (radiusA <= 0 && radiusB <= 0) return;
        var axis = b - a;
        var length = axis.Length();
        if (length < 1e-6f) return;

        var w = axis / length;
        var (u, v) = OrthonormalFrame(w);
        var rings = new[]
        {
            AddRing(builder, a, u, v, MathF.Max(radiusA, 0f)),
            AddRing(builder, b, u, v, MathF.Max(radiusB, 0f)),
        };
        var bottomPole = builder.AddVertex(a);
        var topPole = builder.AddVertex(b);
        StitchShell(builder, rings, bottomPole, topPole);
    }

    /// <summary>
    /// Appends a closed capsule from <paramref name="a"/> to <paramref name="b"/> of radius
    /// <paramref name="radius"/>: a cylinder body with hemispherical caps sharing its end rings.
    /// A zero-length segment degenerates to a sphere.
    /// </summary>
    public static void AppendCapsule(MeshBuilder builder, Vector3 a, Vector3 b, float radius)
    {
        if (radius <= 0) return;
        var axis = b - a;
        var length = axis.Length();
        if (length < 1e-6f)
        {
            AppendSphere(builder, a, radius);
            return;
        }

        var w = axis / length;
        var (u, v) = OrthonormalFrame(w);

        // Rings from the bottom pole (at a - w r) to the top pole (at b + w r): CapStacks rings on
        // the lower hemisphere ending at a's equator, the equator again at b, then CapStacks - 1
        // rings on the upper hemisphere. Ring angle phi runs pole to equator.
        var rings = new int[2 * CapStacks][];
        for (int k = 1; k <= CapStacks; k++)
        {
            var phi = k * (MathF.PI / 2f) / CapStacks;
            var ringRadius = radius * MathF.Sin(phi);
            var drop = radius * MathF.Cos(phi);
            rings[k - 1] = AddRing(builder, a - w * drop, u, v, ringRadius);
        }
        for (int k = CapStacks; k >= 1; k--)
        {
            var phi = k * (MathF.PI / 2f) / CapStacks;
            var ringRadius = radius * MathF.Sin(phi);
            var rise = radius * MathF.Cos(phi);
            rings[2 * CapStacks - k] = AddRing(builder, b + w * rise, u, v, ringRadius);
        }

        var bottomPole = builder.AddVertex(a - w * radius);
        var topPole = builder.AddVertex(b + w * radius);
        StitchShell(builder, rings, bottomPole, topPole);
    }

    /// <summary>Appends a closed UV sphere.</summary>
    public static void AppendSphere(MeshBuilder builder, Vector3 centre, float radius)
    {
        if (radius <= 0) return;
        var (u, v) = OrthonormalFrame(Vector3.UnitZ);

        var rings = new int[2 * CapStacks - 1][];
        for (int k = 1; k <= 2 * CapStacks - 1; k++)
        {
            var phi = k * MathF.PI / (2 * CapStacks);
            var ringRadius = radius * MathF.Sin(phi);
            var height = -radius * MathF.Cos(phi);
            rings[k - 1] = AddRing(builder, centre + Vector3.UnitZ * height, u, v, ringRadius);
        }

        var bottomPole = builder.AddVertex(centre - Vector3.UnitZ * radius);
        var topPole = builder.AddVertex(centre + Vector3.UnitZ * radius);
        StitchShell(builder, rings, bottomPole, topPole);
    }

    private static int[] AddRing(MeshBuilder builder, Vector3 centre, Vector3 u, Vector3 v, float radius)
    {
        var ring = new int[RadialSegments];
        for (int j = 0; j < RadialSegments; j++)
        {
            var angle = j * (2f * MathF.PI / RadialSegments);
            ring[j] = builder.AddVertex(centre + (u * MathF.Cos(angle) + v * MathF.Sin(angle)) * radius);
        }
        return ring;
    }

    /// <summary>
    /// Closes a stack of rings with two pole fans and quad strips between consecutive rings,
    /// wound counter-clockwise seen from outside (rings run counter-clockwise about the axis).
    /// </summary>
    private static void StitchShell(MeshBuilder builder, int[][] rings, int bottomPole, int topPole)
    {
        var first = rings[0];
        var last = rings[^1];
        for (int j = 0; j < RadialSegments; j++)
        {
            var jn = (j + 1) % RadialSegments;
            builder.AddTriangle(bottomPole, first[jn], first[j]);
            builder.AddTriangle(topPole, last[j], last[jn]);
        }
        for (int k = 0; k + 1 < rings.Length; k++)
        {
            var lower = rings[k];
            var upper = rings[k + 1];
            for (int j = 0; j < RadialSegments; j++)
            {
                var jn = (j + 1) % RadialSegments;
                builder.AddTriangle(lower[j], lower[jn], upper[jn]);
                builder.AddTriangle(lower[j], upper[jn], upper[j]);
            }
        }
    }

    private static (Vector3 U, Vector3 V) OrthonormalFrame(Vector3 w)
    {
        var reference = MathF.Abs(w.Z) < 0.9f ? Vector3.UnitZ : Vector3.UnitX;
        var u = Vector3.Normalize(Vector3.Cross(reference, w));
        return (u, Vector3.Cross(w, u));
    }
}

/// <summary>Accumulates indexed triangles across primitives before freezing into a Mesh.</summary>
public sealed class MeshBuilder
{
    private readonly List<Vector3> _positions = new();
    private readonly List<int> _indices = new();

    public int AddVertex(Vector3 position)
    {
        _positions.Add(position);
        return _positions.Count - 1;
    }

    public void AddTriangle(int a, int b, int c)
    {
        _indices.Add(a);
        _indices.Add(b);
        _indices.Add(c);
    }

    public Mesh ToMesh() => new(_positions.ToArray(), _indices.ToArray());
}
