using System.Numerics;
using Danslicer.Core.Geometry;

namespace Danslicer.Core.Supports.Generation;

/// <summary>
/// Contact placement for a <b>painted</b> support region (DESIGN 8.3): an even grid over the
/// painted surface rather than the Poisson sampling used for whole-object generation.
///
/// <para>Painting is an explicit instruction, so this pass ignores the overhang angle — every
/// painted face that has any downward component receives its share of the grid. It is also why
/// the region must be sampled <i>on the surface</i>: the old lattice path cast rays straight up
/// and could only ever reach the lowest painted face in a given XY column, which is why painting
/// three stacked faces produced supports on one of them.</para>
///
/// <para>Each edge-connected patch is handled on its own:</para>
/// <list type="bullet">
/// <item>A patch taller than one vertical pitch is cut into horizontal bands at exactly that
/// pitch, and each band's surface contour is walked at even arc length. Rows are therefore
/// evenly spaced in Z by construction — the user's stated priority — and the horizontal spacing
/// follows the surface, so a swept or tapered wall (which no staggered lattice fits) comes out
/// evenly covered.</item>
/// <item>A patch shorter than one pitch is effectively an underside, where banding degenerates;
/// it gets an XY lattice clipped to that patch's own triangles.</item>
/// </list>
///
/// <para>A pure function of its inputs. Ordering is deterministic — patches by lowest face
/// index, rows bottom-up, contours by their first point — so generation stays reproducible.</para>
/// </summary>
public static class RegionGridSampler
{
    /// <summary>
    /// How much of the outward normal must point down for a painted face to be usable. A face
    /// pointing up cannot be reached from below at all, and one within this of vertical would
    /// only meet a support tangentially.
    /// </summary>
    private const float MinDownwardComponent = 1e-3f;

    public static IReadOnlyList<TipCandidate> Sample(
        Mesh mesh, IReadOnlySet<int> faces, TipPlacementParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(faces);
        ArgumentNullException.ThrowIfNull(parameters);
        var options = parameters.RegionGrid
            ?? throw new ArgumentException("Region grid options are required.", nameof(parameters));

        var vertical = MathF.Max(options.VerticalPitchMm, 1e-3f);
        var horizontal = MathF.Max(options.HorizontalPitchMm, 1e-3f);
        var eligible = faces
            .Where(face => (uint)face < (uint)mesh.TriangleCount &&
                           mesh.FaceNormals[face].Z < -MinDownwardComponent)
            .ToHashSet();
        if (eligible.Count == 0) return Array.Empty<TipCandidate>();

        var results = new List<TipCandidate>();
        var placed = new PointGrid(MathF.Min(vertical, horizontal) * 0.5f);
        var dedup = MathF.Min(vertical, horizontal) * 0.5f;

        foreach (var patch in Patches(mesh, eligible))
        {
            SupportGenerationMonitor.Check();
            var before = results.Count;
            var (minZ, maxZ) = ZExtent(mesh, patch);
            if (maxZ - minZ >= vertical)
                SampleBands(mesh, patch, parameters, options, vertical, horizontal, minZ, maxZ,
                    placed, dedup, results);
            else
                SampleLattice(mesh, patch, parameters, horizontal, placed, dedup, results);

            // A patch the grid missed entirely — a sliver narrower than the pitch in both axes —
            // still needs holding up. One contact at its lowest point is the minimum honest
            // answer to "I painted this".
            if (results.Count == before && TryLowestPoint(mesh, patch, out var point, out var face))
                Emit(mesh, parameters, point, face, placed, dedup, results);
        }

        return results;
    }

    /// <summary>Edge-connected groups within the painted set, ordered by their lowest face index.</summary>
    private static List<List<int>> Patches(Mesh mesh, HashSet<int> eligible)
    {
        var adjacency = SupportRegionSelection.FaceAdjacencyOf(mesh);
        var seen = new HashSet<int>();
        var patches = new List<List<int>>();
        foreach (var seed in eligible.OrderBy(face => face))
        {
            SupportGenerationMonitor.Check();
            if (!seen.Add(seed)) continue;
            var patch = new List<int> { seed };
            var stack = new Stack<int>();
            stack.Push(seed);
            while (stack.Count > 0)
            {
                SupportGenerationMonitor.Check();
                foreach (var next in adjacency[stack.Pop()])
                {
                    SupportGenerationMonitor.Check();
                    if (!eligible.Contains(next) || !seen.Add(next)) continue;
                    patch.Add(next);
                    stack.Push(next);
                }
            }
            patch.Sort();
            patches.Add(patch);
        }
        return patches;
    }

