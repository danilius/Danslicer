using System.Numerics;
using Danslicer.Core.Geometry;
using Danslicer.Core.Supports.Generation;

namespace Danslicer.Tests;

/// <summary>
/// The painted-region grid (user request 2026-09-06, with screenshots): contacts on a painted
/// area must land on an even grid rather than the scattered Poisson distribution, rows must be
/// even in Z above all, and painting several faces must support all of them — the old lattice
/// cast rays straight up, so only the lowest painted face in a column ever received anything.
/// </summary>
public class RegionGridSamplerTests
{
    private static TipPlacementParameters Parameters(float vertical, float horizontal) =>
        TipPlacementParameters.Default with
        {
            RegionGrid = new RegionGridOptions
            {
                VerticalPitchMm = vertical,
                HorizontalPitchMm = horizontal,
            },
        };

    /// <summary>Two triangles spanning a quad, wound so the face normals point downward.</summary>
    private static (Vector3[] Positions, int[] Indices) Quad(
        Vector3 a, Vector3 b, Vector3 c, Vector3 d, int baseIndex) =>
        ([a, b, c, d], [baseIndex, baseIndex + 1, baseIndex + 2,
                        baseIndex + 1, baseIndex + 3, baseIndex + 2]);

    private static Mesh Mesh(params (Vector3[] Positions, int[] Indices)[] quads)
    {
        var positions = quads.SelectMany(q => q.Positions).ToArray();
        var indices = quads.SelectMany(q => q.Indices).ToArray();
        return new Mesh(positions, indices);
    }

    /// <summary>A 45° wall rising in X, its normal pointing down and outward.</summary>
    private static Mesh Ramp() => Mesh(Quad(
        new Vector3(0, 0, 0), new Vector3(0, 10, 0),
        new Vector3(10, 0, 10), new Vector3(10, 10, 10), 0));

    private static HashSet<int> AllFaces(Mesh mesh) => [.. Enumerable.Range(0, mesh.TriangleCount)];

    [Fact]
    public void RowsAreEvenlySpacedInZ()
    {
        var mesh = Ramp();
        Assert.All(mesh.FaceNormals, normal => Assert.True(normal.Z < 0));

        var samples = RegionGridSampler.Sample(mesh, AllFaces(mesh), Parameters(2f, 2f));

        var rows = samples.Select(s => MathF.Round(s.Point.Z, 3)).Distinct().Order().ToList();
        Assert.True(rows.Count >= 4, $"expected several rows, got {rows.Count}");
        for (var i = 1; i < rows.Count; i++)
            Assert.Equal(2f, rows[i] - rows[i - 1], 3);
    }

    [Fact]
    public void EveryRowIsFullyPopulatedAtTheHorizontalPitch()
    {
        var mesh = Ramp();

        var samples = RegionGridSampler.Sample(mesh, AllFaces(mesh), Parameters(2f, 2f));

        foreach (var row in samples.GroupBy(s => MathF.Round(s.Point.Z, 3)))
        {
            var ys = row.Select(s => s.Point.Y).Order().ToList();
            Assert.True(ys.Count >= 5, $"row at z={row.Key} has only {ys.Count} contacts");
            for (var i = 1; i < ys.Count; i++)
                Assert.Equal(2f, ys[i] - ys[i - 1], 2);
        }
    }

    /// <summary>
    /// The regression from the screenshots: three painted faces stacked in the same XY column.
    /// A +Z ray from below reaches only the lowest of them, which is why two were left bare.
    /// </summary>
    [Fact]
    public void StackedPaintedFacesAllReceiveContacts()
    {
        var mesh = Mesh(
            Plate(2f, 0), Plate(5f, 4), Plate(8f, 8));

        var samples = RegionGridSampler.Sample(mesh, AllFaces(mesh), Parameters(2f, 2f));

        var levels = samples.Select(s => MathF.Round(s.Point.Z, 3)).Distinct().Order().ToList();
        Assert.Equal([2f, 5f, 8f], levels);

        static (Vector3[] Positions, int[] Indices) Plate(float z, int baseIndex) => Quad(
            new Vector3(0, 0, z), new Vector3(0, 10, z),
            new Vector3(10, 0, z), new Vector3(10, 10, z), baseIndex);
    }

    /// <summary>A flat underside has no Z extent to band, so it falls back to an XY lattice.</summary>
    [Fact]
    public void FlatUndersideGetsAnEvenLatticeInBothAxes()
    {
        var mesh = Mesh(Quad(
            new Vector3(0, 0, 5), new Vector3(0, 10, 5),
            new Vector3(10, 0, 5), new Vector3(10, 10, 5), 0));

        var samples = RegionGridSampler.Sample(mesh, AllFaces(mesh), Parameters(2f, 2f));

        Assert.All(samples, s => Assert.Equal(5f, s.Point.Z, 3));
        var xs = samples.Select(s => MathF.Round(s.Point.X, 3)).Distinct().Order().ToList();
        var ys = samples.Select(s => MathF.Round(s.Point.Y, 3)).Distinct().Order().ToList();
        Assert.Equal(5, xs.Count);
        Assert.Equal(5, ys.Count);
        for (var i = 1; i < xs.Count; i++) Assert.Equal(2f, xs[i] - xs[i - 1], 3);
        for (var i = 1; i < ys.Count; i++) Assert.Equal(2f, ys[i] - ys[i - 1], 3);
    }

