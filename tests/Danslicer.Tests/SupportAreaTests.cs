using System.Numerics;
using Danslicer.Core.Geometry;
using Danslicer.Core.Supports.Generation;

namespace Danslicer.Tests;

public sealed class SupportAreaTests
{
    private static IReadOnlySet<int> AllFaces(Mesh mesh) =>
        Enumerable.Range(0, mesh.TriangleCount).ToHashSet();

    private static IReadOnlyList<SupportArea> Detect(Mesh mesh, SupportAreaParameters? p = null,
        IReadOnlySet<int>? faces = null) =>
        SupportAreaDetector.Detect(mesh, faces ?? AllFaces(mesh), p ?? SupportAreaParameters.Default);

    [Fact]
    public void CubeOnThePlateHasNoAreas()
    {
        var mesh = Meshes.Box(10, 10, 10);
        Assert.Empty(Detect(mesh));
    }

    [Fact]
    public void FloatingCubeUndersideIsOneHighSeverityArea()
    {
        var mesh = Meshes.FloatingBox(10, 10, 10, z: 5);
        var areas = Detect(mesh);
        var area = Assert.Single(areas);
        Assert.Equal(0, area.Id);
        Assert.Equal(2, area.Faces.Count);
        Assert.Equal(100f, area.AreaMm2, 1);
        Assert.InRange(area.Centroid.X, 4.9f, 5.1f);
        Assert.InRange(area.Centroid.Y, 4.9f, 5.1f);
        Assert.InRange(area.Centroid.Z, 4.9f, 5.1f);
        Assert.True(area.MaxOverhangDegrees > 80f);
        Assert.Equal(SupportAreaSeverity.High, area.Severity);
        Assert.True(area.ContainsIsland);
        Assert.True(area.PatchIds.Count >= 1);
    }

    [Fact]
    public void TableUndersideIsOneAreaAndLegsAreIgnored()
    {
        var mesh = Meshes.Table(top: 20, topThickness: 2, leg: 2, height: 10);
        var areas = Detect(mesh, new SupportAreaParameters { LayerHeightMm = 0.2f });
        var area = Assert.Single(areas);
        Assert.InRange(area.AreaMm2, 300f, 400f);
        Assert.InRange(area.Centroid.Z, 9.5f, 10.5f);
        Assert.True(area.ContainsIsland);
        Assert.All(area.Faces, t => Assert.True(mesh.FaceNormals[t].Z < -0.5f));
    }

    [Fact]
    public void CantileverArmIsAnAreaAwayFromThePost()
    {
        var mesh = Meshes.Cantilever();
        var areas = Detect(mesh, new SupportAreaParameters { LayerHeightMm = 0.2f });
        Assert.NotEmpty(areas);
        Assert.Contains(areas, a => a.Centroid.X > 3f && a.Centroid.Z > 15f);
        Assert.DoesNotContain(areas, a => a.Centroid.Z < 5f);
    }

    [Fact]
    public void TwoDisconnectedFloatingBoxesAreTwoAreas()
    {
        var a = Meshes.Box(8, 8, 8, new Vector3(0, 0, 5));
        var b = Meshes.Box(8, 8, 8, new Vector3(30, 0, 5));
        var mesh = Meshes.Merge(a, b);
        var areas = Detect(mesh);
        Assert.Equal(2, areas.Count);
        Assert.Equal(0, areas[0].Id);
        Assert.Equal(1, areas[1].Id);
        Assert.All(areas, x => Assert.Equal(64f, x.AreaMm2, 1));
        var xs = areas.Select(x => x.Centroid.X).OrderBy(v => v).ToList();
        Assert.InRange(xs[0], 3f, 5f);
        Assert.InRange(xs[1], 33f, 35f);
    }

    [Fact]
    public void DownwardSpikeAreaContainsALocalMinimum()
    {
        var mesh = Meshes.DownwardSpike();
        var areas = Detect(mesh);
        Assert.Contains(areas, a => a.ContainsLocalMinimum);
    }

    [Fact]
    public void RegionSubsetRestrictsFaces()
    {
        var mesh = Meshes.FloatingBox(10, 10, 10, z: 5);
        var bottom = new HashSet<int>();
        for (int t = 0; t < mesh.TriangleCount; t++)
            if (mesh.FaceNormals[t].Z < -0.5f) bottom.Add(t);
        var sides = AllFaces(mesh).Except(bottom).ToHashSet();

        Assert.NotEmpty(Detect(mesh, faces: bottom));
        Assert.Empty(Detect(mesh, faces: sides));
    }

    [Fact]
    public void EmptyRegionProducesNothing()
    {
        var mesh = Meshes.FloatingBox(10, 10, 10, z: 5);
        Assert.Empty(Detect(mesh, faces: new HashSet<int>()));
    }

    [Fact]
    public void InvalidFaceIndexThrows()
    {
        var mesh = Meshes.Box(4, 4, 4);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            SupportAreaDetector.Detect(mesh, new HashSet<int> { 99 }));
    }

    [Fact]
    public void SameInputsAreDeterministicAndIdsAreStable()
    {
        var mesh = Meshes.Table(top: 16, topThickness: 2, leg: 2, height: 8);
        var a = Detect(mesh);
        var b = Detect(mesh);
        Assert.Equal(a.Count, b.Count);
        Assert.Equal(Enumerable.Range(0, a.Count), a.Select(x => x.Id));
        for (int i = 0; i < a.Count; i++)
        {
            Assert.Equal(a[i].Id, b[i].Id);
            Assert.Equal(a[i].Faces, b[i].Faces);
            Assert.Equal(a[i].AreaMm2, b[i].AreaMm2, 3);
            Assert.Equal(a[i].Centroid, b[i].Centroid);
            Assert.Equal(a[i].MeanNormal, b[i].MeanNormal);
            Assert.Equal(a[i].MaxOverhangDegrees, b[i].MaxOverhangDegrees, 3);
            Assert.Equal(a[i].ContainsIsland, b[i].ContainsIsland);
            Assert.Equal(a[i].ContainsLocalMinimum, b[i].ContainsLocalMinimum);
            Assert.Equal(a[i].Severity, b[i].Severity);
            Assert.Equal(a[i].PatchIds, b[i].PatchIds);
        }
    }

    [Fact]
    public void SmallAreasCanBeIgnored()
    {
        var mesh = Meshes.FloatingBox(0.4f, 0.4f, 4, z: 2);
        var none = Detect(mesh, new SupportAreaParameters { MinAreaMm2 = 1f, MinIslandAreaMm2 = 1f });
        Assert.Empty(none);
        var kept = Detect(mesh, new SupportAreaParameters { MinAreaMm2 = 0.01f, MinIslandAreaMm2 = 0.01f });
        Assert.NotEmpty(kept);
    }
}
