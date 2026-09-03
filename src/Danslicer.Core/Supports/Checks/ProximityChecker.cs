using System.Numerics;

namespace Danslicer.Core.Supports.Checks;

internal static class ProximityChecker
{
    public static void SupportSupport(SupportGraph graph, float threshold, List<CheckFinding> output)
    {
        if (graph.SegmentCount < 2 || threshold <= 0) return;
        var component = ComponentIds(graph);
        var segments = graph.Segments.OrderBy(s => s.Id).ToList();
        for (int i = 0; i < segments.Count; i++)
        {
            var a = segments[i];
            if (a.Disabled) continue;
            var na = graph.GetNode(a.NodeA);
            var nb = graph.GetNode(a.NodeB);
            if (na.Disabled || nb.Disabled) continue;
            var ra = a.Diameter * 0.5f;
            for (int j = i + 1; j < segments.Count; j++)
            {
                var b = segments[j];
                if (b.Disabled) continue;
                if (component[a.NodeA] == component[b.NodeA]) continue;
                var ma = graph.GetNode(b.NodeA);
                var mb = graph.GetNode(b.NodeB);
                if (ma.Disabled || mb.Disabled) continue;
                var rb = b.Diameter * 0.5f;
                MeshDistanceQuery.ClosestPointsOnSegments(na.Position, nb.Position, ma.Position, mb.Position, out var pa, out var pb);
                var gap = Vector3.Distance(pa, pb) - ra - rb;
                if (gap >= threshold) continue;
                output.Add(new CheckFinding
                {
                    Kind = CheckKind.SupportProximity,
                    Severity = CheckSeverity.Warning,
                    ElementIdA = a.Id,
                    ElementIdB = b.Id,
                    PointA = pa,
                    PointB = pb,
                    DistanceMm = MathF.Max(gap, 0f),
                });
            }
        }
    }

    public static void SupportModel(
        SupportGraph graph, IReadOnlyList<MeshDistanceQuery> meshes, float threshold, List<CheckFinding> output)
    {
        if (graph.SegmentCount == 0 || meshes.Count == 0 || threshold <= 0) return;
        var tips = graph.Nodes.Where(n => n.Type == SupportNodeType.Tip && !n.Disabled).ToList();

        foreach (var seg in graph.Segments.OrderBy(s => s.Id))
        {
            if (seg.Disabled) continue;
            var na = graph.GetNode(seg.NodeA);
            var nb = graph.GetNode(seg.NodeB);
            if (na.Disabled || nb.Disabled) continue;
            var radius = seg.Diameter * 0.5f;
            for (int m = 0; m < meshes.Count; m++)
            {
                var d = meshes[m].ClosestToSegment(na.Position, nb.Position, out var onSeg, out var onMesh);
                var gap = d - radius;
                if (gap >= threshold) continue;
                if (IsTipContact(onSeg, onMesh, tips, na, nb)) continue;
                output.Add(new CheckFinding
                {
                    Kind = CheckKind.SupportModelProximity,
                    Severity = CheckSeverity.Warning,
                    ObjectIndex = m,
                    ElementIdA = seg.Id,
                    PointA = onSeg,
                    PointB = onMesh,
                    DistanceMm = MathF.Max(gap, 0f),
                });
            }
        }
    }

    public static void ObjectObject(IReadOnlyList<MeshDistanceQuery> meshes, float threshold, List<CheckFinding> output)
    {
        if (meshes.Count < 2 || threshold <= 0) return;
        for (int i = 0; i < meshes.Count; i++)
        for (int j = i + 1; j < meshes.Count; j++)
        {
            var sep = AabbSep(meshes[i].Bounds, meshes[j].Bounds);
            if (sep >= threshold) continue;
            var d = meshes[i].ClosestToMesh(meshes[j], out var a, out var b);
            if (d >= threshold) continue;
            output.Add(new CheckFinding
            {
                Kind = CheckKind.ObjectProximity,
                Severity = CheckSeverity.Warning,
                ObjectIndex = i,
                ObjectIndexB = j,
                PointA = a,
                PointB = b,
                DistanceMm = d,
            });
        }
    }

    private static bool IsTipContact(Vector3 onSeg, Vector3 onMesh, List<SupportNode> tips, SupportNode na, SupportNode nb)
    {
        foreach (var tip in new[] { na, nb })
        {
            if (tip.Type != SupportNodeType.Tip) continue;
            var slop = MathF.Max(tip.TipDiameter * 2f, 1.5f);
            if (Vector3.Distance(onSeg, tip.Position) <= slop &&
                Vector3.Distance(onMesh, tip.Position) <= slop)
                return true;
        }
        foreach (var tip in tips)
        {
            var slop = MathF.Max(tip.TipDiameter * 2f, 1.5f);
            if (Vector3.Distance(onSeg, tip.Position) <= slop &&
                Vector3.Distance(onMesh, tip.Position) <= slop)
                return true;
        }
        return false;
    }

    private static Dictionary<Guid, int> ComponentIds(SupportGraph graph)
    {
        var id = new Dictionary<Guid, int>();
        var next = 0;
        foreach (var node in graph.Nodes.OrderBy(n => n.Id))
        {
            if (id.ContainsKey(node.Id)) continue;
            var c = graph.Component(node.Id, includeBracing: true);
            foreach (var n in c.Nodes) id[n] = next;
            next++;
        }
        return id;
    }

    private static float AabbSep(Geometry.Aabb a, Geometry.Aabb b)
    {
        var dx = MathF.Max(0, MathF.Max(a.Min.X - b.Max.X, b.Min.X - a.Max.X));
        var dy = MathF.Max(0, MathF.Max(a.Min.Y - b.Max.Y, b.Min.Y - a.Max.Y));
        var dz = MathF.Max(0, MathF.Max(a.Min.Z - b.Max.Z, b.Min.Z - a.Max.Z));
        return MathF.Sqrt(dx * dx + dy * dy + dz * dz);
    }
}
