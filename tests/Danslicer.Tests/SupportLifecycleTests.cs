using System.Numerics;
using Danslicer.Core;
using Danslicer.Core.Config;
using Danslicer.Core.Geometry;
using Danslicer.Core.Scene;
using Danslicer.Core.Supports;

namespace Danslicer.Tests;

/// <summary>
/// The support lifecycle rules from task 03: supports belong to their object, so deleting the
/// object takes them with it, and a transform keeps them only when it maps every contact exactly
/// (<see cref="SupportTransformRule"/>). Every discard must be recoverable in ONE undo step —
/// the user chose silent discard with no confirmation dialog (D13), which only works if a single
/// Ctrl+Z puts everything back.
/// </summary>
public sealed class SupportLifecycleTests
{
    /// <summary>Axis-aligned box mesh with outward faces (soup-welded).</summary>
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

    /// <summary>A floating box with one manual support under it, and nothing else.</summary>
    private static (Document Document, SceneObject Box) SupportedBox(string name = "box")
    {
        var document = new Document { PlacementMode = PlacementMode.Off };
        var obj = new SceneObject(name, Box(new Vector3(-5, -5, 8), new Vector3(5, 5, 14)));
        document.AddObject(obj);
        Assert.True(document.AddManualSupport(obj, new Vector3(0, 0, 8), -Vector3.UnitZ),
            "the fixture needs a routed support to test the lifecycle of");
        return (document, obj);
    }

    private static int OwnedNodes(Document document, SceneObject obj) =>
        document.Supports.Nodes.Count(node => node.Origin.ObjectId == obj.Id);

    // ----- The rule itself -----

    [Fact]
    public void TranslationAcrossThePlateMapsContactsExactlyButALiftDoesNot()
    {
        // User decision 2026-09-09: a move in Z discards the supports; the tree stands on the
        // plate and its trunks are the height they are.
        var before = Transform.Identity;
        Assert.True(SupportTransformRule.MapsContactsExactly(before, before with { Translation = new Vector3(12f, -3f, 0f) }));
        Assert.False(SupportTransformRule.MapsContactsExactly(before, before with { Translation = new Vector3(12f, -3f, 4f) }));
    }

