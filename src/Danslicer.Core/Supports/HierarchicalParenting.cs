using System.Numerics;
using Danslicer.Core.Config;
using Danslicer.Core.Supports.Routing;

namespace Danslicer.Core.Supports;

/// <summary>
/// Hierarchical parenting (SUPPORT-GEOMETRY-SPEC "Parenting", user direction 2026-09-08 from a
/// reference image): tips pair off into junctions, junctions pair off again, and one trunk
/// carries the lot. The tree router only ever joins a tip straight onto a trunk, so with a
/// steep branch-angle limit its branches just grow long; this builder merges bottom-up.
///
/// <para>Every tip's cone ends at a junction. The two junctions whose merge costs the least
/// branch length are joined at a point below them where both branches lean at most the
/// branch angle; the new junction takes their place, and so on until nothing can merge within
/// the length, angle, bend, height and clearance limits. Each surviving junction then drops a
/// trunk — straight down, or in grid mode by a branch to the nearest reachable lattice point.
/// No junction is made below the minimum attach height above the plate.</para>
///
/// <para>Deterministic for a seed: the seed only breaks near-ties between merges, which is
/// what lets parenting's rounds differ.</para>
/// </summary>
public static class HierarchicalParenting
{
    private const float ConeLeanLimitDegrees = 45f;
    private const float Epsilon = 1e-4f;

    /// <summary>Diagnostics: why a tip or cluster was refused. Null in production.</summary>
    public static Action<string>? Trace { get; set; }

    private sealed record MemberTag(int A, int B);

    /// <summary>
    /// A vertical trunk of a support that is not being parented, which a surviving junction may
    /// join instead of dropping a trunk of its own (auto-parenting, 2026-09-08). Obstacle capsules
    /// carry the segment id, so <paramref name="ObstacleId"/> is what a branch to this trunk
    /// must ignore; the pieces of a split trunk keep the original's id there.
    /// </summary>
    public sealed record ExistingTrunk(SupportSegment Segment, SupportNode Top, SupportNode Bottom,
        Guid ObstacleId, IReadOnlyList<Guid> SegmentsAtTop)
    {
        public Vector2 Xy => new(Top.Position.X, Top.Position.Y);
        /// <summary>Lowest point a branch may attach: the top of the base, or the bottom junction.</summary>
        public float AttachFloor => Bottom.Type == SupportNodeType.Base
            ? Bottom.Position.Z + Bottom.BaseHeight : Bottom.Position.Z;
    }

    /// <summary>
    /// The vertical trunks of <paramref name="graph"/> owned by <paramref name="targetId"/> (any
    /// owner when null), as candidates for <see cref="Build"/> to join.
    /// </summary>
    public static List<ExistingTrunk> ExistingTrunks(SupportGraph graph, Guid? targetId)
    {
        var result = new List<ExistingTrunk>();
        foreach (var segment in graph.Segments)
        {
            if (segment.Type != SupportSegmentType.Trunk || segment.Disabled) continue;
            if (targetId is { } target && graph.OwningObjectId(segment.Id) is { } owner && owner != target) continue;
            var a = graph.GetNode(segment.NodeA);
            var b = graph.GetNode(segment.NodeB);
            if (Vector2.Distance(new(a.Position.X, a.Position.Y), new(b.Position.X, b.Position.Y)) > 0.01f) continue;
            var (top, bottom) = a.Position.Z >= b.Position.Z ? (a, b) : (b, a);
            result.Add(new ExistingTrunk(segment, top, bottom, segment.Id,
                graph.SegmentsAt(top.Id).Select(x => x.Id).ToList()));
        }
        return result;
    }

    private sealed class Node
    {
        public required int Index { get; init; }
        public required Vector3 Position { get; init; }
        /// <summary>The cone's axis for a cone junction; null once merged (junctions bend freely).</summary>
        public Vector3? Incoming { get; init; }
        public required SupportNode Graph { get; init; }
        public required List<int> Tips { get; init; }
    }

