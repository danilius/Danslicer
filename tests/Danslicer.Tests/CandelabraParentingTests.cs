using System.Numerics;
using System.Text.Json;
using Danslicer.Core.Config;
using Danslicer.Core.Supports;
using Danslicer.Core.Supports.Routing;

namespace Danslicer.Tests;

public sealed class CandelabraParentingTests
{
    private static List<RoutingTip> Row(float slope = 0) => Enumerable.Range(0, 13)
        .Select(i => new RoutingTip(new Vector3(i * 2 - 12, 0, 60 + slope * (i * 2 - 12)), Vector3.UnitZ, 0.4f,
            TipShape: SupportTipShape.Cone, ConeLength: 2, BallDiameter: 0.8f, PenetrationDepth: 0.2f)).ToList();
    private static SupportConfig Settings => new() { UseBaseGrid = false, CandelabraGroupWidthMm = 30, CandelabraMaxTips = 32 };

    [Fact]
    public void DenseSlopedRowAllowsFusedBranchesOnOneCentralTrunk()
    {
        var tips = Enumerable.Range(0, 13).Select(i =>
        {
            var x = (i - 6) * 0.4f;
            return new RoutingTip(new Vector3(x, 0, 60 + x), Vector3.UnitZ, 0.4f);
        }).ToList();
        var result = CandelabraParenting.Build(tips, Settings, new LinearCollisionScene(), SupportOrigin.Manual);
        Assert.Empty(result.Refused);
        var foot = Assert.Single(result.Edit.AddedNodes, n => n.Type == SupportNodeType.Base);
        Assert.Equal(0, foot.Position.X, 3);
        Assert.Equal(tips.Count, result.Edit.AddedNodes.Count(n => n.Type == SupportNodeType.Tip));
        var graph = new SupportGraph();
        foreach (var node in result.Edit.AddedNodes) graph.AddNode(node);
        foreach (var segment in result.Edit.AddedSegments) graph.AddSegment(segment);
        // Dense branches are intentionally allowed to fuse rather than forcing extra trunks.
        Assert.True(MemberSeparation.CountIntersections(graph) > 0);
    }

