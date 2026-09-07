using System.Numerics;
using Danslicer.Core.Geometry;
using Danslicer.Core.Supports;
using Danslicer.Core.Supports.Generation;
using Danslicer.Core.Supports.Guided;

namespace Danslicer.Tests;

/// <summary>
/// Polygon fill (SUPPORT-GEOMETRY-SPEC "Guided tip placement"): the ring a loop lays over the
/// surface, the faces it encloses, the flat inside test, and the gesture that fills the region
/// with boundary and grid tips.
/// </summary>
public sealed class GuidedPolygonTests
{
    /// <summary>
    /// A flat n×n grid of quads on z = 0, wound so every face points down, as an underside does.
    /// Cell (i, j) owns faces 2·(j·n + i) and 2·(j·n + i) + 1; vertices are welded.
    /// </summary>
    private static Mesh DownwardGrid(int n, float cell)
    {
        var positions = new Vector3[(n + 1) * (n + 1)];
        for (var j = 0; j <= n; j++)
            for (var i = 0; i <= n; i++)
                positions[j * (n + 1) + i] = new Vector3(i * cell, j * cell, 0);
        var indices = new List<int>();
        for (var j = 0; j < n; j++)
            for (var i = 0; i < n; i++)
            {
                int a = j * (n + 1) + i, b = a + 1, c = a + n + 1, d = c + 1;
                indices.AddRange([a, c, b, b, c, d]); // clockwise seen from above: normal -Z
            }
        return new Mesh(positions, indices.ToArray());
    }

    private static IEnumerable<int> CellFaces(int n, int i, int j) => [2 * (j * n + i), 2 * (j * n + i) + 1];

    private static TipPlacementParameters Parameters(float spacing) => TipPlacementParameters.Default with
    {
        SpacingMm = spacing, MinSpacingMm = spacing, TipShape = SupportTipShape.Cone,
    };

    [Fact]
    public void GridMeshFacesPointDown()
    {
        var mesh = DownwardGrid(3, 1f);
        Assert.All(mesh.FaceNormals, n => Assert.True(n.Z < -0.99f));
    }

    [Fact]
    public void EnclosedFacesIsTheSmallerSideOfTheRingPlusTheRing()
    {
        const int n = 5;
        var mesh = DownwardGrid(n, 1f);
        var ring = new HashSet<int>();
        for (var j = 1; j <= 3; j++)
            for (var i = 1; i <= 3; i++)
                if (i != 2 || j != 2) ring.UnionWith(CellFaces(n, i, j));

        var enclosed = SurfacePolygon.EnclosedFaces(mesh, ring);

        var expected = new HashSet<int>(ring);
        expected.UnionWith(CellFaces(n, 2, 2));
        Assert.Equal(expected.OrderBy(f => f), enclosed.OrderBy(f => f));
    }

    [Fact]
    public void RingThatCutsNothingOffEnclosesOnlyItself()
    {
        const int n = 5;
        var mesh = DownwardGrid(n, 1f);
        var ring = new HashSet<int>(CellFaces(n, 2, 2));

        var enclosed = SurfacePolygon.EnclosedFaces(mesh, ring);

        Assert.Equal(ring.OrderBy(f => f), enclosed.OrderBy(f => f));
    }

    [Fact]
    public void InsideLoopTestsAgainstTheFlattenedPolygon()
    {
        var square = new List<SurfacePath>
        {
            SurfacePath.Chord(new Vector3(0, 0, 0), 0, new Vector3(4, 0, 0), 0),
            SurfacePath.Chord(new Vector3(4, 0, 0), 0, new Vector3(4, 4, 0), 0),
            SurfacePath.Chord(new Vector3(4, 4, 0), 0, new Vector3(0, 4, 0), 0),
            SurfacePath.Chord(new Vector3(0, 4, 0), 0, new Vector3(0, 0, 0), 0),
        };

        var inside = SurfacePolygon.InsideLoop(square);

        Assert.True(inside(new Vector3(2, 2, 0)));
        Assert.True(inside(new Vector3(4, 2, 0)));   // on the boundary counts
        Assert.False(inside(new Vector3(5, 2, 0)));
        Assert.False(inside(new Vector3(-1, -1, 0)));
    }

