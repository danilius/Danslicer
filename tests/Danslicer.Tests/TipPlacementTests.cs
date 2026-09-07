using System.Numerics;
using Danslicer.Core.Geometry;
using Danslicer.Core.Supports;
using Danslicer.Core.Supports.Generation;
using Danslicer.Core.Supports.Routing;

namespace Danslicer.Tests;

public class TipPlacementTests
{
    private static HashSet<int> AllFaces(Mesh mesh) =>
        Enumerable.Range(0, mesh.TriangleCount).ToHashSet();

    private static TipPlacementParameters P(
        float spacing = 2.5f,
        float minSpacing = 2.5f,
        float overhang = 45f,
        float minIsland = 0.5f,
        float layer = 0.05f,
        float edge = 0f,
        bool forceEdges = false) => new()
    {
        SpacingMm = spacing,
        MinSpacingMm = minSpacing,
        OverhangAngleDegrees = overhang,
        MinIslandAreaMm2 = minIsland,
        FineFeatureMaxAreaMm2 = 0f,
        LayerHeightMm = layer,
        EdgePreference = edge,
        ForceEdgePlacement = forceEdges,
    };

    private static IReadOnlyList<TipCandidate> Place(
        Mesh mesh,
        TipPlacementParameters? parameters = null,
        SupportGraph? graph = null,
        IReadOnlySet<int>? faces = null,
        IReadOnlySet<int>? keepClean = null,
        int seed = 1) =>
        TipPlacer.Place(mesh, faces ?? AllFaces(mesh), parameters ?? P(), graph, keepClean, seed);

    [Fact]
    public void SameInputsAndSeedAreBitIdentical()
    {
        var mesh = Meshes.FloatingBox(10, 10, 10, z: 5);
        var a = Place(mesh, seed: 7);
        var b = Place(mesh, seed: 7);
        Assert.Equal(a, b);
        Assert.NotEmpty(a);
    }

    [Fact]
    public void CrowdedIslandAndRegularContactsFormDeterministicBoundedMiniClusters()
    {
        var contacts = Enumerable.Range(0, 6).Select(index => new TipCandidate(
            new Vector3(index * 0.2f, 0, 10), Vector3.UnitZ, 0.4f, index + 1,
            index < 3 ? TipStrategy.Island : TipStrategy.Overhang, index)).ToList();
        contacts.Add(new TipCandidate(new Vector3(20, 0, 10), Vector3.UnitZ, 0.4f, 1,
            TipStrategy.Island, 99));
        var parameters = TipPlacementParameters.Default with
        {
            // Density clusters are a mini-support feature and run only with minis enabled.
            EnableMiniSupports = true,
            EnableMiniTipClusters = true,
            MiniSupportClusterDistanceMm = 1.25f,
            MiniSupportMaxTipsPerCluster = 4,
            MiniSupportTipDiameterMm = 0.23f,
            MiniSupportConeLengthMm = 0.9f,
        };

        var forward = MiniTipClusterer.Apply(contacts, parameters)
            .OrderBy(candidate => candidate.Point.X).ToList();
        var reverse = MiniTipClusterer.Apply(contacts.AsEnumerable().Reverse().ToList(), parameters)
            .OrderBy(candidate => candidate.Point.X).ToList();

        Assert.Equal(forward, reverse);
        var clustered = forward.Where(candidate => candidate.MiniClusterId is not null).ToList();
        Assert.Equal(6, clustered.Count);
        Assert.Equal([2, 4], clustered.GroupBy(candidate => candidate.MiniClusterId)
            .Select(group => group.Count()).Order().ToArray());
        Assert.All(clustered, candidate =>
        {
            Assert.Equal(TipStrategy.MiniCluster, candidate.Strategy);
            Assert.Equal(0.23f, candidate.TipDiameter);
            Assert.Equal(SupportTipShape.Cone, candidate.TipShape);
            Assert.Equal(0.9f, candidate.ConeLength);
            Assert.NotNull(candidate.MiniClusterCenter);
            Assert.NotNull(candidate.MiniClusterSourceStrategy);
        });
        Assert.Equal(3, clustered.Count(candidate =>
            candidate.MiniClusterSourceStrategy == TipStrategy.Island));
        Assert.Equal(3, clustered.Count(candidate =>
            candidate.MiniClusterSourceStrategy == TipStrategy.Overhang));
        Assert.Null(forward[^1].MiniClusterId);
        Assert.Null(forward[^1].MiniClusterSourceStrategy);
    }

