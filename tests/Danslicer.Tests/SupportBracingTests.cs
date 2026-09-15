using System.Numerics;
using Danslicer.Core;
using Danslicer.Core.Config;
using Danslicer.Core.Geometry;
using Danslicer.Core.IO;
using Danslicer.Core.Scene;
using Danslicer.Core.Supports;
using Danslicer.Core.Supports.Routing;

namespace Danslicer.Tests;

/// <summary>
/// Bracing (SUPPORT-GEOMETRY-SPEC "Bracing"): ladders of braces between neighbouring trunks,
/// added on without splitting a trunk, as one undo step, gone with the supports they tie.
/// </summary>
public sealed class SupportBracingTests
{
    [Theory]
    [InlineData(0f)]
    [InlineData(2f)]
    [InlineData(4f)]
    public void AutomaticFitsAlternatingLaddersWithExactEndpointGapBelowShorterTrunk(float gap)
    {
        var (document, slab) = SlabWithSingles(3, 6);
        foreach (var node in document.Supports.Nodes)
            node.Position = node.Position with { Z = node.Position.Z * (node.Position.X < -1 ? 0.6f : node.Position.X < 1 ? 0.8f : 1f) };
        var tips = document.Supports.Nodes.Where(n => n.Type == SupportNodeType.Tip).Select(n => n.Id).ToList();
        var settings = document.SupportSettings with
        {
            BracingPattern = BracingPattern.Automatic, BracingEndpointGapMm = gap,
            BracingNeighbourDistanceMm = 1, BracingSpacingMm = 100,
        };
        var plan = SupportBracing.Plan(document.Supports, slab.Id, tips, settings, new LinearCollisionScene())!.Value;
        Assert.Equal(3, plan.Outcome.SupportsTied);
        var nodes = plan.Edit.AddedNodes.ToDictionary(n => n.Id);
        var pairs = plan.Edit.AddedSegments.Select(s => (Foot: nodes[s.NodeA].Position, Head: nodes[s.NodeB].Position))
            .GroupBy(r => (Left: MathF.Min(r.Foot.X, r.Head.X), Right: MathF.Max(r.Foot.X, r.Head.X)))
            .OrderBy(g => g.Key.Left).ToArray();
        Assert.Equal(2, pairs.Length);
        var firstDirections = new List<float>();
        foreach (var pair in pairs)
        {
            var rungs = pair.OrderByDescending(r => r.Head.Z).ToArray();
            Assert.True(rungs.Length >= 2);
            firstDirections.Add(MathF.Sign(rungs[0].Head.X - rungs[0].Foot.X));
            var shorterTop = document.Supports.Segments.Where(s => s.Type == SupportSegmentType.Trunk)
                .Select(s => new[] { document.Supports.GetNode(s.NodeA).Position, document.Supports.GetNode(s.NodeB).Position })
                .Where(p => p[0].X == pair.Key.Left || p[0].X == pair.Key.Right)
                .GroupBy(p => p[0].X).Min(g => g.Max(p => p.Max(v => v.Z)));
            Assert.True(rungs[0].Head.Z <= shorterTop);
            for (var i = 1; i < rungs.Length; i++)
            {
                Assert.Equal(rungs[i - 1].Foot.X, rungs[i].Head.X);
                Assert.Equal(gap, rungs[i - 1].Foot.Z - rungs[i].Head.Z, 3);
                Assert.Equal(-MathF.Sign(rungs[i - 1].Head.X - rungs[i - 1].Foot.X),
                    MathF.Sign(rungs[i].Head.X - rungs[i].Foot.X));
            }
        }
        Assert.Equal(-firstDirections[0], firstDirections[1]);
    }

    [Fact]
    public void EndpointGapDefaultsToTwoAndRoundTripsIndependentlyOfLegacyPitch()
    {
        Assert.Equal(2f, new SupportConfig().BracingEndpointGapMm);
        Assert.Equal(2f, System.Text.Json.JsonSerializer.Deserialize<SupportConfig>("{\"BracingSpacingMm\":10}")!.BracingEndpointGapMm);
        var settings = new SupportConfig { BracingEndpointGapMm = 3.5f };
        Assert.Equal(3.5f, System.Text.Json.JsonSerializer.Deserialize<SupportConfig>(
            System.Text.Json.JsonSerializer.Serialize(settings))!.BracingEndpointGapMm);
    }

