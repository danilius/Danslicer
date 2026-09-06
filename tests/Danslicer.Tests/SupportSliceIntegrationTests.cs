using System.Numerics;
using Clipper2Lib;
using Danslicer.Core.Geometry;
using Danslicer.Core.Printers;
using Danslicer.Core.Scene;
using Danslicer.Core.Slicing;
using Danslicer.Core.Supports;
using Danslicer.Core.Supports.Routing;

namespace Danslicer.Tests;

public sealed class SupportSliceIntegrationTests
{
    private const float LayerHeight = 0.05f;
    private const float TrunkDiameter = 1.6f;
    private const float BranchDiameter = 0.9f;
    private const float MiniDiameter = 0.5f;
    private const float MiniConeLength = 0.2f;

    private static readonly PrinterDefinition TestPrinter = new(
        "support-slice-test", false, "Support slice test", "Support slice test", "test",
        48, 30, 30, 480, 300, MirrorX: false, MirrorY: false, FormatVersion: 516);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RoutedSupportAnatomyProducesContinuousSimpleSliceGeometry(bool useBaseGrid)
    {
        var graph = RouteCompleteAnatomy(useBaseGrid);

        Assert.Equal(3, graph.Segments.Count(segment => segment.Type == SupportSegmentType.Tip));
        Assert.Equal(2, graph.Segments.Count(segment => segment.Type == SupportSegmentType.MiniSupport));
        Assert.Contains(graph.Segments,
            segment => segment.Type == SupportSegmentType.Branch);
        Assert.Contains(graph.Segments,
            segment => segment.Type == SupportSegmentType.Trunk);
        var baseNodes = graph.Nodes.Where(node => node.Type == SupportNodeType.Base).ToList();
        Assert.Equal(2, baseNodes.Count);
        Assert.All(baseNodes, node => Assert.Equal(SupportBaseShape.DiscCone, node.BaseShape));
        Assert.Contains(baseNodes, node => Math.Abs(node.Position.X - (useBaseGrid ? 0f : 1f)) < 1e-3f);
        Assert.Contains(baseNodes, node => Math.Abs(node.Position.X + 10f) < 1e-3f);
        Assert.Contains(graph.Segments, segment => segment.Diameter == TrunkDiameter);
        Assert.Contains(graph.Segments, segment => segment.Diameter == BranchDiameter);
        Assert.Contains(graph.Segments, segment => segment.Diameter == MiniDiameter);

        var maxZ = TallestGraphCap(graph);
        var layerCount = (int)Math.Ceiling(maxZ / LayerHeight - 1e-6);
        var previousArea = double.NaN;
        var largestMemberArea = LargestMemberSectionArea(graph);
        for (var layer = 0; layer < layerCount; layer++)
        {
            var z = (layer + 0.5) * LayerHeight;
            var polygons = MeshSlicer.Finish(SupportSliceGeometry.SectionsAt(graph, z), 0);
            Assert.All(polygons, path => AssertSimpleClosedPolygon(path, layer, z));

            var area = MeshSlicer.AreaMm2(polygons);
            if (!double.IsNaN(previousArea))
            {
                var jump = Math.Abs(area - previousArea);
                Assert.True(jump <= largestMemberArea * 1.01,
                    $"layer {layer} at Z {z:0.###}: support area jumped " +
                    $"{jump:0.####} mm² (largest member section {largestMemberArea:0.####} mm²)");
            }
            previousArea = area;
        }

        AssertRegularTipJunctionMatchesParent(graph);
        // Mini rods are set aside (2026-09-07); like every cone tip they now taper to the ball
        // they grow from, so their configured diameter is no longer a slice invariant.

        var sliced = Slice(graph);
        Assert.Equal(layerCount, sliced.LayerCount);
        Assert.True(sliced.PrintHeight >= maxZ);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RoutedHiddenMembersStillSliceWhileDisabledMembersDoNot(bool useBaseGrid)
    {
        var graph = RouteCompleteAnatomy(useBaseGrid);
        var mini = graph.Segments.First(segment => segment.Type == SupportSegmentType.MiniSupport);
        var baseline = Slice(graph);

        var hiddenGraph = Clone(graph);
        hiddenGraph.GetSegment(mini.Id).Hidden = true;
        var hidden = Slice(hiddenGraph);
        Assert.Equal(baseline.LayerCount, hidden.LayerCount);
        Assert.Equal(baseline.VolumeMl, hidden.VolumeMl);
        Assert.Equal(baseline.Layers.Select(layer => layer.AreaMm2),
            hidden.Layers.Select(layer => layer.AreaMm2));

        var disabledGraph = Clone(graph);
        disabledGraph.GetSegment(mini.Id).Disabled = true;
        var disabled = Slice(disabledGraph);
        Assert.Equal(baseline.LayerCount, disabled.LayerCount);
        Assert.True(disabled.VolumeMl < baseline.VolumeMl);
        Assert.Contains(baseline.Layers.Zip(disabled.Layers), pair =>
            pair.First.AreaMm2 > pair.Second.AreaMm2 + 1e-4f);
    }

    private static SupportGraph RouteCompleteAnatomy(bool useBaseGrid)
    {
        var regularTips = new[]
        {
            new RoutingTip(new Vector3(1, 0, 14), Vector3.UnitZ, 0.34f,
                TipShape: SupportTipShape.Cone, ConeLength: 0.6f),
            new RoutingTip(new Vector3(-10, 0, 12.7f), Vector3.UnitZ, 0.32f,
                TipShape: SupportTipShape.Cone, ConeLength: 0.55f),
            new RoutingTip(new Vector3(3, 0, 10), Vector3.UnitZ, 0.3f,
                TipShape: SupportTipShape.Cone, ConeLength: 0.5f),
        };
        var miniTips = new[]
        {
            new RoutingTip(new Vector3(3, 1.5f, 9.5f), Vector3.UnitZ, 0.2f,
                MiniSupportOnly: true),
            new RoutingTip(new Vector3(3, -1.2f, 9.7f), Vector3.UnitZ, 0.2f,
                MiniSupportOnly: true),
        };
        var options = new TreeRoutingOptions
        {
            Seed = 23,
            UseBaseGrid = useBaseGrid,
            BaseGridPitch = 10,
            PreferExistingTrunks = true,
            TrunkDiameter = TrunkDiameter,
            BranchDiameter = BranchDiameter,
            MaxBranchLength = 8,
            ExistingTrunkBranchRange = 8,
            MiniSupportDiameter = MiniDiameter,
            MiniSupportTipDiameter = 0.2f,
            MiniSupportConeLength = MiniConeLength,
            MiniSupportMaxLength = 4,
            MiniSupportMaxAngleDegrees = 75,
            MiniSupportMaxFanPerBranchEnd = 4,
            BaseShape = SupportBaseShape.DiscCone,
            BaseDiameter = 3,
            BaseHeight = 0.6f,
            BaseConeHeight = 1.4f,
        };

        var result = new TreeSupportRouter(new LinearCollisionScene(), GrowthRuleSet.Default)
            .Route(regularTips.Concat(miniTips), options);

        Assert.Empty(result.Failures);
        return result.Graph;
    }

    private static void AssertRegularTipJunctionMatchesParent(SupportGraph graph)
    {
        var tip = Assert.Single(graph.Nodes,
            node => node.Type == SupportNodeType.Tip && node.Position.X == -10);
        var tipSegment = Assert.Single(graph.SegmentsAt(tip.Id),
            segment => segment.Type == SupportSegmentType.Tip);
        var junctionId = tipSegment.NodeA == tip.Id ? tipSegment.NodeB : tipSegment.NodeA;
        var junction = graph.GetNode(junctionId);
        var parent = Assert.Single(graph.SegmentsAt(junctionId), segment =>
            segment.Id != tipSegment.Id &&
            segment.Type is SupportSegmentType.Branch or SupportSegmentType.Trunk);
        var parentOtherId = parent.NodeA == junctionId ? parent.NodeB : parent.NodeA;
        var parentOther = graph.GetNode(parentOtherId);
        var parentAxis = Vector3.Normalize(parentOther.Position - junction.Position);
        var expectedArea = Math.PI * Math.Pow(parent.Diameter * 0.5, 2) /
                           Math.Abs(parentAxis.Z);
        var z = junction.Position.Z - 0.001;
        var polygons = MeshSlicer.Finish(SupportSliceGeometry.SectionsAt(graph, z), 0);
        var junctionPath = Assert.Single(polygons, path => Contains(path, junction.Position));
        var actualArea = Math.Abs(Clipper.Area(junctionPath)) /
                         (MeshSlicer.UnitsPerMm * MeshSlicer.UnitsPerMm);

        Assert.InRange(actualArea, expectedArea * 0.995, expectedArea * 1.005);
    }

    private static SliceResult Slice(SupportGraph graph)
    {
        var model = new SceneObject("synthetic witness", Box(1, 1, 0.5f))
        {
            Transform = Transform.Identity with { Translation = new Vector3(-10, -10, 0) },
        };
        var settings = PrintSettings.Default with
        {
            LayerHeight = LayerHeight,
            AntiAliasing = false,
            XyCompensation = 0,
        };
        return Slicer.Slice([model], TestPrinter, settings, supports: graph);
    }

    private static Mesh Box(float sizeX, float sizeY, float sizeZ)
    {
        var positions = new Vector3[8];
        for (var i = 0; i < positions.Length; i++)
            positions[i] = new Vector3(
                (i & 1) * sizeX, ((i >> 1) & 1) * sizeY, ((i >> 2) & 1) * sizeZ);
        int[] indices =
        {
            0, 2, 3, 0, 3, 1, 4, 5, 7, 4, 7, 6, 0, 1, 5, 0, 5, 4,
            2, 6, 7, 2, 7, 3, 0, 4, 6, 0, 6, 2, 1, 3, 7, 1, 7, 5,
        };
        return new Mesh(positions, indices);
    }

    private static SupportGraph Clone(SupportGraph graph)
    {
        var clone = new SupportGraph();
        foreach (var node in graph.Nodes) clone.AddNode(node.Clone());
        foreach (var segment in graph.Segments) clone.AddSegment(segment.Clone());
        return clone;
    }

    private static double TallestGraphCap(SupportGraph graph) => graph.Segments
        .Where(segment => !segment.Disabled)
        .Max(segment => Math.Max(graph.GetNode(segment.NodeA).Position.Z,
                            graph.GetNode(segment.NodeB).Position.Z) + segment.Diameter * 0.5);

    private static double LargestMemberSectionArea(SupportGraph graph) => graph.Segments
        .Where(segment => !segment.Disabled)
        .Max(segment =>
        {
            var a = graph.GetNode(segment.NodeA).Position;
            var b = graph.GetNode(segment.NodeB).Position;
            var axis = Vector3.Normalize(b - a);
            return Math.PI * Math.Pow(segment.Diameter * 0.5, 2) /
                   Math.Max(Math.Abs(axis.Z), 1e-3f);
        });

    private static bool Contains(Path64 path, Vector3 point)
    {
        var scaled = new Point64(
            (long)Math.Round(point.X * MeshSlicer.UnitsPerMm),
            (long)Math.Round(point.Y * MeshSlicer.UnitsPerMm));
        return Clipper.PointInPolygon(scaled, path) != PointInPolygonResult.IsOutside;
    }

    private static void AssertSimpleClosedPolygon(Path64 path, int layer, double z)
    {
        Assert.True(path.Count >= 3,
            $"layer {layer} at Z {z:0.###}: a closed polygon needs at least three vertices");
        for (var i = 0; i < path.Count; i++)
            Assert.NotEqual(path[i], path[(i + 1) % path.Count]);

        for (var i = 0; i < path.Count; i++)
        {
            var nextI = (i + 1) % path.Count;
            for (var j = i + 1; j < path.Count; j++)
            {
                var nextJ = (j + 1) % path.Count;
                if (i == j || nextI == j || nextJ == i) continue;
                Assert.False(Intersects(path[i], path[nextI], path[j], path[nextJ]),
                    $"layer {layer} at Z {z:0.###}: polygon edges {i} and {j} self-intersect");
            }
        }
    }

    private static bool Intersects(Point64 a, Point64 b, Point64 c, Point64 d)
    {
        var abC = Cross(a, b, c);
        var abD = Cross(a, b, d);
        var cdA = Cross(c, d, a);
        var cdB = Cross(c, d, b);
        if (Math.Sign(abC) != Math.Sign(abD) && Math.Sign(cdA) != Math.Sign(cdB)) return true;
        return abC == 0 && OnSegment(a, b, c) ||
               abD == 0 && OnSegment(a, b, d) ||
               cdA == 0 && OnSegment(c, d, a) ||
               cdB == 0 && OnSegment(c, d, b);
    }

    private static bool OnSegment(Point64 a, Point64 b, Point64 point) =>
        point.X >= Math.Min(a.X, b.X) && point.X <= Math.Max(a.X, b.X) &&
        point.Y >= Math.Min(a.Y, b.Y) && point.Y <= Math.Max(a.Y, b.Y);

    private static long Cross(Point64 a, Point64 b, Point64 c) =>
        (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
}
