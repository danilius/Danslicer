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

    /// <summary>Slabs the sloped edge is drawn as; the slice uses the exact slope per layer.</summary>
    public const int RenderSteps = 3;

    /// <summary>
    /// A render mesh of the raft: the top face at the raft's top, and the sloped edge as
    /// <see cref="RenderSteps"/> vertical slabs with a flat ledge between each pair, each slab
    /// the exact section at its own height. Faces wind counter-clockwise seen from outside.
    /// Null for an empty outline.
    /// </summary>
    public static Mesh? BuildMesh(RaftShape raft)
    {
        var parameters = raft.Parameters.Normalize();
        var thickness = (double)parameters.Thickness;
        if (raft.TopOutline.Count == 0 || thickness <= 0) return null;

        var builder = new MeshBuilder();
        var steps = Math.Max(1, RenderSteps);
        var sections = new Paths64[steps + 1];
        for (var k = 0; k < steps; k++) sections[k] = raft.SectionAt(thickness * k / steps);
        sections[steps] = raft.TopOutline;

        // Bottom face, looking down.
        AppendCap(builder, sections[0], 0, up: false);
        for (var k = 0; k < steps; k++)
        {
            var zLow = thickness * k / steps;
            var zHigh = thickness * (k + 1) / steps;
            AppendWalls(builder, sections[k], zLow, zHigh);
            // The ledge where this slab's outline steps in to the next one's.
            var ledge = Clipper.Difference(sections[k], sections[k + 1], FillRule.NonZero);
            AppendCap(builder, ledge, zHigh, up: true);
        }
        AppendCap(builder, sections[steps], thickness, up: true);
        return builder.ToMesh();
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

    private static void AppendWalls(MeshBuilder builder, Paths64 outline, double zLow, double zHigh)
    {
        foreach (var path in outline)
        {
            if (path.Count < 3) continue;
            // Outer contours run counter-clockwise (positive area) and holes clockwise, so the
            // same quad orientation faces outward from the slab on both.
            var outer = Clipper.Area(path) > 0;
            for (var i = 0; i < path.Count; i++)
            {
                var p = path[i];
                var q = path[(i + 1) % path.Count];
                var (a, b) = outer ? (p, q) : (q, p);
                var aLow = builder.AddVertex(ToMm(a, zLow));
                var bLow = builder.AddVertex(ToMm(b, zLow));
                var bHigh = builder.AddVertex(ToMm(b, zHigh));
                var aHigh = builder.AddVertex(ToMm(a, zHigh));
                builder.AddTriangle(aLow, bLow, bHigh);
                builder.AddTriangle(aLow, bHigh, aHigh);
            }
        }
    }

    private static Vector3 ToMm(Point64 point, double z) => new(
        (float)(point.X / MeshSlicer.UnitsPerMm), (float)(point.Y / MeshSlicer.UnitsPerMm), (float)z);
}
