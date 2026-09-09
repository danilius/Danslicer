using System.Numerics;
using Clipper2Lib;
using Danslicer.Core.Geometry;
using Danslicer.Core.Scene;
using Danslicer.Core.Slicing;

namespace Danslicer.Core.Supports.Rafts;

/// <summary>A raft ready to slice or draw: the parameters and the top outline they produced.</summary>
public readonly record struct RaftShape(RaftParameters Parameters, Paths64 TopOutline)
{
    /// <summary>The raft's cross-section at height <paramref name="z"/> (see <see cref="RaftBuilder.SectionAt"/>).</summary>
    public Paths64 SectionAt(double z) => RaftBuilder.SectionAt(TopOutline, Parameters, z);
}

/// <summary>
/// Where rafts meet the rest of the document: the feet an object's raft stands on, the rafts of
/// the visible objects, their sections for a layer, and the mesh the viewport draws.
/// </summary>
public static class RaftGeometry
{
    /// <summary>The XY positions of <paramref name="obj"/>'s enabled feet (base nodes it owns).</summary>
    public static IReadOnlyList<Vector2> Feet(SupportGraph graph, SceneObject obj)
    {
        var feet = new List<Vector2>();
        foreach (var node in graph.Nodes)
        {
            if (node.Type != SupportNodeType.Base || node.Disabled || node.Origin.ObjectId != obj.Id) continue;
            feet.Add(new Vector2(node.Position.X, node.Position.Y));
        }
        return feet;
    }

    /// <summary>The raft under <paramref name="obj"/>, or null when it has none or no feet.</summary>
    public static RaftShape? Shape(SupportGraph graph, SceneObject obj)
    {
        if (obj.Raft is not { } parameters) return null;
        var outline = RaftBuilder.TopOutline(Feet(graph, obj), parameters);
        return outline.Count == 0 ? null : new RaftShape(parameters, outline);
    }

    /// <summary>
    /// The rafts that print: one per visible rafted object with feet. A hidden model takes its
    /// raft with it, as it takes its supports.
    /// </summary>
    public static IReadOnlyList<RaftShape> Printable(IEnumerable<SceneObject> objects, SupportGraph? graph)
    {
        var rafts = new List<RaftShape>();
        if (graph is null) return rafts;
        foreach (var obj in objects)
        {
            if (obj.RenderState == RenderState.Hidden || obj.Raft is null) continue;
            if (Shape(graph, obj) is { } shape) rafts.Add(shape);
        }
        return rafts;
    }

    /// <summary>Appends every raft's section at <paramref name="z"/> to <paramref name="output"/>.</summary>
    public static void AppendSections(IReadOnlyList<RaftShape> rafts, double z, Paths64 output)
    {
        foreach (var raft in rafts) output.AddRange(raft.SectionAt(z));
    }

    /// <summary>The tallest raft's top, in millimetres; 0 when there is none.</summary>
    public static double MaxTop(IReadOnlyList<RaftShape> rafts)
    {
        double top = 0;
        foreach (var raft in rafts) top = Math.Max(top, raft.Parameters.Normalize().Thickness);
        return top;
    }

    /// <summary>
    /// The XY extent of the rafts at the plate, where they are widest, in millimetres: what the
    /// build-area check must count. Null when there is no raft.
    /// </summary>
    public static (double MinX, double MinY, double MaxX, double MaxY)? PlateBounds(IReadOnlyList<RaftShape> rafts)
    {
        (double MinX, double MinY, double MaxX, double MaxY)? bounds = null;
        foreach (var raft in rafts)
        {
            var bottom = raft.SectionAt(0);
            if (bottom.Count == 0) continue;
            var b = Clipper.GetBounds(bottom);
            var candidate = (b.left / MeshSlicer.UnitsPerMm, b.top / MeshSlicer.UnitsPerMm,
                b.right / MeshSlicer.UnitsPerMm, b.bottom / MeshSlicer.UnitsPerMm);
            bounds = bounds is { } current
                ? (Math.Min(current.MinX, candidate.Item1), Math.Min(current.MinY, candidate.Item2),
                    Math.Max(current.MaxX, candidate.Item3), Math.Max(current.MaxY, candidate.Item4))
                : candidate;
        }
        return bounds;
    }

