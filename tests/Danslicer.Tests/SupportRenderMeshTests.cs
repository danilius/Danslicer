using System.Numerics;
using Danslicer.Core.Geometry;
using Danslicer.Core.Supports;
using Xunit;

namespace Danslicer.Tests;

public class SupportRenderMeshTests
{
    [Fact]
    public void MiniSupportRendersAsItsOwnCapsuleKind()
    {
        var graph = new SupportGraph();
        var tip = new SupportNode { Type = SupportNodeType.Tip, Position = new Vector3(0, 0, 2) };
        var end = new SupportNode { Type = SupportNodeType.Junction, Position = Vector3.Zero };
        graph.AddNode(tip);
        graph.AddNode(end);
        graph.AddSegment(new SupportSegment
        {
            Type = SupportSegmentType.MiniSupport,
            NodeA = tip.Id,
            NodeB = end.Id,
            Diameter = 0.6f,
        });

        var part = Assert.Single(SupportRenderMesh.Build(graph));
        Assert.Equal(SupportRenderKind.MiniSupport, part.Kind);
        Assert.Equal(0.3f, part.Mesh.Bounds.Max.X, 3);
    }

    private static SupportGraph VerticalPillar(out SupportSegment segment, float diameter = 1.2f)
    {
        var graph = new SupportGraph();
        var top = new SupportNode { Type = SupportNodeType.Junction, Position = new Vector3(0, 0, 10) };
        var bottom = new SupportNode { Type = SupportNodeType.Base, Position = Vector3.Zero };
        graph.AddNode(top);
        graph.AddNode(bottom);
        segment = new SupportSegment { Type = SupportSegmentType.Branch, NodeA = top.Id, NodeB = bottom.Id, Diameter = diameter };
        graph.AddSegment(segment);
        return graph;
    }

    /// <summary>Every directed edge must occur exactly once, i.e. a closed orientable surface.</summary>
    private static void AssertClosed(Mesh mesh)
    {
        var edges = new Dictionary<(int, int), int>();
        for (int t = 0; t < mesh.TriangleCount; t++)
        {
            for (int c = 0; c < 3; c++)
            {
                var i = mesh.Indices[t * 3 + c];
                var j = mesh.Indices[t * 3 + (c + 1) % 3];
                var key = (i, j);
                edges[key] = edges.GetValueOrDefault(key) + 1;
            }
        }
        foreach (var ((i, j), count) in edges)
        {
            Assert.Equal(1, count);
            Assert.True(edges.ContainsKey((j, i)), $"edge {j}->{i} missing its partner");
        }
    }

    [Fact]
    public void PillarBecomesOneClosedCapsule()
    {
        var graph = VerticalPillar(out _);
        var parts = SupportRenderMesh.Build(graph);

        var part = Assert.Single(parts);
        Assert.Equal(SupportRenderKind.Branch, part.Kind);
        Assert.False(part.Selected);
        Assert.False(part.Disabled);
        Assert.Equal(SupportRenderMesh.TrianglesPerCapsule, part.Mesh.TriangleCount);
        AssertClosed(part.Mesh);
    }

    [Fact]
    public void CapsuleBoundsMatchTheAnalyticCapsule()
    {
        var graph = VerticalPillar(out _, diameter: 2f);
        var mesh = Assert.Single(SupportRenderMesh.Build(graph)).Mesh;

        // Poles reach exactly one radius past the endpoints; the equator is inscribed in radius 1.
        Assert.Equal(-1f, mesh.Bounds.Min.Z, 3);
        Assert.Equal(11f, mesh.Bounds.Max.Z, 3);
        Assert.True(mesh.Bounds.Max.X <= 1f + 1e-4f);
        Assert.True(mesh.Bounds.Max.X > 0.9f); // a ring vertex lies on the +X axis
    }

