using System.Numerics;

namespace Danslicer.Core.Supports.Routing;

/// <summary>
/// Options for spec-shaped tree routing (docs/SUPPORT-GEOMETRY-SPEC.md). Every support is a
/// vertical trunk rising from a base, at most one angled branch spanning from the trunk top
/// toward the contact, and an angled conical tip member; additional tips may join an earlier
/// trunk with their own branch, forming trees. All angles default to 45°.
/// </summary>
public sealed record TreeRoutingOptions
{
    public float TrunkDiameter { get; init; } = 1.2f;
    public float BranchDiameter { get; init; } = 1.2f;
    /// <summary>Maximum angle from vertical for tip members and branches.</summary>
    public float MaxMemberAngleDegrees { get; init; } = 45f;
    /// <summary>Length of the tip member from the contact to its junction.</summary>
    public float TipMemberLength { get; init; } = 2f;
    /// <summary>Longest branch member allowed, in millimetres along the member.</summary>
    public float MaxBranchLength { get; init; } = 8f;
    /// <summary>Directions tried when a branch must swing around an obstacle or reach a trunk.</summary>
    public int BranchDirections { get; init; } = 12;
    /// <summary>Branch lengths tried per direction, as fractions of <see cref="MaxBranchLength"/>.</summary>
    public int BranchLengthSteps { get; init; } = 4;
    public float PlateZ { get; init; }
    public int Seed { get; init; } = 1;
    public SupportOrigin Origin { get; init; } = SupportOrigin.Manual;
    public IReadOnlySet<object>? KeepCleanObstacleTags { get; init; }

    // Base geometry stamped on every plate base this router creates.
    public SupportBaseShape BaseShape { get; init; } = SupportBaseShape.Disc;
    public float BaseDiameter { get; init; } = 4f;
    public float BaseHeight { get; init; } = 0.8f;
    public float BaseConeHeight { get; init; } = 2f;
}

/// <summary>
/// Deterministic routing that emits exactly the user's support anatomy: base / trunk / branch /
/// tip. Tips leave the surface along the (angle-clamped) outward normal for one tip-member
/// length, then either join an existing trunk with a single 45° branch, drop a vertical trunk
/// straight to the plate, or swing one branch to a clear vertical drop line. Supports never land
/// on the model: a tip with no clear path to the plate is refused with a reason.
/// </summary>
public sealed class TreeSupportRouter
{
    private const float Epsilon = 1e-5f;
    private const float SiblingBranchFusionDistance = 0.5f;
    private readonly ICollisionScene _obstacles;
    private readonly GrowthRuleSet _rules;

    public TreeSupportRouter(ICollisionScene obstacles, GrowthRuleSet rules)
    {
        _obstacles = obstacles;
        _rules = rules;
    }