    [Fact]
    public void TeethCombKeepsEveryDeduplicatedIslandContactWhenClustering()
    {
        var mesh = Meshes.Merge(Enumerable.Range(0, 6)
            .Select(index => Meshes.Box(0.4f, 0.4f, 3,
                new Vector3(index * 0.8f, 0, 5)))
            .ToArray());
        var parameters = P(minIsland: 0.1f) with
        {
            EnableMiniSupports = true,
            EnableMiniTipClusters = false,
            MiniIslandMaxAreaMm2 = 0.1f,
            IslandSpacingMm = 0.5f,
            MiniSupportClusterDistanceMm = 1.25f,
            MiniSupportMaxTipsPerCluster = 4,
        };

        var before = Place(mesh, parameters);
        var beforeIslands = before.Where(candidate => candidate.Strategy == TipStrategy.Island)
            .OrderBy(candidate => candidate.Point.X).ToList();
        var after = Place(mesh, parameters with
        {
            EnableMiniTipClusters = true,
            FineFeatureMaxAreaMm2 = 1f,
        });
        var afterIslands = after.Where(candidate =>
                candidate.MiniClusterSourceStrategy == TipStrategy.Island)
            .OrderBy(candidate => candidate.Point.X).ToList();

        Assert.Equal(6, beforeIslands.Count);
        Assert.Equal(beforeIslands.Count, afterIslands.Count);
        Assert.Equal(beforeIslands.Select(candidate => candidate.Point),
            afterIslands.Select(candidate => candidate.Point));
        Assert.Equal(before.Count, after.Count);
        Assert.DoesNotContain(after, candidate =>
            candidate.MiniClusterSourceStrategy == TipStrategy.MiniIsland);
    }

    [Fact]
    public void AdjacentIslandsEachKeepTheirOwnTip()
    {
        // Two floating teeth 2 mm apart: separate newborn islands closer than MinSpacingMm.
        // Each island physically needs its own support, so spacing must never cost an island
        // its only tip (user screen test 2026-09-03: Drogon's teeth had no supports).
        var mesh = Meshes.Merge(
            Meshes.Box(0.5f, 0.5f, 3, new Vector3(0, 0, 5)),
            Meshes.Box(0.5f, 0.5f, 3, new Vector3(2, 0, 5)));

        var islands = Place(mesh, P(minIsland: 0.1f))
            .Where(c => c.Strategy == TipStrategy.Island).ToList();

        Assert.Equal(2, islands.Count);
    }

    [Fact]
    public void SubThresholdIslandIsHandedToMiniSupportPassWithFineContactGeometry()
    {
        var mesh = Meshes.Box(0.25f, 0.25f, 3, new Vector3(0, 0, 5));
        var parameters = P(minIsland: 0.1f) with
        {
            EnableMiniSupports = true,
            MiniSupportTipDiameterMm = 0.23f,
            MiniSupportConeLengthMm = 0.9f,
        };

        var mini = Assert.Single(Place(mesh, parameters),
            candidate => candidate.Strategy == TipStrategy.MiniIsland);
        Assert.Equal(0.23f, mini.TipDiameter);
        Assert.Equal(SupportTipShape.Cone, mini.TipShape);
        Assert.Equal(0.9f, mini.ConeLength);
    }

    [Fact]
    public void MiniIslandMaximumAreaIsIndependentFromRegularIslandThreshold()
    {
        var mesh = Meshes.Box(0.25f, 0.25f, 3, new Vector3(0, 0, 5));
        var parameters = P(minIsland: 0.2f) with
        {
            EnableMiniSupports = true,
            MiniIslandMaxAreaMm2 = 0.05f,
        };

        var bandIsland = Place(mesh, parameters);

        Assert.DoesNotContain(bandIsland,
            candidate => candidate.Strategy == TipStrategy.MiniIsland);
        Assert.Contains(bandIsland,
            candidate => candidate.Strategy == TipStrategy.Island);
        Assert.Contains(Place(mesh, parameters with { MiniIslandMaxAreaMm2 = 0.1f }),
            candidate => candidate.Strategy == TipStrategy.MiniIsland);
    }

