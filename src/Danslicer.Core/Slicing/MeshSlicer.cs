using System.Numerics;
using Clipper2Lib;
using Danslicer.Core.Geometry;

namespace Danslicer.Core.Slicing;

/// <summary>
/// Intersects triangle meshes with horizontal planes and chains the resulting segments into closed
/// contours. Contours are returned in Clipper integer units, <see cref="UnitsPerMm"/> per millimetre.
/// Outer loops wind counter-clockwise (positive area), holes clockwise, derived from face normals.
/// </summary>
public static class MeshSlicer
{
    public const double UnitsPerMm = 1000.0; // 1 µm resolution

    /// <summary>A mesh transformed to world space in double precision with per-triangle Z ranges.</summary>
    public sealed class PreparedMesh
    {
        public double[] X { get; }
        public double[] Y { get; }
        public double[] Z { get; }
        public int[] Indices { get; }
        public double[] TriMinZ { get; }
        public double[] TriMaxZ { get; }
        /// <summary>World-space face normal XY components, used to orient segments.</summary>
        public Vector2[] NormalXy { get; }
        public double MinX { get; }
        public double MaxX { get; }
        public double MinY { get; }
        public double MaxY { get; }
        public double MinZ { get; }
        public double MaxZ { get; }
        public int TriangleCount => Indices.Length / 3;

        public PreparedMesh(Mesh mesh, Matrix4x4 world)
        {
            var n = mesh.VertexCount;
            X = new double[n];
            Y = new double[n];
            Z = new double[n];
            var minX = double.PositiveInfinity; var maxX = double.NegativeInfinity;
            var minY = double.PositiveInfinity; var maxY = double.NegativeInfinity;
            var minZ = double.PositiveInfinity; var maxZ = double.NegativeInfinity;
            for (int i = 0; i < n; i++)
            {
                var p = Vector3.Transform(mesh.Positions[i], world);
                X[i] = p.X;
                Y[i] = p.Y;
                Z[i] = p.Z;
                if (p.X < minX) minX = p.X;
                if (p.X > maxX) maxX = p.X;
                if (p.Y < minY) minY = p.Y;
                if (p.Y > maxY) maxY = p.Y;
                if (p.Z < minZ) minZ = p.Z;
                if (p.Z > maxZ) maxZ = p.Z;
            }
            MinX = minX;
            MaxX = maxX;
            MinY = minY;
            MaxY = maxY;
            MinZ = minZ;
            MaxZ = maxZ;

            Indices = mesh.Indices;
            var t = mesh.TriangleCount;
            TriMinZ = new double[t];
            TriMaxZ = new double[t];
            NormalXy = new Vector2[t];
            // Normals transform with the inverse-transpose; for a rigid transform plus uniform scale the
            // linear part suffices. Non-uniform scale is handled by recomputing from world vertices.
            for (int tri = 0; tri < t; tri++)
            {
                int ia = Indices[tri * 3], ib = Indices[tri * 3 + 1], ic = Indices[tri * 3 + 2];
                TriMinZ[tri] = Math.Min(Z[ia], Math.Min(Z[ib], Z[ic]));
                TriMaxZ[tri] = Math.Max(Z[ia], Math.Max(Z[ib], Z[ic]));

                var ax = X[ib] - X[ia]; var ay = Y[ib] - Y[ia]; var az = Z[ib] - Z[ia];
                var bx = X[ic] - X[ia]; var by = Y[ic] - Y[ia]; var bz = Z[ic] - Z[ia];
                var nx = ay * bz - az * by;
                var ny = az * bx - ax * bz;
                NormalXy[tri] = new Vector2((float)nx, (float)ny);
            }
        }
    }

    /// <summary>
    /// Buckets triangle indices by layer for fast per-layer lookup. Layer i spans
    /// [i * layerHeight, (i + 1) * layerHeight) and is sliced at its mid-height.
    /// </summary>
    public static List<int>[] BucketTriangles(PreparedMesh mesh, double layerHeight, int layerCount)
    {
        var buckets = new List<int>[layerCount];
        for (int t = 0; t < mesh.TriangleCount; t++)
        {
            var minZ = mesh.TriMinZ[t];
            var maxZ = mesh.TriMaxZ[t];
            if (maxZ - minZ < 1e-12) continue; // horizontal triangle never produces a segment
            // Slice planes at (i + 0.5) * h. Triangle intersects plane i when minZ < z_i < maxZ.
            var first = (int)Math.Floor(minZ / layerHeight - 0.5) + 1;
            var last = (int)Math.Ceiling(maxZ / layerHeight - 0.5) - 1;
            first = Math.Max(first, 0);
            last = Math.Min(last, layerCount - 1);
            for (int i = first; i <= last; i++)
                (buckets[i] ??= new List<int>()).Add(t);
        }
        return buckets;
    }

