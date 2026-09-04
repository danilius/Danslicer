using System.Numerics;
using Danslicer.Core.Geometry;
using Danslicer.Core.Supports;
using Danslicer.Core.Supports.Generation;
using Danslicer.Core.Supports.Routing;
using System.Text.Json;

namespace Danslicer.Tests;

/// <summary>End-to-end: tip placement into grid routing, the seam between the two stages.</summary>
public sealed class SupportGeneratorTests
{
    [Fact]
    public void PenetrationDepthRoundTripsThroughThePlacementSchema()
    {
        var before = new TipPlacementParameters { PenetrationDepthMm = 0.35f };

        var after = JsonSerializer.Deserialize<TipPlacementParameters>(JsonSerializer.Serialize(before));

        Assert.NotNull(after);
        Assert.Equal(0.35f, after.PenetrationDepthMm);
    }
    /// <summary>Axis-aligned box as a welded mesh with outward faces.</summary>
    private static Mesh Box(Vector3 min, Vector3 max)
    {
        var (a, b) = (min, max);
        var corners = new Vector3[]
        {
            new(a.X, a.Y, a.Z), new(b.X, a.Y, a.Z), new(b.X, b.Y, a.Z), new(a.X, b.Y, a.Z),
            new(a.X, a.Y, b.Z), new(b.X, a.Y, b.Z), new(b.X, b.Y, b.Z), new(a.X, b.Y, b.Z),
        };
        int[] quads = // outward winding
        [
            0, 3, 2, 1, // bottom (-Z)
            4, 5, 6, 7, // top (+Z)
            0, 1, 5, 4, // -Y
            2, 3, 7, 6, // +Y
            0, 4, 7, 3, // -X
            1, 2, 6, 5, // +X
        ];
        var soup = new List<Vector3>();
        for (int q = 0; q < quads.Length; q += 4)
        {
            soup.Add(corners[quads[q]]); soup.Add(corners[quads[q + 1]]); soup.Add(corners[quads[q + 2]]);
            soup.Add(corners[quads[q]]); soup.Add(corners[quads[q + 2]]); soup.Add(corners[quads[q + 3]]);
        }
        return Mesh.FromTriangleSoup(soup.ToArray());
    }

    private static IReadOnlySet<int> AllFaces(Mesh mesh) =>
        Enumerable.Range(0, mesh.TriangleCount).ToHashSet();

    private static GenerationResult GenerateForFloatingBox(int seed)
    {
        var mesh = Box(new Vector3(-5, -5, 5), new Vector3(5, 5, 15));
        var obstacles = new LinearCollisionScene();
        obstacles.AddMesh(mesh, Matrix4x4.Identity);
        return SupportGenerator.Generate(
            mesh,
            AllFaces(mesh),
            new TipPlacementParameters { SpacingMm = 4f },
            new GridRoutingOptions { Spacing = 4f },
            GrowthRuleSet.Default,
            obstacles,
            seed: seed);
    }

    [Fact]
    public void FloatingBoxGetsAFullySupportedUnderside()
    {
        var result = GenerateForFloatingBox(seed: 7);

        Assert.NotEmpty(result.Candidates);
        Assert.Empty(result.Routing.UnroutedTips);

        var graph = result.Routing.Graph;
        var tips = graph.Nodes.Where(n => n.Type == SupportNodeType.Tip).ToList();
        Assert.Equal(result.Candidates.Count, tips.Count);
        Assert.All(tips, t => Assert.Equal(5f, t.Position.Z, 2));
        Assert.All(graph.Nodes.Where(n => n.Type == SupportNodeType.Base),
            n => Assert.Equal(0f, n.Position.Z, 3));
        Assert.Contains(graph.Segments, s => s.Type == SupportSegmentType.Tip);
    }

    [Fact]
    public void TipNodesGetTheOutwardSurfaceNormal()
    {
        var result = GenerateForFloatingBox(seed: 7);

        // Underside candidates carry the inward normal +Z; graph nodes must hold outward -Z.
        Assert.All(result.Candidates, c => Assert.True(c.InwardNormal.Z > 0.9f));
        var tips = result.Routing.Graph.Nodes.Where(n => n.Type == SupportNodeType.Tip);
        Assert.All(tips, t => Assert.True(t.SurfaceNormal.Z < -0.9f,
            $"tip normal {t.SurfaceNormal} should point outward (down)"));
    }

