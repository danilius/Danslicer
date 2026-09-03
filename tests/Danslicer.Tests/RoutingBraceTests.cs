using System.Numerics;
using Danslicer.Core.Supports;
using Danslicer.Core.Supports.Routing;

namespace Danslicer.Tests;

public sealed class RoutingBraceTests
{
    [Fact]
    public void StageAddsOneBraceBetweenSlenderNeighboursAndIsIdempotent()
    {
        var graph = TwoPillars();
        var rules = GrowthRuleSet.Default;
        var brace = rules.Find<BraceGrowthRule>()!;
        brace.MinHeight = 5;
        brace.MinSlenderness = 5;
        brace.NeighbourDistance = 8;
        brace.MaxLength = 10;
        var stage = new SupportBraceStage(new LinearCollisionScene(), rules);

        Assert.Equal(1, stage.Apply(graph, new BraceStageOptions { Diameter = 0.7f, Seed = 42 }));
        var segment = Assert.Single(graph.Segments, item => item.Type == SupportSegmentType.Bracing);
        Assert.Equal(0.7f, segment.Diameter);
        Assert.Equal(0, stage.Apply(graph, new BraceStageOptions { Diameter = 0.7f, Seed = 42 }));
        Assert.Single(graph.Segments, item => item.Type == SupportSegmentType.Bracing);
    }

    [Fact]
    public void BraceRuleCanRejectShortOrDistantPillars()
    {
        var graph = TwoPillars();
        var rules = GrowthRuleSet.Default;
        var brace = rules.Find<BraceGrowthRule>()!;
        brace.MinSlenderness = 20;
        var stage = new SupportBraceStage(new LinearCollisionScene(), rules);

        Assert.Equal(0, stage.Apply(graph, new BraceStageOptions()));
        Assert.DoesNotContain(graph.Segments, item => item.Type == SupportSegmentType.Bracing);
    }

    private static SupportGraph TwoPillars()
    {
        var graph = new SupportGraph();
        AddPillar(graph, 0);
        AddPillar(graph, 6);
        return graph;
    }

    private static void AddPillar(SupportGraph graph, float x)
    {
        var baseNode = new SupportNode { Type = SupportNodeType.Base, Position = new Vector3(x, 0, 0) };
        var top = new SupportNode { Type = SupportNodeType.Junction, Position = new Vector3(x, 0, 10) };
        graph.AddNode(baseNode);
        graph.AddNode(top);
        graph.AddSegment(new SupportSegment
        {
            Type = SupportSegmentType.Branch,
            NodeA = baseNode.Id,
            NodeB = top.Id,
            Diameter = 1,
        });
    }
}