    /// <summary>Collects raw oriented segments (in Clipper units) for one plane.</summary>
    public static void CollectSegments(PreparedMesh mesh, IReadOnlyList<int>? triangles, double z, List<Segment> output)
    {
        if (triangles is null) return;
        // stackalloc must stay outside the loop: stack space is reclaimed on return, not per iteration.
        Span<(double x, double y)> pts = stackalloc (double, double)[3];
        foreach (var t in triangles)
        {
            if (z <= mesh.TriMinZ[t] || z >= mesh.TriMaxZ[t]) continue;
            int ia = mesh.Indices[t * 3], ib = mesh.Indices[t * 3 + 1], ic = mesh.Indices[t * 3 + 2];

            var count = 0;
            Cross(mesh, ia, ib, z, pts, ref count);
            Cross(mesh, ib, ic, z, pts, ref count);
            Cross(mesh, ic, ia, z, pts, ref count);
            if (count != 2) continue;

            var (x0, y0) = pts[0];
            var (x1, y1) = pts[1];
            // Solid lies to the left when walking along cross(+Z, normal) = (-ny, nx), giving
            // counter-clockwise outer loops and clockwise holes.
            var n = mesh.NormalXy[t];
            var dir = (x1 - x0) * -n.Y + (y1 - y0) * n.X;
            var a = new Point64((long)Math.Round(x0 * UnitsPerMm), (long)Math.Round(y0 * UnitsPerMm));
            var b = new Point64((long)Math.Round(x1 * UnitsPerMm), (long)Math.Round(y1 * UnitsPerMm));
            if (a == b) continue;
            output.Add(dir >= 0 ? new Segment(a, b) : new Segment(b, a));
        }
    }

    private static void Cross(PreparedMesh m, int i, int j, double z, Span<(double, double)> pts, ref int count)
    {
        var zi = m.Z[i];
        var zj = m.Z[j];
        if ((zi < z) == (zj < z)) return; // both on the same side, or one exactly on the plane counted once
        if (count >= 2) return;
        var t = (z - zi) / (zj - zi);
        pts[count++] = (m.X[i] + (m.X[j] - m.X[i]) * t, m.Y[i] + (m.Y[j] - m.Y[i]) * t);
    }

    public readonly record struct Segment(Point64 A, Point64 B);

    /// <summary>
    /// Chains segments into closed polygons. Segments that cannot be closed within
    /// <paramref name="joinTolerance"/> units are discarded.
    /// </summary>
    public static Paths64 ChainSegments(List<Segment> segments, long joinTolerance = 5)
    {
        var result = new Paths64();
        if (segments.Count == 0) return result;

        // Index segments by start point for O(1) chaining. Quantise to the join tolerance so
        // near-coincident endpoints from adjacent triangles snap together.
        var q = Math.Max(joinTolerance, 1);
        var byStart = new Dictionary<(long, long), List<int>>(segments.Count);
        for (int i = 0; i < segments.Count; i++)
        {
            var key = Key(segments[i].A, q);
            if (!byStart.TryGetValue(key, out var list)) byStart[key] = list = new List<int>(2);
            list.Add(i);
        }

        var used = new bool[segments.Count];
        var path = new Path64();
        for (int start = 0; start < segments.Count; start++)
        {
            if (used[start]) continue;
            path.Clear();
            used[start] = true;
            path.Add(segments[start].A);
            var current = segments[start].B;
            var startPoint = segments[start].A;
            var closed = false;

            while (true)
            {
                if (Near(current, startPoint, q) && path.Count >= 3)
                {
                    closed = true;
                    break;
                }
                path.Add(current);
                if (!TryTakeNext(byStart, used, segments, current, q, out var nextIndex))
                    break;
                current = segments[nextIndex].B;
            }

            if (closed)
            {
                var clean = RemoveDuplicates(path);
                if (clean.Count >= 3) result.Add(clean);
            }
        }
        return result;
    }

    private static bool TryTakeNext(Dictionary<(long, long), List<int>> byStart, bool[] used, List<Segment> segments,
        Point64 from, long q, out int index)
    {
        // Check the cell of the point and its neighbours so tolerance spans quantisation boundaries.
        var (kx, ky) = Key(from, q);
        index = -1;
        double bestDistance = double.PositiveInfinity;
        for (long dx = -1; dx <= 1; dx++)
        for (long dy = -1; dy <= 1; dy++)
        {
            if (!byStart.TryGetValue((kx + dx, ky + dy), out var list)) continue;
            foreach (var i in list)
            {
                if (used[i] || !Near(segments[i].A, from, q)) continue;
                var deltaX = (double)segments[i].A.X - from.X;
                var deltaY = (double)segments[i].A.Y - from.Y;
                var distance = deltaX * deltaX + deltaY * deltaY;
                if (distance >= bestDistance) continue;
                bestDistance = distance;
                index = i;
            }
        }
        if (index < 0) return false;
        used[index] = true;
        return true;
    }

    private static (long, long) Key(Point64 p, long q) => (p.X / q, p.Y / q);

    private static bool Near(Point64 a, Point64 b, long tol) =>
        Math.Abs(a.X - b.X) <= tol && Math.Abs(a.Y - b.Y) <= tol;