    [Fact]
    public void FaceNormalsPointAwayFromTheAxis()
    {
        var graph = VerticalPillar(out _);
        var mesh = Assert.Single(SupportRenderMesh.Build(graph)).Mesh;

        for (int t = 0; t < mesh.TriangleCount; t++)
        {
            mesh.GetTriangle(t, out var a, out var b, out var c);
            var centroid = (a + b + c) / 3f;
            // Nearest point on the segment (0,0,0)-(0,0,10) to the centroid.
            var s = Math.Clamp(centroid.Z, 0f, 10f);
            var outward = centroid - new Vector3(0, 0, s);
            Assert.True(Vector3.Dot(mesh.FaceNormals[t], outward) > 0,
                $"triangle {t} normal points inward");
        }
    }

    [Fact]
    public void OnlyOneMemberDrawsTheBallAtAJoint()
    {
        // A branch meeting a trunk of the same diameter: the trunk owns the ball, and the
        // branch's cap at that node is tucked inside it, so no two surfaces coincide.
        var graph = new SupportGraph();
        var baseNode = new SupportNode { Type = SupportNodeType.Base, Position = Vector3.Zero };
        var joint = new SupportNode { Type = SupportNodeType.Junction, Position = new Vector3(0, 0, 10) };
        var end = new SupportNode { Type = SupportNodeType.Junction, Position = new Vector3(3, 0, 13) };
        graph.AddNode(baseNode);
        graph.AddNode(joint);
        graph.AddNode(end);
        graph.AddSegment(new SupportSegment
            { Type = SupportSegmentType.Trunk, NodeA = joint.Id, NodeB = baseNode.Id, Diameter = 1.2f });
        graph.AddSegment(new SupportSegment
            { Type = SupportSegmentType.Branch, NodeA = joint.Id, NodeB = end.Id, Diameter = 1.2f });

        var parts = SupportRenderMesh.Build(graph);
        var trunk = Assert.Single(parts, part => part.Kind == SupportRenderKind.Trunk).Mesh;
        var branch = Assert.Single(parts, part => part.Kind == SupportRenderKind.Branch).Mesh;
        Assert.Equal(SupportRenderMesh.TrianglesPerCapsule, trunk.TriangleCount);
        Assert.Equal(SupportRenderMesh.TrianglesPerCapsule + 2 * SupportRenderMesh.RadialSegments,
            branch.TriangleCount);
        AssertClosed(branch);

        // Every branch vertex on the joint side of the node lies strictly inside the ball.
        var axis = Vector3.Normalize(end.Position - joint.Position);
        var capSide = branch.Positions
            .Where(position => Vector3.Dot(position - joint.Position, axis) <= 1e-5f)
            .Select(position => Vector3.Distance(position, joint.Position))
            .ToList();
        Assert.NotEmpty(capSide);
        Assert.True(capSide.Max() <= 0.6f * 0.94f + 1e-4f,
            $"a tucked cap reaches {capSide.Max()} from the joint");
    }

    [Fact]
    public void ZeroLengthSegmentBecomesAClosedSphere()
    {
        var graph = new SupportGraph();
        var a = new SupportNode { Type = SupportNodeType.Junction, Position = new Vector3(3, 4, 5) };
        var b = new SupportNode { Type = SupportNodeType.Junction, Position = new Vector3(3, 4, 5) };
        graph.AddNode(a);
        graph.AddNode(b);
        graph.AddSegment(new SupportSegment { Type = SupportSegmentType.Branch, NodeA = a.Id, NodeB = b.Id, Diameter = 1f });

        var mesh = Assert.Single(SupportRenderMesh.Build(graph)).Mesh;
        Assert.Equal(SupportRenderMesh.TrianglesPerSphere, mesh.TriangleCount);
        AssertClosed(mesh);
        Assert.Equal(4.5f, mesh.Bounds.Min.Z, 3);
        Assert.Equal(5.5f, mesh.Bounds.Max.Z, 3);
    }