    [Theory]
    [InlineData(BracingPattern.Automatic)]
    [InlineData(BracingPattern.Zigzag)]
    public void DenseLaddersAvoidCrossoversWithinAndBetweenPairs(BracingPattern pattern)
    {
        var (document, _) = SlabWithSingles(3, 6);
        document.SupportSettings = document.SupportSettings with
        {
            BracingPattern = pattern, BracingSpacingMm = 2, BracingNeighbourDistanceMm = 15,
        };
        var outcome = document.BraceSupports()!;
        Assert.Equal(3, outcome.SupportsTied);
        Assert.True(outcome.Braces >= 2);
        AssertNoBraceCrossovers(document.Supports);
        AssertBraceEndsSound(document);
    }

    [Fact]
    public void NewLaddersAvoidRetainedBraces()
    {
        var (document, slab) = SlabWithSingles(3, 6);
        var a = new SupportNode { Type = SupportNodeType.BraceEnd, Position = new Vector3(-6, 0, 12) };
        var b = new SupportNode { Type = SupportNodeType.BraceEnd, Position = new Vector3(6, 0, 30) };
        document.Supports.AddNode(a);
        document.Supports.AddNode(b);
        document.Supports.AddSegment(new SupportSegment
        {
            Type = SupportSegmentType.Bracing, NodeA = a.Id, NodeB = b.Id, Diameter = 1.2f,
            Origin = SupportOrigin.ManualFor(slab.Id),
        });
        var tips = document.Supports.Nodes.Where(n => n.Type == SupportNodeType.Tip).Select(n => n.Id).ToList();
        var plan = SupportBracing.Plan(document.Supports, slab.Id, tips,
            document.SupportSettings with { BracingPattern = BracingPattern.Automatic }, new LinearCollisionScene())!.Value;
        Assert.True(plan.Outcome.Braces > 0);
        foreach (var node in plan.Edit.AddedNodes) document.Supports.AddNode(node);
        foreach (var segment in plan.Edit.AddedSegments) document.Supports.AddSegment(segment);
        AssertNoBraceCrossovers(document.Supports);
        AssertBraceEndsSound(document);
    }

    private static void AssertNoBraceCrossovers(SupportGraph graph)
    {
        var braces = graph.Segments.Where(s => s.Type == SupportSegmentType.Bracing).ToArray();
        for (var i = 0; i < braces.Length; i++)
        for (var j = i + 1; j < braces.Length; j++)
        {
            var first = braces[i]; var second = braces[j];
            var a = graph.GetNode(first.NodeA).Position; var b = graph.GetNode(first.NodeB).Position;
            var c = graph.GetNode(second.NodeA).Position; var d = graph.GetNode(second.NodeB).Position;
            if (new[] { Vector3.Distance(a, c), Vector3.Distance(a, d), Vector3.Distance(b, c), Vector3.Distance(b, d) }.Min() <= 0.01f)
                continue; // shared attachment, including equivalent nodes from an earlier operation
            Assert.True(GeometryDistance.SegmentSegmentSquared(a, b, c, d) >=
                MathF.Pow((first.Diameter + second.Diameter) / 2, 2), "Brace cylinders cross or overlap away from a joint.");
        }
    }

    [Fact]
    public void AutomaticBracesShortPairsAndKeepsUndoAndRepeatBehaviour()
    {
        var (document, _) = SlabWithSingles(2, 6, height: 15);
        document.SupportSettings = document.SupportSettings with { BracingPattern = BracingPattern.Automatic };
        var result = document.BraceSupports();
        Assert.NotNull(result);
        Assert.True(result.Braces > 0);
        Assert.Equal(2, result.SupportsTied);
        AssertBraceEndsSound(document);
        Assert.Equal(0, document.BraceSupports()!.Braces);
        Assert.True(document.History.Undo());
        Assert.Equal(0, Braces(document));
        Assert.True(document.History.Redo());
        Assert.Equal(result.Braces, Braces(document));
    }

