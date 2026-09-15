using System.Numerics;
using Danslicer.Core.Config;
using Danslicer.Core.Supports.Routing;

namespace Danslicer.Core.Supports;

/// <summary>One additive brace between two member axes; never rebuilds surrounding bracing.</summary>
public static class ManualBrace
{
    public static Vector3? Snap45(Vector3 start, Vector3 a, Vector3 b, Vector3 near)
    {
        var d = b - a; var q = a - start;
        float Metric(Vector3 x, Vector3 y) => x.Z * y.Z - x.X * y.X - x.Y * y.Y;
        var aa = Metric(d, d); var bb = 2 * Metric(q, d); var cc = Metric(q, q);
        var roots = new List<float>();
        if (MathF.Abs(aa) < 1e-6f)
        {
            if (MathF.Abs(bb) > 1e-6f) roots.Add(-cc / bb);
            else if (MathF.Abs(cc) < 1e-6f)
                return GeometryDistance.ClosestPointOnSegment(near, a, b);
        }
        else
        {
            var disc = bb * bb - 4 * aa * cc;
            if (disc >= 0)
            {
                roots.Add((-bb + MathF.Sqrt(disc)) / (2 * aa));
                roots.Add((-bb - MathF.Sqrt(disc)) / (2 * aa));
            }
        }
        return roots.Where(t => t >= 0 && t <= 1).Select(t => (Vector3?)Vector3.Lerp(a, b, t))
            .OrderBy(p => Vector3.DistanceSquared(p!.Value, near)).FirstOrDefault();
    }

    public static SupportGraphEdit? Plan(SupportGraph graph, Guid owner, Guid first, Vector3 start,
        Guid second, Vector3 end, SupportConfig settings, ICollisionScene models, out string? refusal)
    {
        refusal = "Choose two different trunk or branch members";
        bool Valid(Guid id) => graph.TryGetSegment(id, out var s) &&
            s.Type is SupportSegmentType.Trunk or SupportSegmentType.Branch && !s.Hidden && !s.Disabled &&
            !graph.GetNode(s.NodeA).Hidden && !graph.GetNode(s.NodeB).Hidden &&
            !graph.GetNode(s.NodeA).Disabled && !graph.GetNode(s.NodeB).Disabled &&
            graph.OwningObjectId(id) == owner;
        if (!Valid(first) || !Valid(second) || first == second) return null;
        Vector3 Axis(Guid id, Vector3 p)
        {
            var s = graph.GetSegment(id);
            return GeometryDistance.ClosestPointOnSegment(p, graph.GetNode(s.NodeA).Position, graph.GetNode(s.NodeB).Position);
        }
        start = Axis(first, start); end = Axis(second, end);
        var diameter = settings.ManualBraceDiameter;
        refusal = "Brace is too short";
        if (Vector3.Distance(start, end) < 0.05f || MathF.Min(start.Z, end.Z) <= 0) return null;
        bool Same(Vector3 a, Vector3 b) => Vector3.DistanceSquared(a, b) < 0.0001f;
        foreach (var s in graph.Segments.Where(s => s.Type == SupportSegmentType.Bracing))
        {
            var a = graph.GetNode(s.NodeA).Position; var b = graph.GetNode(s.NodeB).Position;
            if ((Same(start, a) && Same(end, b)) || (Same(start, b) && Same(end, a)))
            { refusal = "This brace already exists"; return null; }
        }
        refusal = "Brace intersects a model (Manual settings > Avoid models)";
        if (settings.ManualBraceAvoidModels && models.IntersectsCapsule(start, end, diameter * 0.5f)) return null;
        refusal = "Brace intersects a support (Manual settings > Avoid supports)";
        var obstacles = new LinearCollisionScene();
        obstacles.AddSupportGraph(graph);
        if (settings.ManualBraceAvoidSupports && obstacles.IntersectsCapsule(start, end, diameter * 0.5f, tag =>
        {
            if (tag is not Guid id) return true;
            if (id == first || id == second) return false;
            if (!graph.TryGetSegment(id, out var s) || s.Type != SupportSegmentType.Bracing) return true;
            var a = graph.GetNode(s.NodeA).Position; var b = graph.GetNode(s.NodeB).Position;
            return !(Same(start, a) || Same(start, b) || Same(end, a) || Same(end, b));
        })) return null;
        var origin = SupportOrigin.ManualFor(owner);
        var n1 = new SupportNode { Type = SupportNodeType.BraceEnd, Position = start, Origin = origin };
        var n2 = new SupportNode { Type = SupportNodeType.BraceEnd, Position = end, Origin = origin };
        var brace = new SupportSegment { Type = SupportSegmentType.Bracing, NodeA = n1.Id, NodeB = n2.Id,
            Diameter = diameter, Origin = origin };
        refusal = null;
        return new SupportGraphEdit([n1, n2], [brace], []);
    }
}
