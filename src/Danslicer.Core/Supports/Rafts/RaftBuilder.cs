using System.Numerics;
using Clipper2Lib;
using Danslicer.Core.Slicing;

namespace Danslicer.Core.Supports.Rafts;

/// <summary>
/// Builds a raft's shape from the object's support feet (SUPPORT-GEOMETRY-SPEC "Rafts"). Pure
/// geometry: feet in, the top outline and its sloped sections out, all in Clipper units
/// (<see cref="MeshSlicer.UnitsPerMm"/>) so the slicer unions them straight into a layer.
/// </summary>
public static class RaftBuilder
{
    /// <summary>
    /// Which feet are joined by a bar in a Web raft, as index pairs with A &lt; B, each pair once:
    /// the edges of the feet's Delaunay triangulation (user, 2026-09-09: Delaunay works better
    /// than rays), dropping pairs further apart than <see cref="RaftParameters.MaxBarLength"/>.
    /// </summary>
    public static IReadOnlyList<(int A, int B)> Neighbours(IReadOnlyList<Vector2> feet, RaftParameters parameters)
    {
        parameters = parameters.Normalize();
        var pairs = DelaunayPairs(feet);
        var maxLength = parameters.MaxBarLength;
        var result = new List<(int, int)>();
        foreach (var (a, b) in pairs.OrderBy(p => p.A).ThenBy(p => p.B))
        {
            if (maxLength > 0 && Vector2.Distance(feet[a], feet[b]) > maxLength) continue;
            result.Add((a, b));
        }
        return result;
    }

    /// <summary>
    /// The raft's outline at its top, in Clipper units: the Plate silhouette or the Web's discs and
    /// bars. Empty when there are no feet.
    /// </summary>
    public static Paths64 TopOutline(IReadOnlyList<Vector2> feet, RaftParameters parameters)
    {
        parameters = parameters.Normalize();
        if (feet.Count == 0) return new Paths64();
        var discRadius = parameters.DiscDiameter * 0.5;
        var shapes = new Paths64();
        foreach (var foot in feet) shapes.Add(Circle(foot, discRadius));

        if (parameters.Type == RaftType.Web)
        {
            foreach (var (a, b) in Neighbours(feet, parameters))
                shapes.Add(Bar(feet[a], feet[b], parameters.BarWidth));
            return Clipper.Union(shapes, FillRule.NonZero);
        }

        // Plate: the union of the foot discs, grown by the margin, then closed (out by a closing
        // radius and back in) so a gap between feet up to the bridging distance fills in and a
        // wider one stays a notch. Round discs pinch: the neck two grown discs share is thinner
        // than their gap is wide, so the closing radius is b/2 + b²/(8·R0) rather than b/2 —
        // exactly the radius at which two margin-grown discs of radius R0 whose edges are b
        // apart still touch after the inward step, and discs further apart do not. The growth
        // and the closing's outward half are one inflate. Holes are dropped: the interior is
        // solid ("fills it all in", user 2026-09-09).
        var union = Clipper.Union(shapes, FillRule.NonZero);
        var grownRadius = discRadius + parameters.Margin;
        var bridging = parameters.BridgingDistance;
        var closing = bridging <= 0 ? 0 : bridging * 0.5 + bridging * bridging / (8 * grownRadius);
        var grown = Clipper.InflatePaths(union, (parameters.Margin + closing) * MeshSlicer.UnitsPerMm,
            JoinType.Round, EndType.Polygon);
        var plate = closing <= 0
            ? grown
            : Clipper.InflatePaths(grown, -closing * MeshSlicer.UnitsPerMm, JoinType.Round, EndType.Polygon);
        var solid = new Paths64();
        foreach (var path in Clipper.Union(plate, FillRule.NonZero))
            if (Clipper.IsPositive(path)) solid.Add(path);
        return solid;
    }

    /// <summary>
    /// How far the scraper lip stands outside the footprint at height <paramref name="z"/>, in
    /// millimetres: nothing at the plate, growing at the edge angle to <see cref="LipWidth"/>
    /// at the top, so the bottom of the lip is narrower than the top (user, 2026-09-09).
    /// </summary>
    public static double EdgeOffsetAt(RaftParameters parameters, double z)
    {
        parameters = parameters.Normalize();
        if (z <= 0) return 0;
        var t = Math.Min(z / parameters.Thickness, 1.0);
        return LipWidth(parameters) * t;
    }

    /// <summary>The lip's overhang at the raft's top: Thickness / tan(edge angle); 0 at 90°.</summary>
    public static double LipWidth(RaftParameters parameters)
    {
        parameters = parameters.Normalize();
        if (parameters.EdgeAngleDegrees >= 90f - 1e-3f) return 0;
        return parameters.Thickness / Math.Tan(parameters.EdgeAngleDegrees * Math.PI / 180.0);
    }

    /// <summary>
    /// The raft's cross-section at height <paramref name="z"/>: the footprint, with the scraper
    /// lip pushing its OUTER contours out by <see cref="EdgeOffsetAt"/>. Holes (the voids a Web's
    /// bars enclose) keep their shape: the lip exists only on the outside of the raft (user,
    /// 2026-09-09). Empty below the plate and from the raft's top upward.
    /// </summary>
    public static Paths64 SectionAt(Paths64 footprint, RaftParameters parameters, double z)
    {
        parameters = parameters.Normalize();
        if (footprint.Count == 0 || z < 0 || z >= parameters.Thickness) return new Paths64();
        var offset = EdgeOffsetAt(parameters, z);
        if (offset <= 1e-9) return new Paths64(footprint);
        return WithLip(footprint, offset);
    }

