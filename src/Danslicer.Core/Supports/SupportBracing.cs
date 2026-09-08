using System.Numerics;
using Danslicer.Core.Config;
using Danslicer.Core.Geometry;
using Danslicer.Core.Supports.Routing;

namespace Danslicer.Core.Supports;

/// <summary>What one bracing run did, for the status line.</summary>
public sealed record BracingOutcome(int Operands, int Braces, int SupportsTied);

/// <summary>
/// Bracing (SUPPORT-GEOMETRY-SPEC "Bracing", user-approved 2026-09-09): short cross-members
/// between the trunks of neighbouring supports, laid as a ladder up each pair. Braces are added
/// on — each end is a <see cref="SupportNodeType.BraceEnd"/> node on the trunk's axis and the
/// trunk itself is never split. Pure planning over a graph: the document turns the plan into
/// one undo step.
/// </summary>
public static class SupportBracing
{
    /// <summary>How far off a member's axis a brace end may sit and still count as carried by it.</summary>
    public const float CarrierTolerance = 0.05f;
    private const float Epsilon = 1e-3f;

    /// <summary>One support's vertical run of trunk segments sharing an axis.</summary>
    private sealed class Column
    {
        public required int Support { get; init; }
        public required Vector2 Xy { get; init; }
        /// <summary>Lowest point a brace may sit: the base top, or the lowest node.</summary>
        public required float Bottom { get; init; }
        public required float Top { get; init; }
        public required List<SupportSegment> Segments { get; init; }
        public required HashSet<Guid> SegmentIds { get; init; }
        public required List<SupportNode> Nodes { get; init; }
        public int Partners { get; set; }
    }