    [Fact]
    public void SameSeedIsDeterministicAcrossTheWholePipeline()
    {
        var first = GenerateForFloatingBox(seed: 11);
        var second = GenerateForFloatingBox(seed: 11);

        Assert.Equal(first.Candidates, second.Candidates);
        Assert.Equal(
            first.Routing.Graph.Nodes.Select(n => (n.Id, n.Type, n.Position)),
            second.Routing.Graph.Nodes.Select(n => (n.Id, n.Type, n.Position)));
        Assert.Equal(
            first.Routing.Graph.Segments.Select(s => (s.Id, s.Type, s.NodeA, s.NodeB, s.Diameter)),
            second.Routing.Graph.Segments.Select(s => (s.Id, s.Type, s.NodeA, s.NodeB, s.Diameter)));
    }

    [Fact]
    public void RoutedSupportsClearTheModelExceptAtTheContacts()
    {
        var result = GenerateForFloatingBox(seed: 7);
        var mesh = Box(new Vector3(-5, -5, 5), new Vector3(5, 5, 15));
        var audit = new LinearCollisionScene();
        audit.AddMesh(mesh, Matrix4x4.Identity);

        var graph = result.Routing.Graph;
        foreach (var segment in graph.Segments)
        {
            var a = graph.GetNode(segment.NodeA);
            var b = graph.GetNode(segment.NodeB);
            // Necks end at the surface by design; audit only the model-free portion.
            var (from, to) = (a.Position, b.Position);
            if (a.Type == SupportNodeType.Tip || b.Type == SupportNodeType.Tip)
            {
                var tip = a.Type == SupportNodeType.Tip ? a : b;
                var other = a.Type == SupportNodeType.Tip ? b.Position : a.Position;
                var axis = tip.Position - other;
                var length = axis.Length();
                if (length < 1.5f) continue;
                (from, to) = (other, other + axis * ((length - 1.5f) / length));
            }
            Assert.False(audit.IntersectsCapsule(from, to, segment.Diameter * 0.5f),
                $"{segment.Type} {from} -> {to} intersects the model");
        }
    }

    [Fact]
    public void GridStrategyPlacesTipsOnLatticeVerticalsMatchingBases()
    {
        var mesh = Box(new Vector3(-5, -5, 5), new Vector3(5, 5, 15));
        var obstacles = new LinearCollisionScene();
        obstacles.AddMesh(mesh, Matrix4x4.Identity);
        var routing = new GridRoutingOptions { Spacing = 5f };
        var placement = new TipPlacementParameters { SpacingMm = 5f, MinSpacingMm = 1f };

        var result = SupportGenerator.Generate(
            mesh, AllFaces(mesh), placement, routing, GrowthRuleSet.Default, obstacles, seed: 1);

        Assert.Contains(result.Candidates, c => c.Strategy == TipStrategy.GridProjection);
        Assert.DoesNotContain(result.Candidates, c => c.Strategy == TipStrategy.Overhang);
        Assert.Empty(result.Routing.UnroutedTips);

        var lattice = BaseLattice.WorldPointsCovering(new Vector2(-5, -5), new Vector2(5, 5), routing).ToList();
        var projected = result.Candidates.Where(c => c.Strategy == TipStrategy.GridProjection).ToList();
        Assert.All(projected, t =>
        {
            var xy = new Vector2(t.Point.X, t.Point.Y);
            Assert.True(lattice.Any(p => Vector2.Distance(p, xy) < 1e-3f),
                $"grid tip XY ({xy.X},{xy.Y}) is not on the lattice");
            Assert.Contains(result.Routing.BasePositions,
                b => Vector2.Distance(new Vector2(b.X, b.Y), xy) < 1e-3f);
        });
    }

