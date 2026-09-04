using System.Numerics;
using Danslicer.Core.Geometry;
using Danslicer.Core.Supports.Generation;

namespace Danslicer.Tests;

/// <summary>
/// <see cref="ContactFaceFilter"/> narrows the stage-1 candidate list, not the region: islands are
/// discovered from the sliced layer stack rather than the region, so a region-level filter would
/// miss them (user request 2026-09-04: the gripper test model should get supports only from its
/// lower faces). Two independent settings — <see cref="TipPlacementParameters.MaxContactFaceAngleDegrees"/>
/// and <see cref="TipPlacementParameters.RequireContactSeesPlate"/> — compose rather than acting as
/// exclusive modes.
/// </summary>
public class ContactFaceFilterTests
{
    private static TipCandidate Candidate(Vector3 point, int faceIndex,
        TipStrategy strategy = TipStrategy.Overhang) =>
        new(point, -Vector3.UnitZ, 0.4f, 1f, strategy, faceIndex);

    /// <summary>Single triangle whose face normal points <paramref name="degreesFromDown"/> off straight down.</summary>
    private static Mesh SingleTriangleAtDownAngle(float degreesFromDown)
    {
        var theta = degreesFromDown * MathF.PI / 180f;
        var normal = new Vector3(MathF.Sin(theta), 0f, -MathF.Cos(theta));
        return SingleTriangleWithNormal(normal, Vector3.Zero);
    }

