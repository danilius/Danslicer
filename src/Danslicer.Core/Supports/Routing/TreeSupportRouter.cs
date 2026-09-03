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
    public int MiniSupportMaxFanPerBranchEnd { get; init; } = 4;
    /// <summary>Pitch of the plate-origin-aligned square base grid.</summary>
    public float BaseGridPitch { get; init; } = 20f;
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
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.BaseGridPitch);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.ExistingTrunkBranchRange);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MiniSupportDiameter);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MiniSupportTipDiameter);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MiniSupportConeLength);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MiniSupportMaxLength);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MiniSupportMaxFanPerBranchEnd);
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
        var pendingMini = new List<(RoutingTip Tip, int Index, RoutingFailureReason Reason)>();
        foreach (var item in expandedTips.Select((tip, index) => (Tip: tip, Index: index))
                     .OrderByDescending(item => item.Tip.SurfacePoint.Z).ThenBy(item => item.Index))
        {
            var reason = RoutingFailureReason.NoClearStep;
            if (!item.Tip.MiniSupportOnly && RouteOne(item.Tip, options, state, out reason))
                continue;
            pendingMini.Add((item.Tip, item.Index, reason));
        }
        foreach (var pending in pendingMini.OrderByDescending(item => item.Tip.SurfacePoint.Z)
                     .ThenBy(item => item.Index))
        {
            if (TryRouteMiniSupport(pending.Tip, options, state, out var miniReason)) continue;
            unrouted.Add(pending.Tip);
            failures.Add(new RoutingFailure(pending.Tip,
                pending.Tip.MiniSupportOnly ? miniReason : pending.Reason));
        }

        var bases = graph.Nodes.Where(node => node.Type == SupportNodeType.Base)
            .Select(node => node.Position).OrderBy(p => p.X).ThenBy(p => p.Y).ToList();
        return new RoutingResult(graph, unrouted, bases, state.MaxLean, failures);
    }

    private bool TryRouteMiniSupport(RoutingTip tip, TreeRoutingOptions options, RouteState state,
        out RoutingFailureReason reason)
    {
        reason = RoutingFailureReason.NoClearStep;
        if (tip.SurfacePoint.Z <= options.PlateZ + Epsilon)
        {
            reason = RoutingFailureReason.BelowPlate;
            return false;
        }
        var hasBranchEndInRange = false;
        foreach (var branchEnd in state.BranchEnds
                     .OrderBy(node => Vector3.DistanceSquared(node.Position, tip.SurfacePoint))
                     .ThenBy(node => node.Id))
        {
            var length = Vector3.Distance(branchEnd.Position, tip.SurfacePoint);
            if (length > options.MiniSupportMaxLength + Epsilon || length <= Epsilon) continue;
            hasBranchEndInRange = true;
            if (state.MiniFanCount(branchEnd.Id) >= options.MiniSupportMaxFanPerBranchEnd) continue;
            var bodyRadius = options.MiniSupportDiameter * 0.5f;
            var queryRadius = bodyRadius + state.Clearance.ModelDistance;
            var delta = tip.SurfacePoint - branchEnd.Position;
            var contactAllowance = MathF.Max(options.MiniSupportTipDiameter * 0.5f,
                queryRadius) * 2 + 0.01f;
            var clearEnd = length > contactAllowance
                ? tip.SurfacePoint - delta / length * contactAllowance
                : branchEnd.Position;
            if (clearEnd != branchEnd.Position &&
                _obstacles.IntersectsCapsule(branchEnd.Position, clearEnd, queryRadius)) continue;
            var incident = state.Graph.SegmentsAt(branchEnd.Id).Select(segment => segment.Id).ToList();
            if (state.HitsGenerated(branchEnd.Position, clearEnd, queryRadius, incident)) continue;

            var miniTip = state.NewNode(SupportNodeType.Tip, tip.SurfacePoint, options.Origin);
            RoutingUtilities.ApplyContact(miniTip, tip with
            {
                TipDiameter = options.MiniSupportTipDiameter,
                TipShape = SupportTipShape.Cone,
                ConeLength = options.MiniSupportConeLength,
                BallDiameter = 0f,
            });
            state.Graph.AddNode(miniTip);
            state.AddSegment(SupportSegmentType.MiniSupport, branchEnd, miniTip,
                options.MiniSupportDiameter, options.Origin);
            state.IncrementMiniFan(branchEnd.Id);
            return true;
        }
        if (!hasBranchEndInRange) reason = RoutingFailureReason.NoBranchEndInRange;
        return false;
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
            else if (IsOnBaseGrid(trunkJ1, options) && TrunkIsClear(trunkJ1, options, state))
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
        var trunkTops = TrunkTopCandidates(branchJunction.Value, options).ToList();
        foreach (var trunkTop in trunkTops)
        {
            if (Vector2.DistanceSquared(new(branchJunction.Value.X, branchJunction.Value.Y),
                    new(trunkTop.X, trunkTop.Y)) <= Epsilon * Epsilon) continue;
            if (!MemberIsClear(branchJunction.Value, trunkTop,
                    options.BranchDiameter * 0.5f, state))
                continue;
            if (!TrunkIsClear(trunkTop, options, state)) continue;
            EmitSupport(tip, trunkTop, branchJunction.Value, tipOnly: false,
                options, state, branchTipDiameter);
            return true;
        }
        if (!options.PreferExistingTrunks && branchJunction is { } fallbackJ1 &&
            fallbackJ1.Z > options.PlateZ + Epsilon &&
            TryAttachToTrunk(tip, fallbackJ1, options, state, branchTipDiameter)) return true;
        var straightGridCandidateWasBlocked = trunkJunction is { } straightJunction &&
                                              straightJunction.Z > options.PlateZ + Epsilon &&
                                              IsOnBaseGrid(straightJunction, options);
        if (!straightGridCandidateWasBlocked && trunkTops.Count == 0)
            reason = RoutingFailureReason.NoReachableGridPoint;
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
            if (BaseIsClear(candidate, options, state)) return candidate;
        }
        return null;
    }

    private IEnumerable<Vector3> TipJunctionCandidates(RoutingTip tip,
        TreeRoutingOptions options, RouteState state, float tipMemberDiameter,
        float tipMemberLength)
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
                yield return end;
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
        var sinAngle = MathF.Sin(maxAngle);

        foreach (var trunk in state.Trunks
                     .OrderBy(t => Vector2.DistanceSquared(new(j1.X, j1.Y), t.Xy))
                     .ThenBy(t => t.BaseNodeId))
        {
            if (trunk.BranchCount >= maxBranches) continue;
            var hDist = Vector2.Distance(new(j1.X, j1.Y), trunk.Xy);
            var branchLength = sinAngle > Epsilon ? hDist / sinAngle : float.MaxValue;
            if (branchLength > options.ExistingTrunkBranchRange) continue;
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
            state.RegisterBranchEnd(tipNode.Junction);
            trunk.BranchSegmentIds.Add(branch.Id);
            trunk.BranchCount++;
            return true;
        }
        return false;
    }

    /// <summary>
    /// Reachable plate-origin square-grid drop lines, nearest first. At each point the maximum,
    /// 30° and 15° branch angles are tried, preserving the shallow obstacle-avoidance fallback.
    /// </summary>
    private static IEnumerable<Vector3> TrunkTopCandidates(Vector3 j1, TreeRoutingOptions options)
    {
        var angle = options.MaxMemberAngleDegrees * MathF.PI / 180f;
        var maxHorizontal = options.MaxBranchLength * MathF.Sin(angle);
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
                if (top.Z > options.PlateZ + options.BaseHeight + Epsilon) yield return top;
            }
        }
    }

    private static bool IsOnBaseGrid(Vector3 point, TreeRoutingOptions options)
    {
        var x = MathF.Round(point.X / options.BaseGridPitch) * options.BaseGridPitch;
        var y = MathF.Round(point.Y / options.BaseGridPitch) * options.BaseGridPitch;
        return Vector2.DistanceSquared(new(point.X, point.Y), new(x, y)) <= Epsilon * Epsilon;
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
        public IEnumerable<SupportNode> BranchEnds => _branchEndIds.Select(Graph.GetNode);
        public float MaxLean { get; private set; }
        private readonly DeterministicIds _ids;
        private readonly List<GeneratedCapsule> _capsules = new();
        private readonly HashSet<Guid> _branchEndIds = new();
        private readonly Dictionary<Guid, int> _miniFanCounts = new();

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

        public void RegisterBranchEnd(SupportNode node) => _branchEndIds.Add(node.Id);
        public int MiniFanCount(Guid nodeId) => _miniFanCounts.GetValueOrDefault(nodeId);
        public void IncrementMiniFan(Guid nodeId) =>
            _miniFanCounts[nodeId] = MiniFanCount(nodeId) + 1;

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