    [Fact]
    public void AutomaticRetriesAnglesAndOffsetsAroundModelObstacles()
    {
        var (document, slab) = SlabWithSingles(2, 6);
        var ids = document.Supports.Nodes.Where(n => n.Type == SupportNodeType.Tip).Select(n => n.Id).ToList();
        var scene = new LinearCollisionScene();
        scene.AddSphere(new Vector3(0, 0, 25), 2);
        var fixedSettings = document.SupportSettings with { BracingAngleDegrees = 1 };
        var fixedPlan = SupportBracing.Plan(document.Supports, slab.Id, ids, fixedSettings, scene)!.Value;
        Assert.Empty(fixedPlan.Edit.AddedSegments);
        var automatic = SupportBracing.Plan(document.Supports, slab.Id, ids,
            fixedSettings with { BracingPattern = BracingPattern.Automatic }, scene)!.Value;
        Assert.True(automatic.Outcome.Braces >= 2);
        var nodes = automatic.Edit.AddedNodes.ToDictionary(n => n.Id);
        foreach (var segment in automatic.Edit.AddedSegments)
        {
            var a = nodes[segment.NodeA].Position;
            var b = nodes[segment.NodeB].Position;
            Assert.False(scene.IntersectsCapsule(a, b, segment.Diameter / 2));
            Assert.True(MathF.Abs(b.Z - a.Z) > 0);
        }
    }

    [Fact]
    public void AutomaticPlansEveryNeighbourPairAtItsOwnHeight()
    {
        var (document, slab) = SlabWithSingles(3, 6);
        foreach (var node in document.Supports.Nodes)
            node.Position = node.Position with { Z = node.Position.Z * (node.Position.X < -1 ? 0.25f : node.Position.X < 1 ? 0.5f : 1f) };
        var ids = document.Supports.Nodes.Where(n => n.Type == SupportNodeType.Tip).Select(n => n.Id).ToList();
        var plan = SupportBracing.Plan(document.Supports, slab.Id, ids,
            document.SupportSettings with { BracingPattern = BracingPattern.Automatic }, new LinearCollisionScene())!.Value;
        Assert.Equal(3, plan.Outcome.SupportsTied);
        var nodes = plan.Edit.AddedNodes.ToDictionary(n => n.Id);
        var pairs = plan.Edit.AddedSegments.Select(s =>
            (MathF.Min(nodes[s.NodeA].Position.X, nodes[s.NodeB].Position.X),
             MathF.Max(nodes[s.NodeA].Position.X, nodes[s.NodeB].Position.X))).ToHashSet();
        Assert.Contains((-6f, 0f), pairs);
        Assert.Contains((0f, 6f), pairs);
    }

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

    /// <summary>A floating slab at <paramref name="height"/> with single supports along X at <paramref name="pitch"/>.</summary>
    private static (Document Document, SceneObject Slab) SlabWithSingles(int count, float pitch, float height = 40,
        bool autoBracing = false)
    {
        var document = new Document();
        var slab = new SceneObject("slab", Box(new Vector3(-30, -30, height), new Vector3(30, 30, height + 6)));
        document.AddObject(slab);
        document.Select(slab);
        document.SupportSettings = new SupportConfig
        {
            UseBaseGrid = false, IndependentManualSupports = true, AutoParenting = false, AutoBracing = autoBracing,
            BracingPattern = BracingPattern.Zigzag,
        };
        for (var i = 0; i < count; i++)
            Assert.True(document.AddManualSupport(slab, new Vector3(i * pitch - (count - 1) * pitch / 2f, 0, height), -Vector3.UnitZ));
        return (document, slab);
    }

    private static int Braces(Document d) => d.Supports.Segments.Count(s => s.Type == SupportSegmentType.Bracing);
    private static int BraceEnds(Document d) => d.Supports.Nodes.Count(n => n.Type == SupportNodeType.BraceEnd);
    private static int Trunks(Document d) => d.Supports.Segments.Count(s => s.Type == SupportSegmentType.Trunk);