    private static Path64 RemoveDuplicates(Path64 path)
    {
        var clean = new Path64(path.Count);
        foreach (var p in path)
            if (clean.Count == 0 || clean[^1] != p) clean.Add(p);
        if (clean.Count > 1 && clean[0] == clean[^1]) clean.RemoveAt(clean.Count - 1);
        return clean;
    }

    /// <summary>Unions loops into clean polygons and applies XY compensation.</summary>
    public static Paths64 Finish(Paths64 loops, double xyCompensationMm)
    {
        if (loops.Count == 0) return loops;
        var merged = Clipper.Union(loops, FillRule.NonZero);
        if (Math.Abs(xyCompensationMm) > 1e-9 && merged.Count > 0)
            merged = Clipper.InflatePaths(merged, xyCompensationMm * UnitsPerMm, JoinType.Round, EndType.Polygon);
        return merged;
    }

    /// <summary>Finished cross-section polygons at an arbitrary world-space Z plane.</summary>
    public static Paths64 PolygonsAt(PreparedMesh mesh, double z)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        if (z <= mesh.MinZ || z >= mesh.MaxZ) return new Paths64();

        var segments = new List<Segment>();
        Span<(double x, double y)> points = stackalloc (double, double)[3];
        for (int triangle = 0; triangle < mesh.TriangleCount; triangle++)
        {
            if (z <= mesh.TriMinZ[triangle] || z >= mesh.TriMaxZ[triangle]) continue;
            int ia = mesh.Indices[triangle * 3], ib = mesh.Indices[triangle * 3 + 1],
                ic = mesh.Indices[triangle * 3 + 2];
            var count = 0;
            Cross(mesh, ia, ib, z, points, ref count);
            Cross(mesh, ib, ic, z, points, ref count);
            Cross(mesh, ic, ia, z, points, ref count);
            if (count != 2) continue;

            var (x0, y0) = points[0];
            var (x1, y1) = points[1];
            var normal = mesh.NormalXy[triangle];
            var direction = (x1 - x0) * -normal.Y + (y1 - y0) * normal.X;
            var a = new Point64((long)Math.Round(x0 * UnitsPerMm), (long)Math.Round(y0 * UnitsPerMm));
            var b = new Point64((long)Math.Round(x1 * UnitsPerMm), (long)Math.Round(y1 * UnitsPerMm));
            if (a == b) continue;
            segments.Add(direction >= 0 ? new Segment(a, b) : new Segment(b, a));
        }
        return Finish(ChainSegments(segments), 0);
    }

    /// <summary>Total area in square millimetres, holes subtracted.</summary>
    public static double AreaMm2(Paths64 paths)
    {
        double sum = 0;
        foreach (var p in paths) sum += Clipper.Area(p);
        return Math.Abs(sum) / (UnitsPerMm * UnitsPerMm);
    }

    /// <summary>
    /// Contours at every layer mid-height from Z = 0 up to the mesh top. Empty layers are
    /// included so indices match the slicer's numbering. No XY compensation.
    /// Additive helper; does not change any existing member.
    /// </summary>
    public static List<Paths64> LayerPolygons(PreparedMesh mesh, double layerHeight)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        var result = new List<Paths64>();
        if (layerHeight <= 1e-12 || mesh.MaxZ <= 1e-9) return result;

        var layerCount = (int)Math.Ceiling(mesh.MaxZ / layerHeight - 1e-6);
        if (layerCount <= 0) return result;

        var buckets = BucketTriangles(mesh, layerHeight, layerCount);
        var segments = new List<Segment>();
        for (int i = 0; i < layerCount; i++)
        {
            var z = (i + 0.5) * layerHeight;
            segments.Clear();
            CollectSegments(mesh, buckets[i], z, segments);
            result.Add(Finish(ChainSegments(segments), 0));
        }
        return result;
    }

    /// <summary>
    /// Per-layer XY that is not supported by the previous layer. The previous contours are
    /// inflated by <paramref name="inflateMm"/> (overhang slope plus a sliver epsilon) before
    /// the difference, so self-supporting rims are not islands. A layer with nothing below
    /// it (including layer 0) is returned as-is. Plate-supported layer 0 is the caller's
    /// decision — this helper does not know about the plate.
    /// </summary>
    public static List<Paths64> NewbornIslands(IReadOnlyList<Paths64> layers, double inflateMm)
    {
        ArgumentNullException.ThrowIfNull(layers);
        var result = new List<Paths64>(layers.Count);
        Paths64? previous = null;
        foreach (var polygons in layers)
        {
            Paths64 newborn;
            if (previous is null || previous.Count == 0)
            {
                newborn = polygons;
            }
            else
            {
                var supported = Clipper.InflatePaths(
                    previous, inflateMm * UnitsPerMm, JoinType.Round, EndType.Polygon);
                newborn = Clipper.Difference(polygons, supported, FillRule.NonZero);
            }
            result.Add(newborn);
            previous = polygons;
        }
        return result;
    }
}