    [Fact]
    public void HiddenSegmentsAndEndpointsAreSkipped()
    {
        var graph = VerticalPillar(out var segment);
        segment.Hidden = true;
        Assert.Empty(SupportRenderMesh.Build(graph));

        segment.Hidden = false;
        graph.GetNode(segment.NodeA).Hidden = true;
        Assert.Empty(SupportRenderMesh.Build(graph));
    }

    [Fact]
    public void DisabledAndSelectedSegmentsGetTheirOwnParts()
    {
        var graph = new SupportGraph();
        var nodes = new SupportNode[4];
        for (int i = 0; i < nodes.Length; i++)
        {
            nodes[i] = new SupportNode { Type = SupportNodeType.Junction, Position = new Vector3(i * 5, 0, i % 2 * 8) };
            graph.AddNode(nodes[i]);
        }
        var plain = new SupportSegment { Type = SupportSegmentType.Branch, NodeA = nodes[0].Id, NodeB = nodes[1].Id };
        var selected = new SupportSegment { Type = SupportSegmentType.Branch, NodeA = nodes[1].Id, NodeB = nodes[2].Id };
        var disabled = new SupportSegment { Type = SupportSegmentType.Branch, NodeA = nodes[2].Id, NodeB = nodes[3].Id, Disabled = true };
        graph.AddSegment(plain);
        graph.AddSegment(selected);
        graph.AddSegment(disabled);

        var parts = SupportRenderMesh.Build(graph, id => id == selected.Id);

        Assert.Equal(3, parts.Count);
        Assert.All(parts, p => Assert.Equal(SupportRenderKind.Branch, p.Kind));
        Assert.Single(parts, p => p.Selected && !p.Disabled);
        Assert.Single(parts, p => !p.Selected && p.Disabled);
        Assert.Single(parts, p => !p.Selected && !p.Disabled);
    }

    [Fact]
    public void SegmentTypesMapToKinds()
    {
        var graph = new SupportGraph();
        var nodes = new SupportNode[5];
        for (int i = 0; i < nodes.Length; i++)
        {
            nodes[i] = new SupportNode { Type = SupportNodeType.Junction, Position = new Vector3(i * 4, i * 3, i * 2 + 1) };
            graph.AddNode(nodes[i]);
        }
        graph.AddSegment(new SupportSegment { Type = SupportSegmentType.Tip, NodeA = nodes[0].Id, NodeB = nodes[1].Id });
        graph.AddSegment(new SupportSegment { Type = SupportSegmentType.Branch, NodeA = nodes[1].Id, NodeB = nodes[2].Id });
        graph.AddSegment(new SupportSegment { Type = SupportSegmentType.Trunk, NodeA = nodes[2].Id, NodeB = nodes[3].Id });
        graph.AddSegment(new SupportSegment { Type = SupportSegmentType.Bracing, NodeA = nodes[3].Id, NodeB = nodes[4].Id });

        var kinds = SupportRenderMesh.Build(graph).Select(p => p.Kind).ToHashSet();
        Assert.Equal(4, kinds.Count);
    }

    [Fact]
    public void ConeTipRendersFrustumRemainderAndContactSphere()
    {
        var graph = new SupportGraph();
        var tip = new SupportNode
        {
            Type = SupportNodeType.Tip, Position = new Vector3(0, 0, 10),
            TipShape = SupportTipShape.Cone, ConeLength = 2f, TipDiameter = 0.4f,
        };
        var junction = new SupportNode { Type = SupportNodeType.Junction, Position = new Vector3(0, 0, 5) };
        graph.AddNode(tip);
        graph.AddNode(junction);
        graph.AddSegment(new SupportSegment
        {
            Type = SupportSegmentType.Tip, NodeA = tip.Id, NodeB = junction.Id, Diameter = 0.8f,
        });

        var part = Assert.Single(SupportRenderMesh.Build(graph));
        Assert.Equal(SupportRenderKind.Tip, part.Kind);
        // Three-ring closed body (contact, inset start, junction) plus the contact sphere: the
        // whole member is one taper to the ball's radius.
        Assert.Equal(6 * SupportRenderMesh.RadialSegments
            + SupportRenderMesh.TrianglesPerSphere, part.Mesh.TriangleCount);
        AssertClosed(part.Mesh);
    }

