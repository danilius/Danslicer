using System.Numerics;
using Danslicer.Core.Config;
using Danslicer.Core.Geometry;
using Danslicer.Core.Supports;
using Danslicer.Core.Supports.Routing;

namespace Danslicer.Tests;

/// <summary>
/// Hierarchical parenting (SUPPORT-GEOMETRY-SPEC "Parenting", hierarchical tree): a run of
/// tips becomes one tree on one trunk, no junction sits below the minimum branch height, and
/// a tip with no clear cone keeps its support.
/// </summary>
public sealed class HierarchicalParentingTests
{
    private static Mesh Box(Vector3 min, Vector3 max)
    {
        var p = new[]
        {
            new Vector3(min.X, min.Y, min.Z), new(max.X, min.Y, min.Z),
            new(max.X, max.Y, min.Z), new(min.X, max.Y, min.Z),
            new(min.X, min.Y, max.Z), new(max.X, min.Y, max.Z),
            new(max.X, max.Y, max.Z), new(min.X, max.Y, max.Z),
        };
        int[] indices =
        [
            0, 3, 2, 0, 2, 1, 4, 5, 6, 4, 6, 7,
            0, 1, 5, 0, 5, 4, 2, 3, 7, 2, 7, 6,
            0, 4, 7, 0, 7, 3, 1, 2, 6, 1, 6, 5,
        ];
        return new Mesh(p, indices);
    }

    private static ICollisionScene Slab(float bottomZ)
    {
        var scene = new LinearCollisionScene();
        scene.AddMesh(Box(new Vector3(-40, -40, bottomZ), new Vector3(40, 40, bottomZ + 6)), Matrix4x4.Identity);
        return scene;
    }

    private static List<RoutingTip> Row(int count, float pitch, float z) =>
        Enumerable.Range(0, count)
            .Select(i => new RoutingTip(new Vector3(i * pitch - (count - 1) * pitch / 2f, 0, z), Vector3.UnitZ, 0.4f,
                TipShape: SupportTipShape.Cone, ConeLength: 2f))
            .ToList();

    private static SupportConfig Settings(bool grid, float angle = 45f) => new()
    {
        UseBaseGrid = grid, BaseGridPitch = 6f, MemberAngleDegrees = angle,
        MaxBranchLength = 50f, ExistingTrunkBranchRange = 50f, MinBranchAttachHeightMm = 10f,
        TipMemberLength = 2f,
    };

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ARowOfTipsBecomesOneTreeOnOneTrunk(bool grid)
    {
        var tips = Row(8, 2.5f, z: 40);

        var (edit, refused) = HierarchicalParenting.Build(tips, Settings(grid), Slab(40), SupportOrigin.Manual, seed: 1);

        Assert.Empty(refused);
        Assert.Equal(8, edit.AddedNodes.Count(n => n.Type == SupportNodeType.Tip));
        Assert.Equal(1, edit.AddedNodes.Count(n => n.Type == SupportNodeType.Base));
        Assert.Equal(1, edit.AddedSegments.Count(s => s.Type == SupportSegmentType.Trunk));
        // 8 cones, 7 merges: every merge adds two branches, plus at most one lattice branch.
        Assert.InRange(edit.AddedSegments.Count(s => s.Type == SupportSegmentType.Branch), 14, 15);
        Assert.All(edit.AddedNodes.Where(n => n.Type == SupportNodeType.Junction),
            n => Assert.True(n.Position.Z >= 10f - 1e-3f, $"junction at {n.Position.Z} below the minimum height"));
        Assert.All(edit.AddedSegments, s => Assert.Contains(edit.AddedNodes, n => n.Id == s.NodeA));
        Assert.All(edit.AddedSegments, s => Assert.Contains(edit.AddedNodes, n => n.Id == s.NodeB));
    }

    [Fact]
    public void ASteepAngleLimitStillMergesByStackingJunctions()
    {
        var tips = Row(6, 2.5f, z: 60);

        var (edit, refused) = HierarchicalParenting.Build(tips, Settings(grid: false, angle: 10f), Slab(60), SupportOrigin.Manual, seed: 1);

        Assert.Empty(refused);
        Assert.Equal(1, edit.AddedNodes.Count(n => n.Type == SupportNodeType.Base));
        // Every branch leans at most 10° from vertical.
        foreach (var s in edit.AddedSegments.Where(s => s.Type == SupportSegmentType.Branch))
        {
            var a = edit.AddedNodes.Single(n => n.Id == s.NodeA).Position;
            var b = edit.AddedNodes.Single(n => n.Id == s.NodeB).Position;
            var d = b - a;
            var lean = MathF.Atan2(new Vector2(d.X, d.Y).Length(), MathF.Abs(d.Z)) * 180f / MathF.PI;
            Assert.True(lean <= 10.01f, $"branch leans {lean:0.0}°");
        }
    }