    /// <summary>
    /// The image-2 case: a curved surface no lattice fits. Spacing is measured along the
    /// surface, so contacts stay evenly spread around the curve — here a cone whose rows are
    /// circles of a different circumference at every height.
    /// </summary>
    [Fact]
    public void CurvedSurfaceIsSpacedAlongTheSurfaceNotAcrossTheLattice()
    {
        const int steps = 128;
        var positions = new List<Vector3> { Vector3.Zero };
        var indices = new List<int>();
        for (var i = 0; i < steps; i++)
        {
            var angle = 2f * MathF.PI * i / steps;
            positions.Add(new Vector3(10f * MathF.Cos(angle), 10f * MathF.Sin(angle), 10f));
        }
        for (var i = 0; i < steps; i++)
            indices.AddRange([0, 1 + (i + 1) % steps, 1 + i]);
        var mesh = new Mesh([.. positions], [.. indices]);
        Assert.All(mesh.FaceNormals, normal => Assert.True(normal.Z < 0));

        var samples = RegionGridSampler.Sample(mesh, AllFaces(mesh), Parameters(2f, 2f));

        Assert.NotEmpty(samples);
        foreach (var row in samples.GroupBy(s => MathF.Round(s.Point.Z, 3)))
        {
            // On a cone of half-angle 45° the row at height z is a circle of radius z.
            var radius = row.Key;
            if (radius < 3f) continue;
            var angles = row.Select(s => MathF.Atan2(s.Point.Y, s.Point.X)).Order().ToList();
            Assert.Equal((int)MathF.Round(2f * MathF.PI * radius / 2f), angles.Count);
            for (var i = 1; i < angles.Count; i++)
                Assert.InRange((angles[i] - angles[i - 1]) * radius, 1.9f, 2.1f);
        }
    }

    [Fact]
    public void UpwardFacingPaintedFacesAreSkipped()
    {
        var mesh = Mesh(Quad(
            new Vector3(0, 10, 5), new Vector3(0, 0, 5),
            new Vector3(10, 10, 5), new Vector3(10, 0, 5), 0));
        Assert.All(mesh.FaceNormals, normal => Assert.True(normal.Z > 0));

        Assert.Empty(RegionGridSampler.Sample(mesh, AllFaces(mesh), Parameters(2f, 2f)));
    }

    /// <summary>A painted sliver smaller than the pitch in every axis still gets one contact.</summary>
    [Fact]
    public void ATinyPaintedPatchStillGetsOneContact()
    {
        var mesh = Mesh(Quad(
            new Vector3(0, 0, 5), new Vector3(0, 0.2f, 5),
            new Vector3(0.2f, 0, 5), new Vector3(0.2f, 0.2f, 5), 0));

        var samples = RegionGridSampler.Sample(mesh, AllFaces(mesh), Parameters(2f, 2f));

        Assert.Single(samples);
    }

    [Fact]
    public void SamplingIsDeterministic()
    {
        var mesh = Ramp();
        var faces = AllFaces(mesh);

        var first = RegionGridSampler.Sample(mesh, faces, Parameters(1.5f, 1.7f));
        var second = RegionGridSampler.Sample(mesh, faces, Parameters(1.5f, 1.7f));

        Assert.Equal(first, second);
    }

    /// <summary>
    /// The other half of the screenshot regression: two of the three painted faces were shallower
    /// than the 45° overhang threshold, and overhang sampling skipped them. Painting is explicit,
    /// so the grid ignores the threshold.
    /// </summary>
    [Fact]
    public void PaintedFacesShallowerThanTheOverhangThresholdStillGetContacts()
    {
        // A wall only ~17° off vertical: well inside the 45° "not an overhang" band.
        var mesh = Mesh(Quad(
            new Vector3(0, 0, 5), new Vector3(0, 10, 5),
            new Vector3(3, 0, 15), new Vector3(3, 10, 15), 0));
        Assert.All(mesh.FaceNormals, normal =>
            Assert.True(TipPlacementParameters.OverhangDegrees(normal) < 45f));
        var faces = AllFaces(mesh);
        var painted = Parameters(2f, 2f);

        var withoutRegion = TipPlacer.Place(mesh, faces, painted with { RegionGrid = null });
        var withRegion = TipPlacer.Place(mesh, faces, painted);

        Assert.DoesNotContain(withoutRegion, c => c.Strategy == TipStrategy.RegionGrid);
        var grid = withRegion.Where(c => c.Strategy == TipStrategy.RegionGrid).ToList();
        Assert.True(grid.Count > withoutRegion.Count,
            $"painted region produced {grid.Count} contacts, unpainted {withoutRegion.Count}");
        var rows = grid.Select(c => MathF.Round(c.Point.Z, 3)).Distinct().Order().ToList();
        for (var i = 1; i < rows.Count; i++)
            Assert.Equal(2f, rows[i] - rows[i - 1], 3);
    }
}
