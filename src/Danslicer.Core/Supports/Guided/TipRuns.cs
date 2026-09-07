using System.Numerics;
using Danslicer.Core.Geometry;

namespace Danslicer.Core.Supports.Guided;

/// <summary>
/// Densify and thin (SUPPORT-GEOMETRY-SPEC "Guided tip placement"): the runs a set of tips
/// forms, and what to add between them or take from them. Pure geometry over tip positions;
/// the document decides which tips and commits the result.
///
/// <para>Neighbours are the edges of the minimum spanning tree over the tips, minus any edge
/// longer than <see cref="MaxLinkFactor"/> times the median edge — so a selection made of
/// several separate lines is treated as several runs, and two lines far apart are never
/// bridged.</para>
/// </summary>
public static class TipRuns
{
    /// <summary>An edge longer than this many median edges does not join two tips into a run.</summary>
    public const float MaxLinkFactor = 2.5f;

    /// <summary>The neighbour pairs, as indices into <paramref name="tips"/>.</summary>
    public static IReadOnlyList<(int A, int B)> Links(IReadOnlyList<Vector3> tips)
    {
        var n = tips.Count;
        if (n < 2) return [];
        // Prim's algorithm; selections are hundreds of tips at most, so O(n²) is fine.
        var inTree = new bool[n];
        var best = new float[n];
        var from = new int[n];
        Array.Fill(best, float.PositiveInfinity);
        best[0] = 0;
        var edges = new List<(int A, int B, float Length)>(n - 1);
        for (var step = 0; step < n; step++)
        {
            var next = -1;
            for (var i = 0; i < n; i++)
                if (!inTree[i] && (next < 0 || best[i] < best[next])) next = i;
            inTree[next] = true;
            if (step > 0) edges.Add((from[next], next, best[next]));
            for (var i = 0; i < n; i++)
            {
                if (inTree[i]) continue;
                var d = Vector3.Distance(tips[next], tips[i]);
                if (d < best[i]) { best[i] = d; from[i] = next; }
            }
        }
        var sorted = edges.Select(e => e.Length).OrderBy(l => l).ToList();
        var median = sorted[sorted.Count / 2];
        var limit = median * MaxLinkFactor;
        return edges.Where(e => e.Length <= limit).Select(e => (e.A, e.B)).ToList();
    }

    /// <summary>
    /// Points to add: <paramref name="insertions"/> evenly along the surface path between every
    /// pair of neighbours. Each carries the face it lies on, ready for
    /// <see cref="GuidedTipPlacement.Candidates"/>.
    /// </summary>
    public static IReadOnlyList<(Vector3 Point, int Face)> Densify(Mesh mesh,
        IReadOnlyList<(Vector3 Point, int Face)> tips, int insertions)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(insertions);
        var result = new List<(Vector3, int)>();
        foreach (var (a, b) in Links(tips.Select(t => t.Point).ToList()))
        {
            var path = SurfacePath.Between(mesh, tips[a].Point, tips[a].Face, tips[b].Point, tips[b].Face)
                ?? SurfacePath.Chord(tips[a].Point, tips[a].Face, tips[b].Point, tips[b].Face);
            if (path.Length <= 0) continue;
            for (var k = 1; k <= insertions; k++)
                result.Add(PointAlong(path, path.Length * k / (insertions + 1)));
        }
        return result;
    }

    /// <summary>
    /// Which tips to remove so that one in <paramref name="keepEvery"/> remains along each run.
    /// Runs are walked from an end; the first tip of a run is always kept, so the ends of a line
    /// survive thinning. Indices into <paramref name="tips"/>.
    /// </summary>
    public static IReadOnlyList<int> Thin(IReadOnlyList<Vector3> tips, int keepEvery)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(keepEvery, 2);
        var n = tips.Count;
        var adjacency = new List<int>[n];
        for (var i = 0; i < n; i++) adjacency[i] = [];
        foreach (var (a, b) in Links(tips))
        {
            adjacency[a].Add(b);
            adjacency[b].Add(a);
        }
        var visited = new bool[n];
        var remove = new List<int>();
        // Start each run at a leaf (degree ≤ 1) when it has one, so walking order follows the line.
        foreach (var start in Enumerable.Range(0, n).OrderBy(i => adjacency[i].Count > 1 ? 1 : 0).ThenBy(i => i))
        {
            if (visited[start]) continue;
            var order = 0;
            var stack = new Stack<int>();
            stack.Push(start);
            while (stack.Count > 0)
            {
                var i = stack.Pop();
                if (visited[i]) continue;
                visited[i] = true;
                if (order++ % keepEvery != 0) remove.Add(i);
                foreach (var next in adjacency[i].OrderByDescending(j => j))
                    if (!visited[next]) stack.Push(next);
            }
        }
        return remove;
    }

    private static (Vector3, int) PointAlong(SurfacePath path, float arc)
    {
        var walked = 0f;
        for (var i = 0; i + 1 < path.Points.Count; i++)
        {
            var segment = Vector3.Distance(path.Points[i], path.Points[i + 1]);
            if (segment > 0 && arc <= walked + segment + 1e-6f)
                return (Vector3.Lerp(path.Points[i], path.Points[i + 1], Math.Clamp((arc - walked) / segment, 0f, 1f)), path.Faces[i + 1]);
            walked += segment;
        }
        return (path.Points[^1], path.Faces[^1]);
    }
}
