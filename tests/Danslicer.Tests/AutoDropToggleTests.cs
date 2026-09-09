using System.Numerics;
using Danslicer.Core;
using Danslicer.Core.Config;
using Danslicer.Core.Geometry;
using Danslicer.Core.Scene;
using Xunit;

namespace Danslicer.Tests;

/// <summary>
/// Switching Auto Drop on seats the selected objects at once (user, 2026-09-09); the view
/// model calls <see cref="Document.PlaceSelection"/> from its toggle handler.
/// </summary>
public class AutoDropToggleTests
{
    private static SceneObject Slab(string name, float z) => new(name, new Mesh(
        [new(0, 0, z), new(40, 0, z), new(0, 40, z)], [0, 1, 2]));

    [Fact]
    public void PlaceSelectionDropsTheSelectedObjectsOnly()
    {
        var document = new Document { PlacementMode = PlacementMode.Off };
        var selected = Slab("selected", 12f);
        var other = Slab("other", 7f);
        document.AddObject(selected);
        document.AddObject(other);
        document.Select(selected);

        document.PlacementMode = PlacementMode.AutoDrop;
        document.PlaceSelection();

        Assert.Equal(0f, selected.WorldBounds.Min.Z, 4);
        Assert.Equal(7f, other.WorldBounds.Min.Z, 4);
        Assert.Equal("Auto drop", document.History.UndoName);
        Assert.True(document.Undo());
        Assert.Equal(12f, selected.WorldBounds.Min.Z, 4);
    }

    [Fact]
    public void PlaceSelectionHonoursTheRaiseHeight()
    {
        var document = new Document { PlacementMode = PlacementMode.RaiseAbovePlate, PlacementHeightMm = 3f };
        var obj = Slab("slab", 12f);
        document.AddObject(obj);
        document.Select(obj);

        document.PlaceSelection();

        Assert.Equal(3f, obj.WorldBounds.Min.Z, 4);
    }

    [Fact]
    public void PlaceSelectionLeavesASeatedObjectAndItsHistoryAlone()
    {
        var document = new Document { PlacementMode = PlacementMode.AutoDrop };
        var obj = Slab("slab", 0f);
        document.AddObject(obj);
        document.Select(obj);
        var undoName = document.History.UndoName;

        document.PlaceSelection();

        Assert.Equal(undoName, document.History.UndoName);
    }

    [Fact]
    public void PlaceSelectionDoesNothingWhenPlacementIsOff()
    {
        var document = new Document { PlacementMode = PlacementMode.Off };
        var obj = Slab("slab", 12f);
        document.AddObject(obj);
        document.Select(obj);

        document.PlaceSelection();

        Assert.Equal(12f, obj.WorldBounds.Min.Z, 4);
    }
}
