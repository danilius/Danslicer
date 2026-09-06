using System.Numerics;
using Danslicer.App.ViewModels;
using Danslicer.Core.Geometry;
using Danslicer.Core.Scene;
using Danslicer.Core.Supports;
using Danslicer.Core.Supports.Generation;
using Xunit;

namespace Danslicer.Tests;

/// <summary>
/// DESIGN 8.3 stage 2. Every mesh here is small enough that the right answer can be worked out
/// by hand, which is the point: these operations decide what the user's brush and click actually
/// select, so "it looked right in the viewport" is not evidence.
/// </summary>
public class SupportRegionSelectionTests
{
    /// <summary>
    /// Two triangles sharing the edge (1,2), the second folded up by <paramref name="foldDegrees"/>
    /// about that edge. Face 0 is flat; face 1 turns away from it by exactly that angle.
    /// </summary>
    private static Mesh Hinge(float foldDegrees)
    {
        var radians = foldDegrees * MathF.PI / 180f;
        Vector3[] p =
        [
            new(0, 0, 0),   // 0
            new(1, 0, 0),   // 1
            new(1, 1, 0),   // 2 — shared edge runs from 1 to 2
            new(2, 0, MathF.Sin(radians) * 0), // placeholder, replaced below
        ];
        // The far vertex of the second triangle, rotated about the shared edge (the +Y axis
        // through x = 1) by the fold angle.
        p[3] = new Vector3(1 + MathF.Cos(radians), 0, -MathF.Sin(radians));
        int[] indices = [0, 1, 2, 1, 3, 2];
        return new Mesh(p, indices);
    }

    /// <summary>A closed box: 12 triangles, 2 per face, every dihedral 90°.</summary>
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

    /// <summary>Two boxes far apart in one mesh: 24 triangles in two disconnected components.</summary>
    private static Mesh TwoBoxes()
    {
        var a = Box(new Vector3(0, 0, 0), new Vector3(1, 1, 1));
        var b = Box(new Vector3(10, 0, 0), new Vector3(11, 1, 1));
        var positions = a.Positions.Concat(b.Positions).ToArray();
        var indices = a.Indices.Concat(b.Indices.Select(i => i + a.VertexCount)).ToArray();
        return new Mesh(positions, indices);
    }

    // ----- Dihedral growth -----

    [Fact]
    public void GrowthCrossesAnEdgeBelowTheThresholdAndStopsAboveIt()
    {
        var mesh = Hinge(40f);

        // 40° fold: a 45° threshold crosses it, a 30° one does not.
        Assert.Equal([0, 1], SupportRegionSelection.GrowByDihedral(mesh, [0], 45f));
        Assert.Equal([0], SupportRegionSelection.GrowByDihedral(mesh, [0], 30f));
    }

    [Fact]
    public void TheThresholdIsALiveArgumentNotRememberedState()
    {
        // The same mesh and seed, walked up and back down through the fold angle, must give the
        // same answer each time it passes the same threshold — that is what makes a slider safe.
        var mesh = Hinge(40f);

        foreach (var angle in new[] { 10f, 39f, 41f, 90f, 41f, 39f, 10f })
        {
            var expected = angle > 40f ? new[] { 0, 1 } : [0];
            Assert.Equal(expected, SupportRegionSelection.GrowByDihedral(mesh, [0], angle));
        }
    }

    [Fact]
    public void ClickingAPlanarPatchOfABoxSelectsThatFaceOnly()
    {
        // Every dihedral on a box is 90°, so a patch threshold keeps to the two triangles of the
        // clicked side.
        var mesh = Box(new Vector3(0, 0, 0), new Vector3(1, 1, 1));

        var patch = SupportRegionSelection.GrowByDihedral(mesh, [0], MeshAnalysis.DefaultPlanarDegrees);

        Assert.Equal([0, 1], patch); // the two triangles of the bottom face
    }

    [Fact]
    public void SeedsOutsideTheMeshAreIgnoredRatherThanThrowing()
    {
        var mesh = Box(new Vector3(0, 0, 0), new Vector3(1, 1, 1));

        Assert.Equal([0, 1], SupportRegionSelection.GrowByDihedral(mesh, [0, -1, 999], 1f));
    }

    // ----- Facing down -----