    /// <summary>
    /// A render mesh of the raft: the top face at the raft's top, the bottom face on the plate,
    /// and the sloped edge as one smooth wall between them (user, 2026-09-09: a slope, not
    /// steps). The wall is built vertex by vertex: each top vertex is pushed out along its
    /// outline normal by the plate-level edge offset, mitred at the corners, so the top and
    /// bottom rings share a vertex count and quads join them exactly. The slice uses the exact
    /// round-join offset per layer; the two agree to within the mitre at sharp corners. Faces
    /// wind counter-clockwise seen from outside. Null for an empty outline.
    /// </summary>
    public static Mesh? BuildMesh(RaftShape raft)
    {
        var parameters = raft.Parameters.Normalize();
        var thickness = (double)parameters.Thickness;
        if (raft.TopOutline.Count == 0 || thickness <= 0) return null;

        var builder = new MeshBuilder();
        var offset = RaftBuilder.EdgeOffsetAt(parameters, 0);
        var bottom = new Paths64();
        foreach (var path in raft.TopOutline)
        {
            if (path.Count < 3) continue;
            var outer = Clipper.Area(path) > 0;
            var pushed = PushOut(path, outer ? offset : -offset);
            bottom.Add(pushed);
            AppendWall(builder, path, pushed, thickness, outer);
        }
        AppendCap(builder, bottom, 0, up: false);
        AppendCap(builder, raft.TopOutline, thickness, up: true);
        return builder.ToMesh();
    }

    /// <summary>
    /// The path with every vertex moved outward (for a counter-clockwise outer contour; a hole
    /// passes a negative distance) along the bisector of its two edge normals, mitre-limited
    /// to twice the distance so a sharp corner does not spike.
    /// </summary>
    internal static Path64 PushOut(Path64 path, double distanceMm)
    {
        var n = path.Count;
        var result = new Path64(n);
        var scale = distanceMm * MeshSlicer.UnitsPerMm;
        for (var i = 0; i < n; i++)
        {
            var previous = path[(i + n - 1) % n];
            var current = path[i];
            var next = path[(i + 1) % n];
            // Outward normal of a counter-clockwise edge (dx, dy) is (dy, -dx).
            var n1 = Normal(previous, current);
            var n2 = Normal(current, next);
            var bisector = Vector2.Normalize(n1 + n2);
            if (!float.IsFinite(bisector.X)) bisector = n2;
            var cosHalf = Math.Max(Vector2.Dot(bisector, n2), 0.5f); // mitre limit 2×
            var move = bisector * (float)(scale / cosHalf);
            result.Add(new Point64((long)Math.Round(current.X + move.X), (long)Math.Round(current.Y + move.Y)));
        }
        return result;

        static Vector2 Normal(Point64 a, Point64 b)
        {
            var d = new Vector2(b.X - a.X, b.Y - a.Y);
            var normal = new Vector2(d.Y, -d.X);
            return normal.LengthSquared() > 0 ? Vector2.Normalize(normal) : Vector2.UnitX;
        }
    }

    private static void AppendWall(MeshBuilder builder, Path64 top, Path64 bottom, double thickness, bool outer)
    {
        var n = top.Count;
        for (var i = 0; i < n; i++)
        {
            var j = (i + 1) % n;
            // For a counter-clockwise outer contour the edge runs left to right seen from
            // outside; a hole runs the other way, so swap to keep the quad facing out.
            var (a, b) = outer ? (i, j) : (j, i);
            var aLow = builder.AddVertex(ToMm(bottom[a], 0));
            var bLow = builder.AddVertex(ToMm(bottom[b], 0));
            var bHigh = builder.AddVertex(ToMm(top[b], thickness));
            var aHigh = builder.AddVertex(ToMm(top[a], thickness));
            builder.AddTriangle(aLow, bLow, bHigh);
            builder.AddTriangle(aLow, bHigh, aHigh);
        }
    }

    private static void AppendCap(MeshBuilder builder, Paths64 polygons, double z, bool up)
    {
        if (polygons.Count == 0) return;
        foreach (var triangle in PolygonTriangulator.Triangulate(polygons))
        {
            if (triangle.Count != 3) continue;
            var a = ToMm(triangle[0], z);
            var b = ToMm(triangle[1], z);
            var c = ToMm(triangle[2], z);
            // Clipper's winding is in its own Y-down sense; orient by the signed area instead.
            var counterClockwise = (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X) > 0;
            if (counterClockwise != up) (b, c) = (c, b);
            var ia = builder.AddVertex(a);
            var ib = builder.AddVertex(b);
            var ic = builder.AddVertex(c);
            builder.AddTriangle(ia, ib, ic);
        }
    }

    private static Vector3 ToMm(Point64 point, double z) => new(
        (float)(point.X / MeshSlicer.UnitsPerMm), (float)(point.Y / MeshSlicer.UnitsPerMm), (float)z);
}
