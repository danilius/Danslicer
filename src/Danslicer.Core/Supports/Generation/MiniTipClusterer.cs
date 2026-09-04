using System.Numerics;

namespace Danslicer.Core.Supports.Generation;

/// <summary>
/// Converts unusually crowded regular-size contacts, including required island contacts, into
/// deterministic mini-tip clusters. Connected groups are split into bounded, spatially compact
/// chunks so every chunk can own one branch end.
/// </summary>
internal static class MiniTipClusterer
{
    private const int MinimumClusterSize = 3;

    public static IReadOnlyList<TipCandidate> Apply(IReadOnlyList<TipCandidate> candidates,
        TipPlacementParameters parameters)
    {
        if (candidates.Count < MinimumClusterSize ||
            !float.IsFinite(parameters.MiniSupportClusterDistanceMm) ||
            parameters.MiniSupportClusterDistanceMm <= 0)
            return candidates;

        var regular = candidates.Select((candidate, index) => (Candidate: candidate, Index: index))
            .Where(item => IsDensityClusterCandidate(item.Candidate.Strategy))
            .OrderBy(item => item.Candidate.Point.X)
            .ThenBy(item => item.Candidate.Point.Y)
            .ThenBy(item => item.Candidate.Point.Z)
            .ThenBy(item => item.Candidate.FaceIndex)
            .ThenBy(item => item.Index)
            .ToList();
        if (regular.Count < MinimumClusterSize) return candidates;

        var parent = Enumerable.Range(0, regular.Count).ToArray();
        var thresholdSquared = parameters.MiniSupportClusterDistanceMm *
                               parameters.MiniSupportClusterDistanceMm;
        for (var i = 0; i < regular.Count; i++)
        for (var j = i + 1; j < regular.Count; j++)
        {
            var dx = regular[j].Candidate.Point.X - regular[i].Candidate.Point.X;
            if (dx * dx > thresholdSquared) break;
            if (Vector3.DistanceSquared(regular[i].Candidate.Point, regular[j].Candidate.Point) <
                thresholdSquared) Union(parent, i, j);
        }

        var output = candidates.ToArray();
        var maxPerCluster = Math.Max(1, parameters.MiniSupportMaxTipsPerCluster);
        var nextClusterId = 1;
        foreach (var component in regular.Select((item, index) => (item, index))
                     .GroupBy(value => Find(parent, value.index))
                     .Select(group => group.Select(value => value.item).ToList())
                     .Where(group => group.Count >= MinimumClusterSize)
                     .OrderBy(group => group.Min(item => item.Candidate.Point.X))
                     .ThenBy(group => group.Min(item => item.Candidate.Point.Y))
                     .ThenBy(group => group.Min(item => item.Candidate.Point.Z)))
        {
            // A nearest-neighbour walk keeps cap-induced subclusters compact and deterministic.
            var remaining = component.ToList();
            while (remaining.Count > 0)
            {
                var chunk = new List<(TipCandidate Candidate, int Index)>
                {
                    remaining[0],
                };
                remaining.RemoveAt(0);
                while (chunk.Count < maxPerCluster && remaining.Count > 0)
                {
                    var center = WeightedCenter(chunk.Select(item => item.Candidate));
                    var next = remaining.OrderBy(item =>
                            Vector3.DistanceSquared(item.Candidate.Point, center))
                        .ThenBy(item => item.Candidate.Point.X)
                        .ThenBy(item => item.Candidate.Point.Y)
                        .ThenBy(item => item.Candidate.Point.Z)
                        .ThenBy(item => item.Candidate.FaceIndex)
                        .First();
                    chunk.Add(next);
                    remaining.Remove(next);
                }

                var clusterCenter = WeightedCenter(chunk.Select(item => item.Candidate));
                foreach (var item in chunk)
                {
                    output[item.Index] = item.Candidate with
                    {
                        Strategy = TipStrategy.MiniCluster,
                        TipDiameter = parameters.MiniSupportTipDiameterMm,
                        TipShape = parameters.MiniTipShape,
                        ConeLength = parameters.MiniTipShape == SupportTipShape.Cone
                            ? parameters.MiniSupportConeLengthMm
                            : 0f,
                        BallDiameter = 0f,
                        PenetrationDepth = 0f,
                        MiniClusterId = nextClusterId,
                        MiniClusterCenter = clusterCenter,
                        MiniClusterSourceStrategy = item.Candidate.Strategy,
                    };
                }
                nextClusterId++;
            }
        }
        return output;
    }

    private static bool IsDensityClusterCandidate(TipStrategy strategy) => strategy is
        TipStrategy.Island or
        TipStrategy.LocalMinimum or
        TipStrategy.Overhang or
        TipStrategy.Edge or
        TipStrategy.Corner or
        TipStrategy.GridProjection;

    private static Vector3 WeightedCenter(IEnumerable<TipCandidate> candidates)
    {
        var total = 0f;
        var sum = Vector3.Zero;
        foreach (var candidate in candidates)
        {
            var weight = float.IsFinite(candidate.Score) ? MathF.Max(candidate.Score, 0.001f) : 0.001f;
            sum += candidate.Point * weight;
            total += weight;
        }
        return sum / total;
    }

    private static int Find(int[] parent, int index)
    {
        while (parent[index] != index)
        {
            parent[index] = parent[parent[index]];
            index = parent[index];
        }
        return index;
    }

    private static void Union(int[] parent, int a, int b)
    {
        var rootA = Find(parent, a);
        var rootB = Find(parent, b);
        if (rootA == rootB) return;
        if (rootA < rootB) parent[rootB] = rootA;
        else parent[rootA] = rootB;
    }
}
