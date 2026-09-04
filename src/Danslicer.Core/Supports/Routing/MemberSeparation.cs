using System.Numerics;

namespace Danslicer.Core.Supports.Routing;

/// <summary>
/// Pure member-to-member clearance geometry. Separation is measured between straight centrelines;
/// members incident on a shared graph node are an intentional joint and never conflict.
/// </summary>
public static class MemberSeparation
{
    public static bool AreTooClose(Vector3 firstStart, Vector3 firstEnd,
        Guid? firstNodeA, Guid? firstNodeB, Vector3 secondStart, Vector3 secondEnd,
        Guid? secondNodeA, Guid? secondNodeB, float minimumSeparation)
    {
        if (!float.IsFinite(minimumSeparation) || minimumSeparation < 0)
            throw new ArgumentOutOfRangeException(nameof(minimumSeparation));
        if (SharesNode(firstNodeA, firstNodeB, secondNodeA, secondNodeB)) return false;

        return GeometryDistance.SegmentSegmentSquared(
            firstStart, firstEnd, secondStart, secondEnd) <
               minimumSeparation * minimumSeparation;
    }

    public static int CountPairs(SupportGraph graph, float minimumSeparation)
    {
        var count = 0;
        VisitPairs(graph, minimumSeparation, (_, _) => count++);
        return count;
    }

    public static IReadOnlyDictionary<string, int> CountPairsByType(
        SupportGraph graph, float minimumSeparation)
    {
        var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);
        VisitPairs(graph, minimumSeparation, (first, second) =>
        {
            var names = new[] { first.Type.ToString(), second.Type.ToString() };
            Array.Sort(names, StringComparer.Ordinal);
            var key = $"{names[0]}-{names[1]}";
            counts[key] = counts.GetValueOrDefault(key) + 1;
        });
        return counts;
    }

    private static void VisitPairs(SupportGraph graph, float minimumSeparation,
        Action<SupportSegment, SupportSegment> visit)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(visit);
        var members = graph.Segments.Where(segment => !segment.Disabled)
            .OrderBy(segment => segment.Id).ToList();
        for (var firstIndex = 0; firstIndex < members.Count; firstIndex++)
        {
            var first = members[firstIndex];
            var firstStart = graph.GetNode(first.NodeA).Position;
            var firstEnd = graph.GetNode(first.NodeB).Position;
            for (var secondIndex = firstIndex + 1; secondIndex < members.Count; secondIndex++)
            {
                var second = members[secondIndex];
                if (AreTooClose(firstStart, firstEnd, first.NodeA, first.NodeB,
                        graph.GetNode(second.NodeA).Position,
                        graph.GetNode(second.NodeB).Position,
                        second.NodeA, second.NodeB, minimumSeparation))
                    visit(first, second);
            }
        }
    }

    private static bool SharesNode(Guid? firstNodeA, Guid? firstNodeB,
        Guid? secondNodeA, Guid? secondNodeB)
    {
        return Matches(firstNodeA, secondNodeA) || Matches(firstNodeA, secondNodeB) ||
               Matches(firstNodeB, secondNodeA) || Matches(firstNodeB, secondNodeB);

        static bool Matches(Guid? left, Guid? right) =>
            left is { } leftId && right is { } rightId && leftId == rightId;
    }
}