    [Fact]
    public void TipLimitedGroupsCentreTrunksOnTheirOwnContacts()
    {
        var result = CandelabraParenting.Build(Row(), Settings with { CandelabraMaxTips = 4 },
            new LinearCollisionScene(), SupportOrigin.Manual);
        Assert.Empty(result.Refused);
        var graph = new SupportGraph();
        foreach (var node in result.Edit.AddedNodes) graph.AddNode(node);
        foreach (var segment in result.Edit.AddedSegments) graph.AddSegment(segment);
        foreach (var component in graph.Supports())
        {
            var nodes = component.Nodes.Select(graph.GetNode).ToList();
            var tips = nodes.Where(n => n.Type == SupportNodeType.Tip).ToList();
            var foot = Assert.Single(nodes, n => n.Type == SupportNodeType.Base);
            Assert.Equal((tips.Min(n => n.Position.X) + tips.Max(n => n.Position.X)) / 2,
                foot.Position.X, 3);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(0.4f)]
    [InlineData(-0.6f)]
    public void RowHasCentralSpineAndEveryTipConnectsDirectly(float slope)
    {
        var tips = Row(slope);
        var (edit, refused) = CandelabraParenting.Build(tips, Settings, new LinearCollisionScene(), SupportOrigin.Manual);
        Assert.Empty(refused);
        var foot = Assert.Single(edit.AddedNodes, n => n.Type == SupportNodeType.Base);
        Assert.Equal(0, foot.Position.X, 3);
        Assert.Equal(13, edit.AddedNodes.Count(n => n.Type == SupportNodeType.Tip));
        var nodes = edit.AddedNodes.ToDictionary(n => n.Id);
        foreach (var branch in edit.AddedSegments.Where(s => s.Type == SupportSegmentType.Branch))
        {
            Assert.Contains(edit.AddedSegments, s => s.Type == SupportSegmentType.Tip && (s.NodeA == branch.NodeA || s.NodeB == branch.NodeA));
            Assert.Contains(edit.AddedSegments, s => s.Type == SupportSegmentType.Trunk && (s.NodeA == branch.NodeB || s.NodeB == branch.NodeB));
            Assert.Equal(0, nodes[branch.NodeB].Position.X, 3);
        }
        foreach (var node in edit.AddedNodes.Where(n => n.Type == SupportNodeType.Tip))
        {
            Assert.Equal(0.8f, node.BallDiameter); Assert.Equal(0.2f, node.PenetrationDepth);
        }
    }

    [Fact]
    public void WidthAndTipCountSplitGroupsWithoutSubbranches()
    {
        var settings = Settings with { CandelabraGroupWidthMm = 10, CandelabraMaxTips = 4 };
        var (edit, refused) = CandelabraParenting.Build(Row(), settings, new LinearCollisionScene(), SupportOrigin.Manual);
        Assert.Empty(refused);
        var graph = new SupportGraph();
        foreach (var n in edit.AddedNodes) graph.AddNode(n);
        foreach (var s in edit.AddedSegments) graph.AddSegment(s);
        Assert.True(graph.Nodes.Count(n => n.Type == SupportNodeType.Base) >= 4);
        foreach (var (ids, _) in graph.Supports())
        {
            var tips = ids.Select(graph.GetNode).Where(n => n.Type == SupportNodeType.Tip).ToList();
            Assert.InRange(tips.Count, 1, 4);
            Assert.True(tips.Max(n => n.Position.X) - tips.Min(n => n.Position.X) <= 10);
        }
    }

    [Fact]
    public void BlockedCentreFindsNearbyClearTrunkBeforeSplitting()
    {
        var scene = new LinearCollisionScene();
        scene.AddCapsule(new Vector3(0, 0, 5), new Vector3(0, 0, 35), 1);
        var (edit, refused) = CandelabraParenting.Build(Row(), Settings, scene, SupportOrigin.Manual);
        Assert.Empty(refused);
        Assert.Single(edit.AddedNodes, n => n.Type == SupportNodeType.Base);
        Assert.DoesNotContain(edit.AddedNodes, n => n.Type == SupportNodeType.Base && MathF.Abs(n.Position.X) < 1);
        var nodes = edit.AddedNodes.ToDictionary(n => n.Id);
        Assert.All(edit.AddedSegments, s => Assert.False(scene.IntersectsCapsule(
            nodes[s.NodeA].Position, nodes[s.NodeB].Position, s.Diameter / 2)));
        var forced = CandelabraParenting.Build(Row(), Settings, scene, SupportOrigin.Manual, Vector2.Zero);
        Assert.Empty(forced.Edit.AddedNodes);
        Assert.Equal(13, forced.Refused.Count);
    }

    [Fact]
    public void UnreachableContactDoesNotSplitCompatibleTipsAndHasItsOwnReason()
    {
        var tips = Row();
        var blocked = new RoutingTip(new Vector3(0, 3, 9), Vector3.UnitZ, 0.4f);
        tips.Insert(4, blocked);
        var reasons = new Dictionary<Vector3, string>();
        var result = CandelabraParenting.Build(tips, Settings, new LinearCollisionScene(), SupportOrigin.Manual,
            reportTipConstraint: (tip, reason) => reasons[tip.SurfacePoint] = reason);
        Assert.Equal(blocked, Assert.Single(result.Refused));
        Assert.Single(result.Edit.AddedNodes, n => n.Type == SupportNodeType.Base);
        Assert.Equal(13, result.Edit.AddedNodes.Count(n => n.Type == SupportNodeType.Tip));
        Assert.Single(reasons);
        Assert.Contains("10 mm", reasons[blocked.SurfacePoint]);
    }

    [Fact]
    public void MixedHeightGroupsRemainDirectAndRespectLimits()
    {
        var tips = Row().Select((t, i) => t with { SurfacePoint = t.SurfacePoint with { Z = i % 2 == 0 ? 18 : 60 } }).ToList();
        var settings = Settings with { CandelabraMaxTips = 5, ParentingMaxBranchLength = 12 };
        var result = CandelabraParenting.Build(tips, settings, new LinearCollisionScene(), SupportOrigin.Manual);
        Assert.Empty(result.Refused);
        var graph = new SupportGraph();
        foreach (var n in result.Edit.AddedNodes) graph.AddNode(n);
        foreach (var s in result.Edit.AddedSegments) graph.AddSegment(s);
        Assert.Contains(graph.Supports(), c => c.Nodes.Count(id => graph.GetNode(id).Type == SupportNodeType.Tip) >= 3);
        foreach (var component in graph.Supports())
            Assert.InRange(component.Nodes.Count(id => graph.GetNode(id).Type == SupportNodeType.Tip), 1, 5);
        foreach (var branch in graph.Segments.Where(s => s.Type == SupportSegmentType.Branch))
        {
            Assert.True(Vector3.Distance(graph.GetNode(branch.NodeA).Position, graph.GetNode(branch.NodeB).Position) <= 12.001f);
            Assert.Contains(graph.Segments, s => s.Type == SupportSegmentType.Trunk && (s.NodeA == branch.NodeB || s.NodeB == branch.NodeB));
            Assert.Contains(graph.Segments, s => s.Type == SupportSegmentType.Tip && s.NodeB == branch.NodeA);
        }
    }

    [Fact]
    public void ExistingClearContactRoutesAreTriedBeforeRebuildingCones()
    {
        var tips = new[] { -4f, 4f }.Select(x => new RoutingTip(new Vector3(x, 0, 60), Vector3.UnitZ, 0.4f)).ToList();
        var scene = new LinearCollisionScene();
        foreach (var tip in tips)
            scene.AddCapsule(tip.SurfacePoint - Vector3.UnitZ * 1.8f, tip.SurfacePoint - Vector3.UnitZ * 2.1f, 0.1f);
        var ends = tips.ToDictionary(t => t.SurfacePoint, t => t.SurfacePoint +
            Vector3.Normalize(new Vector3(-MathF.Sign(t.SurfacePoint.X), 0, -1)) * 2);
        var original = CandelabraParenting.Build(tips, Settings, scene, SupportOrigin.Manual);
        Assert.Equal(2, original.Refused.Count);
        var result = CandelabraParenting.Build(tips, Settings, scene, SupportOrigin.Manual, contactEnds: ends);
        Assert.Empty(result.Refused);
        Assert.Single(result.Edit.AddedNodes, n => n.Type == SupportNodeType.Base);
        var nodes = result.Edit.AddedNodes.ToDictionary(n => n.Id);
        Assert.All(result.Edit.AddedSegments.Where(s => s.Type == SupportSegmentType.Tip), s =>
            Assert.Equal(ends[nodes[s.NodeA].Position], nodes[s.NodeB].Position));
        // An old route is only a candidate: it still has to pass current collision checks.
        foreach (var end in ends.Values) scene.AddCapsule(end, end - Vector3.UnitZ * 0.1f, 0.1f);
        var blocked = CandelabraParenting.Build(tips, Settings, scene, SupportOrigin.Manual, contactEnds: ends);
        Assert.Equal(2, blocked.Refused.Count);
    }

    [Fact]
    public void ForcedCentreIsRespectedOrRefusedAsAWhole()
    {
        var (edit, refused) = CandelabraParenting.Build(Row(), Settings, new LinearCollisionScene(), SupportOrigin.Manual, new Vector2(2, 1));
        Assert.Empty(refused);
        Assert.Equal(new Vector3(2, 1, 0), Assert.Single(edit.AddedNodes, n => n.Type == SupportNodeType.Base).Position);
        var failed = CandelabraParenting.Build(Row(), Settings, new LinearCollisionScene(), SupportOrigin.Manual, new Vector2(100, 0));
        Assert.Empty(failed.Edit.AddedNodes); Assert.Equal(13, failed.Refused.Count);
    }

    [Fact]
    public void StyleRoundTripsAndOldFlagsStillLoad()
    {
        foreach (var style in Enum.GetValues<ParentingStyle>())
        {
            var source = Settings with { ParentingStyle = style };
            Assert.Equal(style, JsonSerializer.Deserialize<SupportConfig>(JsonSerializer.Serialize(source))!.ParentingStyle);
        }
        Assert.Equal(ParentingStyle.Tree, JsonSerializer.Deserialize<SupportConfig>("{\"ParentingHierarchical\":true}")!.ParentingStyle);
        Assert.Equal(ParentingStyle.Simple, JsonSerializer.Deserialize<SupportConfig>("{\"ParentingHierarchical\":false}")!.ParentingStyle);
        Assert.Equal(ParentingStyle.Candelabra, new SupportConfig().ParentingStyle);
    }

    [Fact]
    public void TiltedConesRetryVerticalWhenConeBendPreventsLateralBranches()
    {
        var tips = Row().Select(t => t with { InwardSurfaceNormal = Vector3.Normalize(new Vector3(0, -1, 1)) }).ToList();
        var reasons = new List<string>();
        var (edit, refused) = CandelabraParenting.Build(tips, Settings with { ParentingMaxConeBend = 45 },
            new LinearCollisionScene(), SupportOrigin.Manual, reportConstraint: reasons.Add);
        Assert.Empty(refused);
        Assert.Empty(reasons);
        Assert.Single(edit.AddedNodes, n => n.Type == SupportNodeType.Base);
        var nodes = edit.AddedNodes.ToDictionary(n => n.Id);
        Assert.All(edit.AddedSegments.Where(s => s.Type == SupportSegmentType.Tip), s =>
            Assert.Equal(nodes[s.NodeA].Position.X, nodes[s.NodeB].Position.X, 4));
        Assert.True(edit.AddedSegments.Count(s => s.Type == SupportSegmentType.Tip &&
            MathF.Abs(nodes[s.NodeA].Position.Y - nodes[s.NodeB].Position.Y) < 0.001f) >= 12);
    }

    [Fact]
    public void BlockedVerticalFallbackKeepsConeBendLimitAndExplainsSingles()
    {
        var tips = Row().Select(t => t with { InwardSurfaceNormal = Vector3.Normalize(new Vector3(0, -1, 1)) }).ToList();
        var scene = new LinearCollisionScene();
        foreach (var t in tips) scene.AddCapsule(t.SurfacePoint - Vector3.UnitZ * 1.8f, t.SurfacePoint - Vector3.UnitZ * 2.1f, 0.1f);
        var reasons = new List<string>();
        var (edit, refused) = CandelabraParenting.Build(tips, Settings with { ParentingMaxConeBend = 45 },
            scene, SupportOrigin.Manual, reportConstraint: reasons.Add);
        Assert.Empty(refused);
        Assert.Equal(13, edit.AddedNodes.Count(n => n.Type == SupportNodeType.Base));
        Assert.Contains(reasons, r => r.Contains("cone bend (45"));
    }
}