    /// <summary>Every brace end sits on a member and carries a brace: nothing dangles.</summary>
    private static void AssertBraceEndsSound(Document d)
    {
        foreach (var node in d.Supports.Nodes.Where(n => n.Type == SupportNodeType.BraceEnd))
        {
            Assert.NotNull(SupportBracing.CarrierOf(d.Supports, node));
            Assert.Contains(d.Supports.SegmentsAt(node.Id), s => s.Type == SupportSegmentType.Bracing);
        }
        foreach (var brace in d.Supports.Segments.Where(s => s.Type == SupportSegmentType.Bracing))
        {
            Assert.Equal(SupportNodeType.BraceEnd, d.Supports.GetNode(brace.NodeA).Type);
            Assert.Equal(SupportNodeType.BraceEnd, d.Supports.GetNode(brace.NodeB).Type);
        }
    }

    [Fact]
    public void BracingLaysALadderWithoutSplittingTrunks()
    {
        var (document, _) = SlabWithSingles(2, 6);
        var trunksBefore = Trunks(document);
        var elementsBefore = document.Supports.Nodes.Count + document.Supports.Segments.Count;

        var outcome = document.BraceSupports();

        Assert.NotNull(outcome);
        Assert.Equal(2, outcome.Operands);
        Assert.Equal(2, outcome.SupportsTied);
        // 40 mm trunks, rungs of 6 mm rise laid from the top down to the 10 mm floor: at least four.
        Assert.True(outcome.Braces >= 4, $"{outcome.Braces} braces");
        Assert.Equal(outcome.Braces, Braces(document));
        // Continuous braces share their end nodes: n braces need n + 1 ends.
        Assert.Equal(outcome.Braces + 1, BraceEnds(document));
        Assert.Equal(trunksBefore, Trunks(document));
        Assert.Equal(elementsBefore + 2 * outcome.Braces + 1, document.Supports.Nodes.Count + document.Supports.Segments.Count);
        AssertBraceEndsSound(document);
        // Each brace rises at 45° over the 6 mm gap, starts where the last ended, and alternates sides.
        var braces = document.Supports.Segments.Where(s => s.Type == SupportSegmentType.Bracing)
            .Select(s =>
            {
                var a = document.Supports.GetNode(s.NodeA).Position;
                var b = document.Supports.GetNode(s.NodeB).Position;
                return a.Z <= b.Z ? (Foot: a, Head: b) : (Foot: b, Head: a);
            })
            .OrderBy(b => b.Foot.Z).ToList();
        // Laid from the top down at exactly 45°; the rung that would start under the 10 mm floor is left out.
        Assert.InRange(braces[0].Foot.Z, 10f, 16f);
        var top = document.Supports.Segments.Where(s => s.Type == SupportSegmentType.Trunk)
            .Max(s => MathF.Max(document.Supports.GetNode(s.NodeA).Position.Z, document.Supports.GetNode(s.NodeB).Position.Z));
        Assert.Equal(top - 0.6f, braces[^1].Head.Z, 2);
        for (var i = 0; i < braces.Count; i++)
        {
            Assert.Equal(6f, braces[i].Head.Z - braces[i].Foot.Z, 2);
            if (i == 0) continue;
            Assert.Equal(braces[i - 1].Head, braces[i].Foot);
            Assert.NotEqual(braces[i - 1].Foot.X, braces[i].Foot.X);
        }
        Assert.Equal("Brace supports", document.History.UndoName);
        // A brace end is no support of its own.
        Assert.DoesNotContain(document.Supports.Supports(),
            c => c.Nodes.All(id => document.Supports.GetNode(id).Type == SupportNodeType.BraceEnd));
    }

