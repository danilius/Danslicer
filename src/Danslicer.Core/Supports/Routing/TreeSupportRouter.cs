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
    public bool PreferExistingTrunks { get; init; } = true;
    /// <summary>Maximum actual branch length when attaching to an existing trunk.</summary>
    public float ExistingTrunkBranchRange { get; init; } = 8f;
    public float MiniSupportDiameter { get; init; } = 0.6f;
    public float MiniSupportTipDiameter { get; init; } = 0.25f;
    public float MiniSupportConeLength { get; init; } = 1f;
    public float MiniSupportMaxLength { get; init; } = 5f;
    /// <summary>Maximum mini-support lean from vertical.</summary>
    public float MiniSupportMaxAngleDegrees { get; init; } = 75f;
    public int MiniSupportMaxFanPerBranchEnd { get; init; } = 4;
    /// <summary>
    /// When true, a refused regular tip may be retried as a mini support. Disabled by default so
    /// structurally required regular contacts remain visible as honest refusals.
    /// </summary>
    public bool RefusedTipsFallBackToMini { get; init; }
    /// <summary>
    /// When true, a fine-feature mini that cannot route is retried as the regular contact it
    /// was converted from, instead of being refused.
    /// </summary>
    public bool FineFeatureMinisFallBackToRegular { get; init; } = true;
    /// <summary>
    /// Minimum centreline clearance between non-incident support members. Zero disables
    /// the additional constraint and preserves legacy routing exactly.
    /// </summary>
    public float MinMemberSeparationMm { get; init; }
    /// <summary>When true, new bases are constrained to the plate-origin square grid.</summary>
    public bool UseBaseGrid { get; init; } = true;
    /// <summary>Pitch of the plate-origin-aligned square base grid.</summary>
    public float BaseGridPitch { get; init; } = 6f;
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
    private const float BranchDirectionPreferenceDiameters = 1f;
    private const float ProjectedBranchClearanceDiameters = 1f;
    private readonly ICollisionScene _obstacles;
    private readonly GrowthRuleSet _rules;

    public TreeSupportRouter(ICollisionScene obstacles, GrowthRuleSet rules)
    {
        _obstacles = obstacles;
        _rules = rules;
    }

    public RoutingResult Route(IEnumerable<RoutingTip> tips, TreeRoutingOptions options,
        SupportGraph? existingGraph = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.TrunkDiameter);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.TipMemberLength);
        if (options.UseBaseGrid)
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.BaseGridPitch);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.ExistingTrunkBranchRange);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MiniSupportDiameter);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MiniSupportTipDiameter);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MiniSupportConeLength);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MiniSupportMaxLength);
        if (!float.IsFinite(options.MiniSupportMaxAngleDegrees) ||
            options.MiniSupportMaxAngleDegrees <= 0 || options.MiniSupportMaxAngleDegrees >= 90)
            throw new ArgumentOutOfRangeException(nameof(options.MiniSupportMaxAngleDegrees));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MiniSupportMaxFanPerBranchEnd);
        if (!float.IsFinite(options.MinMemberSeparationMm) || options.MinMemberSeparationMm < 0)
            throw new ArgumentOutOfRangeException(nameof(options.MinMemberSeparationMm));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.BranchDirections);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.BranchLengthSteps);

        var originalNodeIds = existingGraph?.Nodes.Select(node => node.Id).ToHashSet() ?? [];
        var originalSegmentIds = existingGraph?.Segments.Select(segment => segment.Id).ToHashSet() ?? [];
        var graph = existingGraph is null ? new SupportGraph() : CloneGraph(existingGraph);
        var ids = new DeterministicIds(options.Seed);
        var clearance = RoutingClearance.From(_rules, options.KeepCleanObstacleTags);
        var angleOffset = new Random(options.Seed).NextSingle() * MathF.Tau;
        var state = new RouteState(graph, ids, clearance, angleOffset,
            options.MinMemberSeparationMm);
        if (existingGraph is not null) state.SeedExistingContext();
        var unrouted = new List<RoutingTip>();
        var failures = new List<RoutingFailure>();

        var expandedTips = RoutingUtilities.AddReinforcementTips(tips, _rules, _obstacles, options.Seed)
            .ToList();
        var indexedTips = expandedTips.Select((tip, index) => (Tip: tip, Index: index)).ToList();
        var pendingMini = new List<(RoutingTip Tip, int Index, RoutingFailureReason Reason)>();
        var deferredIslandRetries = new List<(RoutingTip Tip, int Index, RoutingFailureReason Reason)>();
        var pendingFineFeatureRegular =
            new List<(RoutingTip Tip, int Index, RoutingFailureReason Reason)>();
        foreach (var item in indexedTips
                     .OrderByDescending(item => item.Tip.IsIslandPriority)
                     .ThenByDescending(item => item.Tip.SurfacePoint.Z)
                     .ThenBy(item => item.Index))
        {
            if (item.Tip.MiniClusterId is not null) continue;
            var reason = RoutingFailureReason.NoClearStep;
            if (!item.Tip.MiniSupportOnly && RouteOne(item.Tip, options, state, out reason))
                continue;
            if (item.Tip.IsIslandPriority && !item.Tip.MiniSupportOnly)
            {
                deferredIslandRetries.Add((item.Tip, item.Index, reason));
                continue;
            }
            if (item.Tip.MiniSupportOnly || options.RefusedTipsFallBackToMini)
                pendingMini.Add((item.Tip, item.Index, reason));
            else
            {
                unrouted.Add(item.Tip);
                failures.Add(new RoutingFailure(item.Tip, reason));
            }
        }
        // An island gets first use of existing capacity, then one deterministic retry after
        // ordinary structural routes have created additional trunks it may safely share.
        foreach (var retry in deferredIslandRetries.OrderByDescending(item => item.Tip.SurfacePoint.Z)
                     .ThenBy(item => item.Index))
        {
            if (RouteOne(retry.Tip, options, state, out var reason)) continue;
            if (options.RefusedTipsFallBackToMini)
                pendingMini.Add((retry.Tip, retry.Index, reason));
            else
            {
                unrouted.Add(retry.Tip);
                failures.Add(new RoutingFailure(retry.Tip, reason));
            }
        }
        foreach (var cluster in indexedTips
                     .Where(item => item.Tip.MiniClusterId is not null)
                     .GroupBy(item => item.Tip.MiniClusterId!.Value)
                     .OrderBy(group => group.Key))
        {
            var orderedCluster = cluster.OrderBy(item => item.Index).ToList();
            foreach (var failure in RouteMiniCluster(orderedCluster
                         .Select(item => item.Tip).ToList(), options, state))
            {
                var index = orderedCluster.FindIndex(item => item.Tip.Equals(failure.Tip));
                index = index < 0 ? int.MaxValue : orderedCluster[index].Index;
                if (options.FineFeatureMinisFallBackToRegular &&
                    orderedCluster.Count == 1 && failure.Tip.IsFineFeatureMini)
                {
                    pendingFineFeatureRegular.Add((failure.Tip, index, failure.Reason));
                    continue;
                }
                if (failure.Tip.IsIslandPriority)
                {
                    pendingMini.Add((failure.Tip, index, failure.Reason));
                }
                else
                {
                    unrouted.Add(failure.Tip);
                    failures.Add(failure);
                }
            }
        }
        foreach (var failure in pendingFineFeatureRegular.OrderBy(item => item.Index))
        {
            var rebuilt = failure.Tip with
            {
                MiniSupportOnly = false,
                MiniClusterId = null,
                MiniClusterCenter = null,
                IsFineFeatureMini = false,
                TipDiameter = failure.Tip.FallbackTipDiameter ?? failure.Tip.TipDiameter,
                TipShape = failure.Tip.FallbackTipShape ?? failure.Tip.TipShape,
                ConeLength = failure.Tip.FallbackConeLength ?? failure.Tip.ConeLength,
                BallDiameter = failure.Tip.FallbackBallDiameter ?? failure.Tip.BallDiameter,
            };
            if (RouteOne(rebuilt, options, state, out var reason)) continue;
            if (failure.Tip.IsIslandPriority)
                pendingMini.Add((failure.Tip, failure.Index, reason));
            else
            {
                unrouted.Add(failure.Tip);
                failures.Add(new RoutingFailure(failure.Tip, reason));
            }
        }
        foreach (var pending in pendingMini
                     .OrderByDescending(item => item.Tip.SurfacePoint.Z)
                     .ThenBy(item => item.Index))
        {
            if (TryRouteMiniSupport(pending.Tip, options, state, out var miniReason)) continue;
            unrouted.Add(pending.Tip);
            failures.Add(new RoutingFailure(pending.Tip,
                pending.Tip.MiniSupportOnly ? miniReason : pending.Reason));
        }

        var addedNodes = graph.Nodes.Where(node => !originalNodeIds.Contains(node.Id)).ToList();
        var addedSegments = graph.Segments
            .Where(segment => !originalSegmentIds.Contains(segment.Id)).ToList();
        var removedSegments = existingGraph?.Segments
            .Where(segment => !graph.TryGetSegment(segment.Id, out _)).ToList() ?? [];
        var bases = addedNodes.Where(node => node.Type == SupportNodeType.Base)
            .Select(node => node.Position).OrderBy(p => p.X).ThenBy(p => p.Y).ToList();
        return new RoutingResult(graph, unrouted, bases, state.MaxLean, failures)
        {
            Edit = new SupportGraphEdit(addedNodes, addedSegments, removedSegments),
        };
    }

    private static SupportGraph CloneGraph(SupportGraph source)
    {
        var clone = new SupportGraph();
        foreach (var node in source.Nodes) clone.AddNode(node.Clone());
        foreach (var segment in source.Segments) clone.AddSegment(segment.Clone());
        return clone;
    }

    private bool TryRouteMiniSupport(RoutingTip tip, TreeRoutingOptions options, RouteState state,
        out RoutingFailureReason reason, Guid? requiredBranchEndId = null)
    {
        var separationRejections = state.SeparationRejections;
        reason = RoutingFailureReason.NoClearStep;
        if (tip.SurfacePoint.Z <= options.PlateZ + Epsilon)
        {
            reason = RoutingFailureReason.BelowPlate;
            return false;
        }
        var hasBranchEndInRange = false;
        foreach (var branchEnd in state.BranchEnds
                     .Where(node => requiredBranchEndId is null || node.Id == requiredBranchEndId)
                     .OrderBy(node => Vector3.DistanceSquared(node.Position, tip.SurfacePoint))
                     .ThenBy(node => node.Id))
        {
            var length = Vector3.Distance(branchEnd.Position, tip.SurfacePoint);
            if (length > options.MiniSupportMaxLength + Epsilon || length <= Epsilon) continue;
            hasBranchEndInRange = true;
            var delta = tip.SurfacePoint - branchEnd.Position;
            // The rod must ascend to its contact: a tip fed from above prints in mid-air.
            if (delta.Z <= Epsilon) continue;
            var lean = MathF.Atan2(new Vector2(delta.X, delta.Y).Length(), delta.Z) *
                       180 / MathF.PI;
            if (lean > options.MiniSupportMaxAngleDegrees + Epsilon) continue;
            if (state.MiniFanCount(branchEnd.Id) >= options.MiniSupportMaxFanPerBranchEnd) continue;
            var bodyRadius = options.MiniSupportDiameter * 0.5f;
            var queryRadius = bodyRadius + state.Clearance.ModelDistance;
            var contactAllowance = MathF.Max(options.MiniSupportTipDiameter * 0.5f,
                queryRadius) * 2 + 0.01f;
            var clearEnd = length > contactAllowance
                ? tip.SurfacePoint - delta / length * contactAllowance
                : branchEnd.Position;
            var incident = state.Graph.SegmentsAt(branchEnd.Id).Select(segment => segment.Id).ToList();
            if (clearEnd != branchEnd.Position &&
                _obstacles.IntersectsCapsule(branchEnd.Position, clearEnd, queryRadius,
                    Excluding(incident))) continue;
            if (state.HitsGenerated(branchEnd.Position, clearEnd, queryRadius, incident)) continue;
            if (state.ViolatesMemberSeparation(branchEnd.Position, tip.SurfacePoint,
                    branchEnd.Id, null, incident)) continue;

            var miniTip = state.NewNode(SupportNodeType.Tip, tip.SurfacePoint, options.Origin);
            RoutingUtilities.ApplyContact(miniTip, tip with
            {
                TipDiameter = options.MiniSupportTipDiameter,
                TipShape = SupportTipShape.Cone,
                ConeLength = options.MiniSupportConeLength,
                BallDiameter = 0f,
            });
            ClampLeadInToClearPath(miniTip, branchEnd.Position);
            state.Graph.AddNode(miniTip);
            state.AddSegment(SupportSegmentType.MiniSupport, branchEnd, miniTip,
                options.MiniSupportDiameter, options.Origin);
            state.IncrementMiniFan(branchEnd.Id);
            return true;
        }
        if (state.SeparationRejections > separationRejections)
            reason = RoutingFailureReason.MemberCrossing;
        else if (!hasBranchEndInRange)
            reason = RoutingFailureReason.NoBranchEndInRange;
        return false;
    }

    private IReadOnlyList<RoutingFailure> RouteMiniCluster(IReadOnlyList<RoutingTip> members,
        TreeRoutingOptions options, RouteState state)
    {
        if (members.Count == 0) return [];
        var separationRejections = state.SeparationRejections;
        var center = members[0].MiniClusterCenter ??
                     members.Select(member => member.SurfacePoint).Aggregate(Vector3.Zero,
                         (sum, point) => sum + point) / members.Count;
        var lastCarrierReason = RoutingFailureReason.NoClearStep;
        var hadCandidate = false;
        foreach (var position in MiniClusterEndCandidates(members, center, options))
        {
            hadCandidate = true;
            if (!members.Any(member => MiniSupportPathIsClear(member, position, options, state)))
                continue;
            if (!TryRouteClusterCarrier(position, options, state, out var branchEnd,
                    out lastCarrierReason)) continue;

            var failures = new List<RoutingFailure>();
            foreach (var member in members.OrderBy(member => member.SurfacePoint.X)
                         .ThenBy(member => member.SurfacePoint.Y)
                         .ThenBy(member => member.SurfacePoint.Z))
            {
                if (TryRouteMiniSupport(member, options, state, out var memberReason, branchEnd.Id))
                    continue;
                failures.Add(new RoutingFailure(member, memberReason));
            }
            return failures;
        }

        var clusterReason = state.SeparationRejections > separationRejections
            ? RoutingFailureReason.MemberCrossing
            : hadCandidate ? lastCarrierReason : RoutingFailureReason.NoBranchEndInRange;
        return members.Select(member => new RoutingFailure(member, clusterReason)).ToList();
    }

    private static IEnumerable<Vector3> MiniClusterEndCandidates(
        IReadOnlyList<RoutingTip> members, Vector3 center, TreeRoutingOptions options)
    {
        var minZ = members.Min(member => member.SurfacePoint.Z);
        var tanAngle = MathF.Tan(options.MiniSupportMaxAngleDegrees * MathF.PI / 180f);
        var minimumDrop = 0.05f;
        var maximumDrop = float.PositiveInfinity;
        foreach (var member in members)
        {
            var horizontal = Vector2.Distance(new(center.X, center.Y),
                new(member.SurfacePoint.X, member.SurfacePoint.Y));
            if (horizontal >= options.MiniSupportMaxLength) yield break;
            var heightAboveLowest = member.SurfacePoint.Z - minZ;
            minimumDrop = MathF.Max(minimumDrop, horizontal / tanAngle - heightAboveLowest + 1e-3f);
            maximumDrop = MathF.Min(maximumDrop,
                MathF.Sqrt(options.MiniSupportMaxLength * options.MiniSupportMaxLength -
                           horizontal * horizontal) - heightAboveLowest);
        }
        if (maximumDrop < minimumDrop) yield break;

        var preferred = Math.Clamp(options.MiniSupportConeLength, minimumDrop, maximumDrop);
        foreach (var drop in new[]
                 {
                     preferred,
                     minimumDrop,
                     minimumDrop + (maximumDrop - minimumDrop) * 0.33f,
                     minimumDrop + (maximumDrop - minimumDrop) * 0.66f,
                     maximumDrop,
                 }.Distinct().Order())
        {
            var position = new Vector3(center.X, center.Y, minZ - drop);
            if (position.Z > options.PlateZ + Epsilon) yield return position;
        }
    }

    private bool MiniSupportPathIsClear(RoutingTip tip, Vector3 branchEnd,
        TreeRoutingOptions options, RouteState state)
    {
        var delta = tip.SurfacePoint - branchEnd;
        var length = delta.Length();
        if (length > options.MiniSupportMaxLength + Epsilon || length <= Epsilon ||
            delta.Z <= Epsilon) return false;
        var lean = MathF.Atan2(new Vector2(delta.X, delta.Y).Length(), delta.Z) *
                   180 / MathF.PI;
        if (lean > options.MiniSupportMaxAngleDegrees + Epsilon) return false;
        var queryRadius = options.MiniSupportDiameter * 0.5f + state.Clearance.ModelDistance;
        var contactAllowance = MathF.Max(options.MiniSupportTipDiameter * 0.5f,
            queryRadius) * 2 + 0.01f;
        var clearEnd = length > contactAllowance
            ? tip.SurfacePoint - delta / length * contactAllowance
            : branchEnd;
        return clearEnd == branchEnd ||
               (!_obstacles.IntersectsCapsule(branchEnd, clearEnd, queryRadius) &&
                !state.HitsGenerated(branchEnd, clearEnd, queryRadius) &&
                !state.ViolatesMemberSeparation(branchEnd, tip.SurfacePoint));
    }

    private bool TryRouteClusterCarrier(Vector3 branchEndPosition, TreeRoutingOptions options,
        RouteState state, out SupportNode branchEnd, out RoutingFailureReason reason)
    {
        var separationRejections = state.SeparationRejections;
        branchEnd = null!;
        reason = RoutingFailureReason.NoClearStep;
        if (branchEndPosition.Z <= options.PlateZ + Epsilon)
        {
            reason = RoutingFailureReason.BelowPlate;
            return false;
        }

        if (options.PreferExistingTrunks &&
            TryAttachClusterEndToTrunk(branchEndPosition, options, state, out branchEnd))
            return true;

        if ((!options.UseBaseGrid || IsOnBaseGrid(branchEndPosition, options)) &&
            TrunkIsClear(branchEndPosition, options, state))
        {
            branchEnd = EmitClusterCarrier(branchEndPosition, null, options, state);
            return true;
        }

        var trunkTops = TrunkTopCandidates(branchEndPosition, options, state.AngleOffset).ToList();
        foreach (var candidate in trunkTops)
        {
            if (Vector2.DistanceSquared(new(branchEndPosition.X, branchEndPosition.Y),
                    new(candidate.Top.X, candidate.Top.Y)) <= Epsilon * Epsilon) continue;
            if (!BranchIsClear(branchEndPosition, candidate.Top, options, state) ||
                !TrunkIsClear(candidate.Top, options, state)) continue;
            branchEnd = EmitClusterCarrier(branchEndPosition, candidate.Top, options, state);
            return true;
        }

        if (!options.PreferExistingTrunks &&
            TryAttachClusterEndToTrunk(branchEndPosition, options, state, out branchEnd))
            return true;
        if (options.UseBaseGrid && trunkTops.Count == 0)
            reason = RoutingFailureReason.NoReachableGridPoint;
        if (state.SeparationRejections > separationRejections)
            reason = RoutingFailureReason.MemberCrossing;
        return false;
    }

    private bool TryAttachClusterEndToTrunk(Vector3 position, TreeRoutingOptions options,
        RouteState state, out SupportNode branchEnd)
    {
        branchEnd = null!;
        var branchRule = _rules.Find<BranchGrowthRule>();
        var maxBranches = branchRule is { Enabled: true }
            ? branchRule.MaxBranchesPerTrunk
            : int.MaxValue;
        var maxAngle = options.MaxMemberAngleDegrees * MathF.PI / 180f;
        var tanAngle = MathF.Tan(maxAngle);
        var candidates = new List<ExistingTrunkCandidate>();
        foreach (var trunk in state.Trunks)
        {
            if (trunk.BranchCount >= maxBranches) continue;
            var horizontal = Vector2.Distance(new(position.X, position.Y), trunk.Xy);
            if (horizontal <= Epsilon) continue;
            var attachZ = MathF.Min(position.Z - horizontal / tanAngle - 1e-3f, trunk.TopZ);
            if (attachZ < options.PlateZ + options.BaseHeight + Epsilon) continue;
            var attach = new Vector3(trunk.Xy.X, trunk.Xy.Y, attachZ);
            var length = Vector3.Distance(position, attach);
            if (length > options.ExistingTrunkBranchRange + Epsilon) continue;
            candidates.Add(new ExistingTrunkCandidate(trunk, attach, length, length));
        }

        foreach (var candidate in candidates.OrderBy(item => item.Score)
                     .ThenBy(item => item.Trunk.BaseNodeId))
        {
            var trunk = candidate.Trunk;
            var radius = options.BranchDiameter * 0.5f;
            if (!state.Clearance.PillarIsClear(_obstacles, position, candidate.Attach, radius,
                    Excluding(trunk.SegmentIds.Concat(trunk.BranchSegmentIds)))) continue;
            if (state.HitsGeneratedForTrunkAttachment(position, candidate.Attach,
                    radius + state.Clearance.ModelDistance, trunk,
                    SiblingBranchFusionDistance,
                    options.BranchDiameter * ProjectedBranchClearanceDiameters)) continue;
            var targetSegment = trunk.SegmentCovering(candidate.Attach.Z, state.Graph).Segment;
            var sharedNode = MathF.Abs(candidate.Attach.Z - trunk.TopZ) <= 1e-3f
                ? trunk.TopNodeId
                : (Guid?)null;
            if (state.ViolatesMemberSeparation(position, candidate.Attach,
                    null, sharedNode, [targetSegment.Id])) continue;

            var attachNode = MathF.Abs(candidate.Attach.Z - trunk.TopZ) <= 1e-3f
                ? state.Graph.GetNode(trunk.TopNodeId)
                : SplitTrunk(trunk, candidate.Attach.Z, state);
            branchEnd = state.NewNode(SupportNodeType.Junction, position, options.Origin);
            state.Graph.AddNode(branchEnd);
            var branch = state.AddSegment(SupportSegmentType.Branch, branchEnd, attachNode,
                options.BranchDiameter, options.Origin);
            state.RegisterBranchEnd(branchEnd);
            trunk.BranchSegmentIds.Add(branch.Id);
            trunk.BranchCount++;
            return true;
        }
        return false;
    }

    private static SupportNode EmitClusterCarrier(Vector3 branchEndPosition, Vector3? trunkTop,
        TreeRoutingOptions options, RouteState state)
    {
        var branchEnd = state.NewNode(SupportNodeType.Junction, branchEndPosition, options.Origin);
        state.Graph.AddNode(branchEnd);
        var top = branchEnd;
        Guid? branchSegmentId = null;
        if (trunkTop is { } topPosition)
        {
            top = state.NewNode(SupportNodeType.Junction, topPosition, options.Origin);
            state.Graph.AddNode(top);
            branchSegmentId = state.AddSegment(SupportSegmentType.Branch, branchEnd, top,
                options.BranchDiameter, options.Origin).Id;
        }

        var basePosition = new Vector3(top.Position.X, top.Position.Y, options.PlateZ);
        var baseNode = state.NewNode(SupportNodeType.Base, basePosition, options.Origin);
        baseNode.BaseShape = options.BaseShape;
        baseNode.BaseDiameter = options.BaseDiameter;
        baseNode.BaseHeight = options.BaseHeight;
        baseNode.BaseConeHeight = options.BaseConeHeight;
        state.Graph.AddNode(baseNode);
        var trunk = state.AddSegment(SupportSegmentType.Trunk, top, baseNode,
            options.TrunkDiameter, options.Origin);
        state.Trunks.Add(new TrunkRecord(new Vector2(top.Position.X, top.Position.Y), top.Position.Z,
            top.Id, baseNode.Id, trunk.Id, branchSegmentId, state));
        state.RegisterBranchEnd(branchEnd);
        return branchEnd;
    }

    private bool RouteOne(RoutingTip tip, TreeRoutingOptions options, RouteState state,
        out RoutingFailureReason reason)
    {
        var separationRejections = state.SeparationRejections;
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
        if (options.PreferExistingTrunks && branchJunction is { } branchJ1 &&
            branchJ1.Z > options.PlateZ + Epsilon &&
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
                var baseJunction = FindTipBaseJunction(tip, options, state,
                    trunkTipDiameter, tipMemberLength);
                if (baseJunction is { } baseJ1)
                {
                    EmitSupport(tip, new Vector3(baseJ1.X, baseJ1.Y, options.PlateZ), null,
                        tipOnly: true, options, state, trunkTipDiameter);
                    return true;
                }
            }
            else if ((!options.UseBaseGrid || IsOnBaseGrid(trunkJ1, options)) &&
                     TrunkIsClear(trunkJ1, options, state))
            {
                EmitSupport(tip, trunkJ1, null, tipOnly: false,
                    options, state, trunkTipDiameter);
                return true;
            }
        }

        if (branchJunction is null)
        {
            reason = state.SeparationRejections > separationRejections
                ? RoutingFailureReason.MemberCrossing
                : RoutingFailureReason.ContactBlocked;
            return false;
        }

        // The straight candidate was handled with trunk-derived tip geometry above. Every
        // remaining candidate introduces a branch, so both it and its tip use branch settings.
        var trunkTops = TrunkTopCandidates(
            branchJunction.Value, options, state.AngleOffset).ToList();
        foreach (var candidate in trunkTops)
        {
            var trunkTop = candidate.Top;
            if (Vector2.DistanceSquared(new(branchJunction.Value.X, branchJunction.Value.Y),
                    new(trunkTop.X, trunkTop.Y)) <= Epsilon * Epsilon) continue;
            if (!BranchIsClear(branchJunction.Value, trunkTop, options, state))
                continue;
            if (!TrunkIsClear(trunkTop, options, state)) continue;
            if (state.ProposedMembersViolateSeparation(tip.SurfacePoint,
                    branchJunction.Value, trunkTop,
                    new Vector3(trunkTop.X, trunkTop.Y, options.PlateZ))) continue;
            EmitSupport(tip, trunkTop, branchJunction.Value, tipOnly: false,
                options, state, branchTipDiameter);
            return true;
        }
        if (!options.PreferExistingTrunks && branchJunction is { } fallbackJ1 &&
            fallbackJ1.Z > options.PlateZ + Epsilon &&
            TryAttachToTrunk(tip, fallbackJ1, options, state, branchTipDiameter)) return true;
        var straightGridCandidateWasBlocked = options.UseBaseGrid &&
                                              trunkJunction is { } straightJunction &&
                                              straightJunction.Z > options.PlateZ + Epsilon &&
                                              IsOnBaseGrid(straightJunction, options);
        if (options.UseBaseGrid && !straightGridCandidateWasBlocked && trunkTops.Count == 0)
            reason = RoutingFailureReason.NoReachableGridPoint;
        if (state.SeparationRejections > separationRejections)
            reason = RoutingFailureReason.MemberCrossing;
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
        foreach (var candidate in TipJunctionCandidates(tip, options, state,
                     tipMemberDiameter, tipMemberLength))
            return candidate;
        return null;
    }

    private Vector3? FindTipBaseJunction(RoutingTip tip, TreeRoutingOptions options,
        RouteState state, float tipMemberDiameter, float tipMemberLength)
    {
        if (!options.UseBaseGrid)
        {
            foreach (var candidate in TipJunctionCandidates(tip, options, state,
                         tipMemberDiameter, tipMemberLength, includeBaseRelocationFan: true))
            {
                if (candidate.Z > options.PlateZ + Epsilon) continue;
                var basePosition = new Vector3(candidate.X, candidate.Y, options.PlateZ);
                if (BaseIsClear(basePosition, options, state)) return candidate;
            }
            return null;
        }

        var vertical = tip.SurfacePoint.Z - options.PlateZ;
        if (vertical <= Epsilon || vertical > tipMemberLength + Epsilon) return null;
        var maxAngle = options.MaxMemberAngleDegrees * MathF.PI / 180f;
        var maxHorizontal = MathF.Min(vertical * MathF.Tan(maxAngle),
            MathF.Sqrt(MathF.Max(0, tipMemberLength * tipMemberLength - vertical * vertical)));
        foreach (var xy in BaseLattice.NearestSquarePoints(
                     new Vector2(tip.SurfacePoint.X, tip.SurfacePoint.Y),
                     options.BaseGridPitch, maxHorizontal))
        {
            var candidate = new Vector3(xy, options.PlateZ);
            var contactRadius = MathF.Max(0.025f, tip.TipDiameter * 0.5f) +
                                state.Clearance.ModelDistance;
            if (!ContactMemberIsClear(tip.SurfacePoint, candidate, contactRadius)) continue;
            if (state.HitsGenerated(tip.SurfacePoint, candidate,
                    tipMemberDiameter * 0.5f + state.Clearance.ModelDistance)) continue;
            if (state.ViolatesMemberSeparation(tip.SurfacePoint, candidate)) continue;
            if (BaseIsClear(candidate, options, state)) return candidate;
        }
        return null;
    }

    private IEnumerable<Vector3> TipJunctionCandidates(RoutingTip tip,
        TreeRoutingOptions options, RouteState state, float tipMemberDiameter,
        float tipMemberLength, bool includeBaseRelocationFan = false)
    {
        var contactRadius = MathF.Max(0.025f, tip.TipDiameter * 0.5f) + state.Clearance.ModelDistance;
        var memberRadius = tipMemberDiameter * 0.5f + state.Clearance.ModelDistance;
        // Rough or tightly packed contacts (teeth) can block every full-length departure; a
        // short member still gets the support off the surface, as in the top-down router.
        var shortLength = MathF.Min(tipMemberLength, contactRadius * 2);
        foreach (var candidateLength in new[] { tipMemberLength, shortLength }.Distinct())
        {
            var directions = includeBaseRelocationFan
                ? TipToBaseDirections(tip, options, state.AngleOffset)
                : TipDirections(tip, options, state.AngleOffset);
            foreach (var direction in directions)
            {
                var length = direction.Z < -Epsilon
                    ? MathF.Min(candidateLength, (tip.SurfacePoint.Z - options.PlateZ) / -direction.Z)
                    : candidateLength;
                var end = tip.SurfacePoint + direction * length;
                if (!ContactMemberIsClear(tip.SurfacePoint, end, contactRadius)) continue;
                if (state.HitsGenerated(tip.SurfacePoint, end, memberRadius)) continue;
                if (state.ViolatesMemberSeparation(tip.SurfacePoint, end)) continue;
                yield return end;
            }
        }
    }

    /// <summary>
    /// Near the plate, a flat underside's normal produces only a straight-down tip direction.
    /// Add angled directions so a full-size base can move off blocked plate geometry.
    /// </summary>
    private IEnumerable<Vector3> TipToBaseDirections(RoutingTip tip, TreeRoutingOptions options,
        float angleOffset)
    {
        foreach (var direction in TipDirections(tip, options, angleOffset)) yield return direction;
        foreach (var angleDegrees in new[]
                 {
                     options.MaxMemberAngleDegrees,
                     MathF.Min(options.MaxMemberAngleDegrees, 30f),
                     MathF.Min(options.MaxMemberAngleDegrees, 15f),
                 }.Distinct())
        {
            var angle = angleDegrees * MathF.PI / 180f;
            for (var index = 0; index < options.BranchDirections; index++)
            {
                var theta = angleOffset + index * MathF.Tau / options.BranchDirections;
                yield return Vector3.Normalize(new Vector3(
                    MathF.Cos(theta) * MathF.Sin(angle),
                    MathF.Sin(theta) * MathF.Sin(angle),
                    -MathF.Cos(angle)));
            }
        }
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
        var lean = new Vector2(j1.X - tip.SurfacePoint.X, j1.Y - tip.SurfacePoint.Y);
        var leanDirection = lean.LengthSquared() > Epsilon * Epsilon
            ? Vector2.Normalize(lean)
            : Vector2.Zero;
        var candidates = new List<ExistingTrunkCandidate>();
        foreach (var trunk in state.Trunks)
        {
            if (trunk.BranchCount >= maxBranches) continue;
            var hDist = Vector2.Distance(new(j1.X, j1.Y), trunk.Xy);
            // A hair steeper than the exact member angle, so float rounding in the lean rule
            // can never clamp (and thereby reject) a nominally-exact 45° branch.
            var highestAngleLimitedZ = j1.Z -
                                       (tanAngle > Epsilon ? hDist / tanAngle : 0f) - 1e-3f;
            // Use the highest point the existing trunk can offer without exceeding the angle.
            // This is the shortest viable branch; a shorter trunk therefore receives a shallower
            // branch at its top instead of being discarded outright.
            var attachZ = MathF.Min(highestAngleLimitedZ, trunk.TopZ);
            if (attachZ < options.PlateZ + options.BaseHeight + Epsilon) continue;
            if (hDist <= Epsilon) continue; // the junction is on the trunk line; the drop handles it
            var attach = new Vector3(trunk.Xy.X, trunk.Xy.Y, attachZ);
            var branchLength = Vector3.Distance(j1, attach);
            if (branchLength > options.ExistingTrunkBranchRange + Epsilon) continue;

            var towardTrunk = Vector2.Normalize(trunk.Xy - new Vector2(j1.X, j1.Y));
            var alignmentPenalty = leanDirection == Vector2.Zero
                ? 0f
                : (1f - Vector2.Dot(leanDirection, towardTrunk)) * options.BranchDiameter *
                  BranchDirectionPreferenceDiameters;
            candidates.Add(new ExistingTrunkCandidate(
                trunk, attach, branchLength, branchLength + alignmentPenalty));
        }

        foreach (var candidate in candidates.OrderBy(item => item.Score)
                     .ThenBy(item => item.Length).ThenBy(item => item.Trunk.BaseNodeId))
        {
            var trunk = candidate.Trunk;
            var attach = candidate.Attach;
            var attachZ = attach.Z;
            // The spec's member angle governs branch geometry here; the lean rule's step-router
            // clamps (including its tighter near-tip angle) do not apply to tree anatomy.
            var branchRadius = options.BranchDiameter * 0.5f;
            if (!state.Clearance.PillarIsClear(_obstacles, j1, attach, branchRadius,
                    Excluding(trunk.SegmentIds.Concat(trunk.BranchSegmentIds)))) continue;
            if (state.HitsGeneratedForTrunkAttachment(j1, attach,
                    branchRadius + state.Clearance.ModelDistance, trunk,
                    SiblingBranchFusionDistance,
                    options.BranchDiameter * ProjectedBranchClearanceDiameters)) continue;
            var targetSegment = trunk.SegmentCovering(attachZ, state.Graph).Segment;
            var sharedNode = MathF.Abs(attachZ - trunk.TopZ) <= 1e-3f
                ? trunk.TopNodeId
                : (Guid?)null;
            if (state.ViolatesMemberSeparation(j1, attach,
                    null, sharedNode, [targetSegment.Id])) continue;

            var attachNode = MathF.Abs(attachZ - trunk.TopZ) <= 1e-3f
                ? state.Graph.GetNode(trunk.TopNodeId)
                : SplitTrunk(trunk, attachZ, state);
            var tipNode = EmitTipMember(tip, j1, options, state, tipMemberDiameter);
            var branch = state.AddSegment(SupportSegmentType.Branch, tipNode.Junction, attachNode,
                options.BranchDiameter, options.Origin);
            state.RegisterBranchEnd(tipNode.Junction);
            trunk.BranchSegmentIds.Add(branch.Id);
            trunk.BranchCount++;
            return true;
        }
        return false;
    }

    /// <summary>
    /// Grid mode uses reachable plate-origin square-grid drop lines, nearest first. Free mode
    /// restores the deterministic branch fan used before bases were constrained to a grid.
    /// </summary>
    private static IEnumerable<TrunkTopCandidate> TrunkTopCandidates(Vector3 j1,
        TreeRoutingOptions options,
        float angleOffset)
    {
        if (!options.UseBaseGrid)
        {
            yield return new TrunkTopCandidate(j1, 0f, 0f);
            var angles = new[]
            {
                MathF.Min(options.MaxMemberAngleDegrees, 15f),
                MathF.Min(options.MaxMemberAngleDegrees, 30f),
                options.MaxMemberAngleDegrees,
            }.Distinct().Order();
            // Search by physical member length first. At a given length, prefer the more vertical
            // member; this prevents every 45-degree length from winning before a shorter shallow
            // alternative is even considered.
            for (var step = 1; step <= options.BranchLengthSteps; step++)
            {
                var length = options.MaxBranchLength * step / options.BranchLengthSteps;
                foreach (var angleDegrees in angles)
                {
                    var freeAngle = angleDegrees * MathF.PI / 180f;
                    for (var index = 0; index < options.BranchDirections; index++)
                    {
                        var theta = angleOffset + index * MathF.Tau / options.BranchDirections;
                        var direction = new Vector3(
                            MathF.Cos(theta) * MathF.Sin(freeAngle),
                            MathF.Sin(theta) * MathF.Sin(freeAngle),
                            -MathF.Cos(freeAngle));
                        var end = j1 + direction * length;
                        if (end.Z > options.PlateZ + options.BaseHeight + Epsilon)
                            yield return new TrunkTopCandidate(end, length, angleDegrees);
                    }
                }
            }
            yield break;
        }

        var angle = options.MaxMemberAngleDegrees * MathF.PI / 180f;
        var maxHorizontal = options.MaxBranchLength * MathF.Sin(angle);
        var candidates = new List<TrunkTopCandidate>();
        foreach (var xy in BaseLattice.NearestSquarePoints(new Vector2(j1.X, j1.Y),
                     options.BaseGridPitch, maxHorizontal))
        {
            var horizontal = Vector2.Distance(new(j1.X, j1.Y), xy);
            foreach (var candidateDegrees in new[]
                     {
                         options.MaxMemberAngleDegrees,
                         MathF.Min(options.MaxMemberAngleDegrees, 30f),
                         MathF.Min(options.MaxMemberAngleDegrees, 15f),
                     }.Distinct())
            {
                var candidateAngle = candidateDegrees * MathF.PI / 180f;
                var length = horizontal <= Epsilon ? 0 : horizontal / MathF.Sin(candidateAngle);
                if (length > options.MaxBranchLength + Epsilon) continue;
                var drop = horizontal <= Epsilon ? 0 : horizontal / MathF.Tan(candidateAngle);
                var top = new Vector3(xy, j1.Z - drop);
                if (top.Z > options.PlateZ + options.BaseHeight + Epsilon)
                    candidates.Add(new TrunkTopCandidate(top, length, candidateDegrees));
            }
        }
        foreach (var candidate in candidates.OrderBy(item => item.Length)
                     .ThenBy(item => item.AngleDegrees)
                     .ThenBy(item => item.Top.X).ThenBy(item => item.Top.Y))
            yield return candidate;
    }

    private static bool IsOnBaseGrid(Vector3 point, TreeRoutingOptions options)
    {
        var x = MathF.Round(point.X / options.BaseGridPitch) * options.BaseGridPitch;
        var y = MathF.Round(point.Y / options.BaseGridPitch) * options.BaseGridPitch;
        return Vector2.DistanceSquared(new(point.X, point.Y), new(x, y)) <= Epsilon * Epsilon;
    }

    private static Func<object?, bool> Excluding(IEnumerable<Guid> segmentIds)
    {
        var excluded = segmentIds.ToHashSet();
        return tag => tag is not Guid id || !excluded.Contains(id);
    }

    private bool TrunkIsClear(Vector3 top, TreeRoutingOptions options, RouteState state)
    {
        var basePosition = new Vector3(top.X, top.Y, options.PlateZ);
        return MemberIsClear(top, basePosition, options.TrunkDiameter * 0.5f, state) &&
               BaseIsClear(basePosition, options, state);
    }

    private bool BaseIsClear(Vector3 basePosition, TreeRoutingOptions options, RouteState state)
    {
        if (options.BaseShape == SupportBaseShape.None) return true;
        var discTop = basePosition + Vector3.UnitZ * options.BaseHeight;
        return state.Clearance.PillarIsClear(_obstacles, basePosition, discTop,
            options.BaseDiameter * 0.5f);
    }

    private bool MemberIsClear(Vector3 start, Vector3 end, float physicalRadius, RouteState state,
        IReadOnlyCollection<Guid>? excludeSegments = null)
    {
        if (!state.Clearance.PillarIsClear(_obstacles, start, end, physicalRadius)) return false;
        return !state.HitsGenerated(start, end,
                   physicalRadius + state.Clearance.ModelDistance, excludeSegments) &&
               !state.ViolatesMemberSeparation(start, end,
                   null, null, excludeSegments);
    }

    private bool BranchIsClear(Vector3 start, Vector3 end, TreeRoutingOptions options,
        RouteState state)
    {
        var radius = options.BranchDiameter * 0.5f;
        return MemberIsClear(start, end, radius, state) &&
               !state.HasProjectedBranchNearPass(start, end,
                   options.BranchDiameter * ProjectedBranchClearanceDiameters);
    }

    internal static bool ProjectedSegmentsPassTooClose(Vector3 aStart, Vector3 aEnd,
        Vector3 bStart, Vector3 bEnd, float clearance)
    {
        var flatAStart = new Vector3(aStart.X, aStart.Y, 0);
        var flatAEnd = new Vector3(aEnd.X, aEnd.Y, 0);
        var flatBStart = new Vector3(bStart.X, bStart.Y, 0);
        var flatBEnd = new Vector3(bEnd.X, bEnd.Y, 0);
        return GeometryDistance.SegmentSegmentSquared(flatAStart, flatAEnd, flatBStart, flatBEnd)
               <= clearance * clearance;
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
        ClampLeadInToClearPath(contact, j1);
        state.Graph.AddNode(contact);
        var junction = state.NewNode(SupportNodeType.Junction, j1, options.Origin);
        state.Graph.AddNode(junction);
        state.AddSegment(SupportSegmentType.Tip, contact, junction,
            tipMemberDiameter, options.Origin);
        return (contact, junction);
    }

    /// <summary>
    /// The contact-normal bend is derived geometry and must not turn an otherwise clear route
    /// into a model collision. Keep route selection bit-identical and shorten only the stored
    /// lead-in, deterministically, when the requested bend is obstructed.
    /// </summary>
    private void ClampLeadInToClearPath(SupportNode tip, Vector3 junction)
    {
        var requested = tip.TipNormalLeadIn;
        if (requested <= 0) return;
        var radius = MathF.Max(0.025f, tip.TipDiameter * 0.5f);
        var contactAllowance = (radius + 0.25f) * 2 + 0.01f;
        foreach (var factor in new[] { 1f, 0.75f, 0.5f, 0.25f, 0f })
        {
            var candidate = requested * factor;
            if (TipPathIntersectsModel(tip, junction, candidate, radius, contactAllowance))
                continue;
            tip.TipNormalLeadIn = candidate;
            return;
        }
        tip.TipNormalLeadIn = 0;
    }

    private bool TipPathIntersectsModel(SupportNode tip, Vector3 junction, float leadIn,
        float radius, float contactAllowance)
    {
        var points = TipBodyGeometry.Centerline(tip.Position, tip.SurfaceNormal, junction, leadIn);
        var remainingTrim = contactAllowance;
        for (var i = 1; i < points.Count; i++)
        {
            var start = points[i - 1];
            var end = points[i];
            var length = Vector3.Distance(start, end);
            if (remainingTrim >= length)
            {
                remainingTrim -= length;
                continue;
            }
            if (remainingTrim > 0)
            {
                start = Vector3.Lerp(start, end, remainingTrim / length);
                remainingTrim = 0;
            }
            if (_obstacles.IntersectsCapsule(start, end, radius)) return true;
        }
        return false;
    }

    private void EmitSupport(RoutingTip tip, Vector3 trunkTop, Vector3? branchFrom, bool tipOnly,
        TreeRoutingOptions options, RouteState state, float tipMemberDiameter)
    {
        var basePosition = new Vector3(trunkTop.X, trunkTop.Y, options.PlateZ);
        var baseNode = state.NewNode(SupportNodeType.Base, basePosition, options.Origin);
        baseNode.BaseShape = options.BaseShape;
        baseNode.BaseDiameter = options.BaseDiameter;
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
            state.RegisterBranchEnd(junction);
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
            : this(xy, topZ, topNodeId, baseNodeId, [segmentId],
                branchSegmentId is { } id ? [id] : [], 0, state)
        {
        }

        public TrunkRecord(Vector2 xy, float topZ, Guid topNodeId, Guid baseNodeId,
            IEnumerable<Guid> segmentIds, IEnumerable<Guid> branchSegmentIds, int branchCount,
            RouteState state)
        {
            Xy = xy;
            TopZ = topZ;
            TopNodeId = topNodeId;
            BaseNodeId = baseNodeId;
            SegmentIds = segmentIds.ToList();
            BranchSegmentIds = branchSegmentIds.ToList();
            BranchCount = branchCount;
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

    private readonly record struct ExistingTrunkCandidate(TrunkRecord Trunk, Vector3 Attach,
        float Length, float Score);

    private readonly record struct TrunkTopCandidate(Vector3 Top, float Length,
        float AngleDegrees);

    /// <summary>Mutable per-run bookkeeping shared by the routing helpers.</summary>
    private sealed class RouteState
    {
        public SupportGraph Graph { get; }
        public RoutingClearance Clearance { get; }
        public float AngleOffset { get; }
        public List<TrunkRecord> Trunks { get; } = new();
        public IEnumerable<SupportNode> BranchEnds => _branchEndIds.Select(Graph.GetNode);
        public float MaxLean { get; private set; }
        private readonly DeterministicIds _ids;
        private readonly List<GeneratedCapsule> _capsules = new();
        private readonly HashSet<Guid> _branchEndIds = new();
        private readonly Dictionary<Guid, int> _miniFanCounts = new();
        private readonly float _minimumMemberSeparation;
        public int SeparationRejections { get; private set; }

        public RouteState(SupportGraph graph, DeterministicIds ids, RoutingClearance clearance,
            float angleOffset, float minimumMemberSeparation)
        {
            Graph = graph;
            _ids = ids;
            Clearance = clearance;
            AngleOffset = angleOffset;
            _minimumMemberSeparation = minimumMemberSeparation;
        }

        /// <summary>
        /// Reconstructs the same trunk and branch-end bookkeeping produced during a fresh route.
        /// Disabled geometry is not load-bearing; hidden geometry remains printable and active.
        /// </summary>
        public void SeedExistingContext()
        {
            foreach (var segment in Graph.Segments.Where(segment => !segment.Disabled))
            {
                var a = Graph.GetNode(segment.NodeA);
                var b = Graph.GetNode(segment.NodeB);
                if (a.Disabled || b.Disabled) continue;
                _capsules.Add(new GeneratedCapsule(a.Position, b.Position,
                    segment.Diameter * 0.5f, segment.Id, segment.Type,
                    segment.NodeA, segment.NodeB));
            }

            var trunkNodes = new HashSet<Guid>();
            var visitedTrunkSegments = new HashSet<Guid>();
            foreach (var baseNode in Graph.Nodes
                         .Where(node => node.Type == SupportNodeType.Base && !node.Disabled)
                         .OrderBy(node => node.Id))
            {
                var segmentIds = new HashSet<Guid>();
                var nodeIds = new HashSet<Guid> { baseNode.Id };
                var queue = new Queue<Guid>();
                queue.Enqueue(baseNode.Id);
                while (queue.Count > 0)
                {
                    var nodeId = queue.Dequeue();
                    foreach (var segment in Graph.SegmentsAt(nodeId))
                    {
                        if (segment.Disabled || segment.Type != SupportSegmentType.Trunk ||
                            !visitedTrunkSegments.Add(segment.Id)) continue;
                        var otherId = segment.NodeA == nodeId ? segment.NodeB : segment.NodeA;
                        if (Graph.GetNode(otherId).Disabled) continue;
                        segmentIds.Add(segment.Id);
                        if (nodeIds.Add(otherId)) queue.Enqueue(otherId);
                    }
                }
                if (segmentIds.Count == 0) continue;

                trunkNodes.UnionWith(nodeIds);
                var top = nodeIds.Select(Graph.GetNode)
                    .OrderByDescending(node => node.Position.Z).ThenBy(node => node.Id).First();
                var branches = Graph.Segments
                    .Where(segment => !segment.Disabled && segment.Type == SupportSegmentType.Branch &&
                        (nodeIds.Contains(segment.NodeA) || nodeIds.Contains(segment.NodeB)))
                    .Select(segment => segment.Id).Distinct().ToList();
                Trunks.Add(new TrunkRecord(new Vector2(top.Position.X, top.Position.Y),
                    top.Position.Z, top.Id, baseNode.Id, segmentIds, branches, branches.Count, this));
            }

            foreach (var branch in Graph.Segments
                         .Where(segment => !segment.Disabled &&
                             segment.Type == SupportSegmentType.Branch))
            {
                var aOnTrunk = trunkNodes.Contains(branch.NodeA);
                var bOnTrunk = trunkNodes.Contains(branch.NodeB);
                if (aOnTrunk == bOnTrunk) continue;
                var branchEndId = aOnTrunk ? branch.NodeB : branch.NodeA;
                var branchEnd = Graph.GetNode(branchEndId);
                if (branchEnd.Disabled) continue;
                RegisterBranchEnd(branchEnd);
                _miniFanCounts[branchEndId] = Graph.SegmentsAt(branchEndId).Count(segment =>
                    !segment.Disabled && segment.Type == SupportSegmentType.MiniSupport);
            }
        }

        public SupportNode NewNode(SupportNodeType type, Vector3 position, SupportOrigin origin)
            => new() { Id = NextUnusedId(), Type = type, Position = position, Origin = origin };

        public void RegisterBranchEnd(SupportNode node) => _branchEndIds.Add(node.Id);
        public int MiniFanCount(Guid nodeId) => _miniFanCounts.GetValueOrDefault(nodeId);
        public void IncrementMiniFan(Guid nodeId) =>
            _miniFanCounts[nodeId] = MiniFanCount(nodeId) + 1;

        public SupportSegment AddSegment(SupportSegmentType type, SupportNode a, SupportNode b,
            float diameter, SupportOrigin origin)
        {
            var segment = new SupportSegment
            {
                Id = NextUnusedId(), Type = type, NodeA = a.Id, NodeB = b.Id,
                Diameter = diameter, Origin = origin,
            };
            Graph.AddSegment(segment);
            _capsules.Add(new GeneratedCapsule(a.Position, b.Position,
                diameter * 0.5f, segment.Id, type, a.Id, b.Id));
            var delta = b.Position - a.Position;
            var lean = MathF.Atan2(new Vector2(delta.X, delta.Y).Length(), MathF.Abs(delta.Z))
                * 180 / MathF.PI;
            if (delta.LengthSquared() > Epsilon * Epsilon) MaxLean = MathF.Max(MaxLean, lean);
            return segment;
        }

        private Guid NextUnusedId()
        {
            Guid id;
            do id = _ids.Next();
            while (Graph.TryGetNode(id, out _) || Graph.TryGetSegment(id, out _));
            return id;
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

        public bool ViolatesMemberSeparation(Vector3 start, Vector3 end,
            Guid? nodeA = null, Guid? nodeB = null,
            IReadOnlyCollection<Guid>? excludeSegments = null)
        {
            if (_minimumMemberSeparation <= 0) return false;
            foreach (var member in _capsules.OrderBy(item => item.SegmentId))
            {
                if (excludeSegments is not null && excludeSegments.Contains(member.SegmentId))
                    continue;
                if (!MemberSeparation.AreTooClose(start, end, nodeA, nodeB,
                        member.Start, member.End, member.NodeA, member.NodeB,
                        _minimumMemberSeparation)) continue;
                SeparationRejections++;
                return true;
            }
            return false;
        }

        public bool ProposedMembersViolateSeparation(Vector3 firstStart, Vector3 firstEnd,
            Vector3 secondStart, Vector3 secondEnd)
        {
            if (_minimumMemberSeparation <= 0 ||
                !MemberSeparation.AreTooClose(firstStart, firstEnd, null, null,
                    secondStart, secondEnd, null, null, _minimumMemberSeparation)) return false;
            SeparationRejections++;
            return true;
        }

        /// <summary>
        /// Tests a branch joining an existing trunk. Branches already attached to that trunk may
        /// overlap inside a small fusion zone around the trunk axis, but remain ordinary collision
        /// obstacles beyond it. Trunk segments are excluded because they are the join target.
        /// </summary>
        public bool HitsGeneratedForTrunkAttachment(Vector3 start, Vector3 end, float radius,
            TrunkRecord trunk, float fusionDistance, float projectedClearance)
        {
            foreach (var capsule in _capsules)
            {
                if (trunk.SegmentIds.Contains(capsule.SegmentId)) continue;
                var sum = radius + capsule.Radius;
                if (!trunk.BranchSegmentIds.Contains(capsule.SegmentId))
                {
                    if (GeometryDistance.SegmentSegmentSquared(
                            start, end, capsule.Start, capsule.End) <= sum * sum) return true;
                    if (capsule.Type == SupportSegmentType.Branch &&
                        ProjectedSegmentsPassTooClose(start, end, capsule.Start, capsule.End,
                            projectedClearance)) return true;
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
                if (capsule.Type == SupportSegmentType.Branch &&
                    ProjectedSegmentsPassTooClose(proposedStart, proposedEnd,
                        existingStart, existingEnd, projectedClearance)) return true;
            }
            return false;
        }

        public bool HasProjectedBranchNearPass(Vector3 start, Vector3 end, float clearance)
        {
            foreach (var capsule in _capsules)
                if (capsule.Type == SupportSegmentType.Branch &&
                    ProjectedSegmentsPassTooClose(start, end, capsule.Start, capsule.End,
                        clearance))
                    return true;
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
            Guid SegmentId, SupportSegmentType Type, Guid NodeA, Guid NodeB);
    }
}