    [Fact]
    public void TipsTooLowToMergeAboveTheMinimumHeightEachGetATrunk()
    {
        // At z = 11 the cones end at 9 mm: below the 10 mm floor, so no cone can be made at all.
        var tips = Row(3, 2.5f, z: 11);

        var (edit, refused) = HierarchicalParenting.Build(tips, Settings(grid: false), Slab(11), SupportOrigin.Manual, seed: 1);

        Assert.Equal(3, refused.Count);
        Assert.Empty(edit.AddedNodes);
    }

    /// <summary>A standing support: tip at <paramref name="tip"/>, vertical trunk from <paramref name="topZ"/> to a base.</summary>
    private static SupportGraph StandingSupport(Vector3 tip, float topZ, float baseHeight = 0.8f)
    {
        var graph = new SupportGraph();
        var contact = new SupportNode { Type = SupportNodeType.Tip, Position = tip, TipShape = SupportTipShape.Cone };
        var top = new SupportNode { Type = SupportNodeType.Junction, Position = new Vector3(tip.X, tip.Y, topZ) };
        var bottom = new SupportNode
        {
            Type = SupportNodeType.Base, Position = new Vector3(tip.X, tip.Y, 0), BaseHeight = baseHeight,
            BaseShape = SupportBaseShape.Disc,
        };
        graph.AddNode(contact);
        graph.AddNode(top);
        graph.AddNode(bottom);
        graph.AddSegment(new SupportSegment { Type = SupportSegmentType.Tip, NodeA = contact.Id, NodeB = top.Id, Diameter = 1f });
        graph.AddSegment(new SupportSegment { Type = SupportSegmentType.Trunk, NodeA = top.Id, NodeB = bottom.Id, Diameter = 2f });
        return graph;
    }

    [Fact]
    public void AClusterJoinsAnExistingTrunkInsteadOfDroppingItsOwn()
    {
        // Three new tips 6 mm from a standing support: the junction reaches its trunk sideways.
        var tips = Row(3, 2.5f, z: 40);
        var standing = StandingSupport(new Vector3(8, 0, 40), topZ: 38);
        var obstacles = new LinearCollisionScene();
        obstacles.AddSupportGraph(standing);
        var trunks = HierarchicalParenting.ExistingTrunks(standing, null);
        Assert.Single(trunks);

        var (edit, refused) = HierarchicalParenting.Build(tips, Settings(grid: false), new CompositeCollisionScene(Slab(40), obstacles),
            SupportOrigin.Manual, seed: 1, existingTrunks: trunks);

        Assert.Empty(refused);
        Assert.Equal(0, edit.AddedNodes.Count(n => n.Type == SupportNodeType.Base));
        // The trunk was split at the attach point: the old one goes, two pieces come.
        Assert.Single(edit.RemovedSegments);
        Assert.Equal(SupportSegmentType.Trunk, edit.RemovedSegments[0].Type);
        Assert.Equal(2, edit.AddedSegments.Count(s => s.Type == SupportSegmentType.Trunk));
        var attach = edit.AddedNodes.Single(n => n.Type == SupportNodeType.Junction && MathF.Abs(n.Position.X - 8) < 1e-3f);
        Assert.InRange(attach.Position.Z, 10f, 38f);
        Assert.Contains(edit.AddedSegments, s => s.Type == SupportSegmentType.Branch && s.NodeB == attach.Id);
    }

    [Fact]
    public void AJunctionAboveTheTrunkTopJoinsTheTopNode()
    {
        // The standing trunk's top is far below the cluster: the branch lands on the top node.
        var tips = Row(2, 2.5f, z: 40);
        var standing = StandingSupport(new Vector3(3, 0, 40), topZ: 20);
        var obstacles = new LinearCollisionScene();
        obstacles.AddSupportGraph(standing);
        var trunks = HierarchicalParenting.ExistingTrunks(standing, null);
        var top = trunks[0].Top;

        var (edit, refused) = HierarchicalParenting.Build(tips, Settings(grid: false), new CompositeCollisionScene(Slab(40), obstacles),
            SupportOrigin.Manual, seed: 1, existingTrunks: trunks);

        Assert.Empty(refused);
        Assert.Empty(edit.RemovedSegments);
        Assert.Equal(0, edit.AddedNodes.Count(n => n.Type == SupportNodeType.Base));
        Assert.Contains(edit.AddedSegments, s => s.Type == SupportSegmentType.Branch && s.NodeB == top.Id);
    }