    [Fact]
    public void MiniIslandMaximumAreaNormalizesToItsPhysicalAndRegularBounds()
    {
        var mesh = Meshes.Box(0.25f, 0.25f, 3, new Vector3(0, 0, 5));
        var parameters = P(minIsland: 0.1f) with
        {
            EnableMiniSupports = true,
            MiniSupportTipDiameterMm = 0.25f,
        };

        var belowFootprint = Place(mesh, parameters with { MiniIslandMaxAreaMm2 = 0f });
        var aboveRegular = Place(mesh, parameters with { MiniIslandMaxAreaMm2 = 10f });

        Assert.DoesNotContain(belowFootprint,
            candidate => candidate.Strategy == TipStrategy.MiniIsland);
        Assert.Contains(aboveRegular,
            candidate => candidate.Strategy == TipStrategy.MiniIsland);
    }

    [Fact]
    public void CoincidentIslandTipsStillDedup()
    {
        // The island exemption is not a duplicate generator: two tips of the same island
        // cluster (closer than IslandSpacingMm) collapse to one.
        var mesh = Meshes.Merge(
            Meshes.Box(0.5f, 0.5f, 3, new Vector3(0, 0, 5)),
            Meshes.Box(0.5f, 0.5f, 3, new Vector3(0.2f, 0, 5)));

        var islands = Place(mesh, P(minIsland: 0.05f))
            .Where(c => c.Strategy == TipStrategy.Island).ToList();

        Assert.Single(islands);
    }

