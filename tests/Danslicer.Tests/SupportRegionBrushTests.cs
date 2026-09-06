using System.Numerics;
using Danslicer.App.ViewModels;
using Danslicer.Core.Geometry;
using Danslicer.Core.Scene;
using Danslicer.Core.Supports;
using Xunit;

namespace Danslicer.Tests;

/// <summary>
/// DESIGN 8.3 stage 3. The maths of "which faces does a dab cover" and the promise that a whole
/// stroke is one undo step. How the brush FEELS under the cursor is not testable here and has not
/// been checked — see the note in the commit.
/// </summary>
public class SupportRegionBrushTests
{
    /// <summary>
    /// A flat strip of 2n triangles along +X: quad k spans x in [k, k+1], y in [0, 1]. Face 2k and
    /// 2k+1 are its two triangles, so the faces a brush covers can be counted by hand.
    /// </summary>
    private static Mesh Strip(int quads)
    {
        var positions = new List<Vector3>();
        var indices = new List<int>();
        for (var i = 0; i <= quads; i++)
        {
            positions.Add(new Vector3(i, 0, 0));
            positions.Add(new Vector3(i, 1, 0));
        }
        for (var k = 0; k < quads; k++)
        {
            int a = k * 2, b = k * 2 + 1, c = k * 2 + 2, d = k * 2 + 3;
            indices.AddRange([a, c, b]);
            indices.AddRange([b, c, d]);
        }
        return new Mesh(positions.ToArray(), indices.ToArray());
    }

    /// <summary>Two parallel strips 0.2 mm apart: a thin wall, for the bleed-through case.</summary>
    private static Mesh ThinWall()
    {
        var near = Strip(4);
        var far = Strip(4);
        var positions = near.Positions
            .Concat(far.Positions.Select(p => p with { Z = 0.2f }))
            .ToArray();
        var indices = near.Indices
            .Concat(far.Indices.Select(i => i + near.VertexCount))
            .ToArray();
        return new Mesh(positions, indices);
    }

    [Fact]
    public void ADabCoversTheFacesWithinItsRadius()
    {
        var mesh = Strip(6);

        // Centred on the middle of quad 0, a 1 mm brush reaches quad 1 (whose nearest point is
        // 0.5 mm away) but not quad 2 (1.5 mm away).
        var faces = SupportRegionBrush.FacesWithin(mesh, new Vector3(0.5f, 0.5f, 0), 1f, seedFace: 0);

        Assert.Equal([0, 1, 2, 3], faces);
    }

    [Fact]
    public void ABiggerRadiusCoversMore()
    {
        var mesh = Strip(6);
        var centre = new Vector3(0.5f, 0.5f, 0);

        var small = SupportRegionBrush.FacesWithin(mesh, centre, 0.4f, 0);
        var large = SupportRegionBrush.FacesWithin(mesh, centre, 2.5f, 0);

        Assert.Equal([0, 1], small);          // just the quad under the cursor
        Assert.True(large.Count > small.Count);
        Assert.Subset(large.ToHashSet(), small.ToHashSet());
    }

    [Fact]
    public void TheSeedFaceIsAlwaysPaintedHoweverSmallTheBrush()
    {
        var mesh = Strip(4);

        // The quad centre sits exactly on the diagonal, so its neighbour is zero distance away and
        // joins even a zero-radius dab. Away from the diagonal, only the seed is painted.
        Assert.Equal([2, 3], SupportRegionBrush.FacesWithin(mesh, new Vector3(1.5f, 0.5f, 0), 0f, seedFace: 3));
        Assert.Equal([3], SupportRegionBrush.FacesWithin(mesh, new Vector3(1.9f, 0.9f, 0), 0f, seedFace: 3));
    }