    [Theory]
    [InlineData(SupportSegmentType.Trunk, 0.8f)]
    [InlineData(SupportSegmentType.Branch, 1.6f)]
    public void ConeTipBaseRingMatchesTheBallItSitsOn(SupportSegmentType parentType,
        float parentDiameter)
    {
        var graph = ConeTipWithParent(parentType, parentDiameter);
        var parentRadius = parentDiameter * 0.5f;

        var tipPart = Assert.Single(SupportRenderMesh.Build(graph),
            part => part.Kind == SupportRenderKind.Tip);

        // The base ring sits at the centre of the parent's cap ball and carries all but the few
        // percent that keep its rim off that ball's facets, which lie a cosine inside the
        // analytic surface. Any more and the ring shows through as a disc.
        var baseRadius = RadiusAtZ(tipPart.Mesh, 5f);
        Assert.Equal(parentRadius * 0.94f, baseRadius, 3);
        Assert.True(baseRadius < parentRadius * MathF.Cos(MathF.PI / SupportRenderMesh.RadialSegments),
            "the tip's base ring stands proud of the ball it sits on");
        Assert.Equal(5f, tipPart.Mesh.Bounds.Min.Z, 3);
    }

    private static float RadiusAtZ(Mesh mesh, float z) => mesh.Positions
        .Where(position => MathF.Abs(position.Z - z) < 1e-4f)
        .Max(position => new Vector2(position.X, position.Y).Length());

    [Fact]
    public void ConeTipBallReplacesTheContactSphere()
    {
        var graph = new SupportGraph();
        var tip = new SupportNode
        {
            Type = SupportNodeType.Tip, Position = new Vector3(0, 0, 10),
            TipShape = SupportTipShape.Cone, ConeLength = 2f, TipDiameter = 0.4f,
            BallDiameter = 1f, PenetrationDepth = 0.2f, SurfaceNormal = Vector3.UnitZ,
        };
        var junction = new SupportNode { Type = SupportNodeType.Junction, Position = new Vector3(0, 0, 5) };
        graph.AddNode(tip);
        graph.AddNode(junction);
        graph.AddSegment(new SupportSegment
        {
            Type = SupportSegmentType.Tip, NodeA = tip.Id, NodeB = junction.Id, Diameter = 0.8f,
        });

        var mesh = Assert.Single(SupportRenderMesh.Build(graph)).Mesh;
        // The ball sphere sits at the penetrated contact centre: z = 10 - 0.2, radius 0.5.
        Assert.Equal(10f - 0.2f + 0.5f, mesh.Bounds.Max.Z, 3);
    }

    private static SupportGraph ConeTipWithParent(SupportSegmentType parentType,
        float parentDiameter)
    {
        var graph = new SupportGraph();
        var tip = new SupportNode
        {
            Type = SupportNodeType.Tip, Position = new Vector3(0, 0, 10),
            TipShape = SupportTipShape.Cone, ConeLength = 2f, TipDiameter = 0.4f,
        };
        var junction = new SupportNode
            { Type = SupportNodeType.Junction, Position = new Vector3(0, 0, 5) };
        var parentEnd = new SupportNode
            { Type = SupportNodeType.Base, Position = Vector3.Zero };
        graph.AddNode(tip);
        graph.AddNode(junction);
        graph.AddNode(parentEnd);
        graph.AddSegment(new SupportSegment
        {
            Type = SupportSegmentType.Tip, NodeA = tip.Id, NodeB = junction.Id,
            Diameter = 0.4f,
        });
        graph.AddSegment(new SupportSegment
        {
            Type = parentType, NodeA = junction.Id, NodeB = parentEnd.Id,
            Diameter = parentDiameter,
        });
        return graph;
    }