    [Fact]
    public void SeedChangesOverhangSamplesButNotRequiredTips()
    {
        var mesh = Meshes.FloatingBox(10, 10, 10, z: 5);
        var a = Place(mesh, seed: 1);
        var b = Place(mesh, seed: 2);
        var requiredA = a.Where(c => c.Strategy is TipStrategy.Island or TipStrategy.LocalMinimum)
            .Select(c => (c.Point, c.Strategy)).OrderBy(x => x.Point.X).ThenBy(x => x.Point.Y).ToList();
        var requiredB = b.Where(c => c.Strategy is TipStrategy.Island or TipStrategy.LocalMinimum)
            .Select(c => (c.Point, c.Strategy)).OrderBy(x => x.Point.X).ThenBy(x => x.Point.Y).ToList();
        Assert.Equal(requiredA, requiredB);
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void CubeOnThePlateNeedsNoTips()
    {
        var mesh = Meshes.Box(10, 10, 10);
        var tips = Place(mesh);
        Assert.Empty(tips);
    }

    [Fact]
    public void FloatingCubeGetsTipsOnTheUnderside()
    {
        var mesh = Meshes.FloatingBox(10, 10, 10, z: 5);
        var tips = Place(mesh, P(spacing: 3, minSpacing: 3));
        Assert.NotEmpty(tips);
        Assert.Contains(tips, c => c.Strategy == TipStrategy.Island);
        Assert.Contains(tips, c => c.Strategy == TipStrategy.LocalMinimum);
        Assert.All(tips, c =>
        {
            Assert.True(c.Point.Z < 5.2f, $"tip not on underside: z={c.Point.Z}");
            Assert.True(c.InwardNormal.Z > 0.5f, "inward normal should point up into the solid");
            Assert.InRange(c.Point.X, -0.1f, 10.1f);
            Assert.InRange(c.Point.Y, -0.1f, 10.1f);
        });
    }

    [Fact]
    public void EmptyRegionProducesNothing()
    {
        var mesh = Meshes.FloatingBox(8, 8, 8, z: 4);
        var tips = Place(mesh, faces: new HashSet<int>());
        Assert.Empty(tips);
    }

    [Fact]
    public void KeepCleanFacesReceiveNoTips()
    {
        var mesh = Meshes.FloatingBox(10, 10, 10, z: 5);
        var bottom = DownwardFaces(mesh);
        Assert.NotEmpty(bottom);
        var tips = Place(mesh, keepClean: bottom);
        Assert.All(tips, c => Assert.DoesNotContain(c.FaceIndex, bottom));
        // The only overhangs on a cube are the bottom; keep-clean of those leaves nothing.
        Assert.Empty(tips);
    }

    [Fact]
    public void ExistingGraphTipEnforcesMinimumSpacing()
    {
        var mesh = Meshes.FloatingBox(10, 10, 10, z: 5);
        var graph = new SupportGraph();
        var existing = new Vector3(5, 5, 5);
        graph.AddNode(new SupportNode
        {
            Type = SupportNodeType.Tip,
            Position = existing,
            SurfaceNormal = -Vector3.UnitZ,
        });
        var tips = Place(mesh, P(spacing: 2.5f, minSpacing: 3f), graph);
        Assert.All(tips, c => Assert.True(
            Vector3.Distance(c.Point, existing) >= 3f - 1e-3f,
            $"candidate {c.Point} is {Vector3.Distance(c.Point, existing):0.###} from existing tip"));
    }

    [Fact]
    public void PoissonSpacingIsRespectedAmongNewTips()
    {
        var mesh = Meshes.FloatingBox(12, 12, 6, z: 4);
        const float spacing = 3f;
        var tips = Place(mesh, P(spacing: spacing, minSpacing: 1f));
        Assert.True(tips.Count >= 4, $"expected several tips, got {tips.Count}");
        for (int i = 0; i < tips.Count; i++)
        for (int j = i + 1; j < tips.Count; j++)
        {
            var d = Vector3.Distance(tips[i].Point, tips[j].Point);
            // Required tips may sit closer than density; they still respect min spacing (1 mm).
            var min = (IsRequired(tips[i]) && IsRequired(tips[j])) ? 1f : spacing;
            Assert.True(d + 1e-3f >= min * 0.98f, $"tips {i} and {j} are {d:0.###} mm apart (min {min})");
        }
    }

    [Fact]
    public void SmallIslandsCanBeIgnored()
    {
        // A 0.4 x 0.4 mm tab is below the 0.5 mm² default; a 4 x 4 mm tab is not.
        var tiny = Meshes.Table(top: 0.6f, topThickness: 0.4f, leg: 0.2f, height: 3f);
        var tinyTips = Place(tiny, P(spacing: 2f, minSpacing: 0.5f, minIsland: 0.5f, layer: 0.1f));
        Assert.DoesNotContain(tinyTips, c => c.Strategy == TipStrategy.Island);

        var table = Meshes.Table(top: 16, topThickness: 2, leg: 2, height: 8);
        var tableTips = Place(table, P(spacing: 4f, minSpacing: 2f, minIsland: 0.5f));
        Assert.Contains(tableTips, c => c.Strategy == TipStrategy.Island);
    }

    [Fact]
    public void TableGetsIslandTipsUnderTheTopNotOnThePlate()
    {
        var mesh = Meshes.Table(top: 20, topThickness: 2, leg: 2, height: 10);
        var tips = Place(mesh, P(spacing: 4f, minSpacing: 2f));
        var islands = tips.Where(c => c.Strategy == TipStrategy.Island).ToList();
        Assert.NotEmpty(islands);
        Assert.All(islands, c =>
        {
            Assert.InRange(c.Point.Z, 9.5f, 10.5f);
            Assert.InRange(c.Point.X, -10.1f, 10.1f);
            Assert.InRange(c.Point.Y, -10.1f, 10.1f);
        });
        Assert.All(tips, c => Assert.True(c.Point.Z > 0.2f, "no tips on the plate"));
    }

    [Fact]
    public void CantileverArmProducesAnIsland()
    {
        var mesh = Meshes.Cantilever();
        var tips = Place(mesh, P(spacing: 4f, minSpacing: 2f));
        var islands = tips.Where(c => c.Strategy == TipStrategy.Island).ToList();
        Assert.NotEmpty(islands);
        // Arm underside is at z=16, extending in +X past the 4 mm post.
        Assert.Contains(islands, c => c.Point.X > 3f && c.Point.Z > 15f);
    }

    [Fact]
    public void SpherePlacesALocalMinimumNearTheBottom()
    {
        // Floating so the pole is not on the plate.
        var mesh = Meshes.UvSphere(radius: 5, center: new Vector3(0, 0, 8), slices: 16, stacks: 12);
        var tips = Place(mesh, P(spacing: 3f, minSpacing: 2f, overhang: 30f));
        Assert.NotEmpty(tips);
        var min = tips.MinBy(c => c.Point.Z);
        Assert.True(min.Point.Z < 4.5f, $"lowest tip at {min.Point.Z}, expected near the pole z=3");
        // The pole is both a local minimum and the first island; whichever is placed first
        // occupies the point and the other is dropped by min-spacing. Either strategy is correct.
        Assert.Contains(tips, c =>
            c.Strategy is TipStrategy.LocalMinimum or TipStrategy.Island && c.Point.Z < 4.5f);
        Assert.All(tips, c => Assert.True(c.InwardNormal.Z > -0.2f || c.Point.Z > 8f,
            "lower-hemisphere inward normals point mostly up"));
    }

    [Fact]
    public void ForcedEdgePlacementKeepsInteriorClear()
    {
        var mesh = Meshes.FloatingBox(10, 10, 10, z: 5);
        var tips = Place(mesh, P(spacing: 3f, minSpacing: 1.5f, edge: 1f, forceEdges: true));
        Assert.NotEmpty(tips);
        var overhangInterior = tips.Where(c => c.Strategy == TipStrategy.Overhang).ToList();
        Assert.Empty(overhangInterior);
        Assert.Contains(tips, c => c.Strategy is TipStrategy.Edge or TipStrategy.Corner);
        // Corners of the underside.
        Assert.Contains(tips, c => c.Strategy == TipStrategy.Corner ||
                                   (MathF.Abs(c.Point.X) < 0.2f || MathF.Abs(c.Point.X - 10) < 0.2f));
    }

    [Fact]
    public void RegionSubsetRestrictsFaces()
    {
        var mesh = Meshes.FloatingBox(10, 10, 10, z: 5);
        var bottom = DownwardFaces(mesh);
        var sides = AllFaces(mesh);
        sides.ExceptWith(bottom);
        var onSides = Place(mesh, faces: sides);
        Assert.All(onSides, c => Assert.Contains(c.FaceIndex, sides));
        var onBottom = Place(mesh, faces: bottom);
        Assert.NotEmpty(onBottom);
        Assert.All(onBottom, c => Assert.Contains(c.FaceIndex, bottom));
    }

    [Fact]
    public void GridProjectionTipsSitOnLatticeVerticals()
    {
        var mesh = Meshes.FloatingBox(10, 10, 10, z: 5);
        var grid = new GridRoutingOptions { Lattice = BaseLatticeType.Square, Spacing = 5f };
        var tips = Place(mesh, P(spacing: 5, minSpacing: 1f) with { Grid = grid });
        var projected = tips.Where(c => c.Strategy == TipStrategy.GridProjection).ToList();
        Assert.NotEmpty(projected);
        Assert.DoesNotContain(tips, c => c.Strategy == TipStrategy.Overhang);

        var lattice = BaseLattice.WorldPointsCovering(new Vector2(0, 0), new Vector2(10, 10), grid).ToList();
        Assert.All(projected, t =>
        {
            Assert.True(lattice.Any(p => Vector2.Distance(p, new Vector2(t.Point.X, t.Point.Y)) < 1e-3f),
                $"tip XY ({t.Point.X},{t.Point.Y}) is not on the lattice");
            Assert.InRange(t.Point.Z, 4.9f, 5.1f);
            Assert.True(t.InwardNormal.Z > 0.5f);
        });
    }

    [Fact]
    public void GridProjectionTipRoutesToTheSameLatticeBase()
    {
        var mesh = Meshes.FloatingBox(10, 10, 10, z: 5);
        var grid = new GridRoutingOptions { Lattice = BaseLatticeType.Square, Spacing = 5f, PlateZ = 0 };
        var tips = Place(mesh, P(spacing: 5, minSpacing: 1f) with { Grid = grid });
        var projected = tips.Where(c => c.Strategy == TipStrategy.GridProjection).ToList();
        Assert.NotEmpty(projected);

        var router = new GridSupportRouter(new LinearCollisionScene(), GrowthRuleSet.Default);
        foreach (var tip in projected)
        {
            var result = router.Route(
                [new RoutingTip(tip.Point, tip.InwardNormal, tip.TipDiameter)],
                grid);
            var b = Assert.Single(result.BasePositions);
            Assert.InRange(Vector2.Distance(new Vector2(b.X, b.Y), new Vector2(tip.Point.X, tip.Point.Y)), 0, 1e-3f);
        }
    }

    [Fact]
    public void HexGridWithOffsetAndRotationStaysOnTheLattice()
    {
        var mesh = Meshes.FloatingBox(12, 12, 6, z: 4);
        var grid = new GridRoutingOptions
        {
            Lattice = BaseLatticeType.Hexagonal,
            Spacing = 4f,
            Offset = new Vector2(1.25f, -0.5f),
            RotationDegrees = 30f,
        };
        var tips = Place(mesh, P(spacing: 4, minSpacing: 1f) with { Grid = grid });
        var projected = tips.Where(c => c.Strategy == TipStrategy.GridProjection).ToList();
        Assert.NotEmpty(projected);
        var lattice = BaseLattice.WorldPointsCovering(new Vector2(-1, -1), new Vector2(13, 13), grid).ToList();
        Assert.All(projected, t =>
            Assert.True(lattice.Any(p => Vector2.Distance(p, new Vector2(t.Point.X, t.Point.Y)) < 1e-3f),
                $"hex tip ({t.Point.X},{t.Point.Y}) is not on the rotated lattice"));
    }

    [Fact]
    public void GridProjectionIsDeterministic()
    {
        var mesh = Meshes.FloatingBox(10, 10, 10, z: 5);
        var p = P(spacing: 4, minSpacing: 1f) with { Grid = new GridRoutingOptions { Spacing = 4f } };
        Assert.Equal(Place(mesh, p, seed: 3), Place(mesh, p, seed: 3));
        // Seed does not affect grid samples (no RNG); islands/minima are seed-independent too.
        Assert.Equal(Place(mesh, p, seed: 3), Place(mesh, p, seed: 99));
    }

    [Fact]
    public void KeepCleanDistanceDropsCandidatesNearKeepCleanFaces()
    {
        var mesh = Meshes.FloatingBox(10, 10, 10, z: 5);
        var bottom = DownwardFaces(mesh);
        var keepOne = new HashSet<int> { bottom.Min() };

        var membershipOnly = Place(mesh, keepClean: keepOne);
        Assert.NotEmpty(membershipOnly);
        Assert.All(membershipOnly, c => Assert.DoesNotContain(c.FaceIndex, keepOne));

        var far = Place(mesh, P() with { KeepCleanDistanceMm = 20f }, keepClean: keepOne);
        Assert.Empty(far);
    }

    [Fact]
    public void KeepCleanDistanceZeroIsMembershipOnly()
    {
        var mesh = Meshes.FloatingBox(10, 10, 10, z: 5);
        var bottom = DownwardFaces(mesh);
        var keepOne = new HashSet<int> { bottom.Min() };
        var tips = Place(mesh, P() with { KeepCleanDistanceMm = 0f }, keepClean: keepOne);
        Assert.NotEmpty(tips);
        Assert.All(tips, c => Assert.DoesNotContain(c.FaceIndex, keepOne));
    }

    [Fact]
    public void InvalidFaceIndexThrows()
    {
        var mesh = Meshes.Box(4, 4, 4);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            TipPlacer.Place(mesh, new HashSet<int> { 0, 99 }, P()));
    }