    [Fact]
    public void ADabDoesNotBleedThroughAThinWall()
    {
        // The far strip is 0.2 mm away — well inside a 2 mm brush — but is not edge-connected to
        // the near one, so it stays clean. This is the case the connectivity rule exists for.
        var mesh = ThinWall();
        var nearFaces = Strip(4).TriangleCount;

        var faces = SupportRegionBrush.FacesWithin(mesh, new Vector3(2f, 0.5f, 0), 2f, seedFace: 0);

        Assert.NotEmpty(faces);
        Assert.All(faces, f => Assert.True(f < nearFaces, $"face {f} is on the far side of the wall"));
    }

    [Fact]
    public void AnOutOfRangeSeedPaintsNothing()
    {
        var mesh = Strip(2);

        Assert.Empty(SupportRegionBrush.FacesWithin(mesh, Vector3.Zero, 10f, seedFace: 99));
    }

    // ----- Strokes -----

    [Fact]
    public void AStrokeIsOneUndoStepAndUndoRestoresExactlyThePriorRegion()
    {
        var (viewModel, box) = Scene();
        viewModel.RegionBrushRadiusMm = 0.4;
        // Something painted beforehand, so "restores the prior region" means more than "empties".
        viewModel.PaintRegionFromFace(box, triangle: 0, erase: false);
        var before = box.Regions.Faces.ToHashSet();
        var undoDepth = UndoDepth(viewModel);

        viewModel.BeginStroke(box, erase: false);
        for (var i = 0; i < 20; i++) viewModel.BrushStroke(new Vector3(0.5f, 0.5f, 1f), triangle: 2);
        viewModel.EndStroke();

        Assert.True(box.Regions.Faces.Count > before.Count);
        Assert.Equal(undoDepth + 1, UndoDepth(viewModel)); // one step for the whole stroke

        Assert.True(viewModel.Document.Undo());
        Assert.Equal(before.OrderBy(f => f), box.Regions.Faces.OrderBy(f => f));
    }

    [Fact]
    public void ErasingRemovesExactlyWhatTheSameStrokePainted()
    {
        var (viewModel, box) = Scene();
        viewModel.RegionBrushRadiusMm = 0.3;
        var centre = new Vector3(0.5f, 0.5f, 1f);

        viewModel.BeginStroke(box, erase: false);
        viewModel.BrushStroke(centre, triangle: 2);
        viewModel.EndStroke();
        var painted = box.Regions.Faces.ToHashSet();
        Assert.NotEmpty(painted);

        viewModel.BeginStroke(box, erase: true);
        viewModel.BrushStroke(centre, triangle: 2);
        viewModel.EndStroke();

        Assert.Empty(box.Regions.Faces);
    }

    [Fact]
    public void AStrokeThatPaintsNothingNewAddsNoUndoStep()
    {
        var (viewModel, box) = Scene();
        viewModel.RegionBrushRadiusMm = 0.3;
        viewModel.BeginStroke(box, erase: true); // erasing an unpainted region changes nothing
        viewModel.BrushStroke(new Vector3(0.5f, 0.5f, 1f), triangle: 2);
        var undoDepth = UndoDepth(viewModel);

        viewModel.EndStroke();

        Assert.Equal(undoDepth, UndoDepth(viewModel));
    }

    [Fact]
    public void ADabOutsideAStrokeIsIgnoredRatherThanThrowing()
    {
        var (viewModel, box) = Scene();

        viewModel.BrushStroke(new Vector3(0.5f, 0.5f, 1f), triangle: 2);
        viewModel.EndStroke();

        Assert.Empty(box.Regions.Faces);
    }

    private static int UndoDepth(MainViewModel viewModel)
    {
        var depth = 0;
        while (viewModel.Document.Undo()) depth++;
        for (var i = 0; i < depth; i++) viewModel.Document.Redo();
        return depth;
    }

    private static (MainViewModel ViewModel, SceneObject Box) Scene()
    {
        var viewModel = new MainViewModel();
        var box = new SceneObject("box", Box(new Vector3(0, 0, 0), new Vector3(1, 1, 1)));
        viewModel.Document.AddObject(box);
        viewModel.SelectedObject = box;
        return (viewModel, box);
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
}