    [Fact]
    public void DegenerateLoopContainsNothing()
    {
        var line = new List<SurfacePath>
        {
            SurfacePath.Chord(Vector3.Zero, 0, new Vector3(4, 0, 0), 0),
            SurfacePath.Chord(new Vector3(4, 0, 0), 0, Vector3.Zero, 0),
        };
        Assert.False(SurfacePolygon.InsideLoop(line)(new Vector3(2, 0, 0)));
    }

    [Fact]
    public void PolygonWithTwoCornersPreviewsLikeTheLine()
    {
        var mesh = DownwardGrid(10, 1f);
        var polygon = new SurfacePolygonGesture(mesh, 2f);
        var line = new SurfaceLineGesture(mesh, 2f);
        foreach (IGuidedGesture g in new IGuidedGesture[] { polygon, line })
        {
            g.SetCursor(new Vector3(1.5f, 1.2f, 0), SurfacePath.NearestFace(mesh, new Vector3(1.5f, 1.2f, 0)));
            g.AddVertex();
            g.SetCursor(new Vector3(7.5f, 1.2f, 0), SurfacePath.NearestFace(mesh, new Vector3(7.5f, 1.2f, 0)));
        }

        Assert.Equal(2, polygon.CornerCount);
        Assert.Equal(line.Route().Count, polygon.Route().Count);
        Assert.Equal(line.Preview(Parameters(2f), null).Count, polygon.Preview(Parameters(2f), null).Count);
    }

    [Fact]
    public void ThreeCornersCloseTheLoopAndFillItWithBoundaryAndGridTips()
    {
        var mesh = DownwardGrid(10, 1f);
        var gesture = new SurfacePolygonGesture(mesh, 1f);
        Vector3[] corners = [new(1.5f, 1.5f, 0), new(8.5f, 1.5f, 0), new(8.5f, 8.5f, 0)];
        foreach (var corner in corners[..2])
        {
            gesture.SetCursor(corner, SurfacePath.NearestFace(mesh, corner));
            Assert.True(gesture.AddVertex());
        }
        gesture.SetCursor(corners[2], SurfacePath.NearestFace(mesh, corners[2]));

        Assert.Equal(3, gesture.CornerCount);
        var route = gesture.Route();
        Assert.Equal(3, route.Count);
        Assert.Equal(corners[0], route[^1].Points[^1]); // the closing edge returns to the first corner
        Assert.False(gesture.ClosingPathIsChord);

        var tips = gesture.Preview(Parameters(1f), null);
        var inside = SurfacePolygon.InsideLoop(route);
        Assert.All(tips, t => Assert.True(inside(t.Point), $"{t.Point} is outside the triangle"));
        Assert.All(tips, t => Assert.Equal(Vector3.UnitZ, t.InwardNormal));
        // Boundary alone is the perimeter at pitch 1 (7 + 7 + ~9.9); the fill adds interior tips.
        var boundaryOnly = SurfacePath.SampleAtPitch(route, 1f, 1f).Count;
        Assert.True(tips.Count > boundaryOnly, $"{tips.Count} tips is no more than the {boundaryOnly} on the boundary");
        Assert.Contains(tips, t => Vector3.Distance(t.Point, new Vector3(6, 3, 0)) < 0.75f);
        // Nothing outside the triangle, even though the enclosed faces cover more than it.
        Assert.DoesNotContain(tips, t => t.Point.X < t.Point.Y - 1e-3f);
    }

    [Fact]
    public void BackspaceReopensTheLoop()
    {
        var mesh = DownwardGrid(10, 1f);
        var gesture = new SurfacePolygonGesture(mesh, 1f);
        foreach (var corner in new Vector3[] { new(1.5f, 1.5f, 0), new(8.5f, 1.5f, 0), new(8.5f, 8.5f, 0) })
        {
            gesture.SetCursor(corner, SurfacePath.NearestFace(mesh, corner));
            gesture.AddVertex();
        }
        gesture.ClearCursor();
        Assert.Equal(3, gesture.Route().Count);

        Assert.True(gesture.RemoveLastVertex());

        Assert.Equal(2, gesture.CornerCount);
        Assert.Single(gesture.Route());
    }
}
