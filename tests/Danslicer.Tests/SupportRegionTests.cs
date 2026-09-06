using System.Numerics;
using Danslicer.Core;
using Danslicer.Core.Config;
using Danslicer.Core.Geometry;
using Danslicer.Core.IO;
using Danslicer.Core.Scene;
using Danslicer.Core.Supports;

namespace Danslicer.Tests;

/// <summary>
/// Stage 1 of DESIGN 8.3 (support painting): the region model, its persistence and undo, the
/// feed into generation, and the overlay geometry. No selection tools here — those are stages 2
/// and 3.
///
/// <para>The load-bearing test in this file is
/// <see cref="GenerationWithNoRegionIsUnchangedByThisFeature"/>: with nothing painted, generation
/// must be bit-identical to before regions existed, or this feature has broken every existing
/// project.</para>
/// </summary>
public sealed class SupportRegionTests
{
    private static Mesh Box(Vector3 min, Vector3 max)
    {
        var (a, b) = (min, max);
        var corners = new Vector3[]
        {
            new(a.X, a.Y, a.Z), new(b.X, a.Y, a.Z), new(b.X, b.Y, a.Z), new(a.X, b.Y, a.Z),
            new(a.X, a.Y, b.Z), new(b.X, a.Y, b.Z), new(b.X, b.Y, b.Z), new(a.X, b.Y, b.Z),
        };
        int[] quads = [0, 3, 2, 1, 4, 5, 6, 7, 0, 1, 5, 4, 2, 3, 7, 6, 0, 4, 7, 3, 1, 2, 6, 5];
        var soup = new List<Vector3>();
        for (int q = 0; q < quads.Length; q += 4)
        {
            soup.Add(corners[quads[q]]); soup.Add(corners[quads[q + 1]]); soup.Add(corners[quads[q + 2]]);
            soup.Add(corners[quads[q]]); soup.Add(corners[quads[q + 2]]); soup.Add(corners[quads[q + 3]]);
        }
        return Mesh.FromTriangleSoup(soup.ToArray());
    }

    private static (Document Document, SceneObject Box) FloatingBox()
    {
        var document = new Document { PlacementMode = PlacementMode.Off };
        var obj = new SceneObject("box", Box(new Vector3(-8, -8, 10), new Vector3(8, 8, 20)));
        document.AddObject(obj);
        return (document, obj);
    }

    // ----- The model -----

    [Fact]
    public void AnEmptyRegionMeansEveryFaceNotNoFaces()
    {
        // The whole compatibility story rests on this reading.
        var effective = ObjectSupportRegions.Empty.EffectiveFaces(12);

        Assert.Equal(Enumerable.Range(0, 12).ToHashSet(), effective);
    }

    [Fact]
    public void KeepCleanIsSubtractedFromThePaintedRegion()
    {
        var regions = ObjectSupportRegions.From([1, 2, 3, 4], [3]);

        Assert.Equal(new HashSet<int> { 1, 2, 4 }, regions.EffectiveFaces(12));
    }

    [Fact]
    public void KeepCleanAloneStillLeavesEveryOtherFaceAvailable()
    {
        var regions = ObjectSupportRegions.From(null, [0, 5]);

        Assert.Equal(new HashSet<int> { 1, 2, 3, 4 }, regions.EffectiveFaces(6));
    }

    [Fact]
    public void FaceIndicesBeyondTheMeshAreIgnoredRatherThanThrowing()
    {
        // A region can outlive an edit that shortens the mesh. A stale index must not crash
        // generation or the viewport.
        var regions = ObjectSupportRegions.From([0, 99], null);

        Assert.Equal(new HashSet<int> { 0 }, regions.EffectiveFaces(4));
    }

    // ----- Undo -----