    [Fact]
    public void DownwardSpikeIsALocalMinimum()
    {
        var mesh = Meshes.DownwardSpike();
        var tips = Place(mesh, P(spacing: 5f, minSpacing: 1f, overhang: 20f));
        var minima = tips.Where(c => c.Strategy == TipStrategy.LocalMinimum).ToList();
        Assert.NotEmpty(minima);
        var tip = minima.MinBy(c => c.Point.Z);
        Assert.InRange(tip.Point.X, -0.3f, 0.3f);
        Assert.InRange(tip.Point.Y, -0.3f, 0.3f);
        Assert.True(tip.Point.Z < 6f, $"spike tip should be near z=5, got {tip.Point.Z}");
    }

    [Fact]
    public void FineDownwardSpikeGetsOneMemberMiniCluster()
    {
        var mesh = Meshes.DownwardSpike();
        var parameters = P(spacing: 5f, minSpacing: 1f, overhang: 20f) with
        {
            EnableMiniSupports = true,
            EnableMiniTipClusters = true,
            FineFeatureMaxAreaMm2 = 1f,
        };

        var mini = Assert.Single(Place(mesh, parameters), candidate => candidate.IsFineFeatureMini);

        Assert.Equal(TipStrategy.MiniCluster, mini.Strategy);
        Assert.Equal(TipStrategy.LocalMinimum, mini.MiniClusterSourceStrategy);
        Assert.Equal(mini.Point, mini.MiniClusterCenter);
        Assert.InRange(mini.FineFeatureAreaMm2!.Value, 0.5f, 0.8f);
        Assert.Equal(parameters.TipDiameterMm, mini.FallbackTipDiameter);
        Assert.Equal(parameters.TipShape, mini.FallbackTipShape);
        Assert.Equal(parameters.ConeLengthMm, mini.FallbackConeLength);
        Assert.Equal(parameters.BallDiameterMm, mini.FallbackBallDiameter);
    }