    [Fact]
    public void BracingIsIdempotentAndOneUndoStep()
    {
        var (document, _) = SlabWithSingles(2, 6);
        document.BraceSupports();
        var braces = Braces(document);
        var after = document.Supports.Nodes.Count + document.Supports.Segments.Count;

        var again = document.BraceSupports();
        Assert.NotNull(again);
        Assert.Equal(0, again.Braces);
        Assert.Equal(after, document.Supports.Nodes.Count + document.Supports.Segments.Count);

        Assert.True(document.History.Undo());
        Assert.Equal(0, Braces(document));
        Assert.Equal(0, BraceEnds(document));
        Assert.True(document.History.Redo());
        Assert.Equal(braces, Braces(document));
        AssertBraceEndsSound(document);
    }

    [Fact]
    public void ConsecutivePairsClimbInOppositeDirectionsAndMeetAtJoints()
    {
        var (document, _) = SlabWithSingles(3, 6);
        var outcome = document.BraceSupports();

        Assert.NotNull(outcome);
        Assert.Equal(3, outcome.SupportsTied);
        // Pair (−6, 0) starts from −6 and pair (0, +6) from +6, so the top rungs both head to the middle trunk.
        var topHeads = document.Supports.Segments.Where(s => s.Type == SupportSegmentType.Bracing)
            .Select(s =>
            {
                var a = document.Supports.GetNode(s.NodeA).Position;
                var b = document.Supports.GetNode(s.NodeB).Position;
                return a.Z >= b.Z ? a : b;
            })
            .GroupBy(head => MathF.Round(head.X)).Select(g => (X: g.Key, Top: g.Max(h => h.Z))).ToList();
        var highest = topHeads.Max(t => t.Top);
        Assert.Equal([0f], topHeads.Where(t => MathF.Abs(t.Top - highest) < 0.01f).Select(t => t.X).ToList());
        // Both pairs got the full ladder: the second pair was not blocked by the first pair's braces.
        var perPair = outcome.Braces / 2;
        Assert.True(perPair >= 4 && outcome.Braces == perPair * 2, $"{outcome.Braces} braces");
        AssertBraceEndsSound(document);
    }

    [Fact]
    public void BracesRunThroughOtherSupportsButNotTheModel()
    {
        // Three supports in a row 3 mm apart: at the default 10 mm neighbour distance the chain
        // walks −3 → 0 → +3, and a brace between the outer two would have to cross the middle
        // trunk; select the outer two so that pair is what gets braced.
        var (document, _) = SlabWithSingles(3, 3);
        var outer = document.Supports.Nodes.Where(n => n.Type == SupportNodeType.Tip && MathF.Abs(n.Position.X) > 1)
            .Select(n => n.Id).ToList();
        document.SelectSupportElements(outer);
        var outcome = document.BraceSupports();
        Assert.NotNull(outcome);
        Assert.True(outcome.Braces > 0, "a brace may run through another trunk");

        // A wall standing between two supports does block them.
        var blocked = new Document();
        var slab = new SceneObject("slab", Box(new Vector3(-30, -30, 40), new Vector3(30, 30, 46)));
        var wall = new SceneObject("wall", Box(new Vector3(-0.5f, -30, 0), new Vector3(0.5f, 30, 40)));
        blocked.AddObject(wall);
        blocked.AddObject(slab);
        blocked.Select(slab);
        blocked.SupportSettings = new SupportConfig { UseBaseGrid = false, IndependentManualSupports = true, AutoParenting = false, AutoBracing = false };
        Assert.True(blocked.AddManualSupport(slab, new Vector3(-3, 0, 40), -Vector3.UnitZ));
        Assert.True(blocked.AddManualSupport(slab, new Vector3(3, 0, 40), -Vector3.UnitZ));
        var wallOutcome = blocked.BraceSupports();
        Assert.True(wallOutcome is null || wallOutcome.Braces == 0);
    }