    private static (float Min, float Max) ZExtent(Mesh mesh, List<int> patch)
    {
        float min = float.PositiveInfinity, max = float.NegativeInfinity;
        foreach (var face in patch)
        {
            SupportGenerationMonitor.Check();
            mesh.GetTriangle(face, out var a, out var b, out var c);
            min = MathF.Min(min, MathF.Min(a.Z, MathF.Min(b.Z, c.Z)));
            max = MathF.Max(max, MathF.Max(a.Z, MathF.Max(b.Z, c.Z)));
        }
        return (min, max);
    }

    /// <summary>
    /// Horizontal bands at exactly the vertical pitch, each walked at even arc length along the
    /// surface. The band heights come from a lattice anchored at <see cref="RegionGridOptions.AnchorZ"/>,
    /// offset by half a pitch so no row lands on the plate or on a patch's own boundary.
    /// </summary>
    private static void SampleBands(
        Mesh mesh, List<int> patch, TipPlacementParameters parameters, RegionGridOptions options,
        float vertical, float horizontal, float minZ, float maxZ,
        PointGrid placed, float dedup, List<TipCandidate> results)
    {
        var first = (int)MathF.Ceiling((minZ - options.AnchorZ) / vertical - 0.5f);
        var last = (int)MathF.Floor((maxZ - options.AnchorZ) / vertical - 0.5f);
        for (var row = first; row <= last; row++)
        {
            SupportGenerationMonitor.Check();
            var z = options.AnchorZ + (row + 0.5f) * vertical;
            if (z <= minZ || z >= maxZ) continue;
            foreach (var contour in Contours(mesh, patch, z, horizontal))
                WalkContour(mesh, parameters, contour, horizontal, placed, dedup, results);
        }
    }

    /// <summary>One straight piece of a band: the plane crossing of a single triangle.</summary>
    private readonly record struct BandSegment(Vector3 A, Vector3 B, int Face);

    /// <summary>A chained run of band segments, with the face each interval came from.</summary>
    private sealed class Contour
    {
        public List<Vector3> Points { get; } = [];
        public List<int> Faces { get; } = [];
    }

    private static List<Contour> Contours(Mesh mesh, List<int> patch, float z, float horizontal)
    {
        var segments = new List<BandSegment>();
        foreach (var face in patch)
        {
            SupportGenerationMonitor.Check();
            mesh.GetTriangle(face, out var a, out var b, out var c);
            if (TryCrossTriangle(a, b, c, z, out var p, out var q))
                segments.Add(new BandSegment(p, q, face));
        }
        if (segments.Count == 0) return [];

        var tolerance = MathF.Max(1e-4f, horizontal * 1e-3f);
        var ends = new Dictionary<(long, long), List<(int Segment, int End)>>();
        for (var i = 0; i < segments.Count; i++)
        {
            SupportGenerationMonitor.Check();
            Register(ends, Key(segments[i].A, tolerance), i, 0);
            Register(ends, Key(segments[i].B, tolerance), i, 1);
        }

        var used = new bool[segments.Count];
        var contours = new List<Contour>();

        // Open runs first, started from their free end, so a chain is walked once end to end;
        // whatever is left is a closed loop and can start anywhere.
        for (var pass = 0; pass < 2; pass++)
        {
            SupportGenerationMonitor.Check();
            for (var i = 0; i < segments.Count; i++)
            {
                SupportGenerationMonitor.Check();
                if (used[i]) continue;
                var start = -1;
                if (pass == 0)
                {
                    if (Degree(ends, Key(segments[i].A, tolerance)) == 1) start = 0;
                    else if (Degree(ends, Key(segments[i].B, tolerance)) == 1) start = 1;
                    if (start < 0) continue;
                }
                else start = 0;
                contours.Add(Walk(segments, ends, used, i, start, tolerance));
            }
        }
        return contours;
    }