    public RoutingResult Route(IEnumerable<RoutingTip> tips, TreeRoutingOptions options)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.TrunkDiameter);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.TipMemberLength);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.BranchDirections);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.BranchLengthSteps);

        var graph = new SupportGraph();
        var ids = new DeterministicIds(options.Seed);
        var clearance = RoutingClearance.From(_rules, options.KeepCleanObstacleTags);
        var angleOffset = new Random(options.Seed).NextSingle() * MathF.Tau;
        var state = new RouteState(graph, ids, clearance, angleOffset);
        var unrouted = new List<RoutingTip>();
        var failures = new List<RoutingFailure>();

        var expandedTips = RoutingUtilities.AddReinforcementTips(tips, _rules, _obstacles, options.Seed);
        foreach (var item in expandedTips.Select((tip, index) => (Tip: tip, Index: index))
                     .OrderByDescending(item => item.Tip.SurfacePoint.Z).ThenBy(item => item.Index))
        {
            if (RouteOne(item.Tip, options, state, out var reason)) continue;
            unrouted.Add(item.Tip);
            failures.Add(new RoutingFailure(item.Tip, reason));
        }

        var bases = graph.Nodes.Where(node => node.Type == SupportNodeType.Base)
            .Select(node => node.Position).OrderBy(p => p.X).ThenBy(p => p.Y).ToList();
        return new RoutingResult(graph, unrouted, bases, state.MaxLean, failures);
    }

    private bool RouteOne(RoutingTip tip, TreeRoutingOptions options, RouteState state,
        out RoutingFailureReason reason)
    {
        reason = RoutingFailureReason.NoClearStep;
        if (tip.SurfacePoint.Z <= options.PlateZ + Epsilon)
        {
            reason = RoutingFailureReason.BelowPlate;
            return false;
        }

        var (branchTipDiameter, tipMemberLength) = TipMemberDimensions(
            tip, options.BranchDiameter, options.TipMemberLength);
        var branchJunction = FindTipJunction(tip, options, state,
            branchTipDiameter, tipMemberLength);

        // Branch-first: an existing trunk gets first refusal, and a tip feeding that branch
        // tapers from the configured branch diameter.
        if (branchJunction is { } branchJ1 && branchJ1.Z > options.PlateZ + Epsilon &&
            TryAttachToTrunk(tip, branchJ1, options, state, branchTipDiameter)) return true;

        var (trunkTipDiameter, _) = TipMemberDimensions(
            tip, options.TrunkDiameter, options.TipMemberLength);
        var trunkJunction = MathF.Abs(trunkTipDiameter - branchTipDiameter) <= Epsilon
            ? branchJunction
            : FindTipJunction(tip, options, state, trunkTipDiameter, tipMemberLength);

        // A tip connected directly to a trunk (or directly to its base near the plate) tapers
        // from the trunk setting, independently of BranchDiameter.
        if (trunkJunction is { } trunkJ1)
        {
            if (trunkJ1.Z <= options.PlateZ + Epsilon)
            {
                EmitSupport(tip, new Vector3(trunkJ1.X, trunkJ1.Y, options.PlateZ), null,
                    tipOnly: true, options, state, trunkTipDiameter);
                return true;
            }
            if (TrunkIsClear(trunkJ1, options, state))
            {
                EmitSupport(tip, trunkJ1, null, tipOnly: false,
                    options, state, trunkTipDiameter);
                return true;
            }
        }

        if (branchJunction is null)
        {
            reason = RoutingFailureReason.ContactBlocked;
            return false;
        }

        // The straight candidate was handled with trunk-derived tip geometry above. Every
        // remaining candidate introduces a branch, so both it and its tip use branch settings.
        foreach (var trunkTop in TrunkTopCandidates(branchJunction.Value, options, state.AngleOffset).Skip(1))
        {
            if (!MemberIsClear(branchJunction.Value, trunkTop,
                    options.BranchDiameter * 0.5f, state))
                continue;
            if (!TrunkIsClear(trunkTop, options, state)) continue;
            EmitSupport(tip, trunkTop, branchJunction.Value, tipOnly: false,
                options, state, branchTipDiameter);
            return true;
        }
        return false;
    }

    private (float Diameter, float Length) TipMemberDimensions(RoutingTip tip,
        float parentDiameter, float configuredLength)
    {
        var taper = new GrowthContext
        {
            Operation = GrowthOperation.Tip,
            Start = tip.SurfacePoint,
            DesiredEnd = tip.SurfacePoint,
            End = tip.SurfacePoint,
            Diameter = parentDiameter,
            TipLength = configuredLength,
        };
        _rules.Evaluate(taper);
        return (MathF.Max(0.05f, taper.Diameter), MathF.Max(0.1f, taper.TipLength));
    }

    /// <summary>
    /// Where the tip member meets the rest of the support: one tip-member length from the contact
    /// along the outward normal clamped to the member angle (straight down for upward or sideways
    /// contacts), falling back to a deterministic fan of directions when the surface is in the way.
    /// </summary>
    private Vector3? FindTipJunction(RoutingTip tip, TreeRoutingOptions options, RouteState state,
        float tipMemberDiameter, float tipMemberLength)
    {
        var contactRadius = MathF.Max(0.025f, tip.TipDiameter * 0.5f) + state.Clearance.ModelDistance;
        var memberRadius = tipMemberDiameter * 0.5f + state.Clearance.ModelDistance;
        // Rough or tightly packed contacts (teeth) can block every full-length departure; a
        // short member still gets the support off the surface, as in the top-down router.
        var shortLength = MathF.Min(tipMemberLength, contactRadius * 2);
        foreach (var candidateLength in new[] { tipMemberLength, shortLength }.Distinct())
        {
            foreach (var direction in TipDirections(tip, options, state.AngleOffset))
            {
                var length = direction.Z < -Epsilon
                    ? MathF.Min(candidateLength, (tip.SurfacePoint.Z - options.PlateZ) / -direction.Z)
                    : candidateLength;
                var end = tip.SurfacePoint + direction * length;
                if (!ContactMemberIsClear(tip.SurfacePoint, end, contactRadius)) continue;
                if (state.HitsGenerated(tip.SurfacePoint, end, memberRadius)) continue;
                return end;
            }
        }
        return null;
    }

    private IEnumerable<Vector3> TipDirections(RoutingTip tip, TreeRoutingOptions options,
        float angleOffset)
    {
        var outward = -RoutingUtilities.SafeInwardNormal(tip.InwardSurfaceNormal);
        var maxAngle = options.MaxMemberAngleDegrees * MathF.PI / 180f;
        if (outward.Z >= -Epsilon)
        {
            // Upward or sideways contact: leave straight down, as the vertical tree always did.
            yield return -Vector3.UnitZ;
            yield break;
        }

        // Down-facing contact: the outward normal clamped to the member angle from straight down.
        var horizontal = new Vector2(outward.X, outward.Y);
        var down = -outward.Z;
        var normalAngle = MathF.Atan2(horizontal.Length(), down);
        var angle = MathF.Min(normalAngle, maxAngle);
        var lateral = horizontal.LengthSquared() > 1e-12f
            ? Vector2.Normalize(horizontal)
            : Vector2.UnitX;
        yield return Direction(lateral, angle);

        // Fan around the vertical at the same angle, then straight down as a last resort.
        for (var index = 1; index < options.BranchDirections; index++)
        {
            var theta = angleOffset + index * MathF.Tau / options.BranchDirections;
            var rotated = new Vector2(MathF.Cos(theta), MathF.Sin(theta));
            yield return Direction(rotated, angle);
        }
        yield return -Vector3.UnitZ;

        static Vector3 Direction(Vector2 lateral, float angle) => Vector3.Normalize(
            new Vector3(lateral * MathF.Sin(angle), -MathF.Cos(angle)));
    }

    /// <summary>
    /// One 45° (member-angle) branch descending from the junction onto an earlier trunk of this
    /// run. Nearest trunk first; the trunk segment is split at the attachment point.
    /// </summary>
    private bool TryAttachToTrunk(RoutingTip tip, Vector3 j1, TreeRoutingOptions options,
        RouteState state, float tipMemberDiameter)
    {
        var branchRule = _rules.Find<BranchGrowthRule>();
        var maxBranches = branchRule is { Enabled: true } ? branchRule.MaxBranchesPerTrunk : int.MaxValue;
        var maxAngle = options.MaxMemberAngleDegrees * MathF.PI / 180f;
        var tanAngle = MathF.Tan(maxAngle);
        var sinAngle = MathF.Sin(maxAngle);

        foreach (var trunk in state.Trunks
                     .OrderBy(t => Vector2.DistanceSquared(new(j1.X, j1.Y), t.Xy))
                     .ThenBy(t => t.BaseNodeId))
        {
            if (trunk.BranchCount >= maxBranches) continue;
            var hDist = Vector2.Distance(new(j1.X, j1.Y), trunk.Xy);
            var branchLength = sinAngle > Epsilon ? hDist / sinAngle : float.MaxValue;
            if (branchLength > options.MaxBranchLength) continue;
            // A hair steeper than the exact member angle, so float rounding in the lean rule
            // can never clamp (and thereby reject) a nominally-exact 45° branch.
            var attachZ = j1.Z - (tanAngle > Epsilon ? hDist / tanAngle : 0f) - 1e-3f;
            // The attachment must land on the trunk itself, above its base headroom.
            if (attachZ > trunk.TopZ + Epsilon) continue;
            if (attachZ < options.PlateZ + options.BaseHeight + Epsilon) continue;
            if (hDist <= Epsilon) continue; // the junction is on the trunk line; the drop handles it

            // The spec's member angle governs branch geometry here; the lean rule's step-router
            // clamps (including its tighter near-tip angle) do not apply to tree anatomy.
            var attach = new Vector3(trunk.Xy.X, trunk.Xy.Y, attachZ);
            var branchRadius = options.BranchDiameter * 0.5f;
            if (!state.Clearance.PillarIsClear(_obstacles, j1, attach, branchRadius)) continue;
            if (state.HitsGeneratedForTrunkAttachment(j1, attach,
                    branchRadius + state.Clearance.ModelDistance, trunk,
                    SiblingBranchFusionDistance)) continue;

            var attachNode = MathF.Abs(attachZ - trunk.TopZ) <= 1e-3f
                ? state.Graph.GetNode(trunk.TopNodeId)
                : SplitTrunk(trunk, attachZ, state);
            var tipNode = EmitTipMember(tip, j1, options, state, tipMemberDiameter);
            var branch = state.AddSegment(SupportSegmentType.Branch, tipNode.Junction, attachNode,
                options.BranchDiameter, options.Origin);
            trunk.BranchSegmentIds.Add(branch.Id);
            trunk.BranchCount++;
            return true;
        }
        return false;
    }

    /// <summary>
    /// Vertical drop line candidates: straight below the junction, then one-branch fans from the
    /// configured maximum angle down through 30° and 15°. The configured angle is a maximum;
    /// shallower branches can find drop lines that a steep fan overshoots in dense geometry.
    /// </summary>
    private IEnumerable<Vector3> TrunkTopCandidates(Vector3 j1, TreeRoutingOptions options,
        float angleOffset)
    {
        yield return j1;
        var angles = new[]
        {
            options.MaxMemberAngleDegrees,
            MathF.Min(options.MaxMemberAngleDegrees, 30f),
            MathF.Min(options.MaxMemberAngleDegrees, 15f),
        }.Distinct();
        foreach (var angleDegrees in angles)
        {
            var angle = angleDegrees * MathF.PI / 180f;
            for (var step = 1; step <= options.BranchLengthSteps; step++)
            {
                var length = options.MaxBranchLength * step / options.BranchLengthSteps;
                for (var index = 0; index < options.BranchDirections; index++)
                {
                    var theta = angleOffset + index * MathF.Tau / options.BranchDirections;
                    var direction = new Vector3(
                        MathF.Cos(theta) * MathF.Sin(angle),
                        MathF.Sin(theta) * MathF.Sin(angle),
                        -MathF.Cos(angle));
                    var end = j1 + direction * length;
                    if (end.Z > options.PlateZ + options.BaseHeight + Epsilon) yield return end;
                }
            }
        }
    }

    private bool TrunkIsClear(Vector3 top, TreeRoutingOptions options, RouteState state)
    {
        var basePosition = new Vector3(top.X, top.Y, options.PlateZ);
        return MemberIsClear(top, basePosition, options.TrunkDiameter * 0.5f, state);
    }

    /// <summary>
    /// The largest base disc that clears the MODEL at this position: full diameter first, then
    /// shrink steps down to the member diameter, then no base at all (the member itself already
    /// proved clear). Other supports are ignored — neighbouring bases overlap and fuse on the
    /// plate by design; only base-into-model collisions are avoided (user screen test
    /// 2026-09-03: full-size discs were sinking into the model near plate-level geometry).
    /// </summary>
    private (SupportBaseShape Shape, float Diameter) FitBase(Vector3 basePosition,
        float memberDiameter, TreeRoutingOptions options, RouteState state)
    {
        if (options.BaseShape == SupportBaseShape.None)
            return (SupportBaseShape.None, options.BaseDiameter);
        var discTop = basePosition + Vector3.UnitZ * options.BaseHeight;
        var floor = MathF.Max(memberDiameter, 0.1f);
        foreach (var fraction in new[] { 1f, 0.75f, 0.5f, 0f })
        {
            var diameter = MathF.Max(floor, options.BaseDiameter * fraction);
            if (state.Clearance.PillarIsClear(_obstacles, basePosition, discTop, diameter * 0.5f))
                return (options.BaseShape, diameter);
            if (diameter <= floor + 1e-4f) break;
        }
        return (SupportBaseShape.None, options.BaseDiameter);
    }

    private bool MemberIsClear(Vector3 start, Vector3 end, float physicalRadius, RouteState state,
        IReadOnlyCollection<Guid>? excludeSegments = null)
    {
        if (!state.Clearance.PillarIsClear(_obstacles, start, end, physicalRadius)) return false;
        return !state.HitsGenerated(start, end,
            physicalRadius + state.Clearance.ModelDistance, excludeSegments);
    }

    /// <summary>Clear check for the tip member, ignoring its own contact end like the other routers.</summary>
    private bool ContactMemberIsClear(Vector3 contact, Vector3 end, float radius)
    {
        var delta = contact - end;
        var length = delta.Length();
        if (length <= radius * 2 + 0.01f) return true;
        var clearEnd = contact - delta / length * (radius * 2 + 0.01f);
        return !_obstacles.IntersectsCapsule(end, clearEnd, radius);
    }

    private (SupportNode Contact, SupportNode Junction) EmitTipMember(RoutingTip tip, Vector3 j1,
        TreeRoutingOptions options, RouteState state, float tipMemberDiameter)
    {
        var contact = state.NewNode(SupportNodeType.Tip, tip.SurfacePoint, options.Origin);
        RoutingUtilities.ApplyContact(contact, tip);
        state.Graph.AddNode(contact);
        var junction = state.NewNode(SupportNodeType.Junction, j1, options.Origin);
        state.Graph.AddNode(junction);
        state.AddSegment(SupportSegmentType.Tip, contact, junction,
            tipMemberDiameter, options.Origin);
        return (contact, junction);
    }

    private void EmitSupport(RoutingTip tip, Vector3 trunkTop, Vector3? branchFrom, bool tipOnly,
        TreeRoutingOptions options, RouteState state, float tipMemberDiameter)
    {
        var basePosition = new Vector3(trunkTop.X, trunkTop.Y, options.PlateZ);
        var (baseShape, baseDiameter) = FitBase(basePosition,
            tipOnly ? tipMemberDiameter : options.TrunkDiameter, options, state);
        var baseNode = state.NewNode(SupportNodeType.Base, basePosition, options.Origin);
        baseNode.BaseShape = baseShape;
        baseNode.BaseDiameter = baseDiameter;
        baseNode.BaseHeight = options.BaseHeight;
        baseNode.BaseConeHeight = options.BaseConeHeight;

        if (tipOnly)
        {
            var contact = state.NewNode(SupportNodeType.Tip, tip.SurfacePoint, options.Origin);
            RoutingUtilities.ApplyContact(contact, tip);
            state.Graph.AddNode(contact);
            state.Graph.AddNode(baseNode);
            state.AddSegment(SupportSegmentType.Tip, contact, baseNode,
                tipMemberDiameter, options.Origin);
            return;
        }

        var (_, junction) = EmitTipMember(tip, branchFrom ?? trunkTop, options, state,
            tipMemberDiameter);
        SupportNode top = junction;
        Guid? branchSegmentId = null;
        if (branchFrom is not null)
        {
            top = state.NewNode(SupportNodeType.Junction, trunkTop, options.Origin);
            state.Graph.AddNode(top);
            branchSegmentId = state.AddSegment(SupportSegmentType.Branch, junction, top,
                options.BranchDiameter, options.Origin).Id;
        }
        state.Graph.AddNode(baseNode);
        var trunkSegment = state.AddSegment(SupportSegmentType.Trunk, top, baseNode,
            options.TrunkDiameter, options.Origin);
        state.Trunks.Add(new TrunkRecord(new Vector2(trunkTop.X, trunkTop.Y), trunkTop.Z,
            top.Id, baseNode.Id, trunkSegment.Id, branchSegmentId, state));
    }

    /// <summary>
    /// Splits a trunk at the attachment height: the covering segment is replaced by two trunk
    /// segments sharing a new junction, so a branch can join mid-trunk.
    /// </summary>
    private static SupportNode SplitTrunk(TrunkRecord trunk, float attachZ, RouteState state)
    {
        var (segment, high, low) = trunk.SegmentCovering(attachZ, state.Graph);
        var junction = state.NewNode(SupportNodeType.Junction,
            new Vector3(trunk.Xy.X, trunk.Xy.Y, attachZ), segment.Origin);
        state.Graph.AddNode(junction);
        state.Graph.RemoveSegment(segment.Id);
        state.RemoveGeneratedCapsule(segment.Id);
        var upper = state.AddSegment(segment.Type, high, junction, segment.Diameter, segment.Origin);
        var lower = state.AddSegment(segment.Type, junction, low, segment.Diameter, segment.Origin);
        trunk.ReplaceSegment(segment.Id, upper.Id, lower.Id);
        return junction;
    }

    private sealed class TrunkRecord
    {
        public Vector2 Xy { get; }
        public float TopZ { get; }
        public Guid TopNodeId { get; }
        public Guid BaseNodeId { get; }
        public int BranchCount { get; set; }
        public List<Guid> SegmentIds { get; }
        public List<Guid> BranchSegmentIds { get; }
        private readonly RouteState _state;

        public TrunkRecord(Vector2 xy, float topZ, Guid topNodeId, Guid baseNodeId,
            Guid segmentId, Guid? branchSegmentId, RouteState state)
        {
            Xy = xy;
            TopZ = topZ;
            TopNodeId = topNodeId;
            BaseNodeId = baseNodeId;
            SegmentIds = new List<Guid> { segmentId };
            BranchSegmentIds = branchSegmentId is { } id ? new List<Guid> { id } : new List<Guid>();
            _state = state;
        }

        public (SupportSegment Segment, SupportNode High, SupportNode Low) SegmentCovering(
            float z, SupportGraph graph)
        {
            foreach (var id in SegmentIds)
            {
                var segment = graph.GetSegment(id);
                var a = graph.GetNode(segment.NodeA);
                var b = graph.GetNode(segment.NodeB);
                var (high, low) = a.Position.Z >= b.Position.Z ? (a, b) : (b, a);
                if (z <= high.Position.Z + Epsilon && z >= low.Position.Z - Epsilon)
                    return (segment, high, low);
            }
            throw new InvalidOperationException($"No trunk segment covers z = {z}.");
        }

        public void ReplaceSegment(Guid old, Guid upper, Guid lower)
        {
            SegmentIds.Remove(old);
            SegmentIds.Add(upper);
            SegmentIds.Add(lower);
        }
    }

    /// <summary>Mutable per-run bookkeeping shared by the routing helpers.</summary>
    private sealed class RouteState
    {
        public SupportGraph Graph { get; }
        public RoutingClearance Clearance { get; }
        public float AngleOffset { get; }
        public List<TrunkRecord> Trunks { get; } = new();
        public float MaxLean { get; private set; }
        private readonly DeterministicIds _ids;
        private readonly List<GeneratedCapsule> _capsules = new();

        public RouteState(SupportGraph graph, DeterministicIds ids, RoutingClearance clearance,
            float angleOffset)
        {
            Graph = graph;
            _ids = ids;
            Clearance = clearance;
            AngleOffset = angleOffset;
        }

        public SupportNode NewNode(SupportNodeType type, Vector3 position, SupportOrigin origin)
            => new() { Id = _ids.Next(), Type = type, Position = position, Origin = origin };

        public SupportSegment AddSegment(SupportSegmentType type, SupportNode a, SupportNode b,
            float diameter, SupportOrigin origin)
        {
            var segment = new SupportSegment
            {
                Id = _ids.Next(), Type = type, NodeA = a.Id, NodeB = b.Id,
                Diameter = diameter, Origin = origin,
            };
            Graph.AddSegment(segment);
            _capsules.Add(new GeneratedCapsule(a.Position, b.Position,
                diameter * 0.5f, segment.Id));
            var delta = b.Position - a.Position;
            var lean = MathF.Atan2(new Vector2(delta.X, delta.Y).Length(), MathF.Abs(delta.Z))
                * 180 / MathF.PI;
            if (delta.LengthSquared() > Epsilon * Epsilon) MaxLean = MathF.Max(MaxLean, lean);
            return segment;
        }

        public bool HitsGenerated(Vector3 start, Vector3 end, float radius,
            IReadOnlyCollection<Guid>? excludeSegments = null)
        {
            foreach (var capsule in _capsules)
            {
                if (excludeSegments is not null && excludeSegments.Contains(capsule.SegmentId))
                    continue;
                var sum = radius + capsule.Radius;
                if (GeometryDistance.SegmentSegmentSquared(start, end, capsule.Start, capsule.End)
                    <= sum * sum) return true;
            }
            return false;
        }

        /// <summary>
        /// Tests a branch joining an existing trunk. Branches already attached to that trunk may
        /// overlap inside a small fusion zone around the trunk axis, but remain ordinary collision
        /// obstacles beyond it. Trunk segments are excluded because they are the join target.
        /// </summary>
        public bool HitsGeneratedForTrunkAttachment(Vector3 start, Vector3 end, float radius,
            TrunkRecord trunk, float fusionDistance)
        {
            foreach (var capsule in _capsules)
            {
                if (trunk.SegmentIds.Contains(capsule.SegmentId)) continue;
                var sum = radius + capsule.Radius;
                if (!trunk.BranchSegmentIds.Contains(capsule.SegmentId))
                {
                    if (GeometryDistance.SegmentSegmentSquared(
                            start, end, capsule.Start, capsule.End) <= sum * sum) return true;
                    continue;
                }

                // Ignore only the near-axis portions, expanded by both capsule radii so their
                // hemispherical cut ends do not create a false collision at the fusion boundary.
                var outsideRadius = fusionDistance + sum;
                if (!TryOutsideSegment(start, end, trunk.Xy, outsideRadius,
                        out var proposedStart, out var proposedEnd) ||
                    !TryOutsideSegment(capsule.Start, capsule.End, trunk.Xy, outsideRadius,
                        out var existingStart, out var existingEnd)) continue;
                if (GeometryDistance.SegmentSegmentSquared(proposedStart, proposedEnd,
                        existingStart, existingEnd) <= sum * sum) return true;
            }
            return false;
        }

        private static bool TryOutsideSegment(Vector3 a, Vector3 b, Vector2 axis,
            float excludedRadius, out Vector3 outer, out Vector3 boundary)
        {
            var distanceA = Vector2.Distance(new Vector2(a.X, a.Y), axis);
            var distanceB = Vector2.Distance(new Vector2(b.X, b.Y), axis);
            float outerDistance;
            float innerDistance;
            if (distanceA >= distanceB)
            {
                outer = a;
                boundary = b;
                outerDistance = distanceA;
                innerDistance = distanceB;
            }
            else
            {
                outer = b;
                boundary = a;
                outerDistance = distanceB;
                innerDistance = distanceA;
            }
            if (outerDistance <= excludedRadius + Epsilon) return false;
            if (innerDistance >= excludedRadius - Epsilon) return true;

            var fraction = (outerDistance - excludedRadius) /
                MathF.Max(Epsilon, outerDistance - innerDistance);
            boundary = Vector3.Lerp(outer, boundary, fraction);
            return true;
        }

        public void RemoveGeneratedCapsule(Guid segmentId)
            => _capsules.RemoveAll(capsule => capsule.SegmentId == segmentId);

        private readonly record struct GeneratedCapsule(Vector3 Start, Vector3 End, float Radius,
            Guid SegmentId);
    }
}
