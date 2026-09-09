using System.Numerics;
using Danslicer.Core;
using Danslicer.Core.Config;
using Danslicer.Core.Geometry;
using Danslicer.Core.IO;
using Danslicer.Core.Scene;
using Danslicer.Core.Supports;
using Danslicer.Core.Supports.Rafts;
using Xunit;

namespace Danslicer.Tests;

/// <summary>
/// Rafts spec step 2: Add raft strips the bases and the model stays put; Remove raft restores
/// them; new feet on a rafted object are born baseless; the raft survives save and duplicate.
/// </summary>
public class RaftDocumentTests
{
    private static (Document Document, SceneObject Object) Supported(int supports = 2)
    {
        var document = new Document { PlacementMode = PlacementMode.Off };
        document.SupportSettings = document.SupportSettings with { BaseGridPitch = 20f, AutoBracing = false };
        var obj = new SceneObject("slab", new Mesh(
            [new(0, 0, 20), new(60, 0, 20), new(0, 60, 20)], [0, 1, 2]));
        document.AddObject(obj);
        document.Select(obj);
        for (var i = 0; i < supports; i++)
            Assert.True(document.AddManualSupport(obj, new Vector3(10 + 20 * i, 10, 20), -Vector3.UnitZ));
        Assert.Equal(supports, document.FeetOf(obj).Count);
        Assert.All(document.FeetOf(obj), foot => Assert.Equal(SupportBaseShape.Disc, foot.BaseShape));
        return (document, obj);
    }

    [Fact]
    public void AddRaftStripsTheBasesAndLeavesTheModelWhereItIs()
    {
        var (document, obj) = Supported();
        var transform = obj.Transform;
        var feetZ = document.FeetOf(obj).Select(f => f.Position.Z).ToList();

        Assert.Equal(1, document.AddRaftToSelection());

        Assert.NotNull(obj.Raft);
        Assert.Equal(document.SupportSettings.ToRaftParameters(), obj.Raft);
        Assert.All(document.FeetOf(obj), foot => Assert.Equal(SupportBaseShape.None, foot.BaseShape));
        Assert.Equal(transform, obj.Transform);
        Assert.Equal(feetZ, document.FeetOf(obj).Select(f => f.Position.Z).ToList());
        Assert.Equal("Add raft", document.History.UndoName);

        Assert.True(document.Undo());
        Assert.Null(obj.Raft);
        Assert.All(document.FeetOf(obj), foot => Assert.Equal(SupportBaseShape.Disc, foot.BaseShape));
    }

    [Fact]
    public void RemoveRaftGivesTheFeetTheBasesTheSettingsSay()
    {
        var (document, obj) = Supported();
        document.AddRaftToSelection();
        document.SupportSettings = document.SupportSettings with
        {
            BaseShape = SupportBaseShape.DiscCone, BaseDiameter = 7f,
        };

        Assert.Equal(1, document.RemoveRaftFromSelection());

        Assert.Null(obj.Raft);
        Assert.All(document.FeetOf(obj), foot =>
        {
            Assert.Equal(SupportBaseShape.DiscCone, foot.BaseShape);
            Assert.Equal(7f, foot.BaseDiameter);
        });
        Assert.Equal("Remove raft", document.History.UndoName);

        Assert.True(document.Undo());
        Assert.NotNull(obj.Raft);
        Assert.All(document.FeetOf(obj), foot => Assert.Equal(SupportBaseShape.None, foot.BaseShape));
    }

    [Fact]
    public void RemoveRaftOnAnUnraftedSelectionDoesNothing()
    {
        var (document, _) = Supported(1);
        var undo = document.History.UndoName;

        Assert.Equal(0, document.RemoveRaftFromSelection());
        Assert.Equal(undo, document.History.UndoName);
    }

    [Fact]
    public void AFootPlacedOnARaftedObjectIsBornBaseless()
    {
        var (document, obj) = Supported(1);
        document.AddRaftToSelection();

        Assert.True(document.AddManualSupport(obj, new Vector3(40, 10, 20), -Vector3.UnitZ));

        Assert.Equal(2, document.FeetOf(obj).Count);
        Assert.All(document.FeetOf(obj), foot => Assert.Equal(SupportBaseShape.None, foot.BaseShape));
    }

    [Fact]
    public void AddRaftAgainRetakesTheCurrentSettings()
    {
        var (document, obj) = Supported(1);
        document.AddRaftToSelection();
        Assert.Equal(RaftType.Plate, obj.Raft!.Type);

        document.SupportSettings = document.SupportSettings with { RaftType = RaftType.Web };
        document.AddRaftToSelection();

        Assert.Equal(RaftType.Web, obj.Raft!.Type);
    }

    [Fact]
    public void SavingTheSettingsLandsOnTheSelectedRaftedObject()
    {
        var (document, obj) = Supported(1);
        document.AddRaftToSelection();

        document.SupportSettings = document.SupportSettings with { RaftThickness = 2f };
        Assert.Equal(1, document.ApplySupportSettingsToSelection());

        Assert.Equal(2f, obj.Raft!.Thickness);
        Assert.True(document.Undo());
        Assert.Equal(1f, obj.Raft!.Thickness);
    }

    [Fact]
    public void BaseSettingsDoNotTouchARaftedObjectsFeet()
    {
        var (document, obj) = Supported(1);
        document.AddRaftToSelection();
        var foot = document.FeetOf(obj)[0];
        document.SelectSupportElement(foot.Id);

        document.SupportSettings = document.SupportSettings with { BaseDiameter = 9f };
        document.ApplySupportSettingsToSelection();

        Assert.Equal(SupportBaseShape.None, foot.BaseShape);
        Assert.NotEqual(9f, foot.BaseDiameter);
    }

    [Fact]
    public void TheRaftOutlineFollowsTheFeet()
    {
        var (document, obj) = Supported(1);
        Assert.Null(document.RaftTopOutline(obj));

        document.AddRaftToSelection();
        var one = document.RaftTopOutline(obj);
        Assert.NotNull(one);
        Assert.NotEmpty(one);

        Assert.True(document.AddManualSupport(obj, new Vector3(40, 10, 20), -Vector3.UnitZ));
        var two = document.RaftTopOutline(obj);
        Assert.True(Danslicer.Core.Slicing.MeshSlicer.AreaMm2(two!) > Danslicer.Core.Slicing.MeshSlicer.AreaMm2(one));
    }

    [Fact]
    public void DuplicateCarriesTheRaft()
    {
        var (document, obj) = Supported(1);
        document.AddRaftToSelection();

        var copy = Assert.Single(document.DuplicateSelection());

        Assert.Equal(obj.Raft, copy.Raft);
        Assert.All(document.FeetOf(copy), foot => Assert.Equal(SupportBaseShape.None, foot.BaseShape));
    }

    [Fact]
    public void TheRaftRoundTripsThroughTheProjectFile()
    {
        var (document, obj) = Supported(1);
        document.SupportSettings = document.SupportSettings with { RaftType = RaftType.Web, RaftBarWidth = 3.5f, RaftEdgeAngleDegrees = 60f };
        document.AddRaftToSelection();
        var path = Path.Combine(Path.GetTempPath(), $"raft-{Guid.NewGuid():N}.danslicer");
        try
        {
            ProjectFile.Save(path, document, new ProjectViewState());
            var loaded = ProjectFile.Load(path);

            var back = Assert.Single(loaded.Document.Scene.Objects);
            Assert.Equal(obj.Raft, back.Raft);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