    private static Contour Walk(List<BandSegment> segments,
        Dictionary<(long, long), List<(int Segment, int End)>> ends, bool[] used,
        int segment, int end, float tolerance)
    {
        var contour = new Contour();
        var current = segment;
        var from = end;
        contour.Points.Add(from == 0 ? segments[current].A : segments[current].B);
        while (true)
        {
            SupportGenerationMonitor.Check();
            used[current] = true;
            var to = from == 0 ? segments[current].B : segments[current].A;
            contour.Points.Add(to);
            contour.Faces.Add(segments[current].Face);

            var next = -1;
            var nextEnd = 0;
            if (ends.TryGetValue(Key(to, tolerance), out var touching))
            {
                foreach (var (candidate, candidateEnd) in touching)
                {
                    SupportGenerationMonitor.Check();
                    if (candidate == current || used[candidate]) continue;
                    next = candidate;
                    nextEnd = candidateEnd;
                    break;
                }
            }
            if (next < 0) return contour;
            current = next;
            from = nextEnd;
        }
    }

    /// <summary>
    /// Even arc-length samples along one contour, centred on it so a short run is not pushed to
    /// one end. A closed loop is spaced by division instead, which keeps its first and last
    /// contacts a full pitch apart rather than doubling up where the loop meets itself.
    /// </summary>
    private static void WalkContour(Mesh mesh, TipPlacementParameters parameters, Contour contour,
        float horizontal, PointGrid placed, float dedup, List<TipCandidate> results)
    {
        var lengths = new float[contour.Faces.Count];
        var total = 0f;
        for (var i = 0; i < contour.Faces.Count; i++)
        {
            SupportGenerationMonitor.Check();
            lengths[i] = Vector3.Distance(contour.Points[i], contour.Points[i + 1]);
            total += lengths[i];
        }
        if (total <= 1e-6f) return;

        var closed = contour.Points.Count > 2 &&
                     Vector3.Distance(contour.Points[0], contour.Points[^1]) <= 1e-4f;
        float start;
        int count;
        if (closed)
        {
            count = Math.Max(1, (int)MathF.Round(total / horizontal));
            horizontal = total / count;
            start = 0f;
        }
        else
        {
            count = (int)MathF.Floor(total / horizontal) + 1;
            start = (total - (count - 1) * horizontal) * 0.5f;
        }

        for (var i = 0; i < count; i++)
        {
            SupportGenerationMonitor.Check();
            var distance = start + i * horizontal;
            if (!TryPointAt(contour, lengths, distance, out var point, out var face)) continue;
            Emit(mesh, parameters, point, face, placed, dedup, results);
        }
    }

    private static bool TryPointAt(Contour contour, float[] lengths, float distance,
        out Vector3 point, out int face)
    {
        var walked = 0f;
        for (var i = 0; i < lengths.Length; i++)
        {
            SupportGenerationMonitor.Check();
            if (distance <= walked + lengths[i] || i == lengths.Length - 1)
            {
                var t = lengths[i] <= 1e-9f ? 0f : Math.Clamp((distance - walked) / lengths[i], 0f, 1f);
                point = Vector3.Lerp(contour.Points[i], contour.Points[i + 1], t);
                face = contour.Faces[i];
                return true;
            }
            walked += lengths[i];
        }
        point = default;
        face = -1;
        return false;
    }

