using System.Numerics;
using Danslicer.Core.Config;
using Danslicer.Core.Supports.Routing;

namespace Danslicer.Core.Supports;

/// <summary>Plans a central vertical spine before routing any branches. No branch can host another branch.</summary>
public static class CandelabraParenting
{
    private sealed record Contact(RoutingTip Tip, Vector3 End, Vector3 Axis);

    public static (SupportGraphEdit Edit, IReadOnlyList<RoutingTip> Refused) Build(
        IReadOnlyList<RoutingTip> tips, SupportConfig settings, ICollisionScene obstacles, SupportOrigin origin,
        Vector2? trunkCentre = null, Action<string>? reportConstraint = null,
        IReadOnlyDictionary<Vector3, Vector3>? contactEnds = null,
        Action<RoutingTip, string>? reportTipConstraint = null)
    {
        var nodes = new List<SupportNode>();
        var segments = new List<SupportSegment>();
        var refused = new List<RoutingTip>();
        var members = new LinearCollisionScene();
        var scene = new CompositeCollisionScene(obstacles, members);
        var width = MathF.Max(0.1f, settings.CandelabraGroupWidthMm);
        var angle = Math.Clamp(settings.ParentingMaxBranchAngle > 0
            ? settings.ParentingMaxBranchAngle : settings.MemberAngleDegrees, 1f, 89f) * MathF.PI / 180f;
        var maxLength = settings.ParentingMaxBranchLength > 0 ? settings.ParentingMaxBranchLength
            : width * 2;
        var bend = (settings.ParentingMaxConeBend > 0 ? settings.ParentingMaxConeBend : settings.MemberAngleDegrees)
            * MathF.PI / 180f;
        var floor = MathF.Max(settings.BaseHeight + 0.1f, settings.MinBranchAttachHeightMm);
        var radius = settings.BranchDiameter / 2;
        var trunkRadius = settings.TrunkDiameter / 2;
        var clearance = MathF.Max(0, settings.MinMemberSeparationMm);
        var contacts = new List<Contact>();
        var routeCache = new Dictionary<(Contact, Vector2, float), (Vector3 Attach, string Failure)>();
        foreach (var tip in tips.OrderBy(t => t.SurfacePoint.X).ThenBy(t => t.SurfacePoint.Y).ThenBy(t => t.SurfacePoint.Z))
        {
            Contact? contact = null;
            var hasEndpointAboveFloor = false;
            var ends = HierarchicalParenting.ConeDirections(tip)
                .Select(direction => tip.SurfacePoint + direction * settings.TipMemberLength);
            if (contactEnds is not null && contactEnds.TryGetValue(tip.SurfacePoint, out var existingEnd))
                ends = new[] { existingEnd }.Concat(ends);
            foreach (var end in ends)
            {
                if (Vector3.DistanceSquared(end, tip.SurfacePoint) < 1e-8f) continue;
                var direction = Vector3.Normalize(end - tip.SurfacePoint);
                var start = tip.SurfacePoint + direction * (MathF.Max(tip.TipDiameter, 0.05f) + radius * 1.5f);
                if (end.Z < floor) continue;
                hasEndpointAboveFloor = true;
                if (scene.IntersectsCapsule(start, end, radius + clearance)) continue;
                contact = new Contact(tip, end, direction);
                break;
            }
            if (contact is null)
            {
                refused.Add(tip);
                var reason = hasEndpointAboveFloor ? "The tested contact-cone routes hit the model or a retained support."
                    : $"Contact-cone endpoints fall below the {floor:0.##} mm attachment floor.";
                reportConstraint?.Invoke(reason); reportTipConstraint?.Invoke(tip, reason);
            }
            else contacts.Add(contact);
        }

        if (trunkCentre is not null && refused.Count > 0)
            return (new SupportGraphEdit([], [], []), tips);
        // Spatial subdivision keeps long rows balanced and bounds planning cost for dense selections.
        if (trunkCentre is not null) Route(contacts);
        else RouteReachable(contacts);
        return (new SupportGraphEdit(nodes, segments, []), refused);

        void RouteReachable(List<Contact> remaining)
        {
            var verticals = remaining.ToDictionary(c => c, c =>
            {
                var end = c.Tip.SurfacePoint - Vector3.UnitZ * settings.TipMemberLength;
                var start = c.Tip.SurfacePoint - Vector3.UnitZ * (MathF.Max(c.Tip.TipDiameter, 0.05f) + radius * 1.5f);
                return end.Z >= floor && !obstacles.IntersectsCapsule(start, end, radius + clearance)
                    ? new Contact(c.Tip, end, -Vector3.UnitZ) : c;
            });
            var singles = new List<Contact>();
            while (remaining.Count > 0)
            {
                var seed = remaining.OrderBy(c => c.End.Z).ThenBy(c => c.End.X).ThenBy(c => c.End.Y).First();
                var nearby = remaining.Where(c => Vector2.Distance(Xy(c.End), Xy(seed.End)) <= width)
                    .OrderBy(c => Vector3.DistanceSquared(c.End, seed.End)).Take(64).ToList();
                var centre = (nearby.Select(c => Xy(c.End)).Aggregate(Vector2.Min) +
                    nearby.Select(c => Xy(c.End)).Aggregate(Vector2.Max)) / 2;
                var candidates = settings.UseBaseGrid
                    ? BaseLattice.NearestSquarePoints(centre, settings.BaseGridPitch, MathF.Min(width / 2, maxLength)).Take(12)
                    : new[] { centre }.Concat(nearby.Take(8).Select(c => Xy(c.End))).Distinct();
                List<Contact> best = []; Vector2 bestCentre = default; float bestAngle = angle;
                var reasons = new Dictionary<string, int>();
                foreach (var candidate in candidates)
                foreach (var fraction in new[] { 1f, 0.7f, 0.4f })
                {
                    var accepted = new List<Contact>();
                    foreach (var contact in nearby)
                    {
                        if (accepted.Count >= Math.Max(1, settings.CandelabraMaxTips)) break;
                        if (accepted.Any(c => Vector2.Distance(Xy(c.End), Xy(contact.End)) > width)) continue;
                        var trial = accepted.Append(contact).ToList();
                        if (!TryGroup(trial, candidate, angle * fraction, out var reason, out _, false))
                        {
                            trial[^1] = verticals[contact];
                            if (!TryGroup(trial, candidate, angle * fraction, out reason, out _, false))
                            { reasons[reason] = reasons.GetValueOrDefault(reason) + 1; continue; }
                        }
                        accepted = trial;
                    }
                    if (accepted.Count <= best.Count) continue;
                    best = accepted; bestCentre = candidate; bestAngle = angle * fraction;
                    if (best.Count == nearby.Count) break;
                }
                if (best.Count >= 2)
                {
                    TryGroup(best, bestCentre, bestAngle, out _, out _, true);
                    var points = best.Select(c => c.Tip.SurfacePoint).ToHashSet();
                    remaining.RemoveAll(c => points.Contains(c.Tip.SurfacePoint));
                }
                else
                {
                    var reason = nearby.Count == 1 ? $"No unassigned tip remains within Group width ({width:0.##} mm)."
                        : settings.CandelabraMaxTips < 2 ? "Maximum tips / trunk is 1."
                        : "No shared trunk found in the tested nearby positions. Most frequent blocker: " +
                            reasons.OrderByDescending(r => r.Value).Select(r => r.Key).FirstOrDefault();
                    reportConstraint?.Invoke(reason);
                    reportTipConstraint?.Invoke(seed.Tip, reason);
                    singles.Add(seed); remaining.Remove(seed);
                }
            }
            // Independent members must not obstruct a larger compatible group before it is tested.
            foreach (var single in singles) Route([single]);
        }

        void Route(List<Contact> group)
        {
            if (group.Count == 0) return;
            var min = group.Select(c => Xy(c.End)).Aggregate(Vector2.Min);
            var max = group.Select(c => Xy(c.End)).Aggregate(Vector2.Max);
            var fits = group.Count <= settings.CandelabraMaxTips &&
                group.All(a => group.All(b => Vector2.Distance(Xy(a.End), Xy(b.End)) <= width));
            var bestFailure = group.Count > settings.CandelabraMaxTips
                ? $"Group exceeds Maximum tips / trunk ({settings.CandelabraMaxTips})."
                : $"Group exceeds Group width ({width:0.##} mm).";
            var bestProgress = -1;
            if (fits)
            {
                bestFailure = "No reachable base-grid point; reduce Base grid pitch or turn the grid off.";
                // A valid outward cone can still prevent a lateral branch from turning towards
                // the trunk. Retry clear vertical cones before splitting the group into singles.
                var vertical = group.Select(c =>
                {
                    var end = c.Tip.SurfacePoint - Vector3.UnitZ * settings.TipMemberLength;
                    var start = c.Tip.SurfacePoint - Vector3.UnitZ * (MathF.Max(c.Tip.TipDiameter, 0.05f) + radius * 1.5f);
                    return end.Z >= floor && !scene.IntersectsCapsule(start, end, radius + clearance)
                        ? new Contact(c.Tip, end, -Vector3.UnitZ) : c;
                }).ToList();
                foreach (var variant in new[] { group, vertical })
                {
                    var lo = variant.Select(c => Xy(c.End)).Aggregate(Vector2.Min);
                    var hi = variant.Select(c => Xy(c.End)).Aggregate(Vector2.Max);
                    var centre = trunkCentre ?? (lo + hi) / 2;
                    // Prefer a balanced spine, but an organic model may occupy the space below
                    // that midpoint. Search nearby contact positions before splitting the group.
                    // An explicitly placed trunk remains fixed.
                    var candidates = settings.UseBaseGrid
                        ? BaseLattice.NearestSquarePoints(centre, settings.BaseGridPitch, MathF.Min(width / 2, maxLength)).Take(16)
                        : trunkCentre is not null ? new[] { centre }.AsEnumerable()
                        : new[] { centre }.Concat(variant.Select(c => Xy(c.End)).Distinct()
                            .OrderBy(p => Vector2.DistanceSquared(p, centre)).Take(8)
                            .SelectMany(p => new[] { Vector2.Lerp(centre, p, 0.5f), p })).Distinct();
                    foreach (var candidate in candidates)
                        foreach (var fraction in new[] { 1f, 0.85f, 0.7f, 0.55f, 0.4f, 0.25f })
                        {
                            if (TryGroup(variant, candidate, angle * fraction, out var failure, out var progress)) return;
                            if (progress <= bestProgress) continue;
                            bestProgress = progress; bestFailure = failure;
                        }
                }
            }
            reportConstraint?.Invoke(bestFailure);
            // A manually positioned candelabra must fit as a whole; never silently change its centre.
            if (group.Count == 1 || trunkCentre is not null)
            {
                foreach (var c in group) reportTipConstraint?.Invoke(c.Tip, bestFailure);
                refused.AddRange(group.Select(c => c.Tip));
                return;
            }
            var alongX = max.X - min.X >= max.Y - min.Y;
            var sorted = group.OrderBy(c => alongX ? c.End.X : c.End.Y).ThenBy(c => c.End.Z).ToList();
            Route(sorted.Take(sorted.Count / 2).ToList());
            Route(sorted.Skip(sorted.Count / 2).ToList());
        }

        (Vector3 Attach, string Failure) ContactRoute(Contact c, Vector2 centre, float routeAngle)
        {
            if (routeCache.TryGetValue((c, centre, routeAngle), out var cached)) return cached;
            var result = Check(); routeCache[(c, centre, routeAngle)] = result; return result;
            (Vector3 Attach, string Failure) Check()
            {
                var failure = "";
                var tan = MathF.Tan(routeAngle);
                var distance = Vector2.Distance(Xy(c.End), centre);
                var attach = new Vector3(centre, c.End.Z - distance / tan);
                var direction = attach - c.End;
                if (attach.Z < floor) { failure = $"Branches would attach below {floor:0.##} mm; lower Min branch attach height or use smaller groups."; return (attach, failure); }
                if (direction.Length() > maxLength) { failure = $"Branches exceed Max branch length ({maxLength:0.##} mm); increase it or reduce Group width."; return (attach, failure); }
                if (direction.LengthSquared() > 1e-8f && MathF.Acos(Math.Clamp(Vector3.Dot(Vector3.Normalize(direction), c.Axis), -1f, 1f)) > bend + 0.001f)
                { failure = $"Turning towards the trunk exceeds Max cone bend ({bend * 180 / MathF.PI:0.#}°), and a clear vertical-cone route could not be found."; return (attach, failure); }
                if (scene.IntersectsCapsule(c.End, attach, radius + clearance))
                { failure = "A branch hits the model or an existing support. Try a smaller group or move the trunk centre."; return (attach, failure); }
                if (scene.IntersectsCapsule(new Vector3(centre, settings.BaseHeight), attach, trunkRadius + clearance))
                    return (attach, "The central trunk hits the model or an existing support. Move the trunk centre or use smaller groups.");
                return (attach, "");
            }
        }

        bool TryGroup(List<Contact> group, Vector2 centre, float routeAngle, out string failure, out int progress, bool commit = true)
        {
            failure = ""; progress = 0;
            if (group.Count > Math.Max(1, settings.CandelabraMaxTips))
            { failure = $"Maximum tips / trunk ({settings.CandelabraMaxTips}) reached."; return false; }
            if (group.Any(a => group.Any(b => Vector2.Distance(Xy(a.End), Xy(b.End)) > width)))
            { failure = $"Group width ({width:0.##} mm) exceeded."; return false; }
            var routes = new List<(Contact Contact, Vector3 Attach)>();
            foreach (var c in group)
            {
                var result = ContactRoute(c, centre, routeAngle);
                if (result.Failure.Length > 0) { failure = result.Failure; return false; }
                routes.Add((c, result.Attach)); progress++;
            }

            // Check proposed members too. Trim the common spine junction neighbourhood so legitimate
            // converging members may fuse, while crossing branches away from the spine are rejected.
            var local = new LinearCollisionScene();
            for (var i = 0; i < routes.Count; i++)
            {
                var (c, attach) = routes[i];
                var d = attach - c.End;
                var length = d.Length();
                if (length < 0.001f) continue;
                var trim = MathF.Min(length, (trunkRadius + radius) / MathF.Max(0.1f, MathF.Sin(routeAngle)));
                var end = attach - Vector3.Normalize(d) * trim;
                if (length > trim && local.IntersectsCapsule(c.End, end, radius + clearance / 2))
                { failure = "Neighbouring branches overlap at the current diameter and clearance, even after trying steeper branches."; return false; }
                if (length > trim) local.AddCapsule(c.End, end, radius + clearance / 2, i);
            }

            if (!commit) return true;
            var localNodes = new List<SupportNode>();
            var localSegments = new List<SupportSegment>();
            var baseNode = new SupportNode
            {
                Type = SupportNodeType.Base, Position = new Vector3(centre, 0), Origin = origin,
                BaseShape = settings.BaseShape, BaseDiameter = settings.BaseDiameter,
                BaseHeight = settings.BaseHeight, BaseConeHeight = settings.BaseConeHeight,
            };
            localNodes.Add(baseNode);
            var spine = new List<SupportNode> { baseNode };
            foreach (var (c, attach) in routes.OrderBy(r => r.Attach.Z))
            {
                var joint = spine.LastOrDefault(n => n.Type != SupportNodeType.Base && MathF.Abs(n.Position.Z - attach.Z) < 0.001f);
                if (joint is null)
                {
                    joint = new SupportNode { Type = SupportNodeType.Junction, Position = attach, Origin = origin };
                    localNodes.Add(joint);
                    localSegments.Add(Segment(SupportSegmentType.Trunk, spine[^1], joint, settings.TrunkDiameter));
                    spine.Add(joint);
                }
                var tip = new SupportNode { Type = SupportNodeType.Tip, Position = c.Tip.SurfacePoint, Origin = origin };
                RoutingUtilities.ApplyContact(tip, c.Tip);
                var cone = Vector3.DistanceSquared(c.End, joint.Position) < 1e-8f ? joint
                    : new SupportNode { Type = SupportNodeType.Junction, Position = c.End, Origin = origin };
                localNodes.Add(tip);
                if (cone != joint) localNodes.Add(cone);
                localSegments.Add(Segment(SupportSegmentType.Tip, tip, cone, settings.BranchDiameter));
                if (cone != joint) localSegments.Add(Segment(SupportSegmentType.Branch, cone, joint, settings.BranchDiameter));
            }
            routeCache.Clear();
            nodes.AddRange(localNodes); segments.AddRange(localSegments);
            var byId = localNodes.ToDictionary(n => n.Id);
            foreach (var s in localSegments)
                members.AddCapsule(byId[s.NodeA].Position, byId[s.NodeB].Position, s.Diameter / 2);
            return true;
        }

        SupportSegment Segment(SupportSegmentType type, SupportNode a, SupportNode b, float diameter) =>
            new() { Type = type, NodeA = a.Id, NodeB = b.Id, Diameter = diameter, Origin = origin };
    }

    private static Vector2 Xy(Vector3 p) => new(p.X, p.Y);
}