    [Fact]
    public void FacingDownFollowsGenerationsStrictGreaterThanConvention()
    {
        // A hinge folded by N degrees from flat-down gives face 1 an overhang angle of exactly
        // 90 - N; face 0 points straight down, i.e. 90°.
        var mesh = Hinge(40f);
        var faceOne = TipPlacementParameters.OverhangDegrees(mesh.FaceNormals[1]);

        // Just below the face's own angle: selected. Exactly at it: NOT selected (strict >).
        Assert.Contains(1, SupportRegionSelection.FacingDown(mesh, faceOne - 0.5f));
        Assert.DoesNotContain(1, SupportRegionSelection.FacingDown(mesh, faceOne));
        Assert.DoesNotContain(1, SupportRegionSelection.FacingDown(mesh, faceOne + 0.5f));
    }

    [Fact]
    public void FacingDownPicksTheUndersideOfABoxAndNothingElse()
    {
        var mesh = Box(new Vector3(0, 0, 0), new Vector3(1, 1, 1));

        var down = SupportRegionSelection.FacingDown(mesh, 45f);

        Assert.Equal([0, 1], down); // the bottom face; walls are 0°, the top faces up
    }

    [Fact]
    public void FacingDownCanBeLimitedToTheCurrentSelection()
    {
        var mesh = Box(new Vector3(0, 0, 0), new Vector3(1, 1, 1));
        var limit = new HashSet<int> { 1, 4, 5 };

        var down = SupportRegionSelection.FacingDown(mesh, 45f, limit);

        Assert.Equal([1], down); // face 0 is downward too, but outside the limit
    }

    [Fact]
    public void FacingDownAsksAboutTheModelsCurrentOrientationNotItsMeshsIdea()
    {
        // The screen-test failure: a rotated plate-shaped part selected its EDGES, because the
        // mesh's own normals still pointed the way they did before the part was turned. Facing
        // down is a question about gravity, so the object's transform has to be in it.
        var mesh = Box(new Vector3(0, 0, 0), new Vector3(4, 4, 1)); // a flat slab: 0,1 face down

        var upright = SupportRegionSelection.FacingDown(mesh, Matrix4x4.Identity, 45f);
        Assert.Equal([0, 1], upright);

        // Turn it on its side: the faces that now point down are the ones that were a wall.
        var onItsSide = Matrix4x4.CreateFromAxisAngle(Vector3.UnitX, MathF.PI / 2f);
        var turned = SupportRegionSelection.FacingDown(mesh, onItsSide, 45f);

        Assert.NotEqual(upright, turned);
        Assert.DoesNotContain(0, turned);
        Assert.DoesNotContain(1, turned);
        Assert.NotEmpty(turned);
    }

    [Fact]
    public void TurningAModelUpsideDownSelectsWhatWasItsTop()
    {
        var mesh = Box(new Vector3(0, 0, 0), new Vector3(4, 4, 1));
        var flipped = Matrix4x4.CreateFromAxisAngle(Vector3.UnitX, MathF.PI);

        var down = SupportRegionSelection.FacingDown(mesh, flipped, 45f);

        Assert.Equal([2, 3], down); // the top pair, now underneath
    }

    [Fact]
    public void TheFacingDownCommandUsesTheTargetsTransform()
    {
        var viewModel = new MainViewModel();
        var slab = new SceneObject("slab", Box(new Vector3(0, 0, 0), new Vector3(4, 4, 1)))
        {
            Transform = Transform.Identity with
            {
                Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI),
            },
        };
        viewModel.Document.AddObject(slab);
        viewModel.SelectedObject = slab;

        viewModel.SelectFacingDownRegionCommand.Execute(null);