    private static Mesh SingleTriangleWithNormal(Vector3 normal, Vector3 origin)
    {
        var n = Vector3.Normalize(normal);
        var arbitrary = MathF.Abs(n.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitX;
        var u = Vector3.Normalize(Vector3.Cross(arbitrary, n));
        var v = Vector3.Cross(n, u);
        return new Mesh([origin, origin + u, origin + v], [0, 1, 2]);
    }

    [Fact]
    public void DefaultSettingsLeaveTheCandidateListByteIdentical()
    {
        var mesh = Meshes.Box(10, 10, 10);
        var candidates = new[]
        {
            Candidate(new Vector3(5, 5, 0), 0, TipStrategy.Island),
            Candidate(new Vector3(5, 5, 10), 2),
            Candidate(new Vector3(0, 5, 5), 8),
        };

        // TipPlacementParameters.Default: MaxContactFaceAngleDegrees 90, RequireContactSeesPlate false.
        var result = ContactFaceFilter.Apply(candidates, mesh, TipPlacementParameters.Default);

        Assert.Same(candidates, result);
    }

    [Theory]
    [InlineData(44f, true)]  // just under the 45° threshold: kept
    [InlineData(45f, true)]  // exactly at the threshold: kept ("at or below")
    [InlineData(46f, false)] // just over the threshold: dropped
    public void MaxContactFaceAngleAppliesAtItsBoundary(float faceAngleFromDown, bool expectedKept)
    {
        var mesh = SingleTriangleAtDownAngle(faceAngleFromDown);
        var candidates = new[] { Candidate(Vector3.Zero, 0) };
        var parameters = TipPlacementParameters.Default with { MaxContactFaceAngleDegrees = 45f };

        var result = ContactFaceFilter.Apply(candidates, mesh, parameters, out var rejected);

        Assert.Equal(expectedKept, result.Count == 1);
        if (!expectedKept)
        {
            var reason = Assert.Single(rejected);
            Assert.Equal(ContactFaceRejectionReason.Angle, reason.Reason);
        }
    }

    [Fact]
    public void AngleLimitDropsTheFourSideWallsAndKeepsTheUndersideOfABox()
    {
        var mesh = Meshes.Box(10, 10, 10);
        // Face indices per Meshes.Box's winding (see TipPlacementTests.Meshes.Box): 0/1 bottom
        // (-Z), 2/3 top (+Z), 4/5 -Y wall, 6/7 +Y wall, 8/9 -X wall, 10/11 +X wall.
        var candidates = new[]
        {
            Candidate(new Vector3(5, 5, 0), 0, TipStrategy.Island),
            Candidate(new Vector3(5, 5, 10), 2),
            Candidate(new Vector3(5, 0, 5), 4),
            Candidate(new Vector3(5, 10, 5), 6),
            Candidate(new Vector3(0, 5, 5), 8),
            Candidate(new Vector3(10, 5, 5), 10),
        };
        var parameters = TipPlacementParameters.Default with { MaxContactFaceAngleDegrees = 45f };

        var result = ContactFaceFilter.Apply(candidates, mesh, parameters);

        var kept = Assert.Single(result);
        Assert.Equal(0, kept.FaceIndex);
    }

    [Fact]
    public void SeesPlateOffLeavesAngleOnlyFilteringUnaffected()
    {
        // Same box scenario, but with RequireContactSeesPlate explicitly false alongside the
        // angle limit: the two settings are independent, so the result must match the angle-only
        // case exactly.
        var mesh = Meshes.Box(10, 10, 10);
        var candidates = new[]
        {
            Candidate(new Vector3(5, 5, 0), 0, TipStrategy.Island),
            Candidate(new Vector3(5, 0, 5), 4),
        };
        var parameters = TipPlacementParameters.Default with
        {
            MaxContactFaceAngleDegrees = 45f,
            RequireContactSeesPlate = false,
        };

        var result = ContactFaceFilter.Apply(candidates, mesh, parameters);

        var kept = Assert.Single(result);
        Assert.Equal(0, kept.FaceIndex);
    }

    /// <summary>
    /// Two-tier shape: one underside is exposed straight down to the plate, another sits directly
    /// above an occluding slab. The angle limit alone cannot tell them apart; sees-plate must.
    /// </summary>
    private static Mesh TwoTierShape(out Vector3 exposedPoint, out Vector3 occludedPoint)
    {
        exposedPoint = new Vector3(0, 0, 5);
        occludedPoint = new Vector3(20, 20, 10);

        var exposed = SingleTriangleWithNormal(-Vector3.UnitZ, exposedPoint);
        var occluded = SingleTriangleWithNormal(-Vector3.UnitZ, occludedPoint);

        // A slab directly under the occluded point, well clear of the exposed one, blocking its
        // straight-down path to the plate.
        var slabPositions = new[]
        {
            new Vector3(15, 15, 3), new Vector3(25, 15, 3), new Vector3(25, 25, 3), new Vector3(15, 25, 3),
        };

        var positions = new List<Vector3>();
        positions.AddRange(exposed.Positions);
        positions.AddRange(occluded.Positions);
        positions.AddRange(slabPositions);

        int[] indices =
        [
            0, 1, 2, // exposed underside
            3, 4, 5, // occluded underside
            6, 7, 8, 6, 8, 9, // slab, two triangles
        ];
        return new Mesh(positions.ToArray(), indices);
    }

    [Fact]
    public void SeesPlateOnKeepsTheExposedUndersideAndDropsTheOccludedOne()
    {
        var mesh = TwoTierShape(out var exposedPoint, out var occludedPoint);
        var candidates = new[] { Candidate(exposedPoint, 0), Candidate(occludedPoint, 1) };
        var parameters = TipPlacementParameters.Default with
        {
            MaxContactFaceAngleDegrees = 45f,
            RequireContactSeesPlate = true,
        };

        var result = ContactFaceFilter.Apply(candidates, mesh, parameters, out var rejected);

        var kept = Assert.Single(result);
        Assert.Equal(exposedPoint, kept.Point);
        var reason = Assert.Single(rejected);
        Assert.Equal(ContactFaceRejectionReason.Occluded, reason.Reason);
        Assert.Equal(occludedPoint, reason.Candidate.Point);
    }

    [Fact]
    public void SeesPlateOffKeepsBothUndersidesOfTheTwoTierShape()
    {
        var mesh = TwoTierShape(out var exposedPoint, out var occludedPoint);
        var candidates = new[] { Candidate(exposedPoint, 0), Candidate(occludedPoint, 1) };
        var parameters = TipPlacementParameters.Default with
        {
            MaxContactFaceAngleDegrees = 45f,
            RequireContactSeesPlate = false,
        };

        var result = ContactFaceFilter.Apply(candidates, mesh, parameters);

        Assert.Equal(2, result.Count);
    }

    /// <summary>Both settings on at once: angle first (cheap), sees-plate only for survivors.</summary>
    [Fact]
    public void AngleAndSeesPlateComposeRatherThanActingAsExclusiveModes()
    {
        var mesh = TwoTierShape(out var exposedPoint, out var occludedPoint);
        // Give the occluded triangle a steep side-facing normal instead, so it fails on angle
        // alone; the exposed one stays straight down and must pass both settings.
        var sideFacing = SingleTriangleWithNormal(new Vector3(1f, 0f, -0.01f), occludedPoint);
        var positions = new List<Vector3>(mesh.Positions[..3]);
        positions.AddRange(sideFacing.Positions);
        positions.AddRange(mesh.Positions[6..]);
        var combined = new Mesh(positions.ToArray(), [0, 1, 2, 3, 4, 5, 6, 7, 8, 6, 8, 9]);

        var candidates = new[] { Candidate(exposedPoint, 0), Candidate(occludedPoint, 1) };
        var parameters = TipPlacementParameters.Default with
        {
            MaxContactFaceAngleDegrees = 45f,
            RequireContactSeesPlate = true,
        };

        var result = ContactFaceFilter.Apply(candidates, mesh: combined, parameters, out var rejected);

        var kept = Assert.Single(result);
        Assert.Equal(exposedPoint, kept.Point);
        var reason = Assert.Single(rejected);
        Assert.Equal(ContactFaceRejectionReason.Angle, reason.Reason);
    }

    /// <summary>
    /// Island contacts come from <see cref="IslandFinder"/> (the sliced layer stack), not from
    /// overhang sampling on the region. This is the case that would silently regress if the
    /// filter were applied to the region's faces instead of the candidate list.
    /// </summary>
    [Fact]
    public void SeesPlateDropsAnOccludedIslandContactTooNotJustOverhangSamples()
    {
        // An upward-facing occluder (no downward face) blocks the sees-plate raycast without
        // being mistaken by IslandFinder's own downward-facing projection ray for the tooth's
        // underside.
        var occluderPositions = new[]
        {
            new Vector3(10, 10, 3), new Vector3(14, 10, 3), new Vector3(14, 14, 3), new Vector3(10, 14, 3),
        };
        var occluder = new Mesh(occluderPositions, [0, 1, 2, 0, 2, 3]);
        var visibleTooth = Meshes.Box(0.5f, 0.5f, 3, new Vector3(0, 0, 5));
        var occludedTooth = Meshes.Box(0.5f, 0.5f, 3, new Vector3(12, 12, 5));
        var mesh = Meshes.Merge(occluder, visibleTooth, occludedTooth);
        var region = Enumerable.Range(0, mesh.TriangleCount).ToHashSet();
        var parameters = TipPlacementParameters.Default with { MinIslandAreaMm2 = 0.1f };

        var placed = TipPlacer.Place(mesh, region, parameters);
        var islandsBefore = placed.Where(c => c.Strategy == TipStrategy.Island).ToList();
        Assert.True(islandsBefore.Count == 2,
            "islands: " + string.Join(", ", placed.Select(c => $"{c.Strategy}@{c.Point}")));

        var filtered = ContactFaceFilter.Apply(placed, mesh,
            parameters with { RequireContactSeesPlate = true });

        var islandsAfter = filtered.Where(c => c.Strategy == TipStrategy.Island).ToList();
        var kept = Assert.Single(islandsAfter);
        Assert.True(kept.Point.X < 5, "the occluded tooth's island contact should have been dropped");
    }
}
