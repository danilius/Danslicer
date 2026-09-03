namespace Danslicer.Core.Supports.Routing;

internal sealed record ExistingSupportTarget(
    SupportNode Node,
    float LowestTipZ,
    IReadOnlySet<Guid> IncidentSegmentIds);

internal static class ExistingSupportTargets
{
    public static IReadOnlyList<ExistingSupportTarget> From(SupportGraph? graph)
    {
        if (graph is null) return Array.Empty<ExistingSupportTarget>();

        var result = new List<ExistingSupportTarget>();
        foreach (var node in graph.Nodes)
        {
            var loadBearing = graph.SegmentsAt(node.Id)
                .Where(segment => !segment.Disabled &&
                    segment.Type is SupportSegmentType.Branch or SupportSegmentType.Trunk)
                .ToList();
            if (loadBearing.Count == 0 || node.Disabled) continue;

            var component = graph.Component(node.Id);
            var tipHeights = component.Nodes.Select(graph.GetNode)
                .Where(candidate => !candidate.Disabled && candidate.Type == SupportNodeType.Tip)
                .Select(candidate => candidate.Position.Z)
                .ToList();
            result.Add(new ExistingSupportTarget(node,
                tipHeights.Count == 0 ? float.PositiveInfinity : tipHeights.Min(),
                loadBearing.Select(segment => segment.Id).ToHashSet()));
        }

        return result;
    }

    public static Func<object?, bool> ExcludingIncidentSegments(ExistingSupportTarget target) =>
        tag => tag is not Guid id || !target.IncidentSegmentIds.Contains(id);
}