        Assert.Equal([2, 3], slab.Regions.Faces.OrderBy(f => f));
    }

    // ----- Invert, grow, shrink, connected -----

    [Fact]
    public void InvertTwiceIsIdentity()
    {
        var mesh = Box(new Vector3(0, 0, 0), new Vector3(1, 1, 1));
        var faces = new HashSet<int> { 0, 1, 7 };

        var once = SupportRegionSelection.Invert(mesh, faces);
        var twice = SupportRegionSelection.Invert(mesh, once);

        Assert.Equal(mesh.TriangleCount - faces.Count, once.Count);
        Assert.Equal(faces.OrderBy(f => f), twice.ToList());
    }

    [Fact]
    public void GrowThenShrinkReturnsAnInteriorRegionUnchanged()
    {
        // A box side plus its neighbours: growing adds a ring, shrinking removes exactly that
        // ring again, because every original face is then interior.
        var mesh = Box(new Vector3(0, 0, 0), new Vector3(1, 1, 1));
        var region = SupportRegionSelection.GrowByDihedral(mesh, [0], 1f);

        var grown = SupportRegionSelection.Grow(mesh, region);
        var back = SupportRegionSelection.Shrink(mesh, grown);

        Assert.True(grown.Count > region.Count);
        Assert.Equal(region, back);
    }

    [Fact]
    public void ShrinkEmptiesASetWithNoInterior()
    {
        var mesh = Box(new Vector3(0, 0, 0), new Vector3(1, 1, 1));

        // A single triangle has neighbours outside the set, so nothing is interior.
        Assert.Empty(SupportRegionSelection.Shrink(mesh, new HashSet<int> { 0 }));
        // The whole mesh is all interior and survives intact.
        var all = SupportRegionSelection.Invert(mesh, new HashSet<int>());
        Assert.Equal(all, SupportRegionSelection.Shrink(mesh, all));
    }

    [Fact]
    public void ConnectedTakesOneComponentAndNotADisjointOne()
    {
        var mesh = TwoBoxes();

        var component = SupportRegionSelection.Connected(mesh, [0]);

        Assert.Equal(12, component.Count);            // the first box, whole
        Assert.All(component, f => Assert.True(f < 12));
        Assert.DoesNotContain(12, component);          // and nothing from the second
    }

    // ----- Wiring: one undo step, and which set is edited -----

    [Fact]
    public void ARegionOperationIsOneUndoStep()
    {
        var (viewModel, box) = SupportedScene();

        viewModel.SelectFacingDownRegionCommand.Execute(null);

        Assert.NotEmpty(box.Regions.Faces);
        Assert.True(viewModel.Document.Undo());
        Assert.Empty(box.Regions.Faces);
    }

    [Fact]
    public void OperationsEditTheKeepCleanSetWhenAskedTo()
    {
        var (viewModel, box) = SupportedScene();
        viewModel.EditingKeepCleanRegion = true;

        viewModel.SelectFacingDownRegionCommand.Execute(null);

        Assert.NotEmpty(box.Regions.KeepCleanFaces);
        Assert.Empty(box.Regions.Faces); // the support region is untouched
    }

    [Fact]
    public void InvertingAnUnpaintedObjectPaintsEveryFace()
    {
        // Editing reads an empty set literally; generation reads it as "every face". The two
        // agree on the outcome, which is why this is safe: both mean "support anywhere".
        var (viewModel, box) = SupportedScene();

        viewModel.InvertRegionCommand.Execute(null);

        Assert.Equal(box.Mesh.TriangleCount, box.Regions.Faces.Count);
    }

    [Fact]
    public void PaintingAFaceAddsItsPatchAndShiftClickTakesItBack()
    {
        var (viewModel, box) = SupportedScene();

        viewModel.PaintRegionFromFace(box, triangle: 0, erase: false);
        var painted = box.Regions.Faces.ToHashSet();
        Assert.Equal([0, 1], painted.OrderBy(f => f)); // the clicked side's two triangles

        viewModel.PaintRegionFromFace(box, triangle: 0, erase: true);
        Assert.Empty(box.Regions.Faces);
    }

    [Fact]
    public void PaintingRetargetsTheSupportTargetToTheClickedModel()
    {
        var (viewModel, box) = SupportedScene();
        var other = new SceneObject("other", Box(new Vector3(10, 0, 0), new Vector3(11, 1, 1)));
        viewModel.Document.AddObject(other);
        viewModel.SelectedObject = box;

        viewModel.PaintRegionFromFace(other, triangle: 0, erase: false);

        Assert.Same(other, viewModel.SelectedObject);
        Assert.NotEmpty(other.Regions.Faces);
        Assert.Empty(box.Regions.Faces);
    }

    [Fact]
    public void ThePatchAngleBoxActuallyReachesTheOperation()
    {
        // The regression this exists for: an ExpressionBox bound straight at a double looked like
        // it worked, kept the old value, and a 0 degree patch angle still grew across a 90 degree
        // edge. The boxes go through NumericField like every other number in the app.
        var (viewModel, _) = SupportedScene();

        viewModel.RegionDihedralField.Text = "0";

        Assert.Equal(0, viewModel.RegionDihedralDegrees);
    }

    [Fact]
    public void AZeroPatchAngleTakesOnlyTheClickedFacesOwnPlane()
    {
        var (viewModel, box) = SupportedScene();
        viewModel.RegionDihedralField.Text = "0";

        viewModel.PaintRegionFromFace(box, triangle: 0, erase: false);

        Assert.Equal([0, 1], box.Regions.Faces.OrderBy(f => f)); // the clicked side, not its walls
    }

    [Fact]
    public void TheRegionBoxesRoundTripTheirValues()
    {
        var (viewModel, _) = SupportedScene();

        viewModel.RegionOverhangField.Text = "90";
        viewModel.RegionBrushRadiusField.Text = "36";

        Assert.Equal(90, viewModel.RegionOverhangDegrees);
        Assert.Equal(36, viewModel.RegionBrushRadiusPixels); // screen pixels, not millimetres
        Assert.Equal("90", viewModel.RegionOverhangField.Text);
    }

    [Fact]
    public void TheHoverHighlightIsThePatchAClickWouldPaint()
    {
        var (viewModel, box) = SupportedScene();
        viewModel.RegionPickMode = true;
        viewModel.RegionDihedralField.Text = "1";

        viewModel.HoverRegionFace(box, triangle: 0);

        Assert.NotNull(viewModel.RegionHover);
        Assert.Same(box, viewModel.RegionHover!.Object);
        Assert.Equal([0, 1], viewModel.RegionHover.Faces.OrderBy(f => f));

        // And it is the set the click then takes.
        viewModel.PaintRegionFromFace(box, triangle: 0, erase: false);
        Assert.Equal(viewModel.RegionHover.Faces.OrderBy(f => f), box.Regions.Faces.OrderBy(f => f));
    }

    [Fact]
    public void ChangingThePatchAngleRecomputesTheHighlightWithoutMovingTheCursor()
    {
        var (viewModel, box) = SupportedScene();
        viewModel.RegionPickMode = true;
        viewModel.RegionDihedralField.Text = "1";
        viewModel.HoverRegionFace(box, triangle: 0);
        var narrow = viewModel.RegionHover!.Faces.Count;

        viewModel.RegionDihedralField.Text = "120";

        Assert.True(viewModel.RegionHover!.Faces.Count > narrow);
    }

    [Fact]
    public void TheHighlightDisappearsWhenPaintingIsDisarmedOrTheCursorLeavesTheModel()
    {
        var (viewModel, box) = SupportedScene();
        viewModel.RegionPickMode = true;
        viewModel.HoverRegionFace(box, triangle: 0);
        Assert.NotNull(viewModel.RegionHover);

        viewModel.HoverRegionFace(null, -1);
        Assert.Null(viewModel.RegionHover);

        viewModel.HoverRegionFace(box, triangle: 0);
        viewModel.RegionPickMode = false;
        Assert.Null(viewModel.RegionHover);
    }

    [Fact]
    public void TheBrushHasNoClickHighlight()
    {
        // The brush paints where it is dragged; a patch highlight would promise something else.
        var (viewModel, box) = SupportedScene();
        viewModel.RegionPickMode = true;
        viewModel.RegionBrushMode = true;

        viewModel.HoverRegionFace(box, triangle: 0);

        Assert.Null(viewModel.RegionHover);
    }

    private static (MainViewModel ViewModel, SceneObject Box) SupportedScene()
    {
        var viewModel = new MainViewModel();
        var box = new SceneObject("box", Box(new Vector3(0, 0, 0), new Vector3(1, 1, 1)));
        viewModel.Document.AddObject(box);
        viewModel.SelectedObject = box;
        return (viewModel, box);
    }

    [Fact]
    public void EveryOperationReturnsItsFacesInOrder()
    {
        // Face sets are persisted, so the order must not depend on hash iteration.
        var mesh = Box(new Vector3(0, 0, 0), new Vector3(1, 1, 1));
        var seeds = new HashSet<int> { 9, 2, 5 };

        IReadOnlySet<int>[] results =
        [
            SupportRegionSelection.GrowByDihedral(mesh, seeds, 1f),
            SupportRegionSelection.FacingDown(mesh, 10f),
            SupportRegionSelection.Invert(mesh, seeds),
            SupportRegionSelection.Grow(mesh, seeds),
            SupportRegionSelection.Shrink(mesh, seeds),
            SupportRegionSelection.Connected(mesh, seeds),
        ];

        foreach (var result in results)
            Assert.Equal(result.OrderBy(f => f).ToList(), result.ToList());
    }
}