    [Fact]
    public void AClusterIsBracedAsOneBundleWithNothingInside()
    {
        // Three trunks 1.5 mm apart (surfaces 0.3 mm apart: a cluster) and a lone trunk 6 mm beyond them.
        var document = new Document();
        var slab = new SceneObject("slab", Box(new Vector3(-30, -30, 40), new Vector3(30, 30, 46)));
        document.AddObject(slab);
        document.Select(slab);
        document.SupportSettings = new SupportConfig
        {
            UseBaseGrid = false, IndependentManualSupports = true, AutoParenting = false, AutoBracing = false,
        };
        foreach (var x in new[] { -1.5f, 0f, 1.5f, 7.5f })
            Assert.True(document.AddManualSupport(slab, new Vector3(x, 0, 40), -Vector3.UnitZ));

        var outcome = document.BraceSupports();

        Assert.NotNull(outcome);
        Assert.True(outcome.Braces >= 4, $"{outcome.Braces} braces");
        // Every brace runs between the cluster's outer trunk (1.5) and the lone one (7.5): none inside.
        Assert.All(document.Supports.Segments.Where(s => s.Type == SupportSegmentType.Bracing), s =>
        {
            var xs = new[] { document.Supports.GetNode(s.NodeA).Position.X, document.Supports.GetNode(s.NodeB).Position.X }.OrderBy(x => x).ToList();
            Assert.Equal(1.5f, xs[0], 2);
            Assert.Equal(7.5f, xs[1], 2);
        });
        // The bundle counts as tied with all its members.
        Assert.Equal(4, outcome.SupportsTied);

        // With the cluster gap off, the cluster's members chain among themselves as well.
        document.History.Undo();
        document.SupportSettings = document.SupportSettings with { BracingClusterGapMm = 0f };
        var separate = document.BraceSupports();
        Assert.NotNull(separate);
        Assert.Contains(document.Supports.Segments.Where(s => s.Type == SupportSegmentType.Bracing), s =>
            MathF.Abs(document.Supports.GetNode(s.NodeA).Position.X - document.Supports.GetNode(s.NodeB).Position.X) < 2f);
    }

    [Fact]
    public void AFieldOfSupportsIsTiedInBothDirectionsAndEveryTrunkGetsABrace()
    {
        // A 3 x 3 field at 4 mm pitch: a single chain would leave trunks off its path untied.
        var document = new Document();
        var slab = new SceneObject("slab", Box(new Vector3(-30, -30, 40), new Vector3(30, 30, 46)));
        document.AddObject(slab);
        document.Select(slab);
        document.SupportSettings = new SupportConfig
        {
            UseBaseGrid = false, IndependentManualSupports = true, AutoParenting = false, AutoBracing = false,
        };
        for (var x = -4f; x <= 4f; x += 4f)
        for (var y = -4f; y <= 4f; y += 4f)
            Assert.True(document.AddManualSupport(slab, new Vector3(x, y, 40), -Vector3.UnitZ));

        var outcome = document.BraceSupports();

        Assert.NotNull(outcome);
        Assert.Equal(9, outcome.SupportsTied);
        var braces = document.Supports.Segments.Where(s => s.Type == SupportSegmentType.Bracing)
            .Select(s => (A: document.Supports.GetNode(s.NodeA).Position, B: document.Supports.GetNode(s.NodeB).Position)).ToList();
        Assert.Contains(braces, b => MathF.Abs(b.A.X - b.B.X) > 3f); // ties along X
        Assert.Contains(braces, b => MathF.Abs(b.A.Y - b.B.Y) > 3f); // and along Y
        // Rows at the 2.5 mm tip spacing are not a cluster: the gap is between trunk surfaces.
        Assert.Equal(1f, document.SupportSettings.BracingClusterGapMm);
    }

    [Fact]
    public void TwoSelectedSupportsAreBracedRegardlessOfDistance()
    {
        var (document, _) = SlabWithSingles(3, 10);
        var outer = document.Supports.Nodes.Where(n => n.Type == SupportNodeType.Tip && MathF.Abs(n.Position.X) > 1)
            .Select(n => n.Id).ToList();
        document.SelectSupportElements(outer);

        var outcome = document.BraceSupports();

        Assert.NotNull(outcome);
        Assert.Equal(2, outcome.Operands);
        Assert.True(outcome.Braces > 0, "two chosen supports brace even beyond the neighbour distance");
        // Still at the angle: 45° over the 20 mm gap is a 20 mm rise, and the middle trunk is no obstacle.
        Assert.All(document.Supports.Segments.Where(s => s.Type == SupportSegmentType.Bracing), s =>
        {
            var a = document.Supports.GetNode(s.NodeA).Position;
            var b = document.Supports.GetNode(s.NodeB).Position;
            Assert.Equal(20f, MathF.Abs(a.Z - b.Z), 1);
            Assert.Equal(20f, MathF.Abs(a.X - b.X), 1);
        });
    }