    [Fact]
    public void TwoClustersMaySplitTheSameTrunk()
    {
        // Two lone tips either side of a wall that blocks their merge, both within reach of one
        // standing trunk: the second split cuts a piece the first split made (crashed 2026-09-08
        // as a removal of a segment the graph never held).
        var tips = new List<RoutingTip>
        {
            new(new Vector3(3, 0, 40), Vector3.UnitZ, 0.4f, TipShape: SupportTipShape.Cone, ConeLength: 2f),
            new(new Vector3(5, 0, 48), Vector3.UnitZ, 0.4f, TipShape: SupportTipShape.Cone, ConeLength: 2f),
        };
        var standing = StandingSupport(new Vector3(0, 0, 46), topZ: 44);
        var obstacles = new LinearCollisionScene();
        obstacles.AddSupportGraph(standing);
        obstacles.AddMesh(Box(new Vector3(3.5f, -2, 40), new Vector3(4.5f, 2, 43.5f)), Matrix4x4.Identity);
        var settings = Settings(grid: false) with { MaxBranchLength = 10f };

        var (edit, refused) = HierarchicalParenting.Build(tips, settings, obstacles, SupportOrigin.Manual, seed: 1,
            existingTrunks: HierarchicalParenting.ExistingTrunks(standing, null));

        Assert.Empty(refused);
        Assert.Equal(0, edit.AddedNodes.Count(n => n.Type == SupportNodeType.Base));
        Assert.Single(edit.RemovedSegments);
        Assert.Equal(3, edit.AddedSegments.Count(s => s.Type == SupportSegmentType.Trunk));
        Assert.Equal(2, edit.AddedSegments.Count(s => s.Type == SupportSegmentType.Branch));
        // The pieces chain from the old top to the old base through the two attach junctions.
        var pieces = edit.AddedSegments.Where(s => s.Type == SupportSegmentType.Trunk).ToList();
        Assert.Contains(pieces, s => s.NodeA == standing.Nodes.Single(n => n.Type == SupportNodeType.Junction).Id);
        Assert.Contains(pieces, s => s.NodeB == standing.Nodes.Single(n => n.Type == SupportNodeType.Base).Id);
    }

    [Fact]
    public void ATrunkOutOfRangeIsNotJoined()
    {
        var tips = Row(2, 2.5f, z: 40);
        var standing = StandingSupport(new Vector3(30, 0, 40), topZ: 38);
        var obstacles = new LinearCollisionScene();
        obstacles.AddSupportGraph(standing);
        var settings = Settings(grid: false) with { ExistingTrunkBranchRange = 8f };

        var (edit, refused) = HierarchicalParenting.Build(tips, settings, new CompositeCollisionScene(Slab(40), obstacles),
            SupportOrigin.Manual, seed: 1, existingTrunks: HierarchicalParenting.ExistingTrunks(standing, null));

        Assert.Empty(refused);
        Assert.Empty(edit.RemovedSegments);
        Assert.Equal(1, edit.AddedNodes.Count(n => n.Type == SupportNodeType.Base));
    }

    [Fact]
    public void FarApartTipsStaySeparateTrees()
    {
        var tips = new List<RoutingTip>
        {
            new(new Vector3(-30, 0, 40), Vector3.UnitZ, 0.4f, TipShape: SupportTipShape.Cone, ConeLength: 2f),
            new(new Vector3(30, 0, 40), Vector3.UnitZ, 0.4f, TipShape: SupportTipShape.Cone, ConeLength: 2f),
        };
        var settings = Settings(grid: false) with { MaxBranchLength = 8f };

        var (edit, refused) = HierarchicalParenting.Build(tips, settings, Slab(40), SupportOrigin.Manual, seed: 1);

        Assert.Empty(refused);
        Assert.Equal(2, edit.AddedNodes.Count(n => n.Type == SupportNodeType.Base));
    }
}
