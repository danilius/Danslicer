using System.Numerics;
using Clipper2Lib;
using Danslicer.Core.Slicing;
using Danslicer.Core.Supports;

namespace Danslicer.Tests;

public class SupportSliceGeometryTests
{
    [Fact]
    public void MiniSupportSlicesLikeAnOrdinaryMember()
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

        AssertAreaNear(Math.PI * 0.3 * 0.3, SupportSliceGeometry.SectionsAt(graph, 1));
    }

    [Fact]
    public void FilteredSectionsIncludeOnlyTheRequestedViewportCategory()
    {
        var graph = new SupportGraph();
        var branchA = new SupportNode { Type = SupportNodeType.Junction, Position = Vector3.Zero };
        var branchB = new SupportNode { Type = SupportNodeType.Junction, Position = new Vector3(0, 0, 4) };
        var trunkA = new SupportNode { Type = SupportNodeType.Junction, Position = new Vector3(10, 0, 0) };
        var trunkB = new SupportNode { Type = SupportNodeType.Junction, Position = new Vector3(10, 0, 4) };
        foreach (var node in new[] { branchA, branchB, trunkA, trunkB }) graph.AddNode(node);
        graph.AddSegment(new SupportSegment
        {
            Type = SupportSegmentType.Branch, NodeA = branchA.Id, NodeB = branchB.Id, Diameter = 2,
        });
        graph.AddSegment(new SupportSegment
        {
            Type = SupportSegmentType.Trunk, NodeA = trunkA.Id, NodeB = trunkB.Id, Diameter = 2,
        });

        var branchOnly = SupportSliceGeometry.SectionsAt(graph, 2,
            segment => segment.Type == SupportSegmentType.Branch, _ => false);

        AssertAreaNear(Math.PI, branchOnly);
    }

    private static double AreaMm2(Paths64 paths) =>
        MeshSlicer.AreaMm2(Clipper.Union(paths, FillRule.NonZero));

    /// <summary>A 64-gon inscribed in the exact curve is ~0.16% small; allow 0.5%.</summary>
    private static void AssertAreaNear(double expected, Paths64 paths)
    {
        var actual = AreaMm2(paths);
        Assert.True(Math.Abs(actual - expected) <= expected * 0.005,
            $"expected area ~{expected:0.####}, got {actual:0.####}");
    }

    [Fact]
    public void VerticalPillarSlicesToItsCircle()
    {
        var paths = new Paths64();
        SupportSliceGeometry.CapsuleSection(new Vector3(0, 0, 0), new Vector3(0, 0, 20), radius: 1.0, z: 10, paths);
        AssertAreaNear(Math.PI, paths);
    }

    [Fact]
    public void LeaningPillarSlicesToAWiderEllipse()
    {
        // 45-degree lean: ellipse area = pi * r * (r / cos 45) = pi * r^2 * sqrt(2).
        var paths = new Paths64();
        SupportSliceGeometry.CapsuleSection(new Vector3(0, 0, 0), new Vector3(20, 0, 20), radius: 1.0, z: 10, paths);
        AssertAreaNear(Math.PI * Math.Sqrt(2), paths);
    }

    [Fact]
    public void CapsRoundOffTheEnds()
    {
        // Above the top node the section is only the cap sphere: radius sqrt(1 - 0.5^2).
        var paths = new Paths64();
        SupportSliceGeometry.CapsuleSection(new Vector3(0, 0, 0), new Vector3(0, 0, 10), radius: 1.0, z: 10.5, paths);
        AssertAreaNear(Math.PI * (1.0 - 0.25), paths);

        // Well past the cap: nothing.
        var empty = new Paths64();
        SupportSliceGeometry.CapsuleSection(new Vector3(0, 0, 0), new Vector3(0, 0, 10), radius: 1.0, z: 11.5, empty);
        Assert.Empty(empty);
    }

    [Fact]
    public void HorizontalMemberSlicesThroughItsCaps()
    {
        // A horizontal brace at z = 5 cut at its own height: two cap circles at the ends
        // (the analytic body of a horizontal cylinder is left to the caps by design).
        var paths = new Paths64();
        SupportSliceGeometry.CapsuleSection(new Vector3(-5, 0, 5), new Vector3(5, 0, 5), radius: 1.0, z: 5, paths);
        Assert.Equal(2, paths.Count);
    }

    private static SupportGraph TrunkWithBase(SupportBaseShape shape, out SupportNode baseNode,
        float memberDiameter = 1.2f)
    {
        var graph = new SupportGraph();
        var top = new SupportNode { Type = SupportNodeType.Junction, Position = new Vector3(0, 0, 10) };
        baseNode = new SupportNode
        {
            Type = SupportNodeType.Base, Position = Vector3.Zero,
            BaseShape = shape, BaseDiameter = 4f, BaseHeight = 0.8f, BaseConeHeight = 2f,
        };
        graph.AddNode(top);
        graph.AddNode(baseNode);
        graph.AddSegment(new SupportSegment
        {
            Type = SupportSegmentType.Trunk, NodeA = top.Id, NodeB = baseNode.Id,
            Diameter = memberDiameter,
        });
        return graph;
    }

    [Fact]
    public void DiscBaseSlicesToItsFullCircleInsideTheDisc()
    {
        var graph = TrunkWithBase(SupportBaseShape.Disc, out _);
        // Inside the disc the union of trunk circle and disc circle is the disc: radius 2.
        AssertAreaNear(Math.PI * 4, SupportSliceGeometry.SectionsAt(graph, 0.4));
        // Above the disc only the trunk remains: radius 0.6 (plus its cap, same circle).
        AssertAreaNear(Math.PI * 0.36, SupportSliceGeometry.SectionsAt(graph, 5));
    }

    [Fact]
    public void DiscConeBaseInterpolatesToTheMemberDiameter()
    {
        var graph = TrunkWithBase(SupportBaseShape.DiscCone, out _);
        // Halfway up the cone (z = 0.8 + 1.0): radius runs 2 -> 0.6, so 1.3 here.
        AssertAreaNear(Math.PI * 1.3 * 1.3, SupportSliceGeometry.SectionsAt(graph, 1.8));
        // At the very top of the cone the frustum matches the trunk: radius 0.6.
        AssertAreaNear(Math.PI * 0.36, SupportSliceGeometry.SectionsAt(graph, 2.8));
    }

    [Fact]
    public void DiscConeTopExactlyMatchesItsIncidentMemberDiameter()
    {
        var graph = TrunkWithBase(SupportBaseShape.DiscCone, out _, memberDiameter: 0.7f);

        AssertAreaNear(Math.PI * 0.35 * 0.35, SupportSliceGeometry.SectionsAt(graph, 2.8));
    }

    [Fact]
    public void BaseShapeNoneSlicesExactlyAsBefore()
    {
        var graph = TrunkWithBase(SupportBaseShape.None, out _);
        var paths = new Paths64();
        SupportSliceGeometry.CapsuleSection(new Vector3(0, 0, 10), Vector3.Zero, 0.6, 0.4, paths);
        AssertAreaNear(AreaMm2(paths), SupportSliceGeometry.SectionsAt(graph, 0.4));
    }

    [Fact]
    public void DisabledBaseNodeSlicesNoBase()
    {
        var graph = TrunkWithBase(SupportBaseShape.Disc, out var baseNode);
        baseNode.Disabled = true;
        // The trunk segment touches the disabled node, so nothing slices at all.
        Assert.Empty(SupportSliceGeometry.SectionsAt(graph, 0.4));
    }

    [Fact]
    public void SlicerUnionsSupportSectionsIntoTheLayers()
    {
        // A 10 x 10 x 5 box beside a vertical pillar (diameter 2) reaching above the box: the
        // sliced volume gains the pillar, and the pillar's cap extends the print height.
        var p = new Vector3[8];
        for (int i = 0; i < 8; i++) p[i] = new Vector3((i & 1) * 10, ((i >> 1) & 1) * 10, ((i >> 2) & 1) * 5);
        int[] idx =
        {
            0, 2, 3, 0, 3, 1,  4, 5, 7, 4, 7, 6,  0, 1, 5, 0, 5, 4,
            2, 6, 7, 2, 7, 3,  0, 4, 6, 0, 6, 2,  1, 3, 7, 1, 7, 5,
        };
        var obj = new Danslicer.Core.Scene.SceneObject("box", new Danslicer.Core.Geometry.Mesh(p, idx));
        obj.Transform = Danslicer.Core.Scene.Transform.Identity with { Translation = new Vector3(-5, -5, 0) };

        var graph = new SupportGraph();
        var bottom = new SupportNode { Type = SupportNodeType.Base, Position = new Vector3(20, 0, 0) };
        var top = new SupportNode { Type = SupportNodeType.Junction, Position = new Vector3(20, 0, 10) };
        graph.AddNode(bottom);
        graph.AddNode(top);
        graph.AddSegment(new SupportSegment { Type = SupportSegmentType.Branch, NodeA = bottom.Id, NodeB = top.Id, Diameter = 2f });

        var settings = Danslicer.Core.Slicing.PrintSettings.Default with { LayerHeight = 0.5f };
        var plain = Danslicer.Core.Slicing.Slicer.Slice(new[] { obj }, Danslicer.Core.Printers.PrinterDefinition.PhotonMonoX, settings);
        var withSupports = Danslicer.Core.Slicing.Slicer.Slice(new[] { obj }, Danslicer.Core.Printers.PrinterDefinition.PhotonMonoX, settings, supports: graph);

        // Print height now reaches the pillar's cap top (11 mm), not the box top (5 mm).
        Assert.Equal(10, plain.LayerCount);
        Assert.Equal(22, withSupports.LayerCount);

        // Added volume is roughly the pillar's: pi * 1^2 * 11 ml/1000, within rasterisation slack.
        var added = withSupports.VolumeMl - plain.VolumeMl;
        Assert.InRange(added, 0.028, 0.040);

        // The pillar shows up in the footprint.
        Assert.True(withSupports.MaxX > 20.5);
    }

    [Fact]
    public void GraphSectionsSkipDisabledButNotHidden()
    {
        var g = new SupportGraph();
        var a = new SupportNode { Type = SupportNodeType.Base, Position = Vector3.Zero };
        var b = new SupportNode { Type = SupportNodeType.Junction, Position = new Vector3(0, 0, 20) };
        g.AddNode(a);
        g.AddNode(b);
        var s = new SupportSegment { Type = SupportSegmentType.Branch, NodeA = a.Id, NodeB = b.Id, Diameter = 2.0f };
        g.AddSegment(s);

        s.Hidden = true;
        Assert.NotEmpty(SupportSliceGeometry.SectionsAt(g, 10));

        s.Disabled = true;
        Assert.Empty(SupportSliceGeometry.SectionsAt(g, 10));
    }

    [Fact]
    public void VerticalConeHasLinearlyInterpolatedRadius()
    {
        // Tip at z=10, r=0.2; 2 mm down the neck r=0.6. Midpoint z=9 is r=0.4.
        var paths = new Paths64();
        SupportSliceGeometry.ConeSection(new Vector3(0, 0, 10), new Vector3(0, 0, 8), 0.2, 0.6, z: 9, paths);
        AssertAreaNear(Math.PI * 0.4 * 0.4, paths);

        var atTip = new Paths64();
        SupportSliceGeometry.ConeSection(new Vector3(0, 0, 10), new Vector3(0, 0, 8), 0.2, 0.6, z: 10, atTip);
        AssertAreaNear(Math.PI * 0.2 * 0.2, atTip);

        var atBase = new Paths64();
        SupportSliceGeometry.ConeSection(new Vector3(0, 0, 10), new Vector3(0, 0, 8), 0.2, 0.6, z: 8, atBase);
        AssertAreaNear(Math.PI * 0.6 * 0.6, atBase);
    }

    [Fact]
    public void SphereSectionHasKnownRadiusAtOffsetZ()
    {
        var mid = new Paths64();
        SupportSliceGeometry.SphereSection(new Vector3(0, 0, 10), 0.5, z: 10, mid);
        AssertAreaNear(Math.PI * 0.25, mid);

        var offset = new Paths64();
        SupportSliceGeometry.SphereSection(new Vector3(0, 0, 10), 0.5, z: 10.3, offset);
        AssertAreaNear(Math.PI * (0.25 - 0.09), offset);

        var miss = new Paths64();
        SupportSliceGeometry.SphereSection(new Vector3(0, 0, 10), 0.5, z: 10.6, miss);
        Assert.Empty(miss);
    }

    [Fact]
    public void ConeTipGraphHasKnownRadiiAlongTheNeck()
    {
        var g = ConeNeckGraph(ball: false);
        // Mid-cone: only the frustum, r = 0.4.
        AssertAreaNear(Math.PI * 0.4 * 0.4, SupportSliceGeometry.SectionsAt(g, 9));
        // Below the cone the neck is a cylinder of r = 0.6.
        AssertAreaNear(Math.PI * 0.6 * 0.6, SupportSliceGeometry.SectionsAt(g, 5));
    }

    [Theory]
    [InlineData(SupportSegmentType.Trunk, 0.8f)]
    [InlineData(SupportSegmentType.Branch, 1.6f)]
    public void ConeTipSectionApproachesItsParentDiameterAtTheJunction(
        SupportSegmentType parentType, float parentDiameter)
    {
        var graph = ConeTipWithParent(parentType, parentDiameter);

        // The first emitted contour is the tip body's section 0.001 mm above the junction.
        // Its radius is within 0.05% of the exact parent radius; the old constant neck was 50%.
        var sections = SupportSliceGeometry.SectionsAt(graph, 5.001);
        AssertAreaNear(Math.PI * Math.Pow(parentDiameter * 0.5, 2), [sections[0]]);
    }

    [Fact]
    public void EmbeddedConeHasSectionsPastTheContactAndFattensTheSurface()
    {
        var g = ConeNeckGraph(ball: false);
        var tip = Assert.Single(g.Nodes, n => n.Type == SupportNodeType.Tip);
        tip.PenetrationDepth = 0.5f;

        Assert.NotEmpty(SupportSliceGeometry.SectionsAt(g, 10.25));
        var surfaceRadius = 0.2 + (0.6 - 0.2) * (0.5 / 2.5);
        AssertAreaNear(Math.PI * surfaceRadius * surfaceRadius,
            SupportSliceGeometry.SectionsAt(g, 10));
    }

    [Fact]
    public void ZeroEmbeddingDepthMatchesTheOriginalConeSectionsBitForBit()
    {
        var g = ConeNeckGraph(ball: false);
        var tip = Assert.Single(g.Nodes, n => n.Type == SupportNodeType.Tip);
        tip.PenetrationDepth = 0;

        foreach (var z in new[] { 9.0, 9.5, 10.0 })
        {
            var expected = new Paths64();
            SupportSliceGeometry.ConeSection(new Vector3(0, 0, 10),
                new Vector3(0, 0, 8), 0.2, 0.6, z, expected);
            if (z == 10)
                SupportSliceGeometry.SphereSection(new Vector3(0, 0, 10), 0.2, z, expected);
            AssertPathsEqual(expected, SupportSliceGeometry.SectionsAt(g, z));
        }
    }

    [Fact]
    public void NegativeEmbeddingDepthClampsToZero()
    {
        var tip = new SupportNode
            { Type = SupportNodeType.Tip, Position = Vector3.Zero, PenetrationDepth = -1f };

        Assert.Equal(0f, tip.PenetrationDepth);
    }

    [Fact]
    public void ConeAndBallGraphUnionsTheContactSphere()
    {
        var g = ConeNeckGraph(ball: true);
        // Ball centre is at z=10.2 (penetration 0.2 along inward +Z). At the centre, r=0.5.
        AssertAreaNear(Math.PI * 0.25, SupportSliceGeometry.SectionsAt(g, 10.2));
        // The same penetration also extends the cone, slightly increasing its radius at z=9.
        var embeddedRadius = 0.2 + (0.6 - 0.2) * (1.2 / 2.2);
        AssertAreaNear(Math.PI * embeddedRadius * embeddedRadius, SupportSliceGeometry.SectionsAt(g, 9));
    }

    [Fact]
    public void MixedShapesSliceIndependently()
    {
        var g = new SupportGraph();
        AddPillar(g, new Vector3(-10, 0, 0), new Vector3(-10, 0, 10), 2f);
        AddConeNeck(g, new Vector3(0, 0, 10), ball: false);
        AddConeNeck(g, new Vector3(10, 0, 10), ball: true);

        var atMidCone = SupportSliceGeometry.SectionsAt(g, 9);
        Assert.Equal(3, atMidCone.Count);

        var atBall = SupportSliceGeometry.SectionsAt(g, 10.2);
        Assert.True(atBall.Count >= 2, $"expected pillar cap + ball, got {atBall.Count}");
    }

    [Fact]
    public void DefaultTipShapeIsBitIdenticalToCapsuleSections()
    {
        var g = new SupportGraph();
        var tip = new SupportNode
        {
            Type = SupportNodeType.Tip, Position = new Vector3(0, 0, 10),
            SurfaceNormal = -Vector3.UnitZ, TipDiameter = 0.4f,
        };
        var junction = new SupportNode { Type = SupportNodeType.Junction, Position = new Vector3(0, 0, 8) };
        var baseNode = new SupportNode { Type = SupportNodeType.Base, Position = Vector3.Zero };
        g.AddNode(tip); g.AddNode(junction); g.AddNode(baseNode);
        g.AddSegment(new SupportSegment { Type = SupportSegmentType.Tip, NodeA = tip.Id, NodeB = junction.Id, Diameter = 1.2f });
        g.AddSegment(new SupportSegment { Type = SupportSegmentType.Branch, NodeA = junction.Id, NodeB = baseNode.Id, Diameter = 1.2f });

        var zs = new[] { -0.5, 0, 4, 8, 9, 10, 10.5, 11 };
        var baseline = zs.Select(z => Copy(SupportSliceGeometry.SectionsAt(g, z))).ToList();

        // Capsule + leftover cone/ball fields must not change a single clipper point.
        tip.TipShape = SupportTipShape.Capsule;
        tip.ConeLength = 2f;
        tip.BallDiameter = 1f;
        for (int i = 0; i < zs.Length; i++)
            AssertPathsEqual(baseline[i], SupportSliceGeometry.SectionsAt(g, zs[i]));

        // And the graph path matches slicing every segment as a capsule.
        for (int i = 0; i < zs.Length; i++)
        {
            var manual = new Paths64();
            foreach (var segment in g.Segments)
            {
                var a = g.GetNode(segment.NodeA);
                var b = g.GetNode(segment.NodeB);
                SupportSliceGeometry.CapsuleSection(a.Position, b.Position, segment.Diameter * 0.5, zs[i], manual);
            }
            AssertPathsEqual(baseline[i], manual);
        }
    }

    private static SupportGraph ConeNeckGraph(bool ball)
    {
        var g = new SupportGraph();
        AddConeNeck(g, Vector3.Zero with { Z = 10 }, ball);
        return g;
    }

    private static SupportGraph ConeTipWithParent(SupportSegmentType parentType,
        float parentDiameter)
    {
        var graph = new SupportGraph();
        var tip = new SupportNode
        {
            Type = SupportNodeType.Tip, Position = new Vector3(0, 0, 10),
            SurfaceNormal = -Vector3.UnitZ, TipDiameter = 0.4f,
            TipShape = SupportTipShape.Cone, ConeLength = 2f,
        };
        var junction = new SupportNode
            { Type = SupportNodeType.Junction, Position = new Vector3(0, 0, 5) };
        var parentEnd = new SupportNode { Type = SupportNodeType.Base, Position = Vector3.Zero };
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

    private static void AddConeNeck(SupportGraph g, Vector3 tipPos, bool ball)
    {
        var tip = new SupportNode
        {
            Type = SupportNodeType.Tip, Position = tipPos,
            SurfaceNormal = -Vector3.UnitZ, TipDiameter = 0.4f,
            TipShape = SupportTipShape.Cone, ConeLength = 2f,
            BallDiameter = ball ? 1f : 0f, PenetrationDepth = ball ? 0.2f : 0f,
        };
        var junction = new SupportNode { Type = SupportNodeType.Junction, Position = tipPos with { Z = 0 } };
        g.AddNode(tip);
        g.AddNode(junction);
        g.AddSegment(new SupportSegment
        {
            Type = SupportSegmentType.Tip, NodeA = tip.Id, NodeB = junction.Id, Diameter = 1.2f,
        });
    }

    private static void AddPillar(SupportGraph g, Vector3 from, Vector3 to, float diameter)
    {
        var a = new SupportNode { Type = SupportNodeType.Base, Position = from };
        var b = new SupportNode { Type = SupportNodeType.Junction, Position = to };
        g.AddNode(a);
        g.AddNode(b);
        g.AddSegment(new SupportSegment { Type = SupportSegmentType.Branch, NodeA = a.Id, NodeB = b.Id, Diameter = diameter });
    }

    private static Paths64 Copy(Paths64 paths)
    {
        var copy = new Paths64(paths.Count);
        foreach (var path in paths)
            copy.Add([.. path]);
        return copy;
    }

    private static void AssertPathsEqual(Paths64 expected, Paths64 actual)
    {
        Assert.Equal(expected.Count, actual.Count);
        for (int i = 0; i < expected.Count; i++)
        {
            Assert.Equal(expected[i].Count, actual[i].Count);
            for (int j = 0; j < expected[i].Count; j++)
                Assert.Equal(expected[i][j], actual[i][j]);
        }
    }
}