    /// <summary>
    /// Builds trees over <paramref name="tips"/>. <paramref name="obstacles"/> is the model
    /// plus every support that is not being parented; <paramref name="existingTrunks"/> are
    /// those supports' trunks, which a surviving junction joins (nearest first, within the
    /// trunk search range) before dropping a trunk of its own. Returns the elements to add
    /// and remove (a joined trunk is split at the attach point) and the tips that could not be
    /// given a clear cone or a clear trunk, which keep their supports.
    /// </summary>
    public static (SupportGraphEdit Edit, IReadOnlyList<RoutingTip> Refused) Build(
        IReadOnlyList<RoutingTip> tips, SupportConfig settings, ICollisionScene obstacles,
        SupportOrigin origin, int seed, float rangeFactor = 1f,
        IReadOnlyList<ExistingTrunk>? existingTrunks = null)
    {
        var angle = (settings.ParentingMaxBranchAngle > 0 ? settings.ParentingMaxBranchAngle : settings.MemberAngleDegrees)
            * MathF.PI / 180f;
        var tan = MathF.Tan(MathF.Max(angle, 0.01f));
        var maxLength = (settings.ParentingMaxBranchLength > 0 ? settings.ParentingMaxBranchLength : settings.MaxBranchLength) * rangeFactor;
        var trunkRange = (settings.ParentingTrunkRange > 0 ? settings.ParentingTrunkRange : settings.ExistingTrunkBranchRange) * rangeFactor;
        var coneBend = (settings.ParentingMaxConeBend > 0 ? settings.ParentingMaxConeBend : settings.MemberAngleDegrees)
            * MathF.PI / 180f;
        var plateZ = 0f;
        var minZ = plateZ + MathF.Max(settings.BaseHeight, settings.MinBranchAttachHeightMm);
        var branchRadius = settings.BranchDiameter * 0.5f;
        var trunkRadius = settings.TrunkDiameter * 0.5f;
        var jitter = new Random(seed);

        var members = new LinearCollisionScene();
        var scene = new CompositeCollisionScene(obstacles, members);
        var addedNodes = new List<SupportNode>();
        var addedSegments = new List<SupportSegment>();
        var removedSegments = new List<SupportSegment>();
        var trunks = existingTrunks?.ToList() ?? [];
        var refused = new List<RoutingTip>();
        var active = new List<Node>();
        var nextIndex = 0;

        // Stage 1: cones. Along the contact normal clamped to 45° from vertical, else vertical.
        for (var t = 0; t < tips.Count; t++)
        {
            var tip = tips[t];
            var contact = new SupportNode { Type = SupportNodeType.Tip, Position = tip.SurfacePoint, Origin = origin };
            RoutingUtilities.ApplyContact(contact, tip);
            Vector3? end = null;
            Vector3? axis = null;
            foreach (var direction in ConeDirections(tip))
            {
                var candidate = tip.SurfacePoint + direction * settings.TipMemberLength;
                if (candidate.Z < minZ) continue;
                // The cone touches the model at the contact by design, so the clearance capsule
                // starts far enough along the axis that its own radius no longer reaches the surface.
                var start = tip.SurfacePoint + direction * (MathF.Max(tip.TipDiameter, 0.05f) + branchRadius * 1.5f);
                if (Vector3.Distance(start, candidate) < Epsilon) continue;
                if (scene.IntersectsCapsule(start, candidate, branchRadius)) continue;
                end = candidate;
                axis = direction;
                break;
            }
            if (end is not { } junctionPosition || axis is not { } coneAxis)
            {
                Trace?.Invoke($"cone refused at {tip.SurfacePoint} normal {tip.InwardSurfaceNormal}");
                refused.Add(tip);
                continue;
            }
            var junction = new SupportNode { Type = SupportNodeType.Junction, Position = junctionPosition, Origin = origin };
            addedNodes.Add(contact);
            addedNodes.Add(junction);
            addedSegments.Add(new SupportSegment
            {
                Type = SupportSegmentType.Tip, NodeA = contact.Id, NodeB = junction.Id,
                Diameter = settings.BranchDiameter, Origin = origin,
            });
            var index = nextIndex++;
            members.AddCapsule(tip.SurfacePoint, junctionPosition, branchRadius, new MemberTag(index, index));
            active.Add(new Node { Index = index, Position = junctionPosition, Incoming = coneAxis, Graph = junction, Tips = [t] });
        }

        // Stage 2: merge the cheapest feasible pair until none is left.
        while (active.Count > 1)
        {
            Node? bestA = null, bestB = null;
            Vector3 bestMerge = default;
            var bestCost = float.PositiveInfinity;
            for (var i = 0; i < active.Count; i++)
                for (var j = i + 1; j < active.Count; j++)
                {
                    var a = active[i];
                    var b = active[j];
                    foreach (var m in MergeCandidates(a, b))
                    {
                        var cost = Vector3.Distance(a.Position, m) + Vector3.Distance(b.Position, m)
                                   + jitter.NextSingle() * 0.01f;
                        if (cost >= bestCost) continue;
                        // Members meeting at a shared junction are not obstacles to each other.
                        var hostIndex = AtNode(a, m) ? a.Index : AtNode(b, m) ? b.Index : -1;
                        if (!AtNode(a, m) && !BranchClear(a, m, hostIndex)) continue;
                        if (!AtNode(b, m) && !BranchClear(b, m, hostIndex)) continue;
                        bestCost = cost;
                        bestA = a;
                        bestB = b;
                        bestMerge = m;
                        break;
                    }
                }
            if (bestA is null || bestB is null)
            {
                if (Trace is not null && active.Count > 1)
                    for (var i = 0; i < active.Count; i++)
                        for (var j = i + 1; j < active.Count; j++)
                            Trace(WhyNot(active[i], active[j]));
                break;
            }

            // Joining at one junction's own position reuses that junction; otherwise a new one.
            var host = AtNode(bestA, bestMerge) ? bestA : AtNode(bestB, bestMerge) ? bestB : null;
            var merged = host?.Graph ?? new SupportNode { Type = SupportNodeType.Junction, Position = bestMerge, Origin = origin };
            if (host is null) addedNodes.Add(merged);
            var index = host?.Index ?? nextIndex++;
            foreach (var child in new[] { bestA, bestB })
            {
                if (ReferenceEquals(child, host)) continue;
                addedSegments.Add(new SupportSegment
                {
                    Type = SupportSegmentType.Branch, NodeA = child.Graph.Id, NodeB = merged.Id,
                    Diameter = settings.BranchDiameter, Origin = origin,
                });
                members.AddCapsule(child.Position, bestMerge, branchRadius, new MemberTag(child.Index, index));
            }
            active.Remove(bestA);
            active.Remove(bestB);
            active.Add(new Node
            {
                Index = index, Position = bestMerge, Incoming = null, Graph = merged,
                Tips = bestA.Tips.Concat(bestB.Tips).ToList(),
            });
        }

        // Stage 3: every surviving junction joins an existing trunk or drops one of its own.
        foreach (var node in active)
        {
            if (JoinTrunk(node) || DropTrunk(node)) continue;
            Trace?.Invoke($"trunk failed for cluster of {node.Tips.Count} at {node.Position}");
            refused.AddRange(node.Tips.Select(t => tips[t]));
        }

        // A refused tip's elements are withdrawn: the caller keeps its old support instead.
        if (refused.Count > 0)
        {
            var refusedContacts = refused.Select(r => r.SurfacePoint).ToHashSet();
            var refusedTipNodes = addedNodes.Where(n => n.Type == SupportNodeType.Tip && refusedContacts.Contains(n.Position))
                .Select(n => n.Id).ToHashSet();
            Withdraw(addedNodes, addedSegments, refusedTipNodes, active.Where(n => n.Tips.Any(t => refusedContacts.Contains(tips[t].SurfacePoint))));
        }
        return (new SupportGraphEdit(addedNodes, addedSegments, removedSegments), refused);

        // Two ways to join a pair, and the one that stays highest wins, because height is what
        // later merges spend: a junction under the midpoint (both branches lean equally), or the
        // higher junction sending a branch down into the lower one's own position — which costs
        // the lower cluster no height at all, and is how a long run on a sloping edge ends up on
        // one trunk (the reference image, 2026-09-08).
        IEnumerable<Vector3> MergeCandidates(Node a, Node b)
        {
            var horizontal = Vector2.Distance(new(a.Position.X, a.Position.Y), new(b.Position.X, b.Position.Y));
            var lower = a.Position.Z <= b.Position.Z ? a : b;
            var higher = ReferenceEquals(lower, a) ? b : a;

            var mid = (a.Position + b.Position) * 0.5f;
            var midpoint = new Vector3(mid.X, mid.Y, MathF.Min(a.Position.Z, b.Position.Z) - MathF.Max(horizontal * 0.5f / tan, 0.5f));
            // At the lower junction itself when the higher one's branch fits the angle from
            // there; otherwise directly below it, as far down as that branch needs.
            var intoLower = new Vector3(lower.Position.X, lower.Position.Y,
                MathF.Min(lower.Position.Z, higher.Position.Z - horizontal / tan));

            foreach (var m in new[] { intoLower, midpoint }.OrderByDescending(p => p.Z))
            {
                if (m.Z < minZ) continue;
                if (Vector3.Distance(a.Position, m) > maxLength || Vector3.Distance(b.Position, m) > maxLength) continue;
                if (Bend(a, m) > coneBend || Bend(b, m) > coneBend) continue;
                yield return m;
            }
        }

        static bool AtNode(Node node, Vector3 m) => Vector3.DistanceSquared(node.Position, m) < Epsilon * Epsilon;

        string WhyNot(Node a, Node b)
        {
            var horizontal = Vector2.Distance(new(a.Position.X, a.Position.Y), new(b.Position.X, b.Position.Y));
            var lower = a.Position.Z <= b.Position.Z ? a : b;
            var higher = ReferenceEquals(lower, a) ? b : a;
            var mid = (a.Position + b.Position) * 0.5f;
            var midpoint = new Vector3(mid.X, mid.Y, MathF.Min(a.Position.Z, b.Position.Z) - MathF.Max(horizontal * 0.5f / tan, 0.5f));
            var intoLower = new Vector3(lower.Position.X, lower.Position.Y, MathF.Min(lower.Position.Z, higher.Position.Z - horizontal / tan));
            var reasons = new List<string>();
            foreach (var (name, m) in new[] { ("intoLower", intoLower), ("midpoint", midpoint) })
            {
                if (m.Z < minZ) { reasons.Add($"{name}: z {m.Z:0.0} < min {minZ:0.0}"); continue; }
                var la = Vector3.Distance(a.Position, m); var lb = Vector3.Distance(b.Position, m);
                if (la > maxLength || lb > maxLength) { reasons.Add($"{name}: length {la:0.0}/{lb:0.0} > {maxLength}"); continue; }
                var ba = Bend(a, m) * 180 / MathF.PI; var bb = Bend(b, m) * 180 / MathF.PI;
                if (Bend(a, m) > coneBend || Bend(b, m) > coneBend) { reasons.Add($"{name}: bend {ba:0}/{bb:0} > {coneBend * 180 / MathF.PI:0}"); continue; }
                var hostIndex = AtNode(a, m) ? a.Index : AtNode(b, m) ? b.Index : -1;
                var ca = AtNode(a, m) || BranchClear(a, m, hostIndex);
                var cb = AtNode(b, m) || BranchClear(b, m, hostIndex);
                reasons.Add($"{name}: clear {ca}/{cb}");
            }
            return $"pair {a.Tips.Count}t@{a.Position:0.0} / {b.Tips.Count}t@{b.Position:0.0} hd {horizontal:0.0}: {string.Join("; ", reasons)}";
        }

        static float Bend(Node node, Vector3 m)
        {
            if (node.Incoming is not { } incoming) return 0f;
            var d = m - node.Position;
            if (d.LengthSquared() < Epsilon * Epsilon) return 0f;
            return MathF.Acos(Math.Clamp(Vector3.Dot(Vector3.Normalize(incoming), Vector3.Normalize(d)), -1f, 1f));
        }

        bool BranchClear(Node from, Vector3 to, int alsoExclude = -1, IReadOnlySet<Guid>? excludeSegments = null) =>
            !scene.IntersectsCapsule(from.Position, to, branchRadius,
                tag => tag switch
                {
                    MemberTag member => member.A != from.Index && member.B != from.Index &&
                                        member.A != alsoExclude && member.B != alsoExclude,
                    Guid id => excludeSegments is null || !excludeSegments.Contains(id),
                    _ => true,
                });

        // An existing trunk within the trunk search range, nearest first: the branch attaches
        // as high as its angle allows, never above the trunk's top nor below the attach floor
        // (min branch height, base top). Below the top the trunk is split at the attach point;
        // at the top the branch simply joins the top node.
        bool JoinTrunk(Node node)
        {
            var xy = new Vector2(node.Position.X, node.Position.Y);
            foreach (var trunk in trunks.OrderBy(t => Vector2.Distance(xy, t.Xy)).ToList())
            {
                var horizontal = Vector2.Distance(xy, trunk.Xy);
                if (horizontal > trunkRange) break;
                var topZ = trunk.Top.Position.Z;
                var attachZ = MathF.Min(node.Position.Z - horizontal / tan, topZ);
                if (attachZ < MathF.Max(minZ, trunk.AttachFloor)) continue;
                var attach = new Vector3(trunk.Xy.X, trunk.Xy.Y, attachZ);
                var length = Vector3.Distance(node.Position, attach);
                if (length < Epsilon || length > maxLength) continue;
                if (Bend(node, attach) > coneBend) continue;
                var atTop = topZ - attachZ < Epsilon;
                // Landing on the junction under a piece of an already split trunk: join it
                // rather than cutting a zero-length piece.
                var atBottom = !atTop && trunk.Bottom.Type != SupportNodeType.Base && attachZ - trunk.Bottom.Position.Z < Epsilon;
                var exclude = new HashSet<Guid> { trunk.ObstacleId };
                if (atTop) exclude.UnionWith(trunk.SegmentsAtTop);
                if (!BranchClear(node, attach, -1, exclude)) continue;

                SupportNode attachNode;
                if (atTop) attachNode = trunk.Top;
                else if (atBottom) attachNode = trunk.Bottom;
                else
                {
                    attachNode = new SupportNode { Type = SupportNodeType.Junction, Position = attach, Origin = origin };
                    addedNodes.Add(attachNode);
                    // A piece this build made is simply replaced; only a trunk the graph
                    // already holds goes on the removal list.
                    if (!addedSegments.Remove(trunk.Segment)) removedSegments.Add(trunk.Segment);
                    var upper = trunk.Segment.Clone(id: Guid.NewGuid(), nodeA: trunk.Top.Id, nodeB: attachNode.Id);
                    var lower = trunk.Segment.Clone(id: Guid.NewGuid(), nodeA: attachNode.Id, nodeB: trunk.Bottom.Id);
                    addedSegments.Add(upper);
                    addedSegments.Add(lower);
                    trunks.Remove(trunk);
                    trunks.Add(trunk with { Segment = upper, Bottom = attachNode });
                    trunks.Add(trunk with { Segment = lower, Top = attachNode, SegmentsAtTop = [upper.Id, lower.Id] });
                }
                addedSegments.Add(new SupportSegment
                {
                    Type = SupportSegmentType.Branch, NodeA = node.Graph.Id, NodeB = attachNode.Id,
                    Diameter = settings.BranchDiameter, Origin = origin,
                });
                members.AddCapsule(node.Position, attach, branchRadius, new MemberTag(node.Index, node.Index));
                Trace?.Invoke($"cluster of {node.Tips.Count} at {node.Position} joined trunk at {attach}");
                return true;
            }
            return false;
        }

        bool DropTrunk(Node node)
        {
            var xy = new Vector2(node.Position.X, node.Position.Y);
            var tops = new List<Vector2>();
            if (settings.UseBaseGrid)
            {
                // Grid on: lattice points only, nearest first. (The junction's own column was
                // tried first here once, so trunks left the grid — user screen test 2026-09-08.)
                tops.AddRange(BaseLattice.NearestSquarePoints(xy, settings.BaseGridPitch, maxLength));
            }
            else
            {
                tops.Add(xy);
                // A straight drop under a leaning model hits the model: fan out, nearest reach
                // first, so the trunk stands where the column down to the plate is clear.
                var reach = MathF.Min(maxLength, node.Position.Z - minZ) * MathF.Sin(angle);
                foreach (var fraction in new[] { 0.15f, 0.3f, 0.5f, 0.75f, 1f })
                    for (var k = 0; k < 12; k++)
                    {
                        var a = k * MathF.Tau / 12f + seed * 0.1f;
                        tops.Add(xy + new Vector2(MathF.Cos(a), MathF.Sin(a)) * reach * fraction);
                    }
            }
            foreach (var top in tops)
            {
                var horizontal = Vector2.Distance(top, xy);
                var topZ = horizontal <= Epsilon ? node.Position.Z : node.Position.Z - horizontal / tan;
                if (topZ < minZ) continue;
                var trunkTop = new Vector3(top.X, top.Y, topZ);
                if (horizontal > Epsilon)
                {
                    if (Vector3.Distance(node.Position, trunkTop) > maxLength) continue;
                    if (Bend(node, trunkTop) > coneBend) continue;
                    if (!BranchClear(node, trunkTop)) continue;
                }
                var basePosition = new Vector3(top.X, top.Y, plateZ);
                var trunkStart = new Vector3(top.X, top.Y, MathF.Max(plateZ + settings.BaseHeight, topZ - 0.01f));
                if (scene.IntersectsCapsule(trunkTop - new Vector3(0, 0, 0.01f), basePosition + new Vector3(0, 0, settings.BaseHeight), trunkRadius,
                        tag => tag is not MemberTag member || (member.A != node.Index && member.B != node.Index))) continue;

                var trunkNode = node.Graph;
                if (horizontal > Epsilon)
                {
                    trunkNode = new SupportNode { Type = SupportNodeType.Junction, Position = trunkTop, Origin = origin };
                    addedNodes.Add(trunkNode);
                    addedSegments.Add(new SupportSegment
                    {
                        Type = SupportSegmentType.Branch, NodeA = node.Graph.Id, NodeB = trunkNode.Id,
                        Diameter = settings.BranchDiameter, Origin = origin,
                    });
                    members.AddCapsule(node.Position, trunkTop, branchRadius, new MemberTag(node.Index, node.Index));
                }
                var baseNode = new SupportNode
                {
                    Type = SupportNodeType.Base, Position = basePosition, Origin = origin,
                    BaseShape = settings.BaseShape, BaseDiameter = settings.BaseDiameter,
                    BaseHeight = settings.BaseHeight, BaseConeHeight = settings.BaseConeHeight,
                };
                addedNodes.Add(baseNode);
                addedSegments.Add(new SupportSegment
                {
                    Type = SupportSegmentType.Trunk, NodeA = trunkNode.Id, NodeB = baseNode.Id,
                    Diameter = settings.TrunkDiameter, Origin = origin,
                });
                members.AddCapsule(trunkTop, basePosition, trunkRadius, new MemberTag(node.Index, node.Index));
                _ = trunkStart;
                return true;
            }
            return false;
        }
    }

