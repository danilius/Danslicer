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
    /// <summary>
    /// Keeps an existing graph as the edit target while excluding its members, trunks and branch
    /// ends from routing context. Members created by this route still avoid one another.
    /// </summary>
    public bool IgnoreExistingSupports { get; init; }
    /// <summary>
    /// Shares trunks and sees other supports even with the base grid off. Free mode normally
    /// routes every contact blind to every other (user decision 2026-09-07); parenting is the
    /// one operation that asks for sharing there (SUPPORT-GEOMETRY-SPEC "Parenting"). Trunks
    /// are still placed freely, not on any lattice.
    /// </summary>
    public bool ShareTrunks { get; init; }
    /// <summary>
    /// Minimum gap between the surfaces of non-incident support members. Zero disables
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
    /// <summary>Slack on the bend limit so a nominally exact member-angle joint is never refused.</summary>
    private const float BendToleranceDegrees = 0.01f;
    /// <summary>
    /// Tip directions a contact is routed from before it is refused: the clamped normal, then
    /// vertical.
    /// </summary>
    private const int TipDirectionAttempts = 2;
    /// <summary>
    /// A junction closer (horizontally) than this fraction of the tip member length to a trunk
    /// axis or grid drop line is first offered a snap onto it: the cone is re-aimed at the line,
    /// by at most 30° for a vertical cone, and lands on the trunk directly. Only when the snap
    /// is impossible may a branch bridge the gap, and never one shorter than the ball it starts
    /// from (see <see cref="MinBranchOffsetBranchRadii"/>).
    /// </summary>
    private const float JunctionSnapMemberFraction = 0.5f;
    /// <summary>
    /// A branch never starts closer (horizontally, in branch radii) than this to the trunk line
    /// it descends to: such a branch would be shorter than its own ball, a stub that reads as a
    /// defect (user screen test 2026-09-07).
    /// </summary>
    private const float MinBranchOffsetBranchRadii = 1f;
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
        // Free mode (grid off): every contact gets a complete support of its own and knows
        // nothing about any other support, existing or new, even if they collide (user
        // decision 2026-09-07). Only grid mode shares trunks and keeps members apart.
        var shares = options.UseBaseGrid || options.ShareTrunks;
        var state = new RouteState(graph, ids, clearance, angleOffset,
            options.MinMemberSeparationMm, seesOtherSupports: shares);
        if (existingGraph is not null && !options.IgnoreExistingSupports && shares)
            state.SeedExistingContext();
        var unrouted = new List<RoutingTip>();
        var failures = new List<RoutingFailure>();

        var expandedTips = RoutingUtilities.AddReinforcementTips(tips, _rules, _obstacles, options.Seed)
            .ToList();
        var indexedTips = expandedTips.Select((tip, index) => (Tip: tip, Index: index)).ToList();
        var deferredIslandRetries = new List<(RoutingTip Tip, int Index, RoutingFailureReason Reason)>();
        foreach (var item in indexedTips
                     .OrderByDescending(item => item.Tip.IsIslandPriority)
                     .ThenByDescending(item => item.Tip.SurfacePoint.Z)
                     .ThenBy(item => item.Index))
        {
            if (RouteOne(item.Tip, options, state, out var reason)) continue;
            if (item.Tip.IsIslandPriority)
            {
                deferredIslandRetries.Add((item.Tip, item.Index, reason));
                continue;
            }
            unrouted.Add(item.Tip);
            failures.Add(new RoutingFailure(item.Tip, reason));
        }
        // An island gets first use of existing capacity, then one deterministic retry after
        // ordinary structural routes have created additional trunks it may safely share.
        foreach (var retry in deferredIslandRetries.OrderByDescending(item => item.Tip.SurfacePoint.Z)
                     .ThenBy(item => item.Index))
        {
            if (RouteOne(retry.Tip, options, state, out var reason)) continue;
            unrouted.Add(retry.Tip);
            failures.Add(new RoutingFailure(retry.Tip, reason));
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

    /// <summary>
    /// Routes one contact. The cone is tried pointing along its (angle-clamped) normal first;
    /// when no branch or trunk can follow from that junction within the member angle, the whole
    /// route is retried with the cone vertical.
    /// </summary>
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
        var (trunkTipDiameter, _) = TipMemberDimensions(
            tip, options.TrunkDiameter, options.TipMemberLength);
        var anyClear = false;
        foreach (var candidate in TipJunctionCandidates(tip, options, state,
                     branchTipDiameter, options.BranchDiameter, tipMemberLength)
                     .Take(TipDirectionAttempts))
        {
            // A trunk right beside where the cone would end is the cone's junction, even though
            // the cone as first aimed would run into that trunk.
            if (options.UseBaseGrid &&
                TrySnapOntoNearTrunk(tip, candidate.End, trunkTipDiameter, tipMemberLength,
                    options, state)) return true;
            if (!candidate.Clear) continue;
            anyClear = true;
            if (TryRouteFromJunction(tip, candidate.End, branchTipDiameter, trunkTipDiameter,
                    tipMemberLength, options, state, out reason)) return true;
        }
        if (!anyClear)
        {
            reason = state.SeparationRejections > separationRejections
                ? RoutingFailureReason.MemberCrossing
                : RoutingFailureReason.ContactBlocked;
            return false;
        }
        if (!options.UseBaseGrid) return false;

        // Last resort: a base off the grid. Refusing a contact because every lattice point is
        // out of reach, while a trunk could stand right under the junction, reads as absurd on
        // screen (user, 2026-09-07); the grid is a preference, not a reason to refuse.
        var offGrid = options with { UseBaseGrid = false };
        var gridReason = reason;
        foreach (var candidate in TipJunctionCandidates(tip, offGrid, state,
                     branchTipDiameter, offGrid.BranchDiameter, tipMemberLength)
                     .Take(TipDirectionAttempts))
        {
            if (!candidate.Clear) continue;
            if (TryRouteFromJunction(tip, candidate.End, branchTipDiameter, trunkTipDiameter,
                    tipMemberLength, offGrid, state, out _)) return true;
        }
        reason = gridReason;
        return false;
    }

    /// <summary>
    /// Routes from one junction. Two plans are worked out without touching the graph: joining
    /// an existing trunk, and a support of the tip's own (a straight drop, a snapped drop line,
    /// or one branch to a fresh trunk). With the existing-trunk preference on, grid mode joins
    /// the existing trunk when its branch is at most half a grid pitch longer than the fresh
    /// route's (user decision 2026-09-07: a branch should not reach far past a nearer free
    /// base); with the preference off an existing trunk is only a last resort. Free mode never
    /// joins another support.
    /// </summary>
    private bool TryRouteFromJunction(RoutingTip tip, Vector3 branchJunction,
        float branchTipDiameter, float trunkTipDiameter, float tipMemberLength,
        TreeRoutingOptions options, RouteState state, out RoutingFailureReason reason)
    {
        var separationRejections = state.SeparationRejections;
        reason = RoutingFailureReason.NoClearStep;
        var snap = tipMemberLength * JunctionSnapMemberFraction;

        var minBranchOffset = options.BranchDiameter * 0.5f * MinBranchOffsetBranchRadii;
        var existing = (options.UseBaseGrid || options.ShareTrunks) && branchJunction.Z > options.PlateZ + Epsilon
            ? FindTrunkAttachment(tip, branchJunction, options, state, minBranchOffset)
            : null;

        // A tip connected directly to a trunk (or directly to its base near the plate) tapers
        // from the trunk setting, independently of BranchDiameter: the same direction must
        // also be clear for a member of that diameter.
        var trunkMemberRadius = MathF.Max(trunkTipDiameter, options.TrunkDiameter) * 0.5f;
        var trunkJunction = MathF.Abs(trunkTipDiameter - branchTipDiameter) <= Epsilon &&
                            MathF.Abs(options.TrunkDiameter - options.BranchDiameter) <= Epsilon ||
                            (!TipMemberHitsSupports(tip, branchJunction, trunkMemberRadius, state,
                                 null) &&
                             !state.ViolatesMemberSeparation(tip.SurfacePoint, branchJunction,
                                 trunkMemberRadius))
            ? branchJunction
            : (Vector3?)null;

        Action? own = null;
        var ownLength = float.PositiveInfinity;
        if (trunkJunction is { } trunkJ1)
        {
            if (trunkJ1.Z <= options.PlateZ + Epsilon)
            {
                var baseJunction = FindTipBaseJunction(tip, options, state,
                    trunkTipDiameter, tipMemberLength);
                if (baseJunction is { } baseJ1)
                {
                    own = () => EmitSupport(tip,
                        new Vector3(baseJ1.X, baseJ1.Y, options.PlateZ), null,
                        tipOnly: true, options, state, trunkTipDiameter);
                    ownLength = 0f;
                }
            }
            else if ((!options.UseBaseGrid || IsOnBaseGrid(trunkJ1, options)) &&
                     TrunkIsClear(trunkJ1, options, state))
            {
                own = () => EmitSupport(tip, trunkJ1, null, tipOnly: false,
                    options, state, trunkTipDiameter);
                ownLength = 0f;
            }
            else if (options.UseBaseGrid &&
                     FindGridDropLineSnap(tip, trunkJ1, trunkTipDiameter, tipMemberLength,
                         snap, options, state) is { } snapped)
            {
                own = () => EmitSupport(tip, snapped, null, tipOnly: false,
                    options, state, trunkTipDiameter);
                ownLength = 0f;
            }
        }

        // The straight candidate was handled with trunk-derived tip geometry above. Every
        // remaining candidate introduces a branch, so both it and its tip use branch settings.
        var trunkTops = own is null
            ? TrunkTopCandidates(branchJunction, options, state.AngleOffset,
                TipMemberDirection(tip.SurfacePoint, branchJunction), minBranchOffset).ToList()
            : [];
        foreach (var candidate in trunkTops)
        {
            var trunkTop = candidate.Top;
            if (Vector2.DistanceSquared(new(branchJunction.X, branchJunction.Y),
                    new(trunkTop.X, trunkTop.Y)) <= Epsilon * Epsilon) continue;
            if (!BranchIsClear(branchJunction, trunkTop, options, state))
                continue;
            if (!TrunkIsClear(trunkTop, options, state)) continue;
            if (state.ProposedMembersViolateSeparation(tip.SurfacePoint,
                    branchJunction, branchTipDiameter * 0.5f, trunkTop,
                    new Vector3(trunkTop.X, trunkTop.Y, options.PlateZ),
                    options.TrunkDiameter * 0.5f)) continue;
            own = () => EmitSupport(tip, trunkTop, branchJunction, tipOnly: false,
                options, state, branchTipDiameter);
            ownLength = candidate.Length;
            break;
        }

        // Nearer base beats farther trunk (user, 2026-09-07): a join wins only within half a
        // pitch of a fresh trunk's cost. Parenting (ShareTrunks) exists to cut trunks, so there
        // any join within the search range wins.
        var allowance = options.ShareTrunks
            ? options.ExistingTrunkBranchRange
            : options.PreferExistingTrunks
                ? options.BaseGridPitch * 0.5f
                : float.NegativeInfinity;
        if (existing is { } join &&
            (own is null || join.Length <= ownLength + allowance))
        {
            EmitTrunkAttachment(tip, branchJunction, join, options, state, branchTipDiameter);
            return true;
        }
        if (own is not null)
        {
            own();
            return true;
        }

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

    private Vector3? FindTipBaseJunction(RoutingTip tip, TreeRoutingOptions options,
        RouteState state, float tipMemberDiameter, float tipMemberLength)
    {
        if (!options.UseBaseGrid)
        {
            foreach (var candidate in TipJunctionCandidates(tip, options, state,
                         tipMemberDiameter, tipMemberDiameter, tipMemberLength,
                         includeBaseRelocationFan: true)
                         .Where(candidate => candidate.Clear).Select(candidate => candidate.End))
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
            if (state.HitsGenerated(tip.SurfacePoint, candidate, tipMemberDiameter * 0.5f))
                continue;
            if (state.ViolatesMemberSeparation(tip.SurfacePoint, candidate,
                    tipMemberDiameter * 0.5f)) continue;
            if (BaseIsClear(candidate, options, state)) return candidate;
        }
        return null;
    }

    /// <summary>
    /// Junction candidates for a full-length tip member, in direction order, each flagged with
    /// whether the member is clear. The cone is as wide as the ball it grows from, so other
    /// supports are kept clear of that base radius, not of the member's nominal neck; a contact
    /// that cannot take a whole cone is refused rather than given a stub (user screen test
    /// 2026-09-07: stubs piled up on one ball as a fan). Contacts blocked by the model itself
    /// are not offered at all.
    /// </summary>
    private IEnumerable<(Vector3 End, bool Clear)> TipJunctionCandidates(RoutingTip tip,
        TreeRoutingOptions options, RouteState state, float tipMemberDiameter,
        float parentDiameter, float tipMemberLength, bool includeBaseRelocationFan = false)
    {
        var contactRadius = MathF.Max(0.025f, tip.TipDiameter * 0.5f) + state.Clearance.ModelDistance;
        var bodyRadius = TipBodyRadius(tipMemberDiameter, parentDiameter);
        var directions = includeBaseRelocationFan
            ? TipToBaseDirections(tip, options, state.AngleOffset)
            : TipDirections(tip, options, state.AngleOffset);
        foreach (var direction in directions)
        {
            var length = direction.Z < -Epsilon
                ? MathF.Min(tipMemberLength, (tip.SurfacePoint.Z - options.PlateZ) / -direction.Z)
                : tipMemberLength;
            var end = tip.SurfacePoint + direction * length;
            if (!ContactMemberIsClear(tip.SurfacePoint, end, contactRadius)) continue;
            var clear = !TipMemberHitsSupports(tip, end, bodyRadius, state, null) &&
                        !state.ViolatesMemberSeparation(tip.SurfacePoint, end, bodyRadius);
            yield return (end, clear);
        }
    }

    /// <summary>Lands the cone on the nearest trunk whose axis passes within snapping distance of its junction.</summary>
    private bool TrySnapOntoNearTrunk(RoutingTip tip, Vector3 j1, float trunkTipDiameter,
        float tipMemberLength, TreeRoutingOptions options, RouteState state)
    {
        var snap = tipMemberLength * JunctionSnapMemberFraction;
        foreach (var trunk in state.Trunks
                     .Where(trunk => Vector2.Distance(new(j1.X, j1.Y), trunk.Xy) <= snap)
                     .OrderBy(trunk => Vector2.DistanceSquared(new(j1.X, j1.Y), trunk.Xy))
                     .ThenBy(trunk => trunk.BaseNodeId))
            if (TrySnapTipOntoTrunk(tip, trunk, trunkTipDiameter, tipMemberLength, options, state))
                return true;
        return false;
    }

    /// <summary>The radius a tip member occupies: its cone reaches the radius of the ball it grows from.</summary>
    private static float TipBodyRadius(float tipMemberDiameter, float parentDiameter)
        => MathF.Max(tipMemberDiameter, parentDiameter) * 0.5f;

    /// <summary>
    /// Grid mode, junction just off a drop line: rather than a stub branch to the lattice point,
    /// the cone is re-aimed at the drop line (same member length) and the trunk drops straight
    /// from there. Returns the re-aimed junction, or null when no such line is usable.
    /// </summary>
    private Vector3? FindGridDropLineSnap(RoutingTip tip, Vector3 j1, float tipMemberDiameter,
        float tipMemberLength, float snap, TreeRoutingOptions options, RouteState state)
    {
        foreach (var xy in BaseLattice.NearestSquarePoints(new Vector2(j1.X, j1.Y),
                     options.BaseGridPitch, snap))
        {
            if (SnappedJunction(tip, xy, tipMemberLength, options) is not { } snapped) continue;
            if (!TipMemberIsClear(tip, snapped, tipMemberDiameter, options.TrunkDiameter, state,
                    null)) continue;
            if (!TrunkIsClear(snapped, options, state)) continue;
            return snapped;
        }
        return null;
    }

    /// <summary>
    /// Where a tip member of the configured length ends when aimed from its contact at the
    /// vertical line through <paramref name="axis"/>; null when that would exceed the member
    /// angle or reach below the base.
    /// </summary>
    private static Vector3? SnappedJunction(RoutingTip tip, Vector2 axis, float tipMemberLength,
        TreeRoutingOptions options)
    {
        var horizontal = Vector2.Distance(new(tip.SurfacePoint.X, tip.SurfacePoint.Y), axis);
        var maxAngle = options.MaxMemberAngleDegrees * MathF.PI / 180f;
        if (horizontal > tipMemberLength * MathF.Sin(maxAngle) + Epsilon) return null;
        var drop = MathF.Sqrt(MathF.Max(0f,
            tipMemberLength * tipMemberLength - horizontal * horizontal));
        var z = tip.SurfacePoint.Z - drop;
        if (z <= options.PlateZ + options.BaseHeight + Epsilon) return null;
        return new Vector3(axis.X, axis.Y, z);
    }

    private bool TipMemberIsClear(RoutingTip tip, Vector3 junction, float tipMemberDiameter,
        float parentDiameter, RouteState state, IReadOnlyCollection<Guid>? excludeSegments)
    {
        var contactRadius = MathF.Max(0.025f, tip.TipDiameter * 0.5f) + state.Clearance.ModelDistance;
        var bodyRadius = TipBodyRadius(tipMemberDiameter, parentDiameter);
        return ContactMemberIsClear(tip.SurfacePoint, junction, contactRadius) &&
               !TipMemberHitsSupports(tip, junction, bodyRadius, state, excludeSegments) &&
               !state.ViolatesMemberSeparation(tip.SurfacePoint, junction, bodyRadius,
                   null, null, excludeSegments);
    }

    /// <summary>
    /// Whether a cone from the contact to <paramref name="junction"/> runs into another support.
    /// The cone is checked as the frustum it is, in two halves, and may touch a neighbour:
    /// supports that meet simply fuse, so the model clearance does not apply between them
    /// (user screen test 2026-09-07: a manual cone between two generated ones was refused).
    /// </summary>
    private static bool TipMemberHitsSupports(RoutingTip tip, Vector3 junction, float baseRadius,
        RouteState state, IReadOnlyCollection<Guid>? excludeSegments)
    {
        var contactRadius = MathF.Max(0.025f, tip.TipDiameter * 0.5f);
        var mid = (tip.SurfacePoint + junction) * 0.5f;
        return state.HitsGenerated(tip.SurfacePoint, mid, (contactRadius + baseRadius) * 0.5f,
                   excludeSegments) ||
               state.HitsGenerated(mid, junction, baseRadius, excludeSegments);
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

        // The cone points along the normal, or at the member angle when the normal is steeper
        // (user decision 2026-09-07). When that is blocked, fully vertical is always acceptable.
        yield return -Vector3.UnitZ;

        // Only then swing around the vertical at the clamped angle, as a last resort.
        for (var index = 1; index < options.BranchDirections; index++)
        {
            var theta = angleOffset + index * MathF.Tau / options.BranchDirections;
            var rotated = new Vector2(MathF.Cos(theta), MathF.Sin(theta));
            yield return Direction(rotated, angle);
        }

        static Vector3 Direction(Vector2 lateral, float angle) => Vector3.Normalize(
            new Vector3(lateral * MathF.Sin(angle), -MathF.Cos(angle)));
    }

    /// <summary>Unit direction a tip member travels from its contact to its junction.</summary>
    private static Vector3 TipMemberDirection(Vector3 contact, Vector3 junction)
    {
        var delta = junction - contact;
        return delta.LengthSquared() > Epsilon * Epsilon ? Vector3.Normalize(delta) : -Vector3.UnitZ;
    }

    /// <summary>
    /// The bend at a joint, in degrees: the angle between the direction one member arrives and the
    /// direction the next member leaves. Zero means the members are collinear.
    /// </summary>
    internal static float BendDegrees(Vector3 incoming, Vector3 outgoing)
    {
        if (incoming.LengthSquared() <= Epsilon * Epsilon ||
            outgoing.LengthSquared() <= Epsilon * Epsilon) return 0f;
        var dot = Vector3.Dot(Vector3.Normalize(incoming), Vector3.Normalize(outgoing));
        return MathF.Acos(Math.Clamp(dot, -1f, 1f)) * 180f / MathF.PI;
    }

    /// <summary>
    /// One member-angle branch descending from the junction onto an earlier trunk of this run,
    /// found without touching the graph. The bend at the ball between the cone tip and the
    /// branch may not exceed the member angle, so a branch never doubles back on the cone it
    /// grows from. A trunk whose top is too low for a member-angle branch first offers a
    /// shallower branch at its top; only when that is refused is the trunk raised, by a new
    /// segment above its top, to meet a member-angle branch. A trunk closer than
    /// <paramref name="minOffset"/> never gets a branch: that would be a stub. Nearest trunk
    /// first.
    /// </summary>
    private ExistingTrunkCandidate? FindTrunkAttachment(RoutingTip tip, Vector3 j1,
        TreeRoutingOptions options, RouteState state, float minOffset)
    {
        var branchRule = _rules.Find<BranchGrowthRule>();
        var maxBranches = branchRule is { Enabled: true } ? branchRule.MaxBranchesPerTrunk : int.MaxValue;
        var maxAngle = options.MaxMemberAngleDegrees * MathF.PI / 180f;
        var tanAngle = MathF.Tan(maxAngle);
        var tipDirection = TipMemberDirection(tip.SurfacePoint, j1);
        var candidates = new List<ExistingTrunkCandidate>();
        foreach (var trunk in state.Trunks)
        {
            var hDist = Vector2.Distance(new(j1.X, j1.Y), trunk.Xy);
            if (hDist <= minOffset) continue;
            if (trunk.BranchCount >= maxBranches) continue;
            // A hair steeper than the exact member angle, so float rounding in the lean rule
            // can never clamp (and thereby reject) a nominally-exact 45° branch.
            var highestAngleLimitedZ = j1.Z -
                                       (tanAngle > Epsilon ? hDist / tanAngle : 0f) - 1e-3f;
            // The highest point the trunk offers within the angle is the shortest viable branch.
            AddCandidate(trunk, MathF.Min(highestAngleLimitedZ, trunk.TopZ), raisesTrunk: false);
            if (highestAngleLimitedZ > trunk.TopZ + Epsilon)
                AddCandidate(trunk, highestAngleLimitedZ, raisesTrunk: true);
        }

        foreach (var candidate in candidates.OrderBy(item => item.RaisesTrunk ? 1 : 0)
                     .ThenBy(item => item.Score).ThenBy(item => item.Length)
                     .ThenBy(item => item.Trunk.BaseNodeId))
        {
            var trunk = candidate.Trunk;
            var attach = candidate.Attach;
            var attachZ = attach.Z;
            if (candidate.RaisesTrunk && !TrunkRaiseIsClear(trunk, attachZ, state)) continue;
            // The spec's member angle governs branch geometry here; the lean rule's step-router
            // clamps (including its tighter near-tip angle) do not apply to tree anatomy.
            var branchRadius = options.BranchDiameter * 0.5f;
            if (!state.Clearance.PillarIsClear(_obstacles, j1, attach, branchRadius,
                    Excluding(trunk.SegmentIds.Concat(trunk.BranchSegmentIds)))) continue;
            if (state.HitsGeneratedForTrunkAttachment(j1, attach, branchRadius, trunk,
                    SiblingBranchFusionDistance,
                    options.BranchDiameter * ProjectedBranchClearanceDiameters)) continue;
            var attachesAtTop = MathF.Abs(attachZ - trunk.TopZ) <= 1e-3f;
            if (candidate.RaisesTrunk)
            {
                // The raised section will join the branch at the new top: the trunk's own
                // segments are not separate members to keep clear of.
                if (state.ViolatesMemberSeparation(j1, attach, branchRadius,
                        null, null, trunk.SegmentIds)) continue;
            }
            else
            {
                var targetSegment = trunk.SegmentCovering(attachZ, state.Graph).Segment;
                var sharedNode = attachesAtTop ? trunk.TopNodeId : (Guid?)null;
                if (state.ViolatesMemberSeparation(j1, attach, branchRadius,
                        null, sharedNode, [targetSegment.Id])) continue;
            }
            return candidate;
        }
        return null;

        void AddCandidate(TrunkRecord trunk, float attachZ, bool raisesTrunk)
        {
            if (attachZ < options.PlateZ + options.BaseHeight + Epsilon) return;
            var attach = new Vector3(trunk.Xy.X, trunk.Xy.Y, attachZ);
            var branchLength = Vector3.Distance(j1, attach);
            if (branchLength > options.ExistingTrunkBranchRange + Epsilon) return;
            var bend = BendDegrees(tipDirection, attach - j1);
            if (bend > options.MaxMemberAngleDegrees + BendToleranceDegrees) return;
            // Among reachable trunks, the one the cone already points toward wins near ties.
            var bendPenalty = options.MaxMemberAngleDegrees > 0
                ? bend / options.MaxMemberAngleDegrees * options.BranchDiameter *
                  BranchDirectionPreferenceDiameters
                : 0f;
            candidates.Add(new ExistingTrunkCandidate(trunk, attach, branchLength,
                branchLength + bendPenalty, raisesTrunk));
        }
    }

    /// <summary>Emits a validated <see cref="FindTrunkAttachment"/> plan: the tip member, its branch, and any trunk split or raise.</summary>
    private void EmitTrunkAttachment(RoutingTip tip, Vector3 j1, ExistingTrunkCandidate candidate,
        TreeRoutingOptions options, RouteState state, float tipMemberDiameter)
    {
        var trunk = candidate.Trunk;
        var attachZ = candidate.Attach.Z;
        var attachNode = candidate.RaisesTrunk
            ? RaiseTrunk(trunk, attachZ, options, state)
            : MathF.Abs(attachZ - trunk.TopZ) <= 1e-3f
                ? state.Graph.GetNode(trunk.TopNodeId)
                : SplitTrunk(trunk, attachZ, state);
        var tipNode = EmitTipMember(tip, j1, options, state, tipMemberDiameter,
            options.BranchDiameter);
        var branch = state.AddSegment(SupportSegmentType.Branch, tipNode.Junction, attachNode,
            options.BranchDiameter, options.Origin);
        trunk.BranchSegmentIds.Add(branch.Id);
        trunk.BranchCount++;
    }

    /// <summary>
    /// Lands a cone directly on a trunk whose axis passes within snapping distance of the cone's
    /// junction: the member is re-aimed at the axis with its configured length, the trunk is
    /// split there, or raised to meet it when its top is lower. The tip then tapers to the
    /// trunk's own ball. A trunk top already carrying a cone is never shared.
    /// </summary>
    private bool TrySnapTipOntoTrunk(RoutingTip tip, TrunkRecord trunk, float tipMemberDiameter,
        float tipMemberLength, TreeRoutingOptions options, RouteState state)
    {
        if (SnappedJunction(tip, trunk.Xy, tipMemberLength, options) is not { } junction)
            return false;
        var trunkDiameter = trunk.SegmentCovering(trunk.TopZ, state.Graph).Segment.Diameter;
        if (!TipMemberIsClear(tip, junction, tipMemberDiameter, trunkDiameter, state,
                trunk.SegmentIds)) return false;

        SupportNode attachNode;
        if (junction.Z > trunk.TopZ + 1e-3f)
        {
            if (!TrunkRaiseIsClear(trunk, junction.Z, state)) return false;
            attachNode = RaiseTrunk(trunk, junction.Z, options, state);
        }
        else if (MathF.Abs(junction.Z - trunk.TopZ) <= 1e-3f)
        {
            attachNode = state.Graph.GetNode(trunk.TopNodeId);
            if (state.Graph.SegmentsAt(attachNode.Id)
                .Any(segment => segment.Type == SupportSegmentType.Tip)) return false;
        }
        else
        {
            attachNode = SplitTrunk(trunk, junction.Z, state);
        }

        var contact = state.NewNode(SupportNodeType.Tip, tip.SurfacePoint, options.Origin);
        RoutingUtilities.ApplyContact(contact, tip);
        ClampLeadInToClearPath(contact, junction);
        state.Graph.AddNode(contact);
        state.AddTipMember(contact, attachNode, tipMemberDiameter, options.Origin,
            TipBodyRadius(tipMemberDiameter, trunkDiameter));
        return true;
    }

    /// <summary>
    /// A trunk may be raised only through clear space: the model, other supports and the
    /// separation rule all apply to the new section. The trunk's own branches meet it at its
    /// current top and are not obstacles; anything else standing on that top (a cone tip fed
    /// straight by the trunk) is, so such a trunk is never raised into its own tip.
    /// </summary>
    private bool TrunkRaiseIsClear(TrunkRecord trunk, float newTopZ, RouteState state)
    {
        var oldTop = state.Graph.GetNode(trunk.TopNodeId).Position;
        var newTop = new Vector3(trunk.Xy.X, trunk.Xy.Y, newTopZ);
        var radius = trunk.SegmentCovering(trunk.TopZ, state.Graph).Segment.Diameter * 0.5f;
        var own = trunk.SegmentIds.Concat(trunk.BranchSegmentIds).ToList();
        return state.Clearance.PillarIsClear(_obstacles, oldTop, newTop, radius, Excluding(own)) &&
               !state.HitsGenerated(oldTop, newTop, radius, own) &&
               !state.ViolatesMemberSeparation(oldTop, newTop, radius, trunk.TopNodeId, null, own);
    }

    /// <summary>Extends a trunk above its top with a new vertical segment ending at a new top junction.</summary>
    private static SupportNode RaiseTrunk(TrunkRecord trunk, float newTopZ,
        TreeRoutingOptions options, RouteState state)
    {
        var oldTop = state.Graph.GetNode(trunk.TopNodeId);
        var diameter = trunk.SegmentCovering(trunk.TopZ, state.Graph).Segment.Diameter;
        var newTop = state.NewNode(SupportNodeType.Junction,
            new Vector3(trunk.Xy.X, trunk.Xy.Y, newTopZ), options.Origin);
        state.Graph.AddNode(newTop);
        var extension = state.AddSegment(SupportSegmentType.Trunk, newTop, oldTop, diameter,
            options.Origin);
        trunk.Raise(newTop, extension.Id);
        return newTop;
    }

    /// <summary>
    /// Grid mode uses reachable plate-origin square-grid drop lines, nearest first. Free mode
    /// restores the deterministic branch fan used before bases were constrained to a grid.
    /// </summary>
    private static IEnumerable<TrunkTopCandidate> TrunkTopCandidates(Vector3 j1,
        TreeRoutingOptions options, float angleOffset, Vector3? tipDirection = null,
        float minOffset = 0f)
    {
        // With a tip direction, the bend at the ball is limited to the member angle and the
        // branch that simply continues the cone's own axis is offered first at every length.
        var maxBend = options.MaxMemberAngleDegrees + BendToleranceDegrees;
        var continuation = tipDirection is { } direction && direction.Z < -Epsilon &&
                           new Vector2(direction.X, direction.Y).LengthSquared() >
                           Epsilon * Epsilon
            ? direction
            : (Vector3?)null;
        if (!options.UseBaseGrid)
        {
            yield return new TrunkTopCandidate(j1, 0f, 0f, Bend(-Vector3.UnitZ));
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
                if (continuation is { } along)
                {
                    var end = j1 + along * length;
                    if (end.Z > options.PlateZ + options.BaseHeight + Epsilon)
                        yield return new TrunkTopCandidate(end, length, LeanDegrees(along), 0f);
                }
                foreach (var angleDegrees in angles)
                {
                    var freeAngle = angleDegrees * MathF.PI / 180f;
                    for (var index = 0; index < options.BranchDirections; index++)
                    {
                        var theta = angleOffset + index * MathF.Tau / options.BranchDirections;
                        var fanDirection = new Vector3(
                            MathF.Cos(theta) * MathF.Sin(freeAngle),
                            MathF.Sin(theta) * MathF.Sin(freeAngle),
                            -MathF.Cos(freeAngle));
                        var bend = Bend(fanDirection);
                        if (bend > maxBend) continue;
                        var end = j1 + fanDirection * length;
                        if (end.Z > options.PlateZ + options.BaseHeight + Epsilon)
                            yield return new TrunkTopCandidate(end, length, angleDegrees, bend);
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
            // A branch to a drop line this close would be shorter than its own ball.
            if (horizontal <= minOffset) continue;
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
                var bend = Bend(top - j1);
                if (bend > maxBend) continue;
                if (top.Z > options.PlateZ + options.BaseHeight + Epsilon)
                    candidates.Add(new TrunkTopCandidate(top, length, candidateDegrees, bend));
            }
        }
        foreach (var candidate in candidates.OrderBy(item => item.Length)
                     .ThenBy(item => item.BendDegrees)
                     .ThenBy(item => item.AngleDegrees)
                     .ThenBy(item => item.Top.X).ThenBy(item => item.Top.Y))
            yield return candidate;

        float Bend(Vector3 branchDirection) => tipDirection is { } incoming
            ? BendDegrees(incoming, branchDirection)
            : 0f;

        static float LeanDegrees(Vector3 direction) => MathF.Atan2(
            new Vector2(direction.X, direction.Y).Length(), MathF.Abs(direction.Z)) * 180f / MathF.PI;
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
        // Supports that meet fuse; the model clearance is not a gap between members. The
        // optional member-separation rule is the one that keeps members apart.
        return !state.HitsGenerated(start, end, physicalRadius, excludeSegments) &&
               !state.ViolatesMemberSeparation(start, end, physicalRadius,
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
        TreeRoutingOptions options, RouteState state, float tipMemberDiameter,
        float parentDiameter)
    {
        var contact = state.NewNode(SupportNodeType.Tip, tip.SurfacePoint, options.Origin);
        RoutingUtilities.ApplyContact(contact, tip);
        ClampLeadInToClearPath(contact, j1);
        state.Graph.AddNode(contact);
        var junction = state.NewNode(SupportNodeType.Junction, j1, options.Origin);
        state.Graph.AddNode(junction);
        state.AddTipMember(contact, junction, tipMemberDiameter, options.Origin,
            TipBodyRadius(tipMemberDiameter, parentDiameter));
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
            state.AddTipMember(contact, baseNode, tipMemberDiameter, options.Origin,
                tipMemberDiameter * 0.5f);
            return;
        }

        var (_, junction) = EmitTipMember(tip, branchFrom ?? trunkTop, options, state,
            tipMemberDiameter, branchFrom is null ? options.TrunkDiameter : options.BranchDiameter);
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
        public float TopZ { get; private set; }
        public Guid TopNodeId { get; private set; }
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

        /// <summary>Records a new segment above the old top, ending at <paramref name="newTop"/>.</summary>
        public void Raise(SupportNode newTop, Guid segmentId)
        {
            TopZ = newTop.Position.Z;
            TopNodeId = newTop.Id;
            SegmentIds.Add(segmentId);
        }

        public void ReplaceSegment(Guid old, Guid upper, Guid lower)
        {
            SegmentIds.Remove(old);
            SegmentIds.Add(upper);
            SegmentIds.Add(lower);
        }
    }

    private readonly record struct ExistingTrunkCandidate(TrunkRecord Trunk, Vector3 Attach,
        float Length, float Score, bool RaisesTrunk = false);

    private readonly record struct TrunkTopCandidate(Vector3 Top, float Length,
        float AngleDegrees, float BendDegrees);

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
        private readonly float _minimumMemberSeparation;
        public int SeparationRejections { get; private set; }

        /// <summary>
        /// Whether members see one another at all. Off in free mode, where every support is
        /// routed as if alone: no collision, separation or near-pass check against members.
        /// </summary>
        private readonly bool _seesOtherSupports;

        public RouteState(SupportGraph graph, DeterministicIds ids, RoutingClearance clearance,
            float angleOffset, float minimumMemberSeparation, bool seesOtherSupports = true)
        {
            Graph = graph;
            _ids = ids;
            Clearance = clearance;
            AngleOffset = angleOffset;
            _minimumMemberSeparation = minimumMemberSeparation;
            _seesOtherSupports = seesOtherSupports;
        }

        /// <summary>
        /// Reconstructs the same trunk bookkeeping produced during a fresh route.
        /// Disabled geometry is not load-bearing; hidden geometry remains printable and active.
        /// </summary>
        public void SeedExistingContext()
        {
            foreach (var segment in Graph.Segments.Where(segment => !segment.Disabled))
            {
                var a = Graph.GetNode(segment.NodeA);
                var b = Graph.GetNode(segment.NodeB);
                if (a.Disabled || b.Disabled) continue;
                // An existing cone tip occupies its frustum up to the ball it grows from, exactly
                // as a freshly routed one does; seeding it at its neck let new cones crowd it.
                if (SupportSliceGeometry.TryConeTip(a, b, out var tip, out var other))
                {
                    AddTipCapsules(tip, other, segment.Id,
                        MathF.Max(0.025f, tip.TipDiameter * 0.5f),
                        MathF.Max(segment.Diameter,
                            SupportSliceGeometry.TipJunctionDiameter(Graph, segment)) * 0.5f);
                    continue;
                }
                _capsules.Add(new GeneratedCapsule(a.Position, b.Position,
                    segment.Diameter * 0.5f, segment.Id, segment.Type,
                    segment.NodeA, segment.NodeB));
            }

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

                var top = nodeIds.Select(Graph.GetNode)
                    .OrderByDescending(node => node.Position.Z).ThenBy(node => node.Id).First();
                var branches = Graph.Segments
                    .Where(segment => !segment.Disabled && segment.Type == SupportSegmentType.Branch &&
                        (nodeIds.Contains(segment.NodeA) || nodeIds.Contains(segment.NodeB)))
                    .Select(segment => segment.Id).Distinct().ToList();
                Trunks.Add(new TrunkRecord(new Vector2(top.Position.X, top.Position.Y),
                    top.Position.Z, top.Id, baseNode.Id, segmentIds, branches, branches.Count, this));
            }
        }

        public SupportNode NewNode(SupportNodeType type, Vector3 position, SupportOrigin origin)
            => new() { Id = NextUnusedId(), Type = type, Position = position, Origin = origin };

        /// <summary>
        /// Adds a cone tip member from <paramref name="contact"/> to <paramref name="junction"/>.
        /// Later members must keep clear of the cone's real envelope: the frustum from the
        /// contact radius to <paramref name="baseRadius"/>, recorded as two capsules.
        /// </summary>
        public SupportSegment AddTipMember(SupportNode contact, SupportNode junction,
            float diameter, SupportOrigin origin, float baseRadius)
        {
            var segment = new SupportSegment
            {
                Id = NextUnusedId(), Type = SupportSegmentType.Tip, NodeA = contact.Id,
                NodeB = junction.Id, Diameter = diameter, Origin = origin,
            };
            Graph.AddSegment(segment);
            AddTipCapsules(contact, junction, segment.Id,
                MathF.Max(0.025f, contact.TipDiameter * 0.5f), baseRadius);
            var delta = junction.Position - contact.Position;
            var lean = MathF.Atan2(new Vector2(delta.X, delta.Y).Length(), MathF.Abs(delta.Z))
                * 180 / MathF.PI;
            if (delta.LengthSquared() > Epsilon * Epsilon) MaxLean = MathF.Max(MaxLean, lean);
            return segment;
        }

        private void AddTipCapsules(SupportNode contact, SupportNode junction, Guid segmentId,
            float contactRadius, float baseRadius)
        {
            var mid = (contact.Position + junction.Position) * 0.5f;
            _capsules.Add(new GeneratedCapsule(contact.Position, mid,
                (contactRadius + baseRadius) * 0.5f, segmentId, SupportSegmentType.Tip,
                contact.Id, junction.Id));
            _capsules.Add(new GeneratedCapsule(mid, junction.Position, baseRadius, segmentId,
                SupportSegmentType.Tip, contact.Id, junction.Id));
        }

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
            if (!_seesOtherSupports) return false;
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

        public bool ViolatesMemberSeparation(Vector3 start, Vector3 end, float radius,
            Guid? nodeA = null, Guid? nodeB = null,
            IReadOnlyCollection<Guid>? excludeSegments = null)
        {
            if (_minimumMemberSeparation <= 0 || !_seesOtherSupports) return false;
            foreach (var member in _capsules.OrderBy(item => item.SegmentId))
            {
                if (excludeSegments is not null && excludeSegments.Contains(member.SegmentId))
                    continue;
                if (!MemberSeparation.AreTooClose(start, end, radius, nodeA, nodeB,
                        member.Start, member.End, member.Radius, member.NodeA, member.NodeB,
                        _minimumMemberSeparation)) continue;
                SeparationRejections++;
                return true;
            }
            return false;
        }

        public bool ProposedMembersViolateSeparation(Vector3 firstStart, Vector3 firstEnd,
            float firstRadius, Vector3 secondStart, Vector3 secondEnd, float secondRadius)
        {
            if (_minimumMemberSeparation <= 0 ||
                !MemberSeparation.AreTooClose(firstStart, firstEnd, firstRadius, null, null,
                    secondStart, secondEnd, secondRadius, null, null,
                    _minimumMemberSeparation)) return false;
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
            if (!_seesOtherSupports) return false;
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
            if (!_seesOtherSupports) return false;
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