    /// <summary>
    /// Plans the braces for the supports containing <paramref name="operandElementIds"/> (nodes
    /// or segments) on the target. Braces that already tie two of those trunks are kept and that
    /// pair is left alone, so running twice adds nothing. Null when there is nothing to brace.
    /// <paramref name="meshes"/> is the model obstacle scene. With <paramref name="chosen"/> (the
    /// operands are the user's selection) and exactly two supports, the two are braced no matter
    /// what stands between or how far apart they are (user direction 2026-09-09).
    /// </summary>
    public static (SupportGraphEdit Edit, BracingOutcome Outcome)? Plan(SupportGraph graph, Guid targetId,
        IReadOnlyList<Guid> operandElementIds, SupportConfig settings, ICollisionScene meshes, bool chosen = false)
    {
        var components = Components(graph, targetId, operandElementIds);
        if (components.Count < 2) return null;

        var columns = new List<Column>();
        for (var i = 0; i < components.Count; i++) columns.AddRange(Columns(graph, components[i].Segments, i));
        if (columns.Count < 2) return null;

        var minHeight = settings.BracingMinSupportHeightMm;
        var neighbour = settings.BracingNeighbourDistanceMm;
        var diameter = settings.BracingDiameter > 0 ? settings.BracingDiameter : settings.BranchDiameter;
        var radius = diameter * 0.5f;
        var tan = MathF.Tan(Math.Clamp(settings.BracingAngleDegrees, 0f, 80f) * MathF.PI / 180f);
        var lowest = settings.BracingLowestHeightMm > 0 ? settings.BracingLowestHeightMm : settings.MinBranchAttachHeightMm;
        var origin = SupportOrigin.ManualFor(targetId);

        // Braces already standing: count partners and the pairs that are done.
        var braced = new HashSet<(int, int)>();
        foreach (var brace in graph.Segments)
        {
            if (brace.Type != SupportSegmentType.Bracing || brace.Disabled) continue;
            var a = ColumnAt(columns, graph.GetNode(brace.NodeA).Position);
            var b = ColumnAt(columns, graph.GetNode(brace.NodeB).Position);
            if (a < 0 || b < 0 || a == b) continue;
            if (!braced.Add(a < b ? (a, b) : (b, a))) continue;
            columns[a].Partners++;
            columns[b].Partners++;
        }

        // Two supports chosen by hand are braced no matter what stands between or how far apart
        // they are (user direction 2026-09-09): the other supports are not obstacles and the
        // neighbour distance and partner cap do not apply.
        var pairOnly = chosen && components.Count == 2;
        var candidates = columns.Select((c, i) => (Column: c, Index: i)).Where(x => x.Column.Top >= minHeight)
            .Select(x => x.Index).ToList();
        bool Neighbours(int i, int j) => columns[i].Support != columns[j].Support &&
            Vector2.Distance(columns[i].Xy, columns[j].Xy) > settings.TrunkDiameter &&
            (pairOnly || Vector2.Distance(columns[i].Xy, columns[j].Xy) <= neighbour);

        // Chains (user drawing 2026-09-09): from an end of a row, each trunk pairs with its nearest
        // unvisited neighbour, and each pair's ladder runs opposite to the previous pair's.
        var chains = new List<List<int>>();
        var remaining = new HashSet<int>(candidates);
        while (remaining.Count > 0)
        {
            var first = remaining.OrderBy(i => remaining.Count(j => j != i && Neighbours(i, j)))
                .ThenBy(i => columns[i].Xy.X).ThenBy(i => columns[i].Xy.Y).ThenBy(i => columns[i].Segments[0].Id).First();
            var chain = new List<int> { first };
            remaining.Remove(first);
            while (true)
            {
                var last = chain[^1];
                var next = remaining.Where(j => Neighbours(last, j))
                    .OrderBy(j => Vector2.Distance(columns[last].Xy, columns[j].Xy))
                    .ThenBy(j => columns[j].Segments[0].Id).Cast<int?>().FirstOrDefault();
                if (next is not { } n) break;
                chain.Add(n);
                remaining.Remove(n);
            }
            chains.Add(chain);
        }

        ICollisionScene scene = meshes;
        if (!pairOnly)
        {
            var supportsScene = new LinearCollisionScene();
            supportsScene.AddSupportGraph(graph);
            scene = new CompositeCollisionScene(meshes, supportsScene);
        }
        // Braces never block braces: one pair's ladder crosses the next pair's freely.
        var braceIds = graph.Segments.Where(x => x.Type == SupportSegmentType.Bracing).Select(x => x.Id).ToHashSet();
        var addedNodes = new List<SupportNode>();
        var addedSegments = new List<SupportSegment>();
        var tied = new HashSet<int>();

        SupportNode EndAt(Vector3 position)
        {
            foreach (var node in addedNodes)
                if (Vector3.Distance(node.Position, position) <= 0.01f) return node;
            var created = new SupportNode { Type = SupportNodeType.BraceEnd, Position = position, Origin = origin };
            addedNodes.Add(created);
            return created;
        }

        foreach (var chain in chains)
        for (var k = 0; k + 1 < chain.Count; k++)
        {
            var (ia, ib) = (chain[k], chain[k + 1]);
            var a = columns[ia];
            var b = columns[ib];
            if (braced.Contains(ia < ib ? (ia, ib) : (ib, ia))) continue;
            if (!pairOnly && (a.Partners >= settings.BracingMaxPartners || b.Partners >= settings.BracingMaxPartners)) continue;

            var distance = Vector2.Distance(a.Xy, b.Xy);
            var rise = distance * tan;
            var foot = MathF.Max(lowest, MathF.Max(a.Bottom, b.Bottom) + radius);
            // A chosen pair too far apart for the angle gets flatter braces rather than none.
            if (pairOnly) rise = MathF.Max(0f, MathF.Min(rise, MathF.Min(a.Top, b.Top) - radius - foot));
            // Continuous by default: the next brace starts where the last one ended.
            var step = settings.BracingSpacingMm > 0 ? settings.BracingSpacingMm : rise;
            if (step < radius * 2 + Epsilon) step = MathF.Max(rise, radius * 2 + Epsilon);
            // Even pairs climb from the earlier trunk, odd pairs from the later one, so the
            // ladders alternate direction along the row.
            var fromA = k % 2 == 0;
            var laid = 0;
            while (true)
            {
                var (from, to) = fromA ? (a, b) : (b, a);
                var head = foot + rise;
                if (foot > from.Top - radius || head > to.Top - radius) break;
                var startPoint = new Vector3(from.Xy, foot);
                var endPoint = new Vector3(to.Xy, head);
                if (Clear(scene, graph, from, to, startPoint, endPoint, radius, braceIds))
                {
                    var footNode = EndAt(startPoint);
                    var headNode = EndAt(endPoint);
                    addedSegments.Add(new SupportSegment
                    {
                        Type = SupportSegmentType.Bracing, NodeA = footNode.Id, NodeB = headNode.Id,
                        Diameter = diameter, Origin = origin,
                    });
                    laid++;
                }
                foot += step;
                if (settings.BracingPattern == BracingPattern.Zigzag) fromA = !fromA;
            }
            if (laid == 0) continue;
            a.Partners++;
            b.Partners++;
            braced.Add(ia < ib ? (ia, ib) : (ib, ia));
            tied.Add(a.Support);
            tied.Add(b.Support);
        }

        if (addedSegments.Count == 0)
            return (new SupportGraphEdit([], [], []), new BracingOutcome(components.Count, 0, 0));
        return (new SupportGraphEdit(addedNodes, addedSegments, []),
            new BracingOutcome(components.Count, addedSegments.Count, tied.Count));
    }

