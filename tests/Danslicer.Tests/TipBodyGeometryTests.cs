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
    public void RenderedAndAnalyticSectionsAgreeAfterTheNormalLeadInBend()
    {
        var tip = new SupportNode
        {
            Type = SupportNodeType.Tip, Position = new Vector3(0, 0, 10),
            SurfaceNormal = -Vector3.UnitZ, TipDiameter = 0.4f,
            TipShape = SupportTipShape.Cone, ConeLength = 1f, TipNormalLeadIn = 0.3f,
        };
        var junction = new SupportNode
            { Type = SupportNodeType.Junction, Position = new Vector3(2, 0, 8) };
        var leaning = TipBodyGeometry.Sections(tip, junction, 0.3f, 0.3f,
            embedContact: false).First(section => MathF.Abs(section.Start.X - section.End.X) > 0.01f);
        var builder = new MeshBuilder();
        SupportRenderMesh.AppendFrustum(builder, leaning.Start, leaning.End,
            leaning.StartRadius, leaning.EndRadius);
        var renderMesh = builder.ToMesh();
        var z = (leaning.Start.Z + leaning.End.Z) * 0.5;

        var rendered = SliceMesh(renderMesh, z);
        var analytic = new Paths64();
        SupportSliceGeometry.ConeSection(leaning.Start, leaning.End,
            leaning.StartRadius, leaning.EndRadius, z, analytic);
        var renderedArea = MeshSlicer.AreaMm2(Clipper.Union(rendered, FillRule.NonZero));
        var analyticArea = MeshSlicer.AreaMm2(Clipper.Union(analytic, FillRule.NonZero));

        Assert.NotEmpty(rendered);
        Assert.NotEmpty(analytic);
        Assert.True(renderedArea / analyticArea is >= 0.96 and <= 1.01,
            $"rendered {renderedArea}, analytic {analyticArea}");
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
    public void SharedMiniCarrierKeepsContactConesSeparateThroughTheirLeadIns()
    {
        var junction = new SupportNode
            { Type = SupportNodeType.Junction, Position = new Vector3(0, 0, 9.4f) };
        var left = MiniTip(new Vector3(-0.25f, 0, 10));
        var right = MiniTip(new Vector3(0.25f, 0, 10));

        var leftLead = TipBodyGeometry.Sections(left, junction, 0.3f, 0.3f,
            embedContact: false)[0];
        var rightLead = TipBodyGeometry.Sections(right, junction, 0.3f, 0.3f,
            embedContact: false)[0];

        Assert.Equal(-Vector3.UnitZ, Vector3.Normalize(leftLead.End - leftLead.Start));
        Assert.Equal(-Vector3.UnitZ, Vector3.Normalize(rightLead.End - rightLead.Start));
        Assert.True(Vector3.Distance(leftLead.End, rightLead.End) >
                    leftLead.EndRadius + rightLead.EndRadius);
    }

    private static SupportNode MiniTip(Vector3 position) => new()
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
}