    [Fact]
    public void DiscBaseRendersOneFrustumUnderItsKind()
    {
        var graph = new SupportGraph();
        var top = new SupportNode { Type = SupportNodeType.Junction, Position = new Vector3(0, 0, 10) };
        var bottom = new SupportNode
        {
            Type = SupportNodeType.Base, Position = Vector3.Zero,
            BaseShape = SupportBaseShape.Disc, BaseDiameter = 4f, BaseHeight = 0.8f,
        };
        graph.AddNode(top);
        graph.AddNode(bottom);
        graph.AddSegment(new SupportSegment
        {
            Type = SupportSegmentType.Trunk, NodeA = top.Id, NodeB = bottom.Id, Diameter = 1.2f,
        });

        var parts = SupportRenderMesh.Build(graph);
        Assert.Equal(2, parts.Count);
        var basePart = Assert.Single(parts, p => p.Kind == SupportRenderKind.Base);
        Assert.Equal(SupportRenderMesh.TrianglesPerFrustum, basePart.Mesh.TriangleCount);
        AssertClosed(basePart.Mesh);
        Assert.Equal(0f, basePart.Mesh.Bounds.Min.Z, 3);
        Assert.Equal(0.8f, basePart.Mesh.Bounds.Max.Z, 3);
        Assert.Equal(2f, basePart.Mesh.Bounds.Max.X, 3);
    }

    [Fact]
    public void DiscConeBaseAddsTheConeFrustum()
    {
        var graph = new SupportGraph();
        var top = new SupportNode { Type = SupportNodeType.Junction, Position = new Vector3(0, 0, 10) };
        var bottom = new SupportNode
        {
            Type = SupportNodeType.Base, Position = Vector3.Zero,
            BaseShape = SupportBaseShape.DiscCone, BaseDiameter = 4f, BaseHeight = 0.8f,
            BaseConeHeight = 2f,
        };
        graph.AddNode(top);
        graph.AddNode(bottom);
        graph.AddSegment(new SupportSegment
        {
            Type = SupportSegmentType.Trunk, NodeA = top.Id, NodeB = bottom.Id, Diameter = 0.7f,
        });

        var basePart = Assert.Single(SupportRenderMesh.Build(graph), p => p.Kind == SupportRenderKind.Base);
        Assert.Equal(2 * SupportRenderMesh.TrianglesPerFrustum, basePart.Mesh.TriangleCount);
        AssertClosed(basePart.Mesh);
        Assert.Equal(2.8f, basePart.Mesh.Bounds.Max.Z, 3);
        var topRadius = basePart.Mesh.Positions
            .Where(position => MathF.Abs(position.Z - 2.8f) < 1e-4f)
            .Max(position => new Vector2(position.X, position.Y).Length());
        Assert.Equal(0.35f, topRadius, 3);
    }

    [Fact]
    public void BuildSelectedTessellatesOnlyTheSelectedElements()
    {
        var graph = new SupportGraph();
        var top = new SupportNode { Type = SupportNodeType.Junction, Position = new Vector3(0, 0, 10) };
        var bottom = new SupportNode
        {
            Type = SupportNodeType.Base, Position = Vector3.Zero,
            BaseShape = SupportBaseShape.Disc, BaseDiameter = 4f, BaseHeight = 0.8f,
        };
        graph.AddNode(top);
        graph.AddNode(bottom);
        var trunk = new SupportSegment
        {
            Type = SupportSegmentType.Trunk, NodeA = top.Id, NodeB = bottom.Id, Diameter = 1.2f,
        };
        graph.AddSegment(trunk);

        Assert.Null(SupportRenderMesh.BuildSelected(graph, _ => false));

        var trunkOnly = SupportRenderMesh.BuildSelected(graph, id => id == trunk.Id);
        Assert.NotNull(trunkOnly);
        Assert.Equal(SupportRenderMesh.TrianglesPerCapsule, trunkOnly!.TriangleCount);

        var baseOnly = SupportRenderMesh.BuildSelected(graph, id => id == bottom.Id);
        Assert.NotNull(baseOnly);
        Assert.Equal(SupportRenderMesh.TrianglesPerFrustum, baseOnly!.TriangleCount);
    }