    /// <summary>
    /// The braces touching the supports containing <paramref name="operandElementIds"/>, with
    /// both their end nodes, for Unbrace and for taking a support down.
    /// </summary>
    public static (List<Guid> Nodes, List<Guid> Segments) BracesOf(SupportGraph graph, Guid? targetId,
        IReadOnlyList<Guid> operandElementIds)
    {
        var segments = new HashSet<Guid>();
        foreach (var component in Components(graph, targetId, operandElementIds))
            segments.UnionWith(component.Segments);
        return BracesOn(graph, segments);
    }

    /// <summary>
    /// The braces whose ends are carried by any of <paramref name="carrierSegmentIds"/>, with
    /// both end nodes of each. Removing a trunk must remove these with it.
    /// </summary>
    public static (List<Guid> Nodes, List<Guid> Segments) BracesOn(SupportGraph graph, IReadOnlySet<Guid> carrierSegmentIds)
    {
        var nodes = new HashSet<Guid>();
        var segments = new HashSet<Guid>();
        foreach (var node in graph.Nodes)
        {
            if (node.Type != SupportNodeType.BraceEnd) continue;
            if (CarrierOf(graph, node, id => carrierSegmentIds.Contains(id)) is null) continue;
            foreach (var brace in graph.SegmentsAt(node.Id))
            {
                segments.Add(brace.Id);
                nodes.Add(brace.NodeA);
                nodes.Add(brace.NodeB);
            }
        }
        return (nodes.ToList(), segments.ToList());
    }

    /// <summary>
    /// The member whose axis carries <paramref name="braceEnd"/>: a non-bracing segment passing
    /// within <see cref="CarrierTolerance"/> of it, preferring trunks. Null when none does, which
    /// is when the brace end is orphaned.
    /// </summary>
    public static SupportSegment? CarrierOf(SupportGraph graph, SupportNode braceEnd, Func<Guid, bool>? candidate = null)
    {
        SupportSegment? best = null;
        var bestRank = int.MaxValue;
        foreach (var segment in graph.Segments)
        {
            if (segment.Type == SupportSegmentType.Bracing || (candidate is not null && !candidate(segment.Id))) continue;
            var rank = segment.Type == SupportSegmentType.Trunk ? 0 : 1;
            if (rank >= bestRank) continue;
            var a = graph.GetNode(segment.NodeA).Position;
            var b = graph.GetNode(segment.NodeB).Position;
            var closest = GeometryDistance.ClosestPointOnSegment(braceEnd.Position, a, b);
            if (Vector3.Distance(closest, braceEnd.Position) > CarrierTolerance) continue;
            best = segment;
            bestRank = rank;
        }
        return best;
    }

