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

    /// <summary>
    /// One support's stem: the trunk from its base upward, plus any branch that continues it
    /// within the max lean (user drawing 2026-09-09: the near-vertical branches parenting
    /// leaves above a short trunk are what the braces climb). A polyline in Z order.
    /// </summary>
    private sealed class Column
    {
        public required int Support { get; init; }
        /// <summary>Axis points from the bottom node up to the top node.</summary>
        public required List<Vector3> Points { get; init; }
        /// <summary>Lowest point a brace may sit: the base top, or the lowest node.</summary>
        public required float Bottom { get; init; }
        public float Top => Points[^1].Z;
        public Vector2 Xy => new(Points[0].X, Points[0].Y);
        public required List<SupportSegment> Segments { get; init; }
        public required HashSet<Guid> SegmentIds { get; init; }
        public required List<SupportNode> Nodes { get; init; }
        public int Partners { get; set; }

        /// <summary>The axis point at height <paramref name="z"/>, clamped to the stem's ends.</summary>
        public Vector3 At(float z)
        {
            if (z <= Points[0].Z) return Points[0];
            for (var i = 1; i < Points.Count; i++)
            {
                if (z > Points[i].Z) continue;
                var a = Points[i - 1];
                var b = Points[i];
                var t = b.Z - a.Z < 1e-6f ? 1f : (z - a.Z) / (b.Z - a.Z);
                return Vector3.Lerp(a, b, t);
            }
            return Points[^1];
        }

        /// <summary>Distance from <paramref name="position"/> to the stem's axis.</summary>
        public float DistanceTo(Vector3 position)
        {
            var best = float.MaxValue;
            for (var i = 1; i < Points.Count; i++)
                best = MathF.Min(best, Vector3.Distance(position,
                    GeometryDistance.ClosestPointOnSegment(position, Points[i - 1], Points[i])));
            return best;
        }
    }

    /// <summary>
    /// A cluster of stems standing closer than the cluster gap (user decision 2026-09-09): braced
    /// as one stem, with no braces inside it. Every lone stem is a bundle of one.
    /// </summary>
    private sealed class Bundle
    {
        public required List<Column> Members { get; init; }
        public required HashSet<int> Supports { get; init; }
        public Vector2 Xy => Members.Aggregate(Vector2.Zero, (sum, c) => sum + c.Xy) / Members.Count;
        public float Top => Members.Max(c => c.Top);
        public float Bottom => Members.Max(c => c.Bottom);
        public Guid Id => Members.Min(c => c.Segments[0].Id);
        public int Partners { get; set; }

        /// <summary>The member that reaches <paramref name="z"/> and is nearest <paramref name="toward"/> there.</summary>
        public Column? NearestAt(float z, Vector2 toward, float margin) =>
            Members.Where(c => c.Top - margin >= z)
                .OrderBy(c => Vector2.Distance(new(c.At(z).X, c.At(z).Y), toward))
                .ThenBy(c => c.Segments[0].Id).FirstOrDefault();
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
        for (var i = 0; i < components.Count; i++)
            columns.AddRange(Columns(graph, components[i].Segments, i, settings.BracingMaxStemLeanDegrees));
        if (columns.Count < 2) return null;

        var automatic = settings.BracingPattern == BracingPattern.Automatic;
        var minHeight = automatic ? 0 : settings.BracingMinSupportHeightMm;
        var neighbour = settings.BracingNeighbourDistanceMm;
        var diameter = settings.BracingDiameter > 0 ? settings.BracingDiameter : settings.BranchDiameter;
        var radius = diameter * 0.5f;
        // The brace angle is the most a brace may lean from vertical (user, 2026-09-09): every
        // rung is laid at exactly that lean, and a rung that cannot fit at it is dropped.
        var cot = 1f / MathF.Tan(Math.Clamp(settings.BracingAngleDegrees, 1f, 89f) * MathF.PI / 180f);
        var lowest = settings.BracingLowestHeightMm > 0 ? settings.BracingLowestHeightMm : settings.MinBranchAttachHeightMm;
        var origin = SupportOrigin.ManualFor(targetId);

        // Two supports chosen by hand are braced no matter what stands between or how far apart
        // they are (user direction 2026-09-09): the other supports are not obstacles and the
        // neighbour distance and partner cap do not apply, and they are not clustered.
        var pairOnly = chosen && components.Count == 2;

        // Bundles: stems closer than the cluster gap are one stem for bracing (user decision
        // 2026-09-09: a cluster of trunks is already a bundle; rungs inside it would be stubs).
        // The cluster gap is between trunk surfaces, so a field at the ordinary tip spacing is
        // not a cluster (screen test 2026-09-09: 2.5 mm rows had merged into bundles).
        var bundles = Bundles(columns, pairOnly ? 0f : settings.BracingClusterGapMm + settings.TrunkDiameter);
        if (bundles.Count < 2) return null;

        // Braces already standing: count partners and the pairs that are done.
        var braced = new HashSet<(int, int)>();
        foreach (var brace in graph.Segments)
        {
            if (brace.Type != SupportSegmentType.Bracing || brace.Disabled) continue;
            var a = BundleAt(bundles, graph.GetNode(brace.NodeA).Position);
            var b = BundleAt(bundles, graph.GetNode(brace.NodeB).Position);
            if (a < 0 || b < 0 || a == b) continue;
            if (!braced.Add(a < b ? (a, b) : (b, a))) continue;
            bundles[a].Partners++;
            bundles[b].Partners++;
        }

        var candidates = bundles.Select((bundle, index) => (Bundle: bundle, Index: index))
            .Where(x => x.Bundle.Top >= minHeight).Select(x => x.Index).ToList();
        bool Neighbours(int i, int j) => !bundles[i].Supports.Overlaps(bundles[j].Supports) &&
            Vector2.Distance(bundles[i].Xy, bundles[j].Xy) > settings.TrunkDiameter &&
            (automatic || pairOnly || Vector2.Distance(bundles[i].Xy, bundles[j].Xy) <= neighbour);

        // Chains (user drawing 2026-09-09): from an end of a row, each bundle pairs with its
        // nearest unvisited neighbour, and each pair's ladder runs opposite to the previous pair's.
        var chains = new List<List<int>>();
        var remaining = new HashSet<int>(candidates);
        while (remaining.Count > 0)
        {
            var first = remaining.OrderBy(i => remaining.Count(j => j != i && Neighbours(i, j)))
                .ThenBy(i => bundles[i].Xy.X).ThenBy(i => bundles[i].Xy.Y).ThenBy(i => bundles[i].Id).First();
            var chain = new List<int> { first };
            remaining.Remove(first);
            while (true)
            {
                var last = chain[^1];
                var next = remaining.Where(j => Neighbours(last, j))
                    .OrderBy(j => Vector2.Distance(bundles[last].Xy, bundles[j].Xy))
                    .ThenBy(j => bundles[j].Id).Cast<int?>().FirstOrDefault();
                if (next is not { } n) break;
                chain.Add(n);
                remaining.Remove(n);
            }
            chains.Add(chain);
        }

        // Branch intersections remain allowed, but brace crossovers are not. Keep the committed
        // brace geometry separate from trial ladders so rejected alternatives cannot block a pair.
        var scene = meshes;
        var standingBraces = graph.Segments
            .Where(s => s.Type == SupportSegmentType.Bracing && !s.Disabled &&
                !graph.GetNode(s.NodeA).Disabled && !graph.GetNode(s.NodeB).Disabled)
            .Select(s => (Start: graph.GetNode(s.NodeA).Position, End: graph.GetNode(s.NodeB).Position,
                Radius: s.Diameter * 0.5f)).ToList();
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

        // Chain pairs first, alternating direction along each chain; then every remaining
        // neighbour pair nearest first, so a field of supports (not just a row) is tied in more
        // than one direction, up to the partner cap (screen test 2026-09-09: a 2D field left
        // the trunks off the chain's path with nothing).
        var pairs = new List<(int A, int B, bool FromA)>();
        foreach (var chain in chains)
            for (var k = 0; k + 1 < chain.Count; k++) pairs.Add((chain[k], chain[k + 1], k % 2 == 0));
        var extra = new List<(int A, int B, float Distance)>();
        for (var i = 0; i < candidates.Count; i++)
        for (var j = i + 1; j < candidates.Count; j++)
        {
            var (ci, cj) = (candidates[i], candidates[j]);
            if (!Neighbours(ci, cj)) continue;
            extra.Add((ci, cj, Vector2.Distance(bundles[ci].Xy, bundles[cj].Xy)));
        }
        foreach (var e in extra.OrderBy(e => e.Distance).ThenBy(e => bundles[e.A].Id).ThenBy(e => bundles[e.B].Id))
            pairs.Add((e.A, e.B, bundles[e.A].Partners % 2 == 0));

        var attemptedPairs = new HashSet<(int, int)>();
        foreach (var (ia, ib, startFromA) in pairs)
        {
            var a = bundles[ia];
            var b = bundles[ib];
            if (braced.Contains(ia < ib ? (ia, ib) : (ib, ia))) continue;
            if (!attemptedPairs.Add(ia < ib ? (ia, ib) : (ib, ia))) continue;
            if (automatic && a.Partners > 0 && b.Partners > 0) continue;
            if (!automatic && !pairOnly && (a.Partners >= settings.BracingMaxPartners || b.Partners >= settings.BracingMaxPartners)) continue;

            var floor = MathF.Max(automatic ? 0 : lowest, MathF.Max(a.Bottom, b.Bottom) + radius);
            var routes = automatic ? new List<(Vector3 Start, Vector3 End)>()
                : Ladder(startFromA, cot, settings.BracingPattern, 0);
            if (automatic)
            {
                // Fit a whole number of alternating rungs into this pair's common height.
                // Each pair derives its own rise; pitch from another pair is never reused.
                var top = MathF.Min(a.Top, b.Top) - radius;
                var endpointGap = settings.BracingEndpointGapMm;
                var height = top - floor;
                var distance = Vector2.Distance(a.Xy, b.Xy);
                var maxRungs = Math.Clamp((int)MathF.Floor((MathF.Max(0, height) + endpointGap) /
                    (MathF.Max(diameter, 0.1f) + endpointGap)), 1, 256);
                var counts = Enumerable.Range(1, maxRungs).OrderBy(n =>
                    MathF.Abs((height - (n - 1) * endpointGap) / n - distance)).ToArray();
                // Preserve the chain's opposite starting direction whenever it can fit.
                foreach (var direction in new[] { startFromA, !startFromA })
                {
                    foreach (var offset in new[] { 0f, radius, diameter })
                    {
                        foreach (var count in counts)
                        {
                            var trial = FitZigzag(direction, top - offset, count, endpointGap);
                            if (trial.Count == 0) continue;
                            routes = trial;
                            break;
                        }
                        if (routes.Count > 0) break;
                    }
                    if (routes.Count > 0) break;
                }
                // An obstruction may make a complete ladder impossible. After all complete
                // fits fail, keep the longest uninterrupted zigzag, never scattered rungs.
                if (routes.Count == 0)
                {
                    var bestSpan = 0f;
                    foreach (var direction in new[] { startFromA, !startFromA })
                    foreach (var count in counts)
                    {
                        var trial = FitZigzag(direction, top, count, endpointGap, allowPartial: true);
                        if (trial.Count == 0) continue;
                        var span = trial[0].End.Z - trial[^1].Start.Z;
                        if (span <= bestSpan + Epsilon) continue;
                        bestSpan = span;
                        routes = trial;
                    }
                }
            }
            foreach (var (startPoint, endPoint) in routes)
            {
                standingBraces.Add((startPoint, endPoint, radius));
                var footNode = EndAt(startPoint);
                var headNode = EndAt(endPoint);
                addedSegments.Add(new SupportSegment
                {
                    Type = SupportSegmentType.Bracing, NodeA = footNode.Id, NodeB = headNode.Id,
                    Diameter = diameter, Origin = origin,
                });
            }
            if (routes.Count == 0) continue;
            a.Partners++;
            b.Partners++;
            braced.Add(ia < ib ? (ia, ib) : (ib, ia));
            tied.UnionWith(a.Supports);
            tied.UnionWith(b.Supports);

            List<(Vector3 Start, Vector3 End)> FitZigzag(bool fromA, float top, int count, float endpointGap,
                bool allowPartial = false)
            {
                var result = new List<(Vector3 Start, Vector3 End)>();
                var longest = new List<(Vector3 Start, Vector3 End)>();
                var rise = (top - floor - (count - 1) * endpointGap) / count;
                if (rise < MathF.Max(diameter, 0.1f)) return result;
                for (var rung = 0; rung < count; rung++)
                {
                    var head = top - rung * (rise + endpointGap);
                    var foot = head - rise;
                    var (from, to) = fromA ? (a, b) : (b, a);
                    var fromStem = from.NearestAt(foot, to.Xy, radius);
                    var toStem = to.NearestAt(head, from.Xy, radius);
                    if (fromStem is null || toStem is null) return [];
                    var start = fromStem.At(foot);
                    var end = toStem.At(head);
                    var horizontal = Vector2.Distance(new(start.X, start.Y), new(end.X, end.Y));
                    if (horizontal > rise * MathF.Tan(65f * MathF.PI / 180f) + Epsilon ||
                        scene.IntersectsCapsule(start, end, radius) ||
                        standingBraces.Any(r => BracesConflict(start, end, radius, r.Start, r.End, r.Radius)) ||
                        result.Any(r => BracesConflict(start, end, radius, r.Start, r.End, radius)))
                    {
                        if (!allowPartial) return [];
                        if (result.Count > longest.Count) longest = result;
                        result = [];
                    }
                    else result.Add((start, end));
                    fromA = !fromA;
                }
                return longest.Count > result.Count ? longest : result;
            }

            List<(Vector3 Start, Vector3 End)> Ladder(bool fromA, float pairCot, BracingPattern pattern, float offset)
            {
                var result = new List<(Vector3 Start, Vector3 End)>();
                // Even pairs start from the earlier bundle, odd pairs from the later one, so the
                // ladders alternate direction along the row. Laid top-down (user, 2026-09-09): the
                // first rung reaches as high as both stems allow, the next ends where it started.
                var head = (fromA ? b : a).Top - radius - offset;
                var firstRung = true;
                for (var attempt = 0; attempt < 2048; attempt++)
                {
                    var (from, to) = fromA ? (a, b) : (b, a);
                    if (head < floor + radius * 2) break;
                    // A rung lands on the member of each bundle nearest the other bundle at its height.
                    var toStem = to.NearestAt(head, from.Xy, radius);
                    if (toStem is null) { head -= radius * 2; continue; }
                    var toXy = new Vector2(toStem.At(head).X, toStem.At(head).Y);
                    var fromStem = from.NearestAt(MathF.Min(head, from.Top - radius), toXy, radius);
                    if (fromStem is null) break;
                    var gap = Vector2.Distance(new(fromStem.At(head).X, fromStem.At(head).Y), toXy);
                    var rise = gap * pairCot;
                    var foot = head - rise;
                    // The first rung may not start above the stem it leaves: lower it, at its angle.
                    if (firstRung && foot > fromStem.Top - radius)
                    {
                        foot = fromStem.Top - radius;
                        head = foot + rise;
                    }
                    // A rung that would start under the floor cannot be laid at the angle: drop it.
                    if (foot < floor) break;
                    var startPoint = fromStem.At(foot);
                    var endPoint = toStem.At(head);
                    var horizontal = Vector2.Distance(new(startPoint.X, startPoint.Y), new(endPoint.X, endPoint.Y));
                    var withinAngleLimit = !automatic || horizontal <=
                        (endPoint.Z - startPoint.Z) * MathF.Tan(65f * MathF.PI / 180f) + Epsilon;
                    if (withinAngleLimit && !scene.IntersectsCapsule(startPoint, endPoint, radius) &&
                        !standingBraces.Any(r => BracesConflict(startPoint, endPoint, radius, r.Start, r.End, r.Radius)) &&
                        !result.Any(r => BracesConflict(startPoint, endPoint, radius, r.Start, r.End, radius)))
                    {
                        result.Add((startPoint, endPoint));
                    }
                    firstRung = false;
                    // Continuous by default: the next brace ends where this one started.
                    var step = settings.BracingSpacingMm > 0 ? settings.BracingSpacingMm : rise;
                    if (step < radius * 2 + Epsilon) step = MathF.Max(rise, radius * 2 + Epsilon);
                    head -= step;
                    if (pattern == BracingPattern.Zigzag) fromA = !fromA;
                }
                return result;
            }
        }

        if (addedSegments.Count == 0)
            return (new SupportGraphEdit([], [], []), new BracingOutcome(components.Count, 0, 0));
        return (new SupportGraphEdit(addedNodes, addedSegments, []),
            new BracingOutcome(components.Count, addedSegments.Count, tied.Count));
    }

    /// <summary>Shared attachment points are joints; elsewhere the brace cylinders must stay apart.</summary>
    private static bool BracesConflict(Vector3 a, Vector3 b, float radius,
        Vector3 c, Vector3 d, float otherRadius)
    {
        const float jointToleranceSquared = 0.01f * 0.01f;
        if (Vector3.DistanceSquared(a, c) <= jointToleranceSquared ||
            Vector3.DistanceSquared(a, d) <= jointToleranceSquared ||
            Vector3.DistanceSquared(b, c) <= jointToleranceSquared ||
            Vector3.DistanceSquared(b, d) <= jointToleranceSquared) return false;
        var clearance = radius + otherRadius;
        return GeometryDistance.SegmentSegmentSquared(a, b, c, d) < clearance * clearance;
    }

    /// <summary>
    /// Braces entirely within the operand supports, for rebuilding their connections.
    /// Connections to unselected neighbours and shared endpoint nodes must survive.
    /// </summary>
    public static (List<Guid> Nodes, List<Guid> Segments) BracesBetween(SupportGraph graph, Guid? targetId,
        IReadOnlyList<Guid> operandElementIds)
    {
        var carriers = Components(graph, targetId, operandElementIds)
            .SelectMany(component => component.Segments).ToHashSet();
        var segments = graph.Segments.Where(segment => segment.Type == SupportSegmentType.Bracing &&
            IsSelectedEnd(segment.NodeA) && IsSelectedEnd(segment.NodeB)).Select(segment => segment.Id).ToHashSet();
        var nodes = segments.SelectMany(id =>
            new[] { graph.GetSegment(id).NodeA, graph.GetSegment(id).NodeB }).Distinct()
            .Where(id => graph.GetNode(id).Type == SupportNodeType.BraceEnd &&
                graph.SegmentsAt(id).All(segment => segments.Contains(segment.Id))).ToList();
        return (nodes, segments.ToList());

        bool IsSelectedEnd(Guid id) => CarrierOf(graph, graph.GetNode(id)) is { } carrier &&
            carriers.Contains(carrier.Id);
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

    /// <summary>
    /// The stems of one support: from each base, upward through the trunk and then whichever
    /// member continues most nearly vertically, while it leans at most <paramref name="maxLean"/>
    /// from vertical. Cones never count.
    /// </summary>
    private static IEnumerable<Column> Columns(SupportGraph graph, IEnumerable<Guid> segmentIds, int support, float maxLean)
    {
        var members = new HashSet<Guid>(segmentIds);
        var cosLimit = MathF.Cos(maxLean * MathF.PI / 180f);
        var bases = members.SelectMany(id => new[] { graph.GetSegment(id).NodeA, graph.GetSegment(id).NodeB })
            .Distinct().Select(graph.GetNode).Where(n => n.Type == SupportNodeType.Base)
            .OrderBy(n => n.Id).ToList();
        foreach (var baseNode in bases)
        {
            var points = new List<Vector3> { baseNode.Position };
            var nodes = new List<SupportNode> { baseNode };
            var segments = new List<SupportSegment>();
            var current = baseNode;
            while (true)
            {
                SupportSegment? next = null;
                SupportNode? nextNode = null;
                var bestCos = cosLimit;
                foreach (var segment in graph.SegmentsAt(current.Id))
                {
                    if (!members.Contains(segment.Id) || segment.Disabled || segments.Contains(segment)) continue;
                    if (segment.Type is not (SupportSegmentType.Trunk or SupportSegmentType.Branch)) continue;
                    var other = graph.GetNode(segment.NodeA == current.Id ? segment.NodeB : segment.NodeA);
                    if (other.Type == SupportNodeType.Tip) continue;
                    var delta = other.Position - current.Position;
                    var length = delta.Length();
                    if (length < 1e-6f || delta.Z <= 0) continue;
                    var cos = delta.Z / length;
                    if (cos < bestCos - 1e-6f) continue;
                    if (next is not null && MathF.Abs(cos - bestCos) <= 1e-6f && segment.Id.CompareTo(next.Id) >= 0) continue;
                    bestCos = cos;
                    next = segment;
                    nextNode = other;
                }
                if (next is null || nextNode is null) break;
                segments.Add(next);
                nodes.Add(nextNode);
                points.Add(nextNode.Position);
                current = nextNode;
            }
            if (segments.Count == 0) continue;
            yield return new Column
            {
                Support = support, Points = points, Bottom = baseNode.Position.Z + baseNode.BaseHeight,
                Segments = segments, SegmentIds = segments.Select(x => x.Id).ToHashSet(), Nodes = nodes,
            };
        }
    }

    /// <summary>Groups stems whose axes stand within <paramref name="gap"/> of each other (transitively).</summary>
    private static List<Bundle> Bundles(List<Column> columns, float gap)
    {
        var parent = Enumerable.Range(0, columns.Count).ToArray();
        int Find(int i) => parent[i] == i ? i : parent[i] = Find(parent[i]);
        if (gap > 0)
            for (var i = 0; i < columns.Count; i++)
            for (var j = i + 1; j < columns.Count; j++)
                if (Vector2.Distance(columns[i].Xy, columns[j].Xy) <= gap) parent[Find(i)] = Find(j);
        return columns.Select((c, i) => (Column: c, Root: Find(i)))
            .GroupBy(x => x.Root)
            .Select(g => new Bundle
            {
                Members = g.Select(x => x.Column).ToList(),
                Supports = g.Select(x => x.Column.Support).ToHashSet(),
            })
            .OrderBy(b => b.Id).ToList();
    }

    private static int BundleAt(List<Bundle> bundles, Vector3 position)
    {
        for (var i = 0; i < bundles.Count; i++)
            if (bundles[i].Members.Any(c => c.DistanceTo(position) <= CarrierTolerance)) return i;
        return -1;
    }

}
