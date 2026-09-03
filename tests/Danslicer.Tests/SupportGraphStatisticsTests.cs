using System.Numerics;
using Danslicer.Core.Supports;

namespace Danslicer.Tests;

public sealed class SupportGraphStatisticsTests
{
    [Fact]
    public void CountsVisiblePrintableAnatomyAndMeasuresLean()
    {
        var graph = new SupportGraph();
        var tip = Node(SupportNodeType.Tip, new Vector3(3, 0, 4));
        var junction = Node(SupportNodeType.Junction, Vector3.Zero);
        var @base = Node(SupportNodeType.Base, new Vector3(0, 0, -2));
        @base.BaseShape = SupportBaseShape.Disc;
        @base.BaseDiameter = 2;
        @base.BaseHeight = 1;
        graph.AddNode(tip);
        graph.AddNode(junction);
        graph.AddNode(@base);
        graph.AddSegment(Segment(tip, junction, SupportSegmentType.MiniSupport, 2));
        graph.AddSegment(Segment(junction, @base, SupportSegmentType.Trunk, 2));

        var result = SupportGraphStatistics.Calculate(graph);

        Assert.Equal(1, result.MiniCount);
        Assert.Equal(1, result.BaseCount);
        Assert.Equal(36.8699f, result.MaxLeanDegrees, 3);
        Assert.Equal(8d * Math.PI, result.EstimatedVolumeMm3, 6);
    }

    [Fact]
    public void DisabledElementsDoNotContribute()
    {
        var graph = new SupportGraph();
        var a = Node(SupportNodeType.Tip, Vector3.UnitZ);
        var b = Node(SupportNodeType.Base, Vector3.Zero);
        b.Disabled = true;
        b.BaseShape = SupportBaseShape.DiscCone;
        graph.AddNode(a);
        graph.AddNode(b);
        var segment = Segment(a, b, SupportSegmentType.MiniSupport, 1);
        segment.Disabled = true;
        graph.AddSegment(segment);

        var result = SupportGraphStatistics.Calculate(graph);

        Assert.Equal(0, result.MiniCount);
        Assert.Equal(0, result.BaseCount);
        Assert.Equal(0, result.EstimatedVolumeMm3);
    }

    private static SupportNode Node(SupportNodeType type, Vector3 position) => new()
    {
        Type = type,
        Position = position,
    };

    private static SupportSegment Segment(SupportNode a, SupportNode b,
        SupportSegmentType type, float diameter) => new()
    {
        Type = type,
        NodeA = a.Id,
        NodeB = b.Id,
        Diameter = diameter,
    };
}