    /// <summary>The whole supports (bracing excluded) containing the given elements, owned by the target.</summary>
    private static List<(HashSet<Guid> Nodes, HashSet<Guid> Segments)> Components(SupportGraph graph, Guid? targetId,
        IEnumerable<Guid> elementIds)
    {
        var result = new List<(HashSet<Guid>, HashSet<Guid>)>();
        var seen = new HashSet<Guid>();
        foreach (var id in elementIds)
        {
            Guid nodeId;
            if (graph.TryGetNode(id, out var node))
            {
                if (node.Type == SupportNodeType.BraceEnd) continue;
                nodeId = id;
            }
            else if (graph.TryGetSegment(id, out var segment))
            {
                if (segment.Type == SupportSegmentType.Bracing) continue;
                nodeId = segment.NodeA;
            }
            else continue;
            if (seen.Contains(nodeId)) continue;
            if (targetId is { } target && graph.OwningObjectId(nodeId) is { } owner && owner != target) continue;
            var component = graph.Component(nodeId);
            seen.UnionWith(component.Nodes);
            result.Add(component);
        }
        return result;
    }

    /// <summary>The vertical trunk runs of one support, one column per axis.</summary>
    private static IEnumerable<Column> Columns(SupportGraph graph, IEnumerable<Guid> segmentIds, int support)
    {
        var byAxis = new Dictionary<(int, int), List<SupportSegment>>();
        foreach (var id in segmentIds)
        {
            var segment = graph.GetSegment(id);
            if (segment.Type != SupportSegmentType.Trunk || segment.Disabled) continue;
            var a = graph.GetNode(segment.NodeA).Position;
            var b = graph.GetNode(segment.NodeB).Position;
            if (Vector2.Distance(new(a.X, a.Y), new(b.X, b.Y)) > 0.01f) continue;
            var key = ((int)MathF.Round(a.X * 50), (int)MathF.Round(a.Y * 50));
            if (!byAxis.TryGetValue(key, out var list)) byAxis[key] = list = [];
            list.Add(segment);
        }
        foreach (var list in byAxis.Values.OrderBy(l => l[0].Id))
        {
            var nodes = list.SelectMany(s => new[] { graph.GetNode(s.NodeA), graph.GetNode(s.NodeB) })
                .DistinctBy(n => n.Id).ToList();
            var bottom = nodes.Min(n => n.Type == SupportNodeType.Base ? n.Position.Z + n.BaseHeight : n.Position.Z);
            var top = nodes.Max(n => n.Position.Z);
            var first = graph.GetNode(list[0].NodeA).Position;
            yield return new Column
            {
                Support = support, Xy = new Vector2(first.X, first.Y), Bottom = bottom, Top = top,
                Segments = list, SegmentIds = list.Select(s => s.Id).ToHashSet(), Nodes = nodes,
            };
        }
    }

    private static int ColumnAt(List<Column> columns, Vector3 position)
    {
        for (var i = 0; i < columns.Count; i++)
        {
            var column = columns[i];
            if (Vector2.Distance(column.Xy, new Vector2(position.X, position.Y)) > CarrierTolerance) continue;
            if (position.Z < column.Bottom - CarrierTolerance || position.Z > column.Top + CarrierTolerance) continue;
            return i;
        }
        return -1;
    }

    /// <summary>
    /// Whether a brace from <paramref name="start"/> on <paramref name="from"/> to
    /// <paramref name="end"/> on <paramref name="to"/> touches nothing but those two trunks. A
    /// member leaving either trunk at a joint the brace end sits inside is that joint's ball,
    /// not a crossing, so it is ignored; anything further along a member is a real collision.
    /// </summary>
    private static bool Clear(ICollisionScene scene, SupportGraph graph, Column from, Column to,
        Vector3 start, Vector3 end, float radius, HashSet<Guid> braceIds)
    {
        var ignored = new HashSet<Guid>(from.SegmentIds);
        ignored.UnionWith(braceIds);
        ignored.UnionWith(to.SegmentIds);
        foreach (var (column, point) in new[] { (from, start), (to, end) })
            foreach (var node in column.Nodes)
                foreach (var incident in graph.SegmentsAt(node.Id))
                    if (Vector3.Distance(node.Position, point) <= radius + incident.Diameter * 0.5f + Epsilon)
                        ignored.Add(incident.Id);
        return !scene.IntersectsCapsule(start, end, radius, tag => tag is not Guid id || !ignored.Contains(id));
    }
}
