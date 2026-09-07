using System.Numerics;
using Danslicer.Core;
using Danslicer.Core.Geometry;
using Danslicer.Core.Scene;
using Danslicer.Core.Supports;
using Danslicer.Core.Supports.Generation;
using Danslicer.Core.Supports.Guided;

namespace Danslicer.Tests;

/// <summary>
/// Guided tip placement (SUPPORT-GEOMETRY-SPEC "Guided tip placement"): the vertical-plane
/// surface path, sampling at pitch, candidate building, the line gesture, and the document's
/// one-undo-step batch placement.
/// </summary>
public sealed class GuidedTipPlacementTests
{
    /// <summary>Welded box: shared vertices, so faces are edge-connected and the walk can cross.</summary>
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
            0, 3, 2, 0, 2, 1, // bottom: face 0 (x <= y half), face 1 (x >= y half)
            4, 5, 6, 4, 6, 7, // top
            0, 1, 5, 0, 5, 4, // y = min side: faces 4, 5
            2, 3, 7, 2, 7, 6, // y = max side
            0, 4, 7, 0, 7, 3, // x = min side
            1, 2, 6, 1, 6, 5, // x = max side
        ];
        return new Mesh(p, indices);
    }

    private static Mesh UnitBox() => Box(new Vector3(-5, -5, 0), new Vector3(5, 5, 10));

    private static TipPlacementParameters Parameters(float spacing = 2.5f) =>
        TipPlacementParameters.Default with
        {
            SpacingMm = spacing, MinSpacingMm = spacing, TipShape = SupportTipShape.Cone,
        };

    private static void AssertOnSurface(Mesh mesh, SurfacePath path)
    {
        for (var i = 0; i < path.Points.Count; i++)
        {
            mesh.GetTriangle(path.Faces[i], out var a, out var b, out var c);
            var nearest = MeshFeatures.ClosestPointOnTriangle(path.Points[i], a, b, c);
            Assert.True(Vector3.Distance(nearest, path.Points[i]) < 1e-4f,
                $"point {i} {path.Points[i]} is off face {path.Faces[i]}");
        }
    }

    // ----- SurfacePath.Between -----

    [Fact]
    public void PathAcrossTwoCoplanarFacesIsTheStraightLine()
    {
        var mesh = UnitBox();
        var a = new Vector3(-4, 4, 0);
        var b = new Vector3(4, -4, 0);

        var path = SurfacePath.Between(mesh, a, 0, b, 1);

        Assert.NotNull(path);
        Assert.Equal(3, path.Points.Count);
        Assert.Equal(a, path.Points[0]);
        Assert.Equal(b, path.Points[^1]);
        Assert.Equal(Vector3.Distance(a, b), path.Length, 3);
        Assert.Equal(new Vector3(0, 0, 0), path.Points[1]);
        AssertOnSurface(mesh, path);
    }

    [Fact]
    public void PathFromUndersideOntoASideFollowsTheSurfaceRoundTheEdge()
    {
        var mesh = UnitBox();
        var a = new Vector3(0, -2, 0);   // bottom, x >= y half
        var b = new Vector3(1, -5, 3);   // y = min side, lower triangle

        var path = SurfacePath.Between(mesh, a, 1, b, 4);

        Assert.NotNull(path);
        Assert.Equal(a, path.Points[0]);
        Assert.Equal(b, path.Points[^1]);
        Assert.Equal(4, path.Faces[^1]);
        // The corner of the walk lies on the bottom edge y = -5, z = 0.
        var corner = path.Points[1];
        Assert.Equal(-5f, corner.Y, 4);
        Assert.Equal(0f, corner.Z, 4);
        Assert.True(path.Length > Vector3.Distance(a, b));
        AssertOnSurface(mesh, path);
    }

    [Fact]
    public void PathBetweenPointsOnOneFaceIsTheChord()
    {
        var mesh = UnitBox();
        var path = SurfacePath.Between(mesh, new Vector3(-4, 4, 0), 0, new Vector3(-4, 1, 0), 0);

        Assert.NotNull(path);
        Assert.Equal(2, path.Points.Count);
        Assert.Equal(3f, path.Length, 4);
    }

    [Fact]
    public void PathBetweenDisconnectedPiecesIsNull()
    {
        // Two triangles that share no vertex: nothing to walk across.
        var mesh = new Mesh(
        [
            new Vector3(0, 0, 0), new(2, 0, 0), new(0, 2, 0),
            new Vector3(5, 0, 0), new(7, 0, 0), new(5, 2, 0),
        ], [0, 1, 2, 3, 4, 5]);

        Assert.Null(SurfacePath.Between(mesh, new Vector3(0.5f, 0.5f, 0), 0, new Vector3(5.5f, 0.5f, 0), 1));
    }

    [Fact]
    public void PathWithABadFaceIndexIsNull()
    {
        var mesh = UnitBox();
        Assert.Null(SurfacePath.Between(mesh, Vector3.Zero, 0, Vector3.One, 99));
    }

    [Fact]
    public void NearestFaceFindsTheFaceUnderAnExistingContact()
    {
        var mesh = UnitBox();
        Assert.Equal(0, SurfacePath.NearestFace(mesh, new Vector3(-4, 4, 0)));
        Assert.Equal(1, SurfacePath.NearestFace(mesh, new Vector3(4, -4, 0.01f)));
        Assert.Equal(4, SurfacePath.NearestFace(mesh, new Vector3(1, -5, 3)));
    }

    // ----- SurfacePath.SampleAtPitch -----

    private static SurfacePath Straight(Vector3 a, Vector3 b) => SurfacePath.Chord(a, 0, b, 0);

    [Fact]
    public void SamplesAtWholePitchesFromTheStart()
    {
        var path = Straight(Vector3.Zero, new Vector3(10, 0, 0));

        var samples = SurfacePath.SampleAtPitch([path], 2.5f, 2.5f);

        Assert.Equal(new[] { 0f, 2.5f, 5f, 7.5f, 10f }, samples.Select(s => s.Point.X).ToArray());
    }

    [Fact]
    public void EndGetsATipOnlyWhenItIsAWholeMinSpacingPastTheLast()
    {
        var shortTail = SurfacePath.SampleAtPitch([Straight(Vector3.Zero, new Vector3(11, 0, 0))], 2.5f, 2.5f);
        var longTail = SurfacePath.SampleAtPitch([Straight(Vector3.Zero, new Vector3(11, 0, 0))], 2.5f, 1f);

        Assert.Equal(10f, shortTail[^1].Point.X, 4);
        Assert.Equal(11f, longTail[^1].Point.X, 4);
    }

    [Fact]
    public void SpacingContinuesAcrossPolylineJoins()
    {
        var first = Straight(Vector3.Zero, new Vector3(3, 0, 0));
        var second = Straight(new Vector3(3, 0, 0), new Vector3(3, 3, 0));

        var samples = SurfacePath.SampleAtPitch([first, second], 2f, 2f);

        // Arc lengths 0, 2, 4, 6 — the third sample is one unit up the second leg.
        Assert.Equal(4, samples.Count);
        Assert.Equal(new Vector3(3, 1, 0), samples[2].Point);
        Assert.Equal(new Vector3(3, 3, 0), samples[3].Point);
    }

    [Fact]
    public void ZeroPitchIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            SurfacePath.SampleAtPitch([Straight(Vector3.Zero, Vector3.UnitX)], 0f, 1f));
    }

    // ----- GuidedTipPlacement.Candidates -----

    [Fact]
    public void CandidatesTakeTheInwardNormalOfTheirFace()
    {
        var mesh = UnitBox();
        var candidates = GuidedTipPlacement.Candidates(mesh, [(new Vector3(-2, 2, 0), 0)], Parameters());

        var candidate = Assert.Single(candidates);
        Assert.Equal(Vector3.UnitZ, candidate.InwardNormal);
        Assert.Equal(TipStrategy.Guided, candidate.Strategy);
        Assert.Equal(SupportTipShape.Cone, candidate.TipShape);
    }

    [Fact]
    public void CandidateOnAnExistingTipIsDropped()
    {
        var mesh = UnitBox();
        var graph = new SupportGraph();
        graph.AddNode(new SupportNode { Type = SupportNodeType.Tip, Position = new Vector3(-2, 2, 0) });

        var candidates = GuidedTipPlacement.Candidates(mesh,
            [(new Vector3(-2, 2, 0), 0), (new Vector3(-2, 2, 0) + new Vector3(0, 1, 0), 0),
             (new Vector3(2, -2, 0), 1)], Parameters(2.5f), graph);

        var kept = Assert.Single(candidates);
        Assert.Equal(new Vector3(2, -2, 0), kept.Point);
    }

    [Fact]
    public void ExistingTipClearanceIsItsOwnDistanceWhenGiven()
    {
        var mesh = UnitBox();
        var graph = new SupportGraph();
        graph.AddNode(new SupportNode { Type = SupportNodeType.Tip, Position = new Vector3(-2, 2, 0) });
        var parameters = Parameters(2.5f) with { ExistingTipClearanceMm = 1f };

        // 1.5 mm from the existing tip: inside the 2.5 mm spacing, outside the 1 mm clearance.
        var candidates = GuidedTipPlacement.Candidates(mesh,
            [(new Vector3(-2, 3.5f, 0), 0), (new Vector3(-2, 2.5f, 0), 0)], parameters, graph);

        var kept = Assert.Single(candidates);
        Assert.Equal(new Vector3(-2, 3.5f, 0), kept.Point);
    }

    [Fact]
    public void WithNoExistingGraphNothingIsDropped()
    {
        var mesh = UnitBox();
        var candidates = GuidedTipPlacement.Candidates(mesh,
            [(new Vector3(-2, 2, 0), 0), (new Vector3(2, -2, 0), 1)], Parameters(2.5f), existing: null);
        Assert.Equal(2, candidates.Count);
    }

    // ----- SurfaceLineGesture -----

    [Fact]
    public void GestureBeforeTheFirstVertexPreviewsTheTipUnderTheCursor()
    {
        var gesture = new SurfaceLineGesture(UnitBox(), 2.5f);
        Assert.Empty(gesture.Preview(Parameters(), null));

        gesture.SetCursor(new Vector3(-2, 2, 0), 0);

        var tip = Assert.Single(gesture.Preview(Parameters(), null));
        Assert.Equal(new Vector3(-2, 2, 0), tip.Point);
        Assert.Empty(gesture.Route());
    }

    [Fact]
    public void GestureRubberBandsFromTheLastVertexAndSamplesAtPitch()
    {
        var gesture = new SurfaceLineGesture(UnitBox(), 2f);
        gesture.SetCursor(new Vector3(-4, 4, 0), 0);
        Assert.True(gesture.AddVertex());
        gesture.SetCursor(new Vector3(-4, -2, 0), 0);

        var preview = gesture.Preview(Parameters(2f), null);

        Assert.Equal(new[] { 4f, 2f, 0f, -2f },
            preview.Select(c => MathF.Round(c.Point.Y, 4)).ToArray());
        Assert.False(gesture.CursorPathIsChord);
        Assert.Single(gesture.Route());
    }

    [Fact]
    public void GestureCommitsSegmentsAndBackspaceDropsTheLast()
    {
        var gesture = new SurfaceLineGesture(UnitBox(), 2f);
        gesture.SetCursor(new Vector3(-4, 4, 0), 0);
        gesture.AddVertex();
        gesture.SetCursor(new Vector3(-4, 0, 0), 0);
        gesture.AddVertex();
        gesture.SetCursor(new Vector3(0, 0, 0), 0);

        Assert.Equal(2, gesture.Vertices.Count);
        Assert.Equal(2, gesture.Route().Count);

        Assert.True(gesture.RemoveLastVertex());
        Assert.Single(gesture.Vertices);
        // The rubber band now leaves the first vertex and reaches the cursor.
        var route = Assert.Single(gesture.Route());
        Assert.Equal(new Vector3(-4, 4, 0), route.Points[0]);
        Assert.Equal(new Vector3(0, 0, 0), route.Points[^1]);
    }

    [Fact]
    public void ClickingTheLastVertexAgainAddsNothing()
    {
        var gesture = new SurfaceLineGesture(UnitBox(), 2f);
        gesture.SetCursor(new Vector3(-4, 4, 0), 0);
        Assert.True(gesture.AddVertex());
        Assert.False(gesture.AddVertex());
        Assert.Single(gesture.Vertices);
    }

    [Fact]
    public void GestureFallsBackToAChordWhenNoSurfacePathExists()
    {
        var mesh = new Mesh(
        [
            new Vector3(0, 0, 0), new(2, 0, 0), new(0, 2, 0),
            new Vector3(5, 0, 0), new(7, 0, 0), new(5, 2, 0),
        ], [0, 1, 2, 3, 4, 5]);
        var gesture = new SurfaceLineGesture(mesh, 1f);
        gesture.SetCursor(new Vector3(0.5f, 0.5f, 0), 0);
        gesture.AddVertex();

        gesture.SetCursor(new Vector3(5.5f, 0.5f, 0), 1);

        Assert.True(gesture.CursorPathIsChord);
        var route = Assert.Single(gesture.Route());
        Assert.Equal(2, route.Points.Count);
    }

    [Fact]
    public void PitchIsClampedAndSteppedInTenths()
    {
        var gesture = new SurfaceLineGesture(UnitBox(), 0.01f);
        Assert.Equal(SurfaceLineGesture.MinPitchMm, gesture.PitchMm);

        gesture.SetPitch(2f);
        gesture.StepPitch(1);
        Assert.Equal(2.2f, gesture.PitchMm, 4);
        gesture.StepPitch(-30);
        Assert.Equal(SurfaceLineGesture.MinPitchMm, gesture.PitchMm);
    }

    [Fact]
    public void ClearingTheCursorLeavesOnlyCommittedSegments()
    {
        var gesture = new SurfaceLineGesture(UnitBox(), 2f);
        gesture.SetCursor(new Vector3(-4, 4, 0), 0);
        gesture.AddVertex();
        gesture.SetCursor(new Vector3(-4, 0, 0), 0);
        gesture.ClearCursor();

        Assert.Empty(gesture.Route());
        var only = Assert.Single(gesture.Preview(Parameters(2f), null));
        Assert.Equal(new Vector3(-4, 4, 0), only.Point);
    }

    // ----- Document.PlaceGuidedTips -----

    private static (Document Document, SceneObject Box) FloatingBoxDocument()
    {
        var document = new Document();
        var obj = new SceneObject("box", Box(new Vector3(-5, -5, 8), new Vector3(5, 5, 14)));
        document.AddObject(obj);
        return (document, obj);
    }

    [Fact]
    public void GuidedTipsLandAsOneUndoStep()
    {
        var (document, box) = FloatingBoxDocument();
        var world = box.Mesh; // identity transform
        var candidates = GuidedTipPlacement.Candidates(world,
            [(new Vector3(-3, -3.5f, 8), 1), (new Vector3(0, -3.5f, 8), 1), (new Vector3(3, -3.5f, 8), 1)],
            document.GuidedPlacementParameters());
        Assert.Equal(3, candidates.Count);

        var placed = document.PlaceGuidedTips(box, candidates, "Support line", out var refused);

        Assert.Equal(3, placed);
        Assert.Equal(0, refused);
        Assert.Equal(3, document.Supports.Nodes.Count(n => n.Type == SupportNodeType.Tip));
        Assert.All(document.Supports.Nodes.Where(n => n.Type == SupportNodeType.Tip),
            n => Assert.Equal(box.Id, n.ContactObjectId));

        Assert.True(document.Undo());
        Assert.Empty(document.Supports.Nodes);
    }

    [Fact]
    public void GuidedGesturesIgnoreExistingSupportsUnlessTold()
    {
        var (document, box) = FloatingBoxDocument();
        Assert.True(document.AddManualSupport(box, new Vector3(0, 0, 8), -Vector3.UnitZ));

        Assert.Null(document.GuidedExistingSupports());
        Assert.Equal(document.SupportSettings.GuidedExistingClearanceMm,
            document.GuidedPlacementParameters().ExistingTipClearanceMm);

        document.SupportSettings = document.SupportSettings with { GuidedIgnoreExistingSupports = false };
        Assert.Same(document.Supports, document.GuidedExistingSupports());
    }

    [Fact]
    public void IgnoringExistingSupportsPlacesATipRightBesideOne()
    {
        var (document, box) = FloatingBoxDocument();
        Assert.True(document.AddManualSupport(box, new Vector3(0, -3.5f, 8), -Vector3.UnitZ));
        var beside = GuidedTipPlacement.Candidates(box.Mesh, [(new Vector3(0.6f, -3.5f, 8), 1)],
            document.GuidedPlacementParameters(), document.GuidedExistingSupports());
        Assert.Single(beside);

        var placed = document.PlaceGuidedTips(box, beside, "Support line", out var refused);

        Assert.Equal(1, placed);
        Assert.Equal(0, refused);
    }

    [Fact]
    public void GuidedTipsWithNothingToPlaceRecordNoUndoStep()
    {
        var (document, box) = FloatingBoxDocument();
        var undoBefore = document.History.UndoName;

        var placed = document.PlaceGuidedTips(box, [], "Support line", out var refused);

        Assert.Equal(0, placed);
        Assert.Equal(0, refused);
        Assert.Equal(undoBefore, document.History.UndoName);
    }
}