    [Fact]
    public void FatIsolatedFeatureKeepsItsRegularCone()
    {
        var mesh = Meshes.FloatingBox(2, 2, 3, z: 5);
        var parameters = P(minIsland: 0.1f) with
        {
            EnableMiniSupports = true,
            EnableMiniTipClusters = true,
            FineFeatureMaxAreaMm2 = 1f,
        };

        var island = Assert.Single(Place(mesh, parameters), candidate =>
            candidate.Strategy == TipStrategy.Island);

        Assert.False(island.IsFineFeatureMini);
        Assert.InRange(island.FineFeatureAreaMm2!.Value, 3.9f, 4.1f);
    }

    [Fact]
    public void FineIslandConversionPreservesRequiredIslandCoverage()
    {
        var mesh = Meshes.FloatingBox(0.5f, 0.5f, 3, z: 5);
        var parameters = P(minIsland: 0.1f) with
        {
            EnableMiniSupports = true,
            EnableMiniTipClusters = true,
            MiniIslandMaxAreaMm2 = 0.1f,
            FineFeatureMaxAreaMm2 = 1f,
        };

        var candidate = Assert.Single(Place(mesh, parameters));

        Assert.True(candidate.IsFineFeatureMini);
        Assert.Equal(TipStrategy.Island, candidate.MiniClusterSourceStrategy);
        Assert.NotNull(candidate.MiniClusterId);
    }