    [Fact]
    public void BoxOnThePlateGeneratesNothing()
    {
        var mesh = Box(new Vector3(-5, -5, 0), new Vector3(5, 5, 10));
        var obstacles = new LinearCollisionScene();
        obstacles.AddMesh(mesh, Matrix4x4.Identity);

        var result = SupportGenerator.Generate(
            mesh, AllFaces(mesh),
            TipPlacementParameters.Default,
            new GridRoutingOptions(),
            GrowthRuleSet.Default,
            obstacles);

        Assert.Empty(result.Candidates);
        Assert.Equal(0, result.Routing.Graph.NodeCount);
    }

    [Fact]
    public void IslandOnlyScopeKeepsExactlyIslandSourcedCandidates()
    {
        var mesh = Box(new Vector3(-5, -5, 5), new Vector3(5, 5, 15));
        var obstacles = new LinearCollisionScene();
        obstacles.AddMesh(mesh, Matrix4x4.Identity);
        var placement = TipPlacementParameters.Default with
        {
            SpacingMm = 4,
            MinSpacingMm = 4,
            MinIslandAreaMm2 = 0.1f,
        };
        var options = new TreeRoutingOptions { UseBaseGrid = false };

        var full = SupportGenerator.GenerateTree(mesh, AllFaces(mesh), placement, options,
            GrowthRuleSet.Default, obstacles, seed: 7);
        var islands = SupportGenerator.GenerateTree(mesh, AllFaces(mesh), placement, options,
            GrowthRuleSet.Default, obstacles, seed: 7,
            scope: SupportGenerationScope.IslandsOnly);

        var expected = full.Candidates.Where(SupportGenerator.IsIslandCandidate).ToList();
        Assert.NotEmpty(expected);
        Assert.Equal(expected, islands.Candidates);
        Assert.All(islands.Candidates, candidate =>
            Assert.True(SupportGenerator.IsIslandCandidate(candidate)));
    }

    [Fact]
    public void DetectionReportsBareIslandAndClearsItWhenATipReachesTheLayer()
    {
        var mesh = Box(new Vector3(-5, -5, 5), new Vector3(5, 5, 15));
        var bare = IslandDetection.FindUnsupported(mesh, null, 0.5f, 0.1f, 0, 45);
        var island = Assert.Single(bare);
        var graph = new SupportGraph();
        graph.AddNode(new SupportNode
        {
            Type = SupportNodeType.Tip,
            Position = island.Position,
        });

        var supported = IslandDetection.FindUnsupported(mesh, graph, 0.5f, 0.1f, 0, 45);

        Assert.Empty(supported);
        Assert.InRange(island.MarkerRadiusMm, 0.35f, 2f);
    }

    [Fact]
    public void ConeShapeParametersReachCandidatesAndRoutedTips()
    {
        var mesh = Box(new Vector3(-5, -5, 5), new Vector3(5, 5, 15));
        var obstacles = new LinearCollisionScene();
        obstacles.AddMesh(mesh, Matrix4x4.Identity);
        var placement = new TipPlacementParameters
        {
            SpacingMm = 4f,
            TipShape = SupportTipShape.Cone,
            ConeLengthMm = 1.5f,
            BallDiameterMm = 0.8f,
            PenetrationDepthMm = 0.3f,
        };

        var result = SupportGenerator.Generate(
            mesh, AllFaces(mesh), placement, new GridRoutingOptions { Spacing = 4f },
            GrowthRuleSet.Default, obstacles, seed: 7);

        Assert.NotEmpty(result.Candidates);
        Assert.All(result.Candidates, c =>
        {
            Assert.Equal(SupportTipShape.Cone, c.TipShape);
            Assert.Equal(1.5f, c.ConeLength);
            Assert.Equal(0.8f, c.BallDiameter);
            Assert.Equal(0.3f, c.PenetrationDepth);
        });
        var tips = result.Routing.Graph.Nodes.Where(n => n.Type == SupportNodeType.Tip).ToList();
        Assert.NotEmpty(tips);
        Assert.All(tips, t =>
        {
            Assert.Equal(SupportTipShape.Cone, t.TipShape);
            Assert.Equal(1.5f, t.ConeLength);
            Assert.Equal(0.8f, t.BallDiameter);
            Assert.Equal(0.3f, t.PenetrationDepth);
        });
    }
}
