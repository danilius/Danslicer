using System.Numerics;
using Danslicer.App.ViewModels;
using Danslicer.Core;
using Danslicer.Core.Config;
using Danslicer.Core.Geometry;
using Danslicer.Core.IO;
using Danslicer.Core.Scene;
using Danslicer.Core.Supports;

namespace Danslicer.Tests;

/// <summary>
/// The object list's per-model update button (user request 2026-09-06): re-read the file the
/// model came from, keeping its place in the scene. Also covers the import side of the same
/// story — an import obeys the placement mode instead of seating itself.
/// </summary>
public sealed class ObjectReloadTests
{
    private static Mesh Box(float height)
    {
        Vector3[] positions =
        [
            new(0, 0, 0), new(4, 0, 0), new(0, 2, 0),
            new(0, 0, height), new(4, 0, height), new(0, 2, height),
        ];
        int[] indices =
        [
            0, 2, 1, 3, 4, 5,
            0, 1, 4, 0, 4, 3,
            1, 2, 5, 1, 5, 4,
            2, 0, 3, 2, 3, 5,
        ];
        return new Mesh(positions, indices);
    }

    private static string WriteStl(Mesh mesh)
    {
        var path = Path.Combine(Path.GetTempPath(), $"danslicer-reload-{Guid.NewGuid():N}.stl");
        using var writer = new StreamWriter(path);
        writer.WriteLine("solid test");
        for (var triangle = 0; triangle < mesh.TriangleCount; triangle++)
        {
            mesh.GetTriangle(triangle, out var a, out var b, out var c);
            writer.WriteLine("facet normal 0 0 0");
            writer.WriteLine("  outer loop");
            foreach (var vertex in new[] { a, b, c })
                writer.WriteLine($"    vertex {vertex.X:R} {vertex.Y:R} {vertex.Z:R}");
            writer.WriteLine("  endloop");
            writer.WriteLine("endfacet");
        }
        writer.WriteLine("endsolid test");
        return path;
    }

    [Fact]
    public void ReloadSwapsTheMeshKeepsThePlaceAndUndoesInOneStep()
    {
        var document = new Document { PlacementMode = PlacementMode.Off };
        var original = Box(2f);
        var obj = new SceneObject("part", original)
        {
            Transform = Transform.Identity with { Translation = new Vector3(7f, -3f, 0f) },
            Regions = ObjectSupportRegions.From([0, 1], null),
        };
        document.AddObject(obj);
        var transform = obj.Transform;

        var updated = Box(5f);
        document.ReloadObject(obj, updated);

        Assert.Same(updated, obj.Mesh);
        Assert.Equal(transform, obj.Transform);
        Assert.Same(obj, Assert.Single(document.Scene.Objects));
        // The region indexes faces of the old mesh, so it cannot survive the swap.
        Assert.True(obj.Regions.IsEmpty);
        Assert.Equal("Update part from file", document.History.UndoName);

        Assert.True(document.Undo());
        Assert.Same(original, obj.Mesh);
        Assert.Equal([0, 1], obj.Regions.Faces.Order());
    }

    [Fact]
    public void ReloadReSeatsTheModelWhenAutoDropIsOn()
    {
        var document = new Document { PlacementMode = PlacementMode.AutoDrop };
        var obj = new SceneObject("part", Box(2f));
        document.AddObject(obj);
        document.CommitTransform(obj, obj.Transform, obj.Transform);
        Assert.Equal(0f, obj.WorldBounds.Min.Z, 5);

        // New geometry that starts 3 mm above its own origin: auto drop must bring it back down.
        var raised = Box(2f).Translated(new Vector3(0f, 0f, 3f));
        document.ReloadObject(obj, raised);

        Assert.Equal(0f, obj.WorldBounds.Min.Z, 5);
    }

    [Fact]
    public void ReloadDiscardsTheModelsSupportsInTheSameUndoStep()
    {
        var document = new Document { PlacementMode = PlacementMode.Off };
        var obj = new SceneObject("part", Box(2f));
        document.AddObject(obj);
        var origin = new SupportOrigin(obj.Id, 1, obj.Id);
        var tip = new SupportNode
        {
            Type = SupportNodeType.Tip, Position = new Vector3(1f, 1f, 2f), Origin = origin,
        };
        var plate = new SupportNode
        {
            Type = SupportNodeType.Base, Position = new Vector3(1f, 1f, 0f), Origin = origin,
        };
        document.Supports.AddNode(tip);
        document.Supports.AddNode(plate);
        document.Supports.AddSegment(new SupportSegment
        {
            Type = SupportSegmentType.Trunk, NodeA = plate.Id, NodeB = tip.Id, Origin = origin,
        });

        document.ReloadObject(obj, Box(5f));

        Assert.Empty(document.Supports.Nodes);

        Assert.True(document.Undo());
        Assert.Equal(2, document.Supports.Nodes.Count);
    }

    [Fact]
    public void ImportRecordsTheSourceFileAndTheUpdateButtonRereadsIt()
    {
        var path = WriteStl(Box(2f));
        try
        {
            var viewModel = new MainViewModel();
            viewModel.ImportMesh(path);
            var obj = Assert.Single(viewModel.Document.Scene.Objects);
            Assert.Equal(Path.GetFullPath(path), obj.SourcePath);
            var imported = obj.Mesh;

            File.Delete(path);
            File.Move(WriteStl(Box(6f)), path);
            viewModel.ReloadObjectCommand.Execute(obj);

            Assert.NotSame(imported, obj.Mesh);
            Assert.Equal(6f, obj.Mesh.Bounds.Size.Z, 4);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void ImportPlacesTheModelAccordingToTheAutoDropSettings()
    {
        var path = WriteStl(Box(2f).Translated(new Vector3(0f, 0f, 12f)));
        try
        {
            var viewModel = new MainViewModel();

            viewModel.Document.PlacementMode = PlacementMode.AutoDrop;
            viewModel.Document.PlacementHeightMm = 0f;
            viewModel.ImportMesh(path);
            Assert.Equal(0f, viewModel.Document.Scene.Objects[^1].WorldBounds.Min.Z, 4);

            viewModel.Document.PlacementMode = PlacementMode.RaiseAbovePlate;
            viewModel.Document.PlacementHeightMm = 3f;
            viewModel.ImportMesh(path);
            Assert.Equal(3f, viewModel.Document.Scene.Objects[^1].WorldBounds.Min.Z, 4);

            // Placement off still drops an import onto the plate, once (user, 2026-09-09).
            viewModel.Document.PlacementMode = PlacementMode.Off;
            viewModel.ImportMesh(path);
            Assert.Equal(0f, viewModel.Document.Scene.Objects[^1].WorldBounds.Min.Z, 4);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void TheSourceFileSurvivesSaveAndReopen()
    {
        var document = new Document();
        var obj = new SceneObject("part", Box(2f)) { SourcePath = @"C:\models\part.stl" };
        document.AddObject(obj);
        var path = Path.Combine(Path.GetTempPath(), $"danslicer-{Guid.NewGuid():N}.danslicer");
        try
        {
            ProjectFile.Save(path, document, new ProjectViewState());
            var loaded = ProjectFile.Load(path);
            Assert.Equal(@"C:\models\part.stl", loaded.Document.Scene.Objects[0].SourcePath);
            var viewModel = new MainViewModel();
            viewModel.OpenProject(path);
            Assert.Equal(obj.SourcePath, Assert.Single(viewModel.Objects).SourcePath);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