    [Fact]
    public void PaintingARegionIsOneUndoStep()
    {
        var (document, box) = FloatingBox();
        var painted = ObjectSupportRegions.From([0, 1, 2], [7]);

        document.SetSupportRegions(box, painted);

        Assert.Equal(painted, box.Regions);

        Assert.True(document.Undo());
        Assert.True(box.Regions.IsEmpty);

        Assert.True(document.Redo());
        Assert.Equal(painted, box.Regions);
    }

    [Fact]
    public void SettingTheSameRegionAgainIsNotAnUndoStep()
    {
        var (document, box) = FloatingBox();
        var painted = ObjectSupportRegions.From([4, 5], null);
        document.SetSupportRegions(box, painted);
        var depth = document.History.UndoName;

        document.SetSupportRegions(box, ObjectSupportRegions.From([5, 4], null)); // same set

        Assert.Equal(depth, document.History.UndoName);
    }

    [Fact]
    public void ClearingReturnsTheObjectToEveryFace()
    {
        var (document, box) = FloatingBox();
        document.SetSupportRegions(box, ObjectSupportRegions.From([1], null));

        document.ClearSupportRegions(box);

        Assert.True(box.Regions.IsEmpty);
        Assert.Equal(Enumerable.Range(0, box.Mesh.TriangleCount).ToHashSet(),
            box.Regions.EffectiveFaces(box.Mesh.TriangleCount));
    }

    // ----- Persistence -----

