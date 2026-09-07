using System.Numerics;
using Clipper2Lib;
using Danslicer.Core.Geometry;
using Danslicer.Core.Slicing;
using Danslicer.Core.Supports;

namespace Danslicer.Tests;

public sealed class TipBodyGeometryTests
{
    [Fact]
    public void ZeroLeadInIgnoresSurfaceNormalAndKeepsTheLegacyMeshBitIdentical()
    {
        var first = ConeTip(surfaceNormal: -Vector3.UnitZ, leadIn: 0);
        var second = ConeTip(surfaceNormal: Vector3.UnitX, leadIn: 0);

        var firstMesh = Assert.Single(SupportRenderMesh.Build(first)).Mesh;
        var secondMesh = Assert.Single(SupportRenderMesh.Build(second)).Mesh;

        Assert.Equal(firstMesh.Positions, secondMesh.Positions);
        Assert.Equal(firstMesh.Indices, secondMesh.Indices);
        foreach (var z in new[] { 8.25, 9.0, 9.75 })
            Assert.Equal(SupportSliceGeometry.SectionsAt(first, z),
                SupportSliceGeometry.SectionsAt(second, z));
    }

    [Fact]
    public void LeaningTipLeavesTheSurfaceAlongItsNormalForTheConfiguredDistance()
    {
        var contact = new Vector3(0, 0, 10);
        var junction = new Vector3(2, 0, 8);

        var points = TipBodyGeometry.Centerline(contact, -Vector3.UnitZ, junction, 0.3f);

        Assert.Equal(3, points.Count);
        Assert.Equal(contact, points[0]);
        Assert.Equal(new Vector3(0, 0, 9.7f), points[1]);
        Assert.Equal(-Vector3.UnitZ, Vector3.Normalize(points[1] - points[0]));
        Assert.Equal(0.3f, Vector3.Distance(points[0], points[1]), 5);
        Assert.Equal(junction, points[2]);
    }

    [Fact]
    public void NonZeroLeadInRendersOneClosedBodyWithAContactApex()
    {
        var graph = ConeTip(-Vector3.UnitZ, 0.3f);
        var tip = graph.Nodes.Single(node => node.Type == SupportNodeType.Tip);
        var bend = TipBodyGeometry.Centerline(tip.Position, tip.SurfaceNormal,
            graph.Nodes.Single(node => node.Type == SupportNodeType.Junction).Position,
            tip.TipNormalLeadIn)[1];

        var mesh = Assert.Single(SupportRenderMesh.Build(graph)).Mesh;

        AssertClosed(mesh);
        Assert.Single(mesh.Positions, position => Vector3.DistanceSquared(position, tip.Position) < 1e-10f);
        Assert.DoesNotContain(mesh.Positions,
            position => Vector3.DistanceSquared(position, bend) < 1e-10f);
    }

    [Fact]
    public void NormalLeadInDoesNotMakeTheContactEndBlunter()
    {
        var straight = Assert.Single(SupportRenderMesh.Build(
            ConeTip(-Vector3.UnitZ, 0))).Mesh;
        var leadIn = Assert.Single(SupportRenderMesh.Build(
            ConeTip(-Vector3.UnitZ, 0.3f))).Mesh;
        var contact = new Vector3(0, 0, 10);

        var straightRadius = MaxRadiusNearContact(straight, contact, -Vector3.UnitZ, 0.05f);
        var leadInRadius = MaxRadiusNearContact(leadIn, contact, -Vector3.UnitZ, 0.05f);

        Assert.True(leadInRadius <= straightRadius + 1e-5f,
            $"lead-in radius {leadInRadius} exceeds straight radius {straightRadius}");
    }

    [Fact]
    public void RenderedAndAnalyticSectionsAgreeThroughTheNormalLeadInAndBend()
    {
        var graph = ConeTip(-Vector3.UnitZ, 0.3f);
        var renderMesh = Assert.Single(SupportRenderMesh.Build(graph)).Mesh;

        foreach (var z in new[] { 9.85, 9.5, 9.0, 8.5 })
        {
            var rendered = SliceMesh(renderMesh, z);
            var analytic = SupportSliceGeometry.SectionsAt(graph, z);
            var renderedArea = MeshSlicer.AreaMm2(Clipper.Union(rendered, FillRule.NonZero));
            var analyticArea = MeshSlicer.AreaMm2(Clipper.Union(analytic, FillRule.NonZero));

            Assert.NotEmpty(rendered);
            Assert.NotEmpty(analytic);
            Assert.True(renderedArea / analyticArea is >= 0.94 and <= 1.02,
                $"z {z}: rendered {renderedArea}, analytic {analyticArea}");
        }
    }