    [Fact]
    public void BuildAndSelectionHonorViewportFilters()
    {
        var graph = new SupportGraph();
        var tip = new SupportNode { Type = SupportNodeType.Tip, Position = new(0, 0, 10) };
        var junction = new SupportNode { Type = SupportNodeType.Junction, Position = new(0, 0, 5) };
        var bottom = new SupportNode
        {
            Type = SupportNodeType.Base, Position = Vector3.Zero,
            BaseShape = SupportBaseShape.Disc,
        };
        graph.AddNode(tip);
        graph.AddNode(junction);
        graph.AddNode(bottom);
        var tipMember = new SupportSegment
        {
            Type = SupportSegmentType.Tip, NodeA = tip.Id, NodeB = junction.Id,
        };
        var trunk = new SupportSegment
        {
            Type = SupportSegmentType.Trunk, NodeA = junction.Id, NodeB = bottom.Id,
        };
        graph.AddSegment(tipMember);
        graph.AddSegment(trunk);

        var parts = SupportRenderMesh.Build(graph, includeSegment: segment =>
            segment.Type == SupportSegmentType.Tip, includeBase: _ => false);
        var selected = SupportRenderMesh.BuildSelected(graph, _ => true, segment =>
            segment.Type == SupportSegmentType.Tip, _ => false);

        Assert.Single(parts);
        Assert.Equal(SupportRenderKind.Tip, parts[0].Kind);
        Assert.NotNull(selected);
        Assert.Equal(parts[0].Mesh.TriangleCount, selected!.TriangleCount);
    }

    [Fact]
    public void HiddenBaseNodeRendersNoBase()
    {
        var graph = new SupportGraph();
        var bottom = new SupportNode
        {
            Type = SupportNodeType.Base, Position = Vector3.Zero,
            BaseShape = SupportBaseShape.Disc, Hidden = true,
        };
        graph.AddNode(bottom);
        Assert.Empty(SupportRenderMesh.Build(graph));
    }

    [Fact]
    public void LeaningCapsuleStaysClosedAndOutward()
    {
        var graph = new SupportGraph();
        var a = new SupportNode { Type = SupportNodeType.Junction, Position = new Vector3(1, 2, 3) };
        var b = new SupportNode { Type = SupportNodeType.Base, Position = new Vector3(7, -4, 0.5f) };
        graph.AddNode(a);
        graph.AddNode(b);
        graph.AddSegment(new SupportSegment { Type = SupportSegmentType.Branch, NodeA = a.Id, NodeB = b.Id, Diameter = 0.8f });

        var mesh = Assert.Single(SupportRenderMesh.Build(graph)).Mesh;
        AssertClosed(mesh);

        var pa = a.Position;
        var axis = b.Position - a.Position;
        var len2 = axis.LengthSquared();
        for (int t = 0; t < mesh.TriangleCount; t++)
        {
            mesh.GetTriangle(t, out var p, out var q, out var r);
            var centroid = (p + q + r) / 3f;
            var s = Math.Clamp(Vector3.Dot(centroid - pa, axis) / len2, 0f, 1f);
            var outward = centroid - (pa + axis * s);
            Assert.True(Vector3.Dot(mesh.FaceNormals[t], outward) > 0,
                $"triangle {t} normal points inward");
        }
    }
}