    [Fact]
    public void RegionsRoundTripThroughASavedProject()
    {
        var dir = Path.Combine(Path.GetTempPath(), "danslicer-region-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var (document, box) = FloatingBox();
            document.SetSupportRegions(box, ObjectSupportRegions.From([2, 5, 9], [11]));
            var path = Path.Combine(dir, "regions.danslicer");

            ProjectFile.Save(path, document, new ProjectViewState());
            var loaded = ProjectFile.Load(path);

            var reloaded = Assert.Single(loaded.Document.Scene.Objects);
            Assert.Equal(new HashSet<int> { 2, 5, 9 }, reloaded.Regions.Faces);
            Assert.Equal(new HashSet<int> { 11 }, reloaded.Regions.KeepCleanFaces);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public void AProjectWithNoRegionLoadsAsEveryFace()
    {
        var dir = Path.Combine(Path.GetTempPath(), "danslicer-region-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var (document, _) = FloatingBox();
            var path = Path.Combine(dir, "plain.danslicer");
            ProjectFile.Save(path, document, new ProjectViewState());

            var reloaded = Assert.Single(ProjectFile.Load(path).Document.Scene.Objects);

            Assert.True(reloaded.Regions.IsEmpty);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
        }
    }

    // ----- Generation -----

    [Fact]
    public void GenerationWithNoRegionIsUnchangedByThisFeature()
    {
        // The compatibility guard. An unpainted object must hand generation exactly the face set
        // it always got: every face, in the same set, so results are bit-identical.
        var (_, box) = FloatingBox();

        Assert.True(box.Regions.IsEmpty);
        Assert.Equal(
            Enumerable.Range(0, box.Mesh.TriangleCount).ToHashSet(),
            box.Regions.EffectiveFaces(box.Mesh.TriangleCount));
    }

    [Fact]
    public void GeneratingWithNoRegionMatchesGeneratingWithEveryFacePaintedExplicitly()
    {
        // The stronger form of the compatibility guard: run generation both ways and compare the
        // actual output, not just the face set handed in. If the region plumbing perturbed
        // anything — ordering, sampling, seeding — this catches it.
        var (unpainted, plainBox) = FloatingBox();
        var (painted, paintedBox) = FloatingBox();
        painted.SetSupportRegions(paintedBox, ObjectSupportRegions.From(
            Enumerable.Range(0, paintedBox.Mesh.TriangleCount), null));

        var a = Document.ComputeSupportGeneration(unpainted.CaptureSupportGeneration(plainBox));
        var b = Document.ComputeSupportGeneration(painted.CaptureSupportGeneration(paintedBox));

        Assert.NotEmpty(a.Nodes);
        Assert.Equal(a.Nodes.Count, b.Nodes.Count);
        Assert.Equal(a.Segments.Count, b.Segments.Count);
        foreach (var (left, right) in a.Nodes.Zip(b.Nodes))
        {
            Assert.Equal(left.Type, right.Type);
            Assert.Equal(left.Position, right.Position);
        }
    }

    [Fact]
    public void MirroringKeepsFaceIndicesMeaningfulSoTheRegionSurvives()
    {
        // Mirror bakes a reflected mesh, which would invalidate face indices if it reordered
        // triangles — it does not: Mesh.Reflected keeps triangle N as triangle N and only swaps
        // the winding inside it. That is what makes a painted region survive a mirror, and it is
        // the same reason task 03 lets mirror keep its supports.
        var (document, box) = FloatingBox();
        var painted = ObjectSupportRegions.From([0, 1], [2]);
        document.SetSupportRegions(box, painted);
        var trianglesBefore = box.Mesh.TriangleCount;

        document.Select(box);
        document.MirrorSelection(ObjectMirrorAxis.X);

        Assert.Equal(trianglesBefore, box.Mesh.TriangleCount);
        Assert.Equal(painted, box.Regions);
    }

    [Fact]
    public void TheCapturedGenerationRequestCarriesTheObjectsRegion()
    {
        var (document, box) = FloatingBox();
        var painted = ObjectSupportRegions.From([3, 4], [4]);
        document.SetSupportRegions(box, painted);

        var request = document.CaptureSupportGeneration(box);

        Assert.Equal(painted, request.Regions);
        // Captured by value, so painting after the capture cannot change a running generation.
        document.ClearSupportRegions(box);
        Assert.Equal(painted, request.Regions);
    }

    [Fact]
    public void GenerationPlacesContactsOnlyOnFacesInTheRegion()
    {
        var (document, box) = FloatingBox();
        // The box's underside is the first quad: triangles 0 and 1. Restrict the region to one of
        // them and every contact must land on it.
        document.SetSupportRegions(box, ObjectSupportRegions.From([0], null));

        var request = document.CaptureSupportGeneration(box);
        var prepared = Document.ComputeSupportGeneration(request);

        Assert.NotEmpty(prepared.Nodes);
        var worldMesh = box.Mesh; // PlacementMode.Off and an identity transform: local == world
        var triangle = TriangleCorners(worldMesh, 0);
        foreach (var tip in prepared.Nodes.Where(n => n.Type == SupportNodeType.Tip))
            Assert.True(WithinTriangleXY(tip.Position, triangle),
                $"tip at {tip.Position} is outside the painted face");
    }

    [Fact]
    public void GenerationHonoursKeepCleanByPlacingNothingOnThoseFaces()
    {
        var (document, box) = FloatingBox();
        // Whole underside painted, then half of it marked keep-clean: contacts must avoid it.
        document.SetSupportRegions(box, ObjectSupportRegions.From([0, 1], [1]));

        var prepared = Document.ComputeSupportGeneration(document.CaptureSupportGeneration(box));

        var forbidden = TriangleCorners(box.Mesh, 1);
        foreach (var tip in prepared.Nodes.Where(n => n.Type == SupportNodeType.Tip))
            Assert.False(StrictlyInsideTriangleXY(tip.Position, forbidden),
                $"tip at {tip.Position} landed on a keep-clean face");
    }

    private static (Vector3 A, Vector3 B, Vector3 C) TriangleCorners(Mesh mesh, int face) =>
        (mesh.Positions[mesh.Indices[face * 3]],
            mesh.Positions[mesh.Indices[face * 3 + 1]],
            mesh.Positions[mesh.Indices[face * 3 + 2]]);

    /// <summary>Barycentric containment in XY, with a small tolerance for the shared edge.</summary>
    private static bool WithinTriangleXY(Vector3 point, (Vector3 A, Vector3 B, Vector3 C) tri,
        float tolerance = 1e-3f)
    {
        var (u, v, w) = Barycentric(point, tri);
        return u >= -tolerance && v >= -tolerance && w >= -tolerance;
    }

    private static bool StrictlyInsideTriangleXY(Vector3 point, (Vector3 A, Vector3 B, Vector3 C) tri)
    {
        var (u, v, w) = Barycentric(point, tri);
        const float inset = 1e-3f; // a point on the shared edge belongs to both triangles
        return u > inset && v > inset && w > inset;
    }

    private static (float U, float V, float W) Barycentric(Vector3 p,
        (Vector3 A, Vector3 B, Vector3 C) tri)
    {
        var (a, b, c) = tri;
        var area = (b.X - a.X) * (c.Y - a.Y) - (c.X - a.X) * (b.Y - a.Y);
        if (MathF.Abs(area) < 1e-12f) return (-1, -1, -1);
        var u = ((b.X - p.X) * (c.Y - p.Y) - (c.X - p.X) * (b.Y - p.Y)) / area;
        var v = ((c.X - p.X) * (a.Y - p.Y) - (a.X - p.X) * (c.Y - p.Y)) / area;
        return (u, v, 1f - u - v);
    }

    // ----- Overlay geometry -----

    [Fact]
    public void TheOverlayProducesGeometryForExactlyThePaintedFaces()
    {
        var mesh = Box(new Vector3(0, 0, 0), new Vector3(1, 1, 1));

        var overlay = SupportRegionOverlay.Build(mesh, Matrix4x4.Identity,
            new HashSet<int> { 0, 3 });

        Assert.NotNull(overlay);
        Assert.Equal(2, overlay!.TriangleCount);
    }

    [Fact]
    public void TheOverlayIsBuiltInWorldSpace()
    {
        var mesh = Box(new Vector3(0, 0, 0), new Vector3(1, 1, 1));
        var transform = Matrix4x4.CreateTranslation(new Vector3(10, 0, 0));

        var overlay = SupportRegionOverlay.Build(mesh, transform, new HashSet<int> { 0 });

        Assert.NotNull(overlay);
        Assert.All(overlay!.Positions, p => Assert.InRange(p.X, 10f, 11f));
    }

    [Fact]
    public void AnEmptyOrStaleRegionProducesNoOverlayRatherThanThrowing()
    {
        var mesh = Box(new Vector3(0, 0, 0), new Vector3(1, 1, 1));

        Assert.Null(SupportRegionOverlay.Build(mesh, Matrix4x4.Identity, new HashSet<int>()));
        Assert.Null(SupportRegionOverlay.Build(mesh, Matrix4x4.Identity, new HashSet<int> { 500 }));
    }

    [Fact]
    public void KeepCleanGetsItsOwnColourAndDrawsOverTheRegion()
    {
        var mesh = Box(new Vector3(0, 0, 0), new Vector3(1, 1, 1));
        var regions = ObjectSupportRegions.From([0, 1], [1]);

        var layers = SupportRegionOverlay.Build(mesh, Matrix4x4.Identity, regions);

        Assert.Equal(2, layers.Count);
        Assert.Equal(SupportRegionOverlay.RegionColor, layers[0].Color);
        Assert.Equal(SupportRegionOverlay.KeepCleanColor, layers[1].Color); // second = on top
        Assert.NotEqual(SupportRegionOverlay.RegionColor, SupportRegionOverlay.KeepCleanColor);
    }

    [Fact]
    public void AnUnpaintedObjectContributesNoOverlayAtAll()
    {
        var mesh = Box(new Vector3(0, 0, 0), new Vector3(1, 1, 1));

        Assert.Empty(SupportRegionOverlay.Build(mesh, Matrix4x4.Identity, ObjectSupportRegions.Empty));
    }
}