    [Fact]
    public void TiltingAndScalingDoNotMapContactsExactly()
    {
        var before = Transform.Identity;

        Assert.False(SupportTransformRule.MapsContactsExactly(before,
            before with { Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitX, 0.4f) }));
        Assert.False(SupportTransformRule.MapsContactsExactly(before,
            before with { Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.4f) }));
        Assert.False(SupportTransformRule.MapsContactsExactly(before,
            before with { Scale = new Vector3(2f, 2f, 2f) }));
        Assert.False(SupportTransformRule.MapsContactsExactly(before,
            before with { Scale = new Vector3(1f, 1f, 1.5f) })); // non-uniform too
    }

    [Fact]
    public void TurningAboutTheVerticalKeepsSupports()
    {
        // A turn about Z carries contacts, trunks and bases round together: the tree that fitted
        // before fits after, so there is nothing to discard.
        var before = Transform.Identity;

        Assert.True(SupportTransformRule.MapsContactsExactly(before,
            before with { Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 0.4f) }));
        Assert.True(SupportTransformRule.MapsContactsExactly(before,
            before with { Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI) }));

        // Also from an already-turned start, and combined with a move.
        var turned = before with { Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 1.2f) };
        Assert.True(SupportTransformRule.MapsContactsExactly(turned, turned with
        {
            Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, -0.3f),
            Translation = new Vector3(9f, 2f, 0f),
        }));

        // But a turn about Z applied to a tilted object still leaves it tilted, and a tilt added
        // on top of a turn is still a tilt.
        var tilted = before with { Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitX, 0.5f) };
        Assert.False(SupportTransformRule.MapsContactsExactly(tilted, tilted with
        {
            Rotation = Quaternion.Concatenate(tilted.Rotation,
                Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.2f)),
        }));
    }

    [Fact]
    public void AQuaternionAndItsNegationAreTheSameRotation()
    {
        // q and -q name the same orientation. Treating them as different would discard supports
        // on a "rotation" that does not move the object at all.
        var rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitX, 1.1f);
        var before = Transform.Identity with { Rotation = rotation };
        var after = before with { Rotation = -rotation };

        Assert.True(SupportTransformRule.MapsContactsExactly(before, after));
    }

    // ----- Layout: a model and its supports are one thing -----

    [Fact]
    public void EverySupportElementKnowsWhichModelItBelongsTo()
    {
        // What Layout clicking needs: any part of a support answers "which model is this?", so
        // a click anywhere on it can select the model.
        var (document, box) = SupportedBox();

        foreach (var node in document.Supports.Nodes)
            Assert.Equal(box.Id, document.Supports.OwningObjectId(node.Id));
        foreach (var segment in document.Supports.Segments)
            Assert.Equal(box.Id, document.Supports.OwningObjectId(segment.Id));
    }

    [Fact]
    public void AnUnknownElementBelongsToNoModel()
    {
        var (document, _) = SupportedBox();

        Assert.Null(document.Supports.OwningObjectId(Guid.NewGuid()));
    }

    [Fact]
    public void SupportsOfTwoModelsAreToldApart()
    {
        var (document, first) = SupportedBox("first");
        var second = new SceneObject("second", Box(new Vector3(20, -5, 8), new Vector3(30, 5, 14)));
        document.AddObject(second);
        Assert.True(document.AddManualSupport(second, new Vector3(25, 0, 8), -Vector3.UnitZ));

        foreach (var node in document.Supports.Nodes)
        {
            var owner = document.Supports.OwningObjectId(node.Id);
            Assert.True(owner == first.Id || owner == second.Id);
            Assert.Equal(node.Origin.ObjectId, owner);
        }
    }

    // ----- Duplicate -----

    [Theory]
    [InlineData(PlacementMode.Off)]
    [InlineData(PlacementMode.AutoDrop)]
    [InlineData(PlacementMode.RaiseAbovePlate)]
    public void DuplicatingAModelDuplicatesItsSupports(PlacementMode placementMode)
    {
        var (document, box) = SupportedBox();
        document.PlacementMode = placementMode;
        document.PlacementHeightMm = 20;
        var originalTransform = box.Transform;
        var originalPositions = document.Supports.Nodes.ToDictionary(n => n.Id, n => n.Position);
        var nodesBefore = document.Supports.Nodes.Count;
        var segmentsBefore = document.Supports.Segments.Count;
        var tipBefore = document.Supports.Nodes.Single(n => n.Type == SupportNodeType.Tip).Position;

        document.Select(box);
        var copy = Assert.Single(document.DuplicateSelection());

        Assert.Equal(nodesBefore * 2, document.Supports.Nodes.Count);
        Assert.Equal(segmentsBefore * 2, document.Supports.Segments.Count);
        Assert.Equal(nodesBefore, OwnedNodes(document, copy));

        // The copy's supports sit under the copy, shifted by the same offset the model was.
        var offset = copy.Transform.Translation - box.Transform.Translation;
        Assert.Equal(new Vector3(5, 5, 0), offset);
        Assert.Equal(box.WorldBounds.Min.Z, copy.WorldBounds.Min.Z);
        Assert.Equal(originalTransform, box.Transform);
        foreach (var node in document.Supports.Nodes.Where(n => n.Origin.ObjectId == box.Id))
            Assert.Equal(originalPositions[node.Id], node.Position);
        var copiedPositions = document.Supports.Nodes.Where(n => n.Origin.ObjectId == copy.Id)
            .Select(n => n.Position).ToHashSet();
        Assert.True(originalPositions.Values.All(p => copiedPositions.Contains(p + offset)));
        var copiedTip = document.Supports.Nodes
            .Single(n => n.Type == SupportNodeType.Tip && n.Origin.ObjectId == copy.Id);
        Assert.Equal(tipBefore.X + offset.X, copiedTip.Position.X, 4);
        Assert.Equal(tipBefore.Y + offset.Y, copiedTip.Position.Y, 4);
        Assert.Equal(tipBefore.Z + offset.Z, copiedTip.Position.Z, 4);
        Assert.True(document.Undo());
        Assert.Single(document.Scene.Objects);
        Assert.Equal(nodesBefore, document.Supports.Nodes.Count);
        Assert.True(document.Redo());
        Assert.Equal(box.WorldBounds.Min.Z, copy.WorldBounds.Min.Z);
        Assert.Equal(nodesBefore * 2, document.Supports.Nodes.Count);
        Assert.True(copiedPositions.SetEquals(document.Supports.Nodes
            .Where(n => n.Origin.ObjectId == copy.Id).Select(n => n.Position)));
    }

    [Fact]
    public void DuplicateAndItsSupportsAreOneUndoStep()
    {
        var (document, box) = SupportedBox();
        var nodesBefore = document.Supports.Nodes.Count;
        document.Select(box);
        document.DuplicateSelection();

        Assert.True(document.Undo());

        Assert.Single(document.Scene.Objects);
        Assert.Equal(nodesBefore, document.Supports.Nodes.Count);
    }

    [Fact]
    public void DuplicatingAnUnsupportedModelAddsNoSupports()
    {
        var document = new Document();
        var box = new SceneObject("box", Box(new Vector3(-5, -5, 8), new Vector3(5, 5, 14)));
        document.AddObject(box);
        document.Select(box);

        document.DuplicateSelection();

        Assert.Empty(document.Supports.Nodes);
    }

    [Fact]
    public void ACopysSupportsAreItsOwnAndSurviveTheOriginalsDeletion()
    {
        var (document, box) = SupportedBox();
        document.Select(box);
        var copy = Assert.Single(document.DuplicateSelection());
        var copySupports = OwnedNodes(document, copy);

        document.Select(box);
        document.DeleteSelection();

        Assert.Equal(copySupports, OwnedNodes(document, copy));
        Assert.Equal(0, OwnedNodes(document, box));
    }

    // ----- Delete -----

    [Fact]
    public void DeletingAModelDeletesItsSupportsAndOneUndoRestoresBoth()
    {
        var (document, box) = SupportedBox();
        var supportsBefore = document.Supports.Nodes.Count;
        Assert.True(supportsBefore > 0);

        document.Select(box);
        document.DeleteSelection();

        Assert.Empty(document.Scene.Objects);
        Assert.Empty(document.Supports.Nodes);

        Assert.True(document.Undo());

        Assert.Single(document.Scene.Objects);
        Assert.Equal(supportsBefore, document.Supports.Nodes.Count);
    }

    [Fact]
    public void RedoAfterUndoingADeleteRemovesTheModelAndItsSupportsAgain()
    {
        var (document, box) = SupportedBox();
        document.Select(box);
        document.DeleteSelection();
        document.Undo();

        Assert.True(document.Redo());

        Assert.Empty(document.Scene.Objects);
        Assert.Empty(document.Supports.Nodes);
    }

    [Fact]
    public void DeletingOneOfTwoModelsLeavesTheOthersSupportsAlone()
    {
        var (document, first) = SupportedBox("first");
        var second = new SceneObject("second", Box(new Vector3(20, -5, 8), new Vector3(30, 5, 14)));
        document.AddObject(second);
        Assert.True(document.AddManualSupport(second, new Vector3(25, 0, 8), -Vector3.UnitZ));
        var secondSupports = OwnedNodes(document, second);
        Assert.True(secondSupports > 0);

        document.ClearSelection();
        document.Select(first);
        document.DeleteSelection();

        Assert.Equal(0, OwnedNodes(document, first));
        Assert.Equal(secondSupports, OwnedNodes(document, second));
    }

    // ----- Transforms -----

    [Fact]
    public void RotatingDiscardsSupportsAndOneUndoRestoresRotationAndSupports()
    {
        var (document, box) = SupportedBox();
        var supportsBefore = document.Supports.Nodes.Count;
        var before = box.Transform;
        var rotated = before with { Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitX, 0.6f) };

        document.CommitTransform(box, before, rotated, "Rotate");

        Assert.Empty(document.Supports.Nodes);

        Assert.True(document.Undo());

        Assert.Equal(before.Rotation, box.Transform.Rotation);
        Assert.Equal(supportsBefore, document.Supports.Nodes.Count);
    }

    [Theory]
    [InlineData(PlacementMode.Off)]
    [InlineData(PlacementMode.AutoDrop)]
    [InlineData(PlacementMode.RaiseAbovePlate)]
    public void TurningAboutZKeepsSupportsAndCarriesThemRound(PlacementMode placementMode)
    {
        var (document, box) = SupportedBox();
        document.PlacementMode = placementMode;
        document.PlacementHeightMm = 20;
        var supportsBefore = document.Supports.Nodes.Count;
        var tipBefore = document.Supports.Nodes.Single(n => n.Type == SupportNodeType.Tip).Position;
        var before = box.Transform;
        var heightBefore = box.WorldBounds.Min.Z;
        var supportBefore = document.CaptureAssociatedSupportPositions([box]);
        var angle = 0.7f;
        var rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, angle);
        var offset = new Vector3(3, -2, 0);
        var turned = before with { Rotation = rotation, Translation = before.Translation + offset };

        box.Transform = document.ApplyPlacementForTransform(box, before, turned);
        document.ApplyAssociatedSupportTransformsTransient([(box, before)], supportBefore);
        Assert.Equal(turned, box.Transform);
        Assert.Equal(heightBefore, box.WorldBounds.Min.Z, 4);
        foreach (var node in document.Supports.Nodes)
            Assert.Equal(supportBefore[node.Id].Position.Z, node.Position.Z, 4);

        document.CommitTransforms([(box, before, turned)], "Rotate", supportBefore);

        Assert.Equal(turned, box.Transform);
        Assert.Equal(heightBefore, box.WorldBounds.Min.Z, 4);
        Assert.Equal(supportsBefore, document.Supports.Nodes.Count);
        var tipAfter = document.Supports.Nodes.Single(n => n.Type == SupportNodeType.Tip).Position;
        var expected = Vector3.Transform(tipBefore, rotation) + offset;
        Assert.Equal(expected.X, tipAfter.X, 4);
        Assert.Equal(expected.Y, tipAfter.Y, 4);
        Assert.Equal(expected.Z, tipAfter.Z, 4); // the turn is about the vertical: height is untouched
        foreach (var node in document.Supports.Nodes)
            Assert.True(Vector3.Distance(Vector3.Transform(supportBefore[node.Id].Position, rotation) + offset,
                node.Position) < 1e-4f);
        Assert.True(document.Undo());
        Assert.Equal(before, box.Transform);
        foreach (var node in document.Supports.Nodes)
            Assert.Equal(supportBefore[node.Id].Position, node.Position);
        Assert.True(document.Redo());
        Assert.Equal(turned, box.Transform);
        foreach (var node in document.Supports.Nodes)
            Assert.True(Vector3.Distance(Vector3.Transform(supportBefore[node.Id].Position, rotation) + offset,
                node.Position) < 1e-4f);
    }

    [Fact]
    public void TurningAboutZKeepsSupportsEvenWhenItCarriesThemOffThePlate()
    {
        // Off the plate is the build volume's complaint to make, not a reason to destroy work.
        var (document, box) = SupportedBox();
        var supportsBefore = document.Supports.Nodes.Count;
        var before = box.Transform with { Translation = new Vector3(400f, 0f, 0f) };
        box.Transform = before;
        var turned = before with { Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 1.4f) };

        document.CommitTransform(box, before, turned, "Rotate", applyPlacement: false);

        Assert.Equal(supportsBefore, document.Supports.Nodes.Count);
    }

    [Fact]
    public void ScalingDiscardsSupports()
    {
        var (document, box) = SupportedBox();
        var before = box.Transform;

        document.CommitTransform(box, before, before with { Scale = new Vector3(2f, 2f, 2f) }, "Scale");

        Assert.Empty(document.Supports.Nodes);
    }

    [Fact]
    public void PureTranslationKeepsSupportsAndCarriesThemAlong()
    {
        var (document, box) = SupportedBox();
        var supportsBefore = document.Supports.Nodes.Count;
        var tipBefore = document.Supports.Nodes.Single(n => n.Type == SupportNodeType.Tip).Position;
        var before = box.Transform;
        var offset = new Vector3(10f, 0f, 0f);

        document.CommitTransform(box, before, before with { Translation = before.Translation + offset },
            "Move", applyPlacement: false);

        Assert.Equal(supportsBefore, document.Supports.Nodes.Count);
        var tipAfter = document.Supports.Nodes.Single(n => n.Type == SupportNodeType.Tip).Position;
        Assert.Equal(tipBefore.X + offset.X, tipAfter.X, 3);
    }

    [Fact]
    public void OnlyTransformedObjectsInAMultiObjectSelectionLoseTheirSupports()
    {
        var (document, moved) = SupportedBox("moved");
        var untouched = new SceneObject("untouched", Box(new Vector3(20, -5, 8), new Vector3(30, 5, 14)));
        document.AddObject(untouched);
        Assert.True(document.AddManualSupport(untouched, new Vector3(25, 0, 8), -Vector3.UnitZ));
        var untouchedSupports = OwnedNodes(document, untouched);

        // Both objects are in the commit, but only one of them actually changes.
        var movedBefore = moved.Transform;
        document.CommitTransforms(
        [
            (moved, movedBefore, movedBefore with
                { Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitX, 0.5f) }),
            (untouched, untouched.Transform, untouched.Transform),
        ], "Rotate", applyPlacement: false);

        Assert.Equal(0, OwnedNodes(document, moved));
        Assert.Equal(untouchedSupports, OwnedNodes(document, untouched));
    }

    [Fact]
    public void MirrorStillReflectsSupportsRatherThanDiscardingThem()
    {
        // Regression guard for merged duplicate/mirror behaviour. A reflection maps every contact
        // exactly onto a valid new position, so mirror is the one transform that keeps supports —
        // and the rule is written as "unless it maps contacts exactly" precisely so this case is
        // not swept away by a later tidy-up into "transforms destroy supports".
        var (document, box) = SupportedBox();
        var supportsBefore = document.Supports.Nodes.Count;
        var tipBefore = document.Supports.Nodes.Single(n => n.Type == SupportNodeType.Tip).Position;

        document.Select(box);
        document.MirrorSelection(ObjectMirrorAxis.X);

        Assert.Equal(supportsBefore, document.Supports.Nodes.Count);
        var tipAfter = document.Supports.Nodes.Single(n => n.Type == SupportNodeType.Tip).Position;
        Assert.Equal(-tipBefore.X, tipAfter.X, 3);
    }

    [Fact]
    public void ManualSupportsAreDiscardedOnTheSameTermsAsGeneratedOnes()
    {
        // The fixture's support is manual. Ownership is what the rule keys on, not how the
        // support came to exist, so this is the same code path — asserted so a future change
        // that starts treating manual supports as precious does not do it silently.
        var (document, box) = SupportedBox();
        Assert.All(document.Supports.Nodes.Where(n => n.Origin.ObjectId == box.Id),
            node => Assert.Equal(box.Id, node.Origin.ObjectId));
        var before = box.Transform;

        document.CommitTransform(box, before,
            before with { Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.3f) }, "Rotate");

        Assert.Equal(0, OwnedNodes(document, box));
    }

    [Fact]
    public void LayFlatOnFaceIsARotationAndThereforeDiscardsSupports()
    {
        // Worth pinning explicitly because it will surprise someone: F is a convenience command,
        // not obviously a "transform", but it rotates the model onto a face and so cannot map
        // contacts exactly. The brief calls this out as intended behaviour.
        var (document, box) = SupportedBox();
        var supportsBefore = document.Supports.Nodes.Count;

        document.LayFlatOnFace(box, seedTriangle: 4); // a side face, so it actually rotates

        Assert.NotEqual(Quaternion.Identity, box.Transform.Rotation);
        Assert.Empty(document.Supports.Nodes);

        Assert.True(document.Undo());
        Assert.Equal(supportsBefore, document.Supports.Nodes.Count);
    }

    // ----- Layout visibility -----

    [Fact]
    public void LayoutShowsSupportsInFullButSupportModeKeepsTheChosenMode()
    {
        // Not a setting: in Layout a model and its supports are one object being arranged, so
        // the supports are drawn whatever the display mode says.
        var transparent = new SupportDisplayConfig { Mode = SupportDisplayMode.Transparent };

        var inLayout = SupportDisplayPolicy.ForWorkspace(transparent, isLayoutView: true);
        var inSupport = SupportDisplayPolicy.ForWorkspace(transparent, isLayoutView: false);

        Assert.Equal(SupportDisplayMode.Full, inLayout.Mode);
        Assert.Equal(SupportDisplayMode.Transparent, inSupport.Mode);
    }

    /// <summary>
    /// User request 2026-09-06: reducing supports to tips, or switching parts of them off, is a
    /// Support-mode working aid. Layout must show them in full regardless — including the
    /// per-part toggles, or every part being switched off would survive the switch as supports
    /// that are still invisible in Layout.
    /// </summary>
    [Theory]
    [InlineData(SupportDisplayMode.Tips)]
    [InlineData(SupportDisplayMode.Lines)]
    [InlineData(SupportDisplayMode.ContactPoints)]
    [InlineData(SupportDisplayMode.Transparent)]
    public void EveryReducedModeBecomesFullVisibilityInLayout(SupportDisplayMode mode)
    {
        var display = new SupportDisplayConfig
        {
            Mode = mode,
            ShowTips = false,
            ShowBranches = false,
            ShowTrunks = false,
            ShowBases = false,
            ShowBracing = false,
        };

        var inLayout = SupportDisplayPolicy.ForWorkspace(display, isLayoutView: true);

        Assert.Equal(SupportDisplayMode.Full, inLayout.Mode);
        Assert.True(SupportDisplayPolicy.ShowsMeshes(inLayout));
        Assert.All(Enum.GetValues<SupportSegmentType>(),
            type => Assert.True(SupportDisplayPolicy.IsSegmentDisplayed(type, inLayout)));
        Assert.True(inLayout.ShowBases);

        // Support mode is untouched: the user's working view is theirs.
        Assert.Same(display, SupportDisplayPolicy.ForWorkspace(display, isLayoutView: false));
    }

    /// <summary>
    /// User request 2026-09-06: individually hidden supports (Support mode's H) show in Layout
    /// too. The graph's Hidden flags are not touched — Layout just draws through them — so
    /// coming back to Support mode restores exactly what was hidden.
    /// </summary>
    [Fact]
    public void IndividuallyHiddenElementsAreDrawnInLayoutButNotInSupportMode()
    {
        var graph = new SupportGraph();
        var tip = new SupportNode
        {
            Type = SupportNodeType.Tip, Position = new Vector3(0f, 0f, 5f), Hidden = true,
        };
        var plate = new SupportNode
        {
            Type = SupportNodeType.Base, Position = Vector3.Zero,
            BaseShape = SupportBaseShape.Disc, Hidden = true,
        };
        graph.AddNode(tip);
        graph.AddNode(plate);
        var trunk = new SupportSegment
        {
            Type = SupportSegmentType.Trunk, NodeA = plate.Id, NodeB = tip.Id, Hidden = true,
        };
        graph.AddSegment(trunk);

        var config = new SupportDisplayConfig();
        var inSupport = SupportDisplayPolicy.ForWorkspace(config, isLayoutView: false);
        var inLayout = SupportDisplayPolicy.ForWorkspace(config, isLayoutView: true);

        Assert.False(SupportDisplayPolicy.IsElementDisplayed(graph, trunk.Id, inSupport));
        Assert.False(SupportDisplayPolicy.IsElementDisplayed(graph, tip.Id, inSupport));
        Assert.True(SupportDisplayPolicy.IsElementDisplayed(graph, trunk.Id, inLayout));
        Assert.True(SupportDisplayPolicy.IsElementDisplayed(graph, tip.Id, inLayout));
        Assert.Empty(SupportDisplayPolicy.DisplayedElementIds(graph, inSupport));
        Assert.Equal(3, SupportDisplayPolicy.DisplayedElementIds(graph, inLayout).Count());

        // And the geometry actually reaches the viewport, not just the policy.
        Assert.Empty(SupportRenderMesh.Build(graph));
        Assert.NotEmpty(SupportRenderMesh.Build(graph, includeHidden: true));

        // The flags themselves are untouched, so Support mode hides them again.
        Assert.True(tip.Hidden);
        Assert.True(trunk.Hidden);
    }

    [Fact]
    public void LayoutLeavesSettingsThatOnlyMeanAnythingInSupportModeAlone()
    {
        var display = new SupportDisplayConfig
        {
            Mode = SupportDisplayMode.Transparent,
            ShowContactPointsInTransparent = false,
        };

        var inLayout = SupportDisplayPolicy.ForWorkspace(display, isLayoutView: true);

        Assert.False(inLayout.ShowContactPointsInTransparent);
    }
}