    [Fact]
    public void LeadInClampsToAShorterMemberWithoutInversion()
    {
        var contact = Vector3.Zero;
        var junction = new Vector3(0, 0, -0.1f);

        var points = TipBodyGeometry.Centerline(contact, Vector3.UnitX, junction, 0.3f);

        Assert.Equal(3, points.Count);
        Assert.Equal(0.1f, Vector3.Distance(contact, points[1]), 5);
        Assert.All(points, point =>
        {
            Assert.True(float.IsFinite(point.X));
            Assert.True(float.IsFinite(point.Y));
            Assert.True(float.IsFinite(point.Z));
        });
        Assert.True(Vector3.Distance(points[1], junction) > 0);
    }

    [Fact]
    public void SharedCarrierKeepsFineContactConesSeparateThroughTheirLeadIns()
    {
        var junction = new SupportNode
            { Type = SupportNodeType.Junction, Position = new Vector3(0, 0, 9.4f) };
        var left = FineConeTip(new Vector3(-0.25f, 0, 10));
        var right = FineConeTip(new Vector3(0.25f, 0, 10));

        var leftLead = TipBodyGeometry.Sections(left, junction, 0.3f,
            embedContact: false)[0];
        var rightLead = TipBodyGeometry.Sections(right, junction, 0.3f,
            embedContact: false)[0];

        Assert.Equal(-Vector3.UnitZ, Vector3.Normalize(leftLead.End - leftLead.Start));
        Assert.Equal(-Vector3.UnitZ, Vector3.Normalize(rightLead.End - rightLead.Start));
        Assert.True(Vector3.Distance(leftLead.End, rightLead.End) >
                    leftLead.EndRadius + rightLead.EndRadius);
    }

    private static SupportNode FineConeTip(Vector3 position) => new()
    {
        Type = SupportNodeType.Tip,
        Position = position,
        SurfaceNormal = -Vector3.UnitZ,
        TipDiameter = 0.25f,
        TipShape = SupportTipShape.Cone,
        ConeLength = 1f,
        TipNormalLeadIn = 0.3f,
    };

    private static SupportGraph ConeTip(Vector3 surfaceNormal, float leadIn)
    {
        var graph = new SupportGraph();
        var tip = new SupportNode
        {
            Type = SupportNodeType.Tip,
            Position = new Vector3(0, 0, 10),
            SurfaceNormal = surfaceNormal,
            TipDiameter = 0.4f,
            TipShape = SupportTipShape.Cone,
            ConeLength = 1f,
            TipNormalLeadIn = leadIn,
        };
        var junction = new SupportNode
            { Type = SupportNodeType.Junction, Position = new Vector3(2, 0, 8) };
        graph.AddNode(tip);
        graph.AddNode(junction);
        graph.AddSegment(new SupportSegment
        {
            Type = SupportSegmentType.Tip,
            NodeA = tip.Id,
            NodeB = junction.Id,
            Diameter = 0.6f,
        });
        return graph;
    }

    private static Paths64 SliceMesh(Mesh mesh, double z)
    {
        var prepared = new MeshSlicer.PreparedMesh(mesh, Matrix4x4.Identity);
        var segments = new List<MeshSlicer.Segment>();
        MeshSlicer.CollectSegments(prepared,
            Enumerable.Range(0, prepared.TriangleCount).ToList(), z, segments);
        return MeshSlicer.ChainSegments(segments);
    }

    private static float MaxRadiusNearContact(Mesh mesh, Vector3 contact, Vector3 axis,
        float axialDistance)
    {
        var direction = Vector3.Normalize(axis);
        return mesh.Positions
            .Select(position => position - contact)
            .Where(offset =>
            {
                var distance = Vector3.Dot(offset, direction);
                return distance >= 0 && distance <= axialDistance + 1e-5f;
            })
            .Select(offset => (offset - direction * Vector3.Dot(offset, direction)).Length())
            .DefaultIfEmpty(0)
            .Max();
    }

    /// <summary>Every directed edge must have exactly one reverse edge.</summary>
    private static void AssertClosed(Mesh mesh)
    {
        var edges = new Dictionary<(int, int), int>();
        for (var triangle = 0; triangle < mesh.TriangleCount; triangle++)
        for (var corner = 0; corner < 3; corner++)
        {
            var first = mesh.Indices[triangle * 3 + corner];
            var second = mesh.Indices[triangle * 3 + (corner + 1) % 3];
            edges[(first, second)] = edges.GetValueOrDefault((first, second)) + 1;
        }

        foreach (var ((first, second), count) in edges)
        {
            Assert.Equal(1, count);
            Assert.True(edges.ContainsKey((second, first)),
                $"edge {second}->{first} missing its partner");
        }
    }
}