    private static bool IsRequired(TipCandidate c) =>
        c.Strategy is TipStrategy.Island or TipStrategy.LocalMinimum;

    private static HashSet<int> DownwardFaces(Mesh mesh)
    {
        var set = new HashSet<int>();
        for (int t = 0; t < mesh.TriangleCount; t++)
            if (mesh.FaceNormals[t].Z < -0.5f) set.Add(t);
        return set;
    }
}

/// <summary>Procedural meshes used by tip-placement tests. Never committed as binary files.</summary>
internal static class Meshes
{
    /// <summary>Axis-aligned box with min-corner at <paramref name="origin"/>, outward winding.</summary>
    public static Mesh Box(float sx, float sy, float sz, Vector3? origin = null)
    {
        var o = origin ?? Vector3.Zero;
        var p = new Vector3[8];
        for (int i = 0; i < 8; i++)
            p[i] = o + new Vector3((i & 1) * sx, ((i >> 1) & 1) * sy, ((i >> 2) & 1) * sz);
        int[] idx =
        {
            0, 2, 3, 0, 3, 1,  4, 5, 7, 4, 7, 6,  0, 1, 5, 0, 5, 4,
            2, 6, 7, 2, 7, 3,  0, 4, 6, 0, 6, 2,  1, 3, 7, 1, 7, 5,
        };
        return new Mesh(p, idx);
    }

    public static Mesh FloatingBox(float sx, float sy, float sz, float z) =>
        Box(sx, sy, sz, new Vector3(0, 0, z));

    public static Mesh Merge(params Mesh[] parts)
    {
        var soup = new List<Vector3>();
        foreach (var mesh in parts)
        {
            for (int t = 0; t < mesh.TriangleCount; t++)
            {
                mesh.GetTriangle(t, out var a, out var b, out var c);
                soup.Add(a); soup.Add(b); soup.Add(c);
            }
        }
        return Mesh.FromTriangleSoup(soup.ToArray());
    }

