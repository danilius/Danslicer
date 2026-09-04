using System.Numerics;

namespace Danslicer.Core.Supports.Routing;

/// <summary>
/// Pure member-to-member clearance geometry. Separation is measured between the cylindrical
/// surfaces around each straight centreline; members incident on a shared graph node are an
/// intentional joint and never conflict.
/// </summary>
public static class MemberSeparation
{
    public static bool AreTooClose(Vector3 firstStart, Vector3 firstEnd, float firstRadius,
        Guid? firstNodeA, Guid? firstNodeB, Vector3 secondStart, Vector3 secondEnd,
        float secondRadius, Guid? secondNodeA, Guid? secondNodeB, float minimumSeparation)
    {
        if (!float.IsFinite(minimumSeparation) || minimumSeparation < 0)
            throw new ArgumentOutOfRangeException(nameof(minimumSeparation));
        if (SharesNode(firstNodeA, firstNodeB, secondNodeA, secondNodeB)) return false;

        var limit = MathF.Max(0, firstRadius) + MathF.Max(0, secondRadius) + minimumSeparation;
        return GeometryDistance.SegmentSegmentSquared(
            firstStart, firstEnd, secondStart, secondEnd) < limit * limit;
    }

    public static int CountPairs(SupportGraph graph, float minimumSeparation)
    {
        ArgumentNullException.ThrowIfNull(graph);
        var members = graph.Segments.Where(segment => !segment.Disabled)
            .OrderBy(segment => segment.Id).ToList();
        var count = 0;
        for (var firstIndex = 0; firstIndex < members.Count; firstIndex++)
        {
            var first = members[firstIndex];
            var firstStart = graph.GetNode(first.NodeA).Position;
            var firstEnd = graph.GetNode(first.NodeB).Position;
            for (var secondIndex = firstIndex + 1; secondIndex < members.Count; secondIndex++)
            {
                var second = members[secondIndex];
                if (AreTooClose(firstStart, firstEnd, first.Diameter * 0.5f,
                        first.NodeA, first.NodeB,
                        graph.GetNode(second.NodeA).Position,
                        graph.GetNode(second.NodeB).Position, second.Diameter * 0.5f,
                        second.NodeA, second.NodeB, minimumSeparation))
                    count++;
            }
        }
        return count;
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
