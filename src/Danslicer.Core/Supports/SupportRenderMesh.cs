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
    /// Conservative world-space bounds for every support shape the viewport can draw. This is
    /// intentionally analytic: clip-range refreshes run on graph change events, where rebuilding
    /// the tessellated render meshes would make large generated forests unnecessarily expensive.
    /// Hidden elements are omitted just as they are by <see cref="Build"/>; disabled elements stay
    /// in the bounds because the viewport still draws them faded.
    /// </summary>
    public static Aabb VisibleBounds(SupportGraph graph)
    {
        var bounds = Aabb.Empty;
        foreach (var segment in graph.Segments)
        {
            if (segment.Hidden) continue;
            var a = graph.GetNode(segment.NodeA);
            var b = graph.GetNode(segment.NodeB);
            if (a.Hidden || b.Hidden) continue;

            var radius = MathF.Max(segment.Diameter * 0.5f, 0f);
            if (SupportSliceGeometry.TryConeTip(a, b, out var tip, out _))
            {
                radius = MathF.Max(radius, MathF.Max(tip.TipDiameter * 0.5f, 0f));
                radius = MathF.Max(radius,
                    MathF.Max(SupportSliceGeometry.TipJunctionDiameter(graph, segment) * 0.5f, 0f));
                foreach (var point in TipBodyGeometry.Centerline(
                             tip.Position, tip.SurfaceNormal,
                             tip.Id == a.Id ? b.Position : a.Position, tip.TipNormalLeadIn))
                    bounds = IncludeSphere(bounds, point, radius);

                var contactRadius = tip.BallDiameter > 0
                    ? tip.BallDiameter * 0.5f
                    : tip.TipDiameter * 0.5f;
                bounds = IncludeSphere(bounds,
                    tip.BallDiameter > 0 ? tip.ContactBallCenter : tip.Position,
                    MathF.Max(contactRadius, 0f));
                continue;
            }

            bounds = IncludeSphere(bounds, a.Position, radius);
            bounds = IncludeSphere(bounds, b.Position, radius);
        }

        foreach (var node in graph.Nodes)
        {
            if (node.Hidden || node.Type != SupportNodeType.Base ||
                node.BaseShape == SupportBaseShape.None) continue;
            var radius = MathF.Max(node.BaseDiameter * 0.5f, 0f);
            foreach (var segment in graph.SegmentsAt(node.Id))
                if (!segment.Hidden)
                    radius = MathF.Max(radius, MathF.Max(segment.Diameter * 0.5f, 0f));
            var top = node.Position + Vector3.UnitZ *
                (MathF.Max(node.BaseHeight, 0f) +
                 (node.BaseShape == SupportBaseShape.DiscCone
                     ? MathF.Max(node.BaseConeHeight, 0f)
                     : 0f));
            var extent = new Vector3(radius, radius, 0f);
            bounds = bounds.Union(new Aabb(Vector3.Min(node.Position, top) - extent,
                Vector3.Max(node.Position, top) + extent));
        }

        return bounds;
    }

    private static Aabb IncludeSphere(Aabb bounds, Vector3 centre, float radius)
    {
        var extent = new Vector3(radius);
        return bounds.Union(new Aabb(centre - extent, centre + extent));
    }

    /// <summary>
    /// Builds render meshes for every visible segment of <paramref name="graph"/>, grouped by
    /// (kind, selected, disabled). <paramref name="isSelected"/> may be null when nothing is.
    /// </summary>
    /// <param name="includeHidden">Draws elements the user hid individually. Layout passes true
    /// (see <see cref="SupportDisplayPolicy.ForWorkspace"/>); Support mode leaves them out.</param>
    public static IReadOnlyList<SupportRenderPart> Build(SupportGraph graph,
        Func<Guid, bool>? isSelected = null,
        Func<SupportSegment, bool>? includeSegment = null,
        Func<SupportNode, bool>? includeBase = null,
        bool includeHidden = false)
    {
        var builders = new Dictionary<(SupportRenderKind Kind, bool Selected, bool Disabled), MeshBuilder>();

        var capOwners = CapOwners(graph);
        foreach (var segment in graph.Segments)
        {
            if (segment.Hidden && !includeHidden) continue;
            if (!(includeSegment?.Invoke(segment) ?? true)) continue;
            var a = graph.GetNode(segment.NodeA);
            var b = graph.GetNode(segment.NodeB);
            if (!includeHidden && (a.Hidden || b.Hidden)) continue;

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
                AppendConeTip(builder, tip, other,
                    SupportSliceGeometry.TipJunctionDiameter(graph, segment) * 0.5f);
            else
                AppendCapsule(builder, a.Position, b.Position, segment.Diameter * 0.5f,
                    tuckCapA: capOwners.GetValueOrDefault(a.Id) != segment.Id,
                    tuckCapB: capOwners.GetValueOrDefault(b.Id) != segment.Id);
        }

        foreach (var node in graph.Nodes)
        {
            if ((node.Hidden && !includeHidden) || node.Type != SupportNodeType.Base ||
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
        var capOwners = CapOwners(graph);
        foreach (var segment in graph.Segments)
        {
            if (segment.Hidden || !isSelected(segment.Id) ||
                !(includeSegment?.Invoke(segment) ?? true)) continue;
            var a = graph.GetNode(segment.NodeA);
            var b = graph.GetNode(segment.NodeB);
            if (a.Hidden || b.Hidden) continue;
            if (SupportSliceGeometry.TryConeTip(a, b, out var tip, out var other))
                AppendConeTip(builder, tip, other,
                    SupportSliceGeometry.TipJunctionDiameter(graph, segment) * 0.5f);
            else
                AppendCapsule(builder, a.Position, b.Position, segment.Diameter * 0.5f,
                    tuckCapA: capOwners.GetValueOrDefault(a.Id) != segment.Id,
                    tuckCapB: capOwners.GetValueOrDefault(b.Id) != segment.Id);
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
    /// Renders a cone-shaped tip member from the same sections <see cref="SupportSliceGeometry.ConeTipSection"/>
    /// slices: one taper from the contact to the radius of the ball at the junction, with the
    /// base ring at that ball's centre, and the contact ball (or a contact-radius sphere) at
    /// the tip.
    /// </summary>
    public static void AppendConeTip(MeshBuilder builder, SupportNode tip, SupportNode other,
        float junctionRadius)
    {
        var sections = TipBodyGeometry.Sections(tip, other, junctionRadius,
            embedContact: false);
        if (sections.Count == 0)
        {
            AppendCapsule(builder, tip.Position, other.Position, junctionRadius);
            return;
        }

        AppendTipBody(builder, sections);
        if (tip.BallDiameter > 0)
            AppendSphere(builder, tip.ContactBallCenter, tip.BallDiameter * 0.5f);
        else if (tip.TipDiameter > 0)
            AppendSphere(builder, tip.Position, tip.TipDiameter * 0.5f);
    }

    /// <summary>
    /// Stitches a tip into one closed shell. Sections meeting in line share the ring between them;
    /// only a bend gets a second ring, so each ring stays perpendicular to its own analytic
    /// frustum axis. Only the contact and junction ends are capped, the first by the same
    /// contact-position pole the straight tip has always used.
    /// </summary>
    private static void AppendTipBody(MeshBuilder builder, IReadOnlyList<TipBodySection> sections)
    {
        var rings = new List<int[]>(sections.Count + 1);
        Vector3 u = default;
        Vector3 v = default;
        var previousTangent = Vector3.Zero;
        for (var i = 0; i < sections.Count; i++)
        {
            var section = sections[i];
            var tangent = Vector3.Normalize(section.End - section.Start);
            var bend = i > 0 && Vector3.Dot(tangent, previousTangent) < 1f - 1e-6f;
            if (i == 0)
            {
                (u, v) = OrthonormalFrame(tangent);
            }
            else if (bend)
            {
                // Keep corresponding vertices aligned around the bend.
                var transportedU = u - tangent * Vector3.Dot(u, tangent);
                if (transportedU.LengthSquared() <= 1e-12f)
                {
                    (u, v) = OrthonormalFrame(tangent);
                }
                else
                {
                    u = Vector3.Normalize(transportedU);
                    v = Vector3.Cross(tangent, u);
                }
            }

            if (i == 0 || bend)
                rings.Add(AddRing(builder, section.Start, u, v,
                    MathF.Max(section.StartRadius, 0f)));
            rings.Add(AddRing(builder, section.End, u, v, MathF.Max(section.EndRadius, 0f)));
            previousTangent = tangent;
        }

        var tipPole = builder.AddVertex(sections[0].Start);
        var junctionPole = builder.AddVertex(sections[^1].End);
        StitchShell(builder, rings.ToArray(), tipPole, junctionPole);
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
        => AppendCapsule(builder, a, b, radius, tuckCapA: false, tuckCapB: false);

    /// <summary>
    /// How far short of the node a tucked end starts drawing in, as a fraction of the radius,
    /// and how much of the radius its cap keeps. The cap ends up wholly inside the ball the
    /// joint's owner draws, so the two never share a surface; the draw-in pierces that ball on
    /// a clean line instead of lying on it.
    /// </summary>
    private const float TuckedCapRun = 0.25f;
    private const float TuckedCapScale = 0.94f;

    /// <summary>
    /// Capsule with either end cap optionally tucked inside the joint's ball. At a node where
    /// several members meet, exactly one draws the ball; the others tuck (see
    /// <see cref="CapOwners"/>), which removes the flicker of coincident caps.
    /// </summary>
    public static void AppendCapsule(MeshBuilder builder, Vector3 a, Vector3 b, float radius,
        bool tuckCapA, bool tuckCapB)
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
        var run = MathF.Min(radius * TuckedCapRun, length * 0.5f);
        var scaleA = tuckCapA ? TuckedCapScale : 1f;
        var scaleB = tuckCapB ? TuckedCapScale : 1f;

        // Rings from the bottom pole (at a - w r) to the top pole (at b + w r): CapStacks rings on
        // the lower hemisphere ending at a's equator, the equator again at b, then CapStacks - 1
        // rings on the upper hemisphere. Ring angle phi runs pole to equator. A tucked end adds a
        // full-radius ring a short run into the member, so the body draws in to the smaller cap.
        var rings = new List<int[]>(2 * CapStacks + 2);
        for (int k = 1; k <= CapStacks; k++)
        {
            var phi = k * (MathF.PI / 2f) / CapStacks;
            var ringRadius = radius * scaleA * MathF.Sin(phi);
            var drop = radius * scaleA * MathF.Cos(phi);
            rings.Add(AddRing(builder, a - w * drop, u, v, ringRadius));
        }
        if (tuckCapA) rings.Add(AddRing(builder, a + w * run, u, v, radius));
        if (tuckCapB) rings.Add(AddRing(builder, b - w * run, u, v, radius));
        for (int k = CapStacks; k >= 1; k--)
        {
            var phi = k * (MathF.PI / 2f) / CapStacks;
            var ringRadius = radius * scaleB * MathF.Sin(phi);
            var rise = radius * scaleB * MathF.Cos(phi);
            rings.Add(AddRing(builder, b + w * rise, u, v, ringRadius));
        }

        var bottomPole = builder.AddVertex(a - w * radius * scaleA);
        var topPole = builder.AddVertex(b + w * radius * scaleB);
        StitchShell(builder, rings.ToArray(), bottomPole, topPole);
    }

    /// <summary>
    /// For every node, the one capsule member that draws the joint's ball: the widest, then a
    /// trunk over a branch over anything else, then the lowest id. Cone tips never own a ball;
    /// theirs is the parent's.
    /// </summary>
    internal static Dictionary<Guid, Guid> CapOwners(SupportGraph graph)
    {
        var owners = new Dictionary<Guid, Guid>();
        var best = new Dictionary<Guid, (float Diameter, int Rank, Guid Id)>();
        foreach (var segment in graph.Segments)
        {
            var a = graph.GetNode(segment.NodeA);
            var b = graph.GetNode(segment.NodeB);
            if (SupportSliceGeometry.TryConeTip(a, b, out _, out _)) continue;
            var rank = segment.Type switch
            {
                SupportSegmentType.Trunk => 0,
                SupportSegmentType.Branch => 1,
                _ => 2,
            };
            foreach (var nodeId in new[] { segment.NodeA, segment.NodeB })
            {
                var candidate = (segment.Diameter, rank, segment.Id);
                if (!best.TryGetValue(nodeId, out var current) ||
                    candidate.Diameter > current.Diameter + 1e-6f ||
                    MathF.Abs(candidate.Diameter - current.Diameter) <= 1e-6f &&
                    (candidate.rank < current.Rank ||
                     candidate.rank == current.Rank && candidate.Id.CompareTo(current.Id) < 0))
                {
                    best[nodeId] = candidate;
                    owners[nodeId] = segment.Id;
                }
            }
        }
        return owners;
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
