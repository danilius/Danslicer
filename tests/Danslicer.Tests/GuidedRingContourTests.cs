using System.Numerics;
using Danslicer.Core.Geometry;
using Danslicer.Core.Supports;
using Danslicer.Core.Supports.Generation;
using Danslicer.Core.Supports.Guided;

namespace Danslicer.Tests;

/// <summary>
/// Ring and contour (SUPPORT-GEOMETRY-SPEC "Guided tip placement"): a ring hugging the surface
/// around a centre, and the downward contour at a height chained into runs.
/// </summary>
public sealed class GuidedRingContourTests
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

    /// <summary>A square pyramid point-down: four faces all facing down and outward.</summary>
    private static Mesh InvertedPyramid()
    {
        Vector3 apex = new(0, 0, 0);
        Vector3[] rim = [new(-10, -10, 10), new(10, -10, 10), new(10, 10, 10), new(-10, 10, 10)];
        var positions = new[] { apex }.Concat(rim).ToArray();
        // Wound so every normal has a negative Z (checked by a test below).
        int[] indices = [0, 2, 1, 0, 3, 2, 0, 4, 3, 0, 1, 4];
        return new Mesh(positions, indices);
    }

    private static TipPlacementParameters Parameters(float spacing) => TipPlacementParameters.Default with
    {
        SpacingMm = spacing, MinSpacingMm = spacing, TipShape = SupportTipShape.Cone,
    };

    // ----- Ring -----

    [Fact]
    public void RingOnAFlatUndersideIsACircleOfTipsAtPitch()
    {
        var mesh = Box(new Vector3(-20, -20, 0), new Vector3(20, 20, 10));
        var gesture = new SurfaceRingGesture(mesh, 2f);
        gesture.SetCursor(new Vector3(0, 0, 0), 1);
        Assert.False(gesture.ReadyToPlace);
        Assert.True(gesture.AddVertex());              // centre
        gesture.SetCursor(new Vector3(6, 0, 0), 1);    // radius 6
        Assert.True(gesture.ReadyToPlace);
        Assert.Equal(6f, gesture.RadiusMm, 3);

        var tips = gesture.Preview(Parameters(2f), null);

        // Circumference 37.7 at pitch 2 → 18 tips around, plus none at the centre.
        Assert.InRange(tips.Count, 17, 19);
        // Tips sit on the ring's chords, a hair inside the true radius.
        Assert.All(tips, t => Assert.InRange(new Vector2(t.Point.X, t.Point.Y).Length(), 5.95f, 6.0001f));
        Assert.All(tips, t => Assert.Equal(0f, t.Point.Z, 3));
        Assert.All(tips, t => Assert.Equal(Vector3.UnitZ, t.InwardNormal));
        Assert.True(gesture.AddVertex());              // the placing click
    }

    [Fact]
    public void RingProjectsOntoACurvedSurface()
    {
        var mesh = InvertedPyramid();
        Assert.All(mesh.FaceNormals, n => Assert.True(n.Z < 0));
        var gesture = new SurfaceRingGesture(mesh, 1f);
        var centre = new Vector3(0, -3, 3); // on the y = -x... the -Y face, halfway down
        var face = SurfacePath.NearestFace(mesh, centre);
        gesture.SetCursor(centre, face);
        gesture.AddVertex();
        gesture.SetCursor(new Vector3(4, -3, 3), face);

        var route = Assert.Single(gesture.Route());
        var bvh = MeshAnalysis.For(mesh).Bvh;
        Assert.All(route.Points, p => Assert.True(bvh.ClosestPoint(p, out _, out _) < 1e-3f, $"{p} is off the surface"));
        Assert.True(route.Points.Count > 24);
        Assert.Equal(route.Points[0], route.Points[^1]);
        // The ring crosses the crease onto the neighbouring faces and follows them.
        Assert.True(route.Faces.Distinct().Count() > 1);
    }

    [Fact]
    public void RingBackspaceReturnsToChoosingACentre()
    {
        var mesh = Box(new Vector3(-20, -20, 0), new Vector3(20, 20, 10));
        var gesture = new SurfaceRingGesture(mesh, 2f);
        gesture.SetCursor(new Vector3(0, 0, 0), 1);
        gesture.AddVertex();
        gesture.SetCursor(new Vector3(6, 0, 0), 1);

        Assert.True(gesture.RemoveLastVertex());

        Assert.False(gesture.HasVertices);
        Assert.Empty(gesture.Route());
        Assert.False(gesture.ReadyToPlace);
    }

    [Fact]
    public void RingTooSmallForAPitchIsNotAPlaceableRing()
    {
        var mesh = Box(new Vector3(-20, -20, 0), new Vector3(20, 20, 10));
        var gesture = new SurfaceRingGesture(mesh, 2f);
        gesture.SetCursor(new Vector3(0, 0, 0), 1);
        gesture.AddVertex();
        gesture.SetCursor(new Vector3(0.5f, 0, 0), 1);

        Assert.False(gesture.ReadyToPlace);
        var only = Assert.Single(gesture.Preview(Parameters(2f), null));
        Assert.Equal(Vector3.Zero, only.Point);
    }

    // ----- Contour -----

    [Fact]
    public void ContourOfAnInvertedPyramidIsOneClosedSquare()
    {
        var mesh = InvertedPyramid();

        var runs = SurfaceContour.AtHeight(mesh, 5f);

        var run = Assert.Single(runs);
        Assert.Equal(run.Points[0], run.Points[^1]);
        Assert.Equal(40f, run.Length, 2);            // a 10 × 10 square at z = 5
        Assert.All(run.Points, p => Assert.Equal(5f, p.Z, 4));
        Assert.All(run.Points, p => Assert.Equal(5f, MathF.Max(MathF.Abs(p.X), MathF.Abs(p.Y)), 3));
    }

    [Fact]
    public void ContourSkipsVerticalWallsAndTopFaces()
    {
        // A box's walls are vertical and its top faces up: nothing at mid height faces down.
        var mesh = Box(new Vector3(-5, -5, 0), new Vector3(5, 5, 10));
        Assert.Empty(SurfaceContour.AtHeight(mesh, 5f));
    }

    [Fact]
    public void ContourOutsideTheMeshIsEmpty()
    {
        Assert.Empty(SurfaceContour.AtHeight(InvertedPyramid(), 20f));
    }

    [Fact]
    public void ContourGestureTakesTheCursorHeightAndPlacesOnClick()
    {
        var mesh = InvertedPyramid();
        var gesture = new ContourGesture(mesh, 2f);
        Assert.False(gesture.ReadyToPlace);

        var cursor = new Vector3(0, -5, 5);
        gesture.SetCursor(cursor, SurfacePath.NearestFace(mesh, cursor));

        Assert.True(gesture.ReadyToPlace);
        Assert.Equal(5f, gesture.HeightMm!.Value, 4);
        var tips = gesture.Preview(Parameters(2f), null);
        Assert.Equal(20, tips.Count);                 // 40 mm around at pitch 2, start not doubled
        Assert.All(tips, t => Assert.Equal(5f, t.Point.Z, 4));
        Assert.All(tips, t => Assert.True(t.InwardNormal.Z > 0));
        Assert.True(gesture.AddVertex());

        gesture.ClearCursor();
        Assert.False(gesture.ReadyToPlace);
        Assert.Empty(gesture.Preview(Parameters(2f), null));
    }
}