    /// <summary>Four legs on the plate and a rectangular top whose underside is at <paramref name="height"/>.</summary>
    public static Mesh Table(float top, float topThickness, float leg, float height)
    {
        var half = top * 0.5f;
        var topMesh = Box(top, top, topThickness, new Vector3(-half, -half, height));
        var inset = half - leg;
        var legs = new[]
        {
            Box(leg, leg, height, new Vector3(-half, -half, 0)),
            Box(leg, leg, height, new Vector3(-half, inset, 0)),
            Box(leg, leg, height, new Vector3(inset, -half, 0)),
            Box(leg, leg, height, new Vector3(inset, inset, 0)),
        };
        return Merge(new[] { topMesh }.Concat(legs).ToArray());
    }

    /// <summary>Vertical 4×4×20 post on the plate with a 16×4×4 arm along +X at z=16.</summary>
    public static Mesh Cantilever()
    {
        var post = Box(4, 4, 20, new Vector3(-2, -2, 0));
        var arm = Box(16, 4, 4, new Vector3(2, -2, 16));
        return Merge(post, arm);
    }

    /// <summary>Square pyramid pointing down: base at z=10, apex at the origin of XY at z=5.</summary>
    public static Mesh DownwardSpike()
    {
        var apex = new Vector3(0, 0, 5);
        var b00 = new Vector3(-4, -4, 10);
        var b10 = new Vector3(4, -4, 10);
        var b11 = new Vector3(4, 4, 10);
        var b01 = new Vector3(-4, 4, 10);
        // Sides wind outward; base (top) winds +Z.
        var soup = new[]
        {
            apex, b10, b00,
            apex, b11, b10,
            apex, b01, b11,
            apex, b00, b01,
            b00, b10, b11,
            b00, b11, b01,
        };
        return Mesh.FromTriangleSoup(soup);
    }

    /// <summary>
    /// Regular grid of quads in XY, Z = amplitude * sin/cos. <paramref name="cells"/> quads
    /// on a side → 2·cells² triangles.
    /// </summary>
    public static Mesh Heightfield(int cells, float size, float amplitude)
    {
        var nx = cells + 1;
        var positions = new Vector3[nx * nx];
        var step = size / cells;
        for (int y = 0; y < nx; y++)
        for (int x = 0; x < nx; x++)
        {
            var z = amplitude * MathF.Sin(x * 0.13f) * MathF.Cos(y * 0.11f);
            positions[y * nx + x] = new Vector3(x * step, y * step, z);
        }

        var indices = new int[cells * cells * 6];
        int n = 0;
        for (int y = 0; y < cells; y++)
        for (int x = 0; x < cells; x++)
        {
            int i00 = y * nx + x;
            int i10 = i00 + 1;
            int i01 = i00 + nx;
            int i11 = i01 + 1;
            indices[n++] = i00; indices[n++] = i10; indices[n++] = i11;
            indices[n++] = i00; indices[n++] = i11; indices[n++] = i01;
        }
        return new Mesh(positions, indices);
    }

    public static Mesh UvSphere(float radius, Vector3 center, int slices, int stacks)
    {
        Vector3 Sph(int st, int sl)
        {
            var phi = MathF.PI * st / stacks;
            var theta = 2 * MathF.PI * sl / slices;
            return center + new Vector3(
                radius * MathF.Sin(phi) * MathF.Cos(theta),
                radius * MathF.Sin(phi) * MathF.Sin(theta),
                radius * MathF.Cos(phi));
        }

        var soup = new List<Vector3>();
        void Add(Vector3 a, Vector3 b, Vector3 c)
        {
            var n = Vector3.Cross(b - a, c - a);
            var mid = (a + b + c) / 3f - center;
            if (Vector3.Dot(n, mid) < 0) (b, c) = (c, b);
            soup.Add(a); soup.Add(b); soup.Add(c);
        }

        for (int st = 0; st < stacks; st++)
        for (int sl = 0; sl < slices; sl++)
        {
            var a = Sph(st, sl);
            var b = Sph(st + 1, sl);
            var c = Sph(st, sl + 1);
            var d = Sph(st + 1, sl + 1);
            if (st != 0) Add(a, b, c);
            if (st != stacks - 1) Add(c, b, d);
        }
        return Mesh.FromTriangleSoup(soup.ToArray());
    }
}