    [Fact]
    public void ShortSupportsAndDistantNeighboursAreNotBraced()
    {
        var (shortOnes, _) = SlabWithSingles(2, 6, height: 15);
        var outcome = shortOnes.BraceSupports();
        Assert.True(outcome is null || outcome.Braces == 0);
        Assert.Equal(0, Braces(shortOnes));

        var (distant, _) = SlabWithSingles(2, 14);
        outcome = distant.BraceSupports();
        Assert.True(outcome is null || outcome.Braces == 0);
        Assert.Equal(0, Braces(distant));
    }

    [Fact]
    public void DiagonalPatternLeansEveryBraceTheSameWay()
    {
        var (document, _) = SlabWithSingles(2, 6);
        document.SupportSettings = document.SupportSettings with { BracingPattern = BracingPattern.Diagonal };
        document.BraceSupports();
        var feet = document.Supports.Segments.Where(s => s.Type == SupportSegmentType.Bracing)
            .Select(s =>
            {
                var a = document.Supports.GetNode(s.NodeA).Position;
                var b = document.Supports.GetNode(s.NodeB).Position;
                return a.Z < b.Z ? a.X : b.X;
            }).Distinct().ToList();
        Assert.True(Braces(document) >= 2);
        Assert.Single(feet);
    }

    [Fact]
    public void UnbraceRemovesBracesAndTheirEndsAsOneStep()
    {
        var (document, _) = SlabWithSingles(3, 6);
        document.BraceSupports();
        Assert.True(Braces(document) > 0);
        var trunks = Trunks(document);

        var removed = document.UnbraceSupports();

        Assert.True(removed > 0);
        Assert.Equal(0, Braces(document));
        Assert.Equal(0, BraceEnds(document));
        Assert.Equal(trunks, Trunks(document));
        Assert.Equal("Unbrace supports", document.History.UndoName);
        Assert.True(document.History.Undo());
        Assert.Equal(removed, Braces(document));
        AssertBraceEndsSound(document);
    }

    [Fact]
    public void SelectBracesSelectsOnlyBracesAndDeleteRemovesTheirEnds()
    {
        var (document, _) = SlabWithSingles(3, 6);
        document.BraceSupports();
        var braces = Braces(document);
        Assert.True(braces > 0);

        // Additive and independent of the selection: a selected brace stays selected, a
        // selected tip stays selected, and every brace joins them.
        var oneBrace = document.Supports.Segments.First(s => s.Type == SupportSegmentType.Bracing).Id;
        var oneTip = document.Supports.Nodes.First(n => n.Type == SupportNodeType.Tip).Id;
        document.SelectSupportElements([oneBrace, oneTip]);
        Assert.Equal(braces, document.SelectBraces());
        Assert.Equal(braces + 1, document.SupportSelection.Count);
        Assert.Contains(oneBrace, document.SupportSelection);
        Assert.Contains(oneTip, document.SupportSelection);
        Assert.Equal(braces, document.SupportSelection.Count(id => document.Supports.TryGetSegment(id, out var s) && s.Type == SupportSegmentType.Bracing));

        document.ClearSupportSelection();
        Assert.Equal(braces, document.SelectBraces());
        Assert.All(document.SupportSelection, id => Assert.Equal(SupportSegmentType.Bracing, document.Supports.GetSegment(id).Type));

        document.DeleteSupportSelection();
        Assert.Equal(0, Braces(document));
        Assert.Equal(0, BraceEnds(document));
        Assert.Equal(3, document.Supports.Nodes.Count(n => n.Type == SupportNodeType.Tip));
    }

