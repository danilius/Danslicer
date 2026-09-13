using System.Numerics;
using Danslicer.App.ViewModels;
using Danslicer.Core;
using Danslicer.Core.Config;
using Danslicer.Core.Geometry;
using Danslicer.Core.Scene;
using Xunit;

namespace Danslicer.Tests;

/// <summary>
/// The Transform pop-out's position is the object's anchor, the centre-bottom of its world
/// bounding box, recomputed after a rotation (user, 2026-09-09).
/// </summary>
public class ObjectPositionFieldsTests
{
    /// <summary>A 10 × 4 × 20 mm box seated on the origin: X ±5, Y ±2, Z 0..20.</summary>
    private static Mesh Box() => new(
        [
            new(-5, -2, 0), new(5, -2, 0), new(5, 2, 0), new(-5, 2, 0),
            new(-5, -2, 20), new(5, -2, 20), new(5, 2, 20), new(-5, 2, 20),
        ],
        [
            0, 2, 1, 0, 3, 2, 4, 5, 6, 4, 6, 7, 0, 1, 5, 0, 5, 4,
            1, 2, 6, 1, 6, 5, 2, 3, 7, 2, 7, 6, 3, 0, 4, 3, 4, 7,
        ]);

    private static (MainViewModel ViewModel, SceneObject Object) Selected()
    {
        var viewModel = new MainViewModel();
        viewModel.Document.PlacementMode = PlacementMode.Off;
        var obj = new SceneObject("box", Box());
        viewModel.Document.AddObject(obj);
        viewModel.Document.Select(obj);
        Assert.Same(obj, viewModel.SelectedObject);
        return (viewModel, obj);
    }

    [Fact]
    public void PositionReadsTheBoundingBoxCentreBottom()
    {
        var (viewModel, obj) = Selected();
        Assert.Equal("0", viewModel.Position[0].Text);
        Assert.Equal("0", viewModel.Position[2].Text);

        // Lying on its side after a quarter turn about X the box is 10 × 20 × 4; the anchor
        // follows the box, not the transform's origin.
        viewModel.Rotation[0].Text = "90";

        var bounds = obj.WorldBounds;
        Assert.Equal(4f, bounds.Size.Z, 3);
        Assert.Equal(bounds.Center.X, float.Parse(viewModel.Position[0].Text), 3);
        Assert.Equal(bounds.Center.Y, float.Parse(viewModel.Position[1].Text), 3);
        Assert.Equal(bounds.Min.Z, float.Parse(viewModel.Position[2].Text), 3);
        Assert.NotEqual(obj.Transform.Translation.Z, bounds.Min.Z, 3);
    }

    [Fact]
    public void EditingPositionMovesTheAnchorThere()
    {
        var (viewModel, obj) = Selected();
        viewModel.Rotation[0].Text = "90";

        viewModel.Position[0].Text = "10";
        viewModel.Position[2].Text = "0";

        var bounds = obj.WorldBounds;
        Assert.Equal(10f, bounds.Center.X, 3);
        Assert.Equal(0f, bounds.Min.Z, 3);
        Assert.Equal("10", viewModel.Position[0].Text);
        Assert.Equal("0", viewModel.Position[2].Text);
        Assert.Equal("Edit transform", viewModel.Document.History.UndoName);
    }

    [Fact]
    public void AnchorIsTheCentreBottomOfTheWorldBounds()
    {
        var obj = new SceneObject("box", Box())
        {
            Transform = Transform.Identity with { Translation = new Vector3(3, 4, 5) },
        };

        Assert.Equal(new Vector3(3, 4, 5), MainViewModel.Anchor(obj));
    }
}