    /// <summary>The cone axis along the outward normal clamped to 45° from vertical, then straight down.</summary>
    private static IEnumerable<Vector3> ConeDirections(RoutingTip tip)
    {
        var outward = -tip.InwardSurfaceNormal;
        if (outward.LengthSquared() > Epsilon * Epsilon)
        {
            outward = Vector3.Normalize(outward);
            var horizontal = new Vector2(outward.X, outward.Y);
            var lean = MathF.Atan2(horizontal.Length(), -outward.Z) * 180f / MathF.PI;
            if (lean <= ConeLeanLimitDegrees && outward.Z < 0) yield return outward;
            else if (horizontal.LengthSquared() > Epsilon * Epsilon)
            {
                var h = Vector2.Normalize(horizontal);
                var s = MathF.Sin(ConeLeanLimitDegrees * MathF.PI / 180f);
                var c = MathF.Cos(ConeLeanLimitDegrees * MathF.PI / 180f);
                yield return new Vector3(h.X * s, h.Y * s, -c);
            }
        }
        yield return -Vector3.UnitZ;
    }

    /// <summary>Removes the tips that were refused and every cluster that could not get a trunk.</summary>
    private static void Withdraw(List<SupportNode> nodes, List<SupportSegment> segments,
        HashSet<Guid> refusedTipNodes, IEnumerable<Node> failedClusters)
    {
        var doomed = new HashSet<Guid>(refusedTipNodes);
        // A failed cluster: everything reachable from its junction through the added segments.
        var queue = new Queue<Guid>(failedClusters.Select(c => c.Graph.Id));
        while (queue.Count > 0)
        {
            var id = queue.Dequeue();
            if (!doomed.Add(id)) continue;
            foreach (var s in segments)
            {
                if (s.NodeA == id && !doomed.Contains(s.NodeB)) queue.Enqueue(s.NodeB);
                if (s.NodeB == id && !doomed.Contains(s.NodeA)) queue.Enqueue(s.NodeA);
            }
        }
        // A refused tip's own junction goes too when nothing else hangs from it.
        foreach (var tipId in refusedTipNodes)
            foreach (var s in segments.Where(s => s.NodeA == tipId))
                if (segments.Count(o => o.NodeA == s.NodeB || o.NodeB == s.NodeB) <= 1) doomed.Add(s.NodeB);
        segments.RemoveAll(s => doomed.Contains(s.NodeA) || doomed.Contains(s.NodeB));
        nodes.RemoveAll(n => doomed.Contains(n.Id));
    }
}