    [Fact]
    public void DeletingOneSupportTakesItsBracesAndKeepsTheOthers()
    {
        var (document, _) = SlabWithSingles(3, 6);
        document.BraceSupports();
        var middleTip = document.Supports.Nodes.Where(n => n.Type == SupportNodeType.Tip)
            .OrderBy(n => MathF.Abs(n.Position.X)).First();
        var component = document.Supports.Component(middleTip.Id);
        var bracesBefore = Braces(document);

        document.DeleteSupportElements(component.Nodes.Concat(component.Segments));

        Assert.Equal(2, document.Supports.Nodes.Count(n => n.Type == SupportNodeType.Tip));
        Assert.Equal(0, Braces(document)); // both ladders touched the middle trunk
        Assert.True(bracesBefore > 0);
        AssertBraceEndsSound(document);

        Assert.True(document.History.Undo());
        Assert.Equal(bracesBefore, Braces(document));
        AssertBraceEndsSound(document);
    }

    [Fact]
    public void ParentingTakesBracesDownWithTheTrunks()
    {
        var (document, _) = SlabWithSingles(3, 3);
        document.BraceSupports();
        document.SupportSettings = document.SupportSettings with { IndependentManualSupports = false, AutoBracing = false };

        var outcome = document.ParentSupports();

        Assert.NotNull(outcome);
        AssertBraceEndsSound(document);
        foreach (var node in document.Supports.Nodes.Where(n => n.Type == SupportNodeType.BraceEnd))
            Assert.True(document.Supports.TryGetSegment(SupportBracing.CarrierOf(document.Supports, node)!.Id, out _));
    }

    [Fact]
    public void BracedGraphsRoundTripThroughTheProjectFile()
    {
        var (document, _) = SlabWithSingles(3, 6);
        document.BraceSupports();
        var braces = Braces(document);
        var braceEnds = BraceEnds(document);
        Assert.True(braces > 0);
        var path = Path.Combine(Path.GetTempPath(), $"danslicer-bracing-{Guid.NewGuid():N}.{ProjectFile.Extension}");
        try
        {
            ProjectFile.Save(path, document, new ProjectViewState());
            var loaded = ProjectFile.Load(path).Document;
            Assert.Equal(braces, Braces(loaded));
            Assert.Equal(braceEnds, BraceEnds(loaded));
            AssertBraceEndsSound(loaded);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void ExplicitParentingDoesNotAutoBrace()
    {
        var (document, _) = SlabWithSingles(3, 6);
        document.SelectSupportElements(document.Supports.Nodes
            .Where(n => n.Type == SupportNodeType.Tip).OrderBy(n => n.Position.X)
            .Take(2).Select(n => n.Id));
        document.SupportSettings = document.SupportSettings with
        {
            IndependentManualSupports = false,
            AutoBracing = true,
            ParentingMaxBranchLength = 8f,
            BracingNeighbourDistanceMm = 20f,
        };

        var outcome = document.ParentSupports();

        Assert.NotNull(outcome);
        Assert.Equal("Parent supports", document.History.UndoName);
        Assert.Equal(0, Braces(document));
        Assert.NotNull(document.BraceSupports());
        Assert.True(Braces(document) > 0, "explicit bracing should still work on the parented supports");
    }

    [Fact]
    public void AutoBracingFollowsGenerationInsideItsUndoStep()
    {
        var document = new Document();
        var slab = new SceneObject("slab", Box(new Vector3(-8, -8, 40), new Vector3(8, 8, 46)));
        document.AddObject(slab);
        document.Select(slab);
        document.SupportSettings = new SupportConfig { UseBaseGrid = true, BaseGridPitch = 6f, AutoBracing = true, Spacing = 4f };

        var summary = document.GenerateSupports(slab);

        Assert.True(summary.GeneratedTipCount >= 2);
        Assert.True(Braces(document) > 0, "generation should have braced its trunks");
        Assert.Equal("Generate supports", document.History.UndoName);
        AssertBraceEndsSound(document);
        Assert.True(document.History.Undo());
        Assert.Empty(document.Supports.Nodes);
    }
}