    /// <summary>The footprint with its outer contours pushed out by <paramref name="offsetMm"/>, holes untouched.</summary>
    public static Paths64 WithLip(Paths64 footprint, double offsetMm)
    {
        var outers = new Paths64();
        var holes = new Paths64();
        foreach (var path in footprint) (Clipper.IsPositive(path) ? outers : holes).Add(path);
        var lipped = Clipper.InflatePaths(outers, offsetMm * MeshSlicer.UnitsPerMm, JoinType.Round, EndType.Polygon);
        if (holes.Count == 0) return lipped;
        lipped.AddRange(holes);
        return Clipper.Union(lipped, FillRule.NonZero);
    }

    // ----- Neighbour rules -----

    /// <summary>
    /// Bowyer–Watson Delaunay triangulation; every triangle edge between real feet is a pair.
    /// Fewer than three feet, or feet all on one line, fall back to a chain along that line.
    /// </summary>
    internal static HashSet<(int A, int B)> DelaunayPairs(IReadOnlyList<Vector2> feet)
    {
        var pairs = new HashSet<(int, int)>();
        var n = feet.Count;
        if (n < 2) return pairs;
        if (n == 2) { pairs.Add((0, 1)); return pairs; }

        var min = new Vector2(float.MaxValue);
        var max = new Vector2(float.MinValue);
        foreach (var f in feet) { min = Vector2.Min(min, f); max = Vector2.Max(max, f); }
        var span = MathF.Max(max.X - min.X, max.Y - min.Y);
        if (span < 1e-6f) span = 1f;
        var mid = (min + max) * 0.5f;
        var points = new List<Vector2>(feet)
        {
            new(mid.X - 20 * span, mid.Y - span),
            new(mid.X, mid.Y + 20 * span),
            new(mid.X + 20 * span, mid.Y - span),
        };

        var triangles = new List<(int A, int B, int C)> { (n, n + 1, n + 2) };
        for (int p = 0; p < n; p++)
        {
            var point = points[p];
            var bad = new List<(int A, int B, int C)>();
            foreach (var t in triangles)
                if (InCircumcircle(points[t.A], points[t.B], points[t.C], point)) bad.Add(t);

            var boundary = new Dictionary<(int, int), int>();
            foreach (var t in bad)
            {
                foreach (var e in new[] { Edge(t.A, t.B), Edge(t.B, t.C), Edge(t.C, t.A) })
                    boundary[e] = boundary.TryGetValue(e, out var count) ? count + 1 : 1;
            }
            foreach (var t in bad) triangles.Remove(t);
            foreach (var (edge, count) in boundary)
                if (count == 1) triangles.Add((edge.Item1, edge.Item2, p));
        }

        foreach (var t in triangles)
        {
            if (t.A >= n || t.B >= n || t.C >= n) continue;
            pairs.Add(Edge(t.A, t.B));
            pairs.Add(Edge(t.B, t.C));
            pairs.Add(Edge(t.C, t.A));
        }
        if (pairs.Count > 0) return pairs;

        // Collinear feet: no real triangle exists. Chain them along the line.
        var axis = max - min;
        var order = Enumerable.Range(0, n).OrderBy(i => Vector2.Dot(feet[i] - min, axis)).ToList();
        for (int k = 1; k < n; k++) pairs.Add(Edge(order[k - 1], order[k]));
        return pairs;
    }

    private static (int, int) Edge(int a, int b) => (Math.Min(a, b), Math.Max(a, b));

    private static bool InCircumcircle(Vector2 a, Vector2 b, Vector2 c, Vector2 p)
    {
        // Standard determinant test, orientation-normalised so it works for either winding.
        double ax = a.X - p.X, ay = a.Y - p.Y;
        double bx = b.X - p.X, by = b.Y - p.Y;
        double cx = c.X - p.X, cy = c.Y - p.Y;
        var det = (ax * ax + ay * ay) * (bx * cy - cx * by)
                - (bx * bx + by * by) * (ax * cy - cx * ay)
                + (cx * cx + cy * cy) * (ax * by - bx * ay);
        var orientation = (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
        return orientation >= 0 ? det > 0 : det < 0;
    }

    // ----- Shapes -----

    private static Path64 Circle(Vector2 centre, double radius)
    {
        const int vertices = SupportSliceGeometry.ContourVertices;
        var path = new Path64(vertices);
        for (int i = 0; i < vertices; i++)
        {
            var angle = i * (2 * Math.PI / vertices);
            path.Add(Point(centre.X + Math.Cos(angle) * radius, centre.Y + Math.Sin(angle) * radius));
        }
        return path;
    }

    private static Path64 Bar(Vector2 a, Vector2 b, double width)
    {
        var along = b - a;
        if (along.LengthSquared() < 1e-12f) return new Path64();
        var side = Vector2.Normalize(new Vector2(-along.Y, along.X)) * (float)(width * 0.5);
        // Counter-clockwise like the discs: under nonzero fill a clockwise bar would cancel
        // the disc it overlaps instead of joining it.
        return
        [
            Point(a.X - side.X, a.Y - side.Y),
            Point(b.X - side.X, b.Y - side.Y),
            Point(b.X + side.X, b.Y + side.Y),
            Point(a.X + side.X, a.Y + side.Y),
        ];
    }

    private static Point64 Point(double xMm, double yMm) => new(
        (long)Math.Round(xMm * MeshSlicer.UnitsPerMm),
        (long)Math.Round(yMm * MeshSlicer.UnitsPerMm));
}