    /// <summary>
    /// The underside case: a global XY lattice at the horizontal pitch, kept where it falls
    /// inside one of the patch's own triangles. Restricting the test to the patch is what lets
    /// several painted undersides stacked in the same column all receive contacts.
    /// </summary>
    private static void SampleLattice(Mesh mesh, List<int> patch, TipPlacementParameters parameters,
        float horizontal, PointGrid placed, float dedup, List<TipCandidate> results)
    {
        foreach (var face in patch)
        {
            SupportGenerationMonitor.Check();
            mesh.GetTriangle(face, out var a, out var b, out var c);
            var area2 = (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
            if (MathF.Abs(area2) < 1e-9f) continue;

            var minX = MathF.Min(a.X, MathF.Min(b.X, c.X));
            var maxX = MathF.Max(a.X, MathF.Max(b.X, c.X));
            var minY = MathF.Min(a.Y, MathF.Min(b.Y, c.Y));
            var maxY = MathF.Max(a.Y, MathF.Max(b.Y, c.Y));
            var firstI = (int)MathF.Ceiling(minX / horizontal - 0.5f);
            var lastI = (int)MathF.Floor(maxX / horizontal - 0.5f);
            var firstJ = (int)MathF.Ceiling(minY / horizontal - 0.5f);
            var lastJ = (int)MathF.Floor(maxY / horizontal - 0.5f);

            for (var i = firstI; i <= lastI; i++)
            {
                SupportGenerationMonitor.Check();
                for (var j = firstJ; j <= lastJ; j++)
                {
                    SupportGenerationMonitor.Check();
                    var x = (i + 0.5f) * horizontal;
                    var y = (j + 0.5f) * horizontal;
                    var u = ((x - a.X) * (c.Y - a.Y) - (y - a.Y) * (c.X - a.X)) / area2;
                    var v = ((b.X - a.X) * (y - a.Y) - (b.Y - a.Y) * (x - a.X)) / area2;
                    if (u < 0f || v < 0f || u + v > 1f) continue;
                    var z = a.Z + (b.Z - a.Z) * u + (c.Z - a.Z) * v;
                    Emit(mesh, parameters, new Vector3(x, y, z), face, placed, dedup, results);
                }
            }
        }
    }

    private static bool TryLowestPoint(Mesh mesh, List<int> patch, out Vector3 point, out int face)
    {
        point = default;
        face = -1;
        var best = float.PositiveInfinity;
        foreach (var candidate in patch)
        {
            SupportGenerationMonitor.Check();
            mesh.GetTriangle(candidate, out var a, out var b, out var c);
            var centroid = (a + b + c) / 3f;
            if (centroid.Z >= best) continue;
            best = centroid.Z;
            point = centroid;
            face = candidate;
        }
        return face >= 0;
    }

    private static void Emit(Mesh mesh, TipPlacementParameters parameters, Vector3 point, int face,
        PointGrid placed, float dedup, List<TipCandidate> results)
    {
        if (placed.AnyWithin(point, dedup)) return;
        placed.Add(point);
        var outward = mesh.FaceNormals[face];
        var inward = -outward;
        var length = inward.Length();
        inward = length > 1e-12f ? inward / length : -Vector3.UnitZ;
        results.Add(new TipCandidate(point, inward, parameters.TipDiameterMm,
            Score: 5f, TipStrategy.RegionGrid, face, parameters.TipShape, parameters.ConeLengthMm,
            parameters.BallDiameterMm, MathF.Max(parameters.PenetrationDepthMm, 0f),
            TipNormalLeadIn: MathF.Max(parameters.TipNormalLeadInMm, 0f)));
    }

    /// <summary>The crossing of one triangle by the plane z = <paramref name="z"/>.</summary>
    private static bool TryCrossTriangle(Vector3 a, Vector3 b, Vector3 c, float z,
        out Vector3 p, out Vector3 q)
    {
        Vector3 first = default, second = default;
        var count = 0;
        Cross(a, b, ref first, ref second, ref count, z);
        Cross(b, c, ref first, ref second, ref count, z);
        Cross(c, a, ref first, ref second, ref count, z);
        if (count == 2)
        {
            p = first;
            q = second;
            return Vector3.DistanceSquared(p, q) > 1e-12f;
        }
        p = default;
        q = default;
        return false;
    }

    private static void Cross(Vector3 u, Vector3 v, ref Vector3 first, ref Vector3 second,
        ref int count, float z)
    {
        // Half-open on the upper end so a vertex exactly on the plane is counted once.
        if (u.Z <= z == v.Z <= z) return;
        if (count >= 2)
        {
            count++;
            return;
        }
        var t = (z - u.Z) / (v.Z - u.Z);
        var point = Vector3.Lerp(u, v, t);
        if (count == 0) first = point;
        else second = point;
        count++;
    }

    private static (long, long) Key(Vector3 p, float tolerance) =>
        ((long)MathF.Round(p.X / tolerance), (long)MathF.Round(p.Y / tolerance));

    private static void Register(Dictionary<(long, long), List<(int, int)>> ends,
        (long, long) key, int segment, int end)
    {
        if (!ends.TryGetValue(key, out var list)) ends[key] = list = [];
        list.Add((segment, end));
    }

    private static int Degree(Dictionary<(long, long), List<(int, int)>> ends, (long, long) key) =>
        ends.TryGetValue(key, out var list) ? list.Count : 0;
}
