using System.Numerics;
using Clipper2Lib;
using Danslicer.Core;
using Danslicer.Core.Config;
using Danslicer.Core.Geometry;
using Danslicer.Core.Scene;
using Danslicer.Core.Slicing;

namespace Danslicer.Tests;

public sealed class ObjectDuplicateMirrorTests
{
    [Fact]
    public void DuplicateSharesMeshButHasIndependentTransformAndOneUndoStep()
    {
        var document = new Document { PlacementMode = PlacementMode.Off };
        var original = new SceneObject("part", AsymmetricPrism())
        {
            Transform = Transform.Identity with { Translation = new Vector3(-2f, -1f, 0f) },
        };
        document.AddObject(original);
        var originalTransform = original.Transform;

        var copy = Assert.Single(document.DuplicateSelection());

        Assert.Same(original.Mesh, copy.Mesh);
        Assert.Equal(originalTransform.Translation + new Vector3(5f, 5f, 0f),
            copy.Transform.Translation);
        Assert.Equal([copy], document.Selection);
        Assert.Equal("Duplicate part", document.History.UndoName);

        var beforeMove = copy.Transform;
        document.CommitTransform(copy, beforeMove, beforeMove with
        {
            Translation = beforeMove.Translation + Vector3.UnitX,
        }, applyPlacement: false);
        Assert.Equal(originalTransform, original.Transform);

        Assert.True(document.Undo()); // Move.
        Assert.True(document.Undo()); // Duplicate.
        Assert.Equal([original], document.Scene.Objects);
        Assert.True(document.Redo());
        Assert.Equal(2, document.Scene.Objects.Count);
        Assert.Same(copy, document.Scene.Objects[1]);
    }

    [Fact]
    public void DuplicateAppliesAutomaticPlacementToTheCopy()
    {
        var document = new Document();
        var original = new SceneObject("floating", AsymmetricPrism())
        {
            Transform = Transform.Identity with { Translation = new Vector3(0f, 0f, 9f) },
        };
        document.AddObject(original);

        var copy = Assert.Single(document.DuplicateSelection());

        Assert.Equal(9f, original.WorldBounds.Min.Z, 5);
        Assert.Equal(0f, copy.WorldBounds.Min.Z, 5);
    }

    [Theory]
    [InlineData(ObjectMirrorAxis.X)]
    [InlineData(ObjectMirrorAxis.Y)]
    [InlineData(ObjectMirrorAxis.Z)]
    public void MirrorPreservesOutwardWindingSliceAreaAndLitPixels(ObjectMirrorAxis axis)
    {
        var document = new Document();
        var originalMesh = AsymmetricPrism();
        var obj = new SceneObject("asymmetric", originalMesh)
        {
            Transform = Transform.Identity with { Translation = new Vector3(-2f, -1f, 0f) },
        };
        document.AddObject(obj);
        var beforeTransform = obj.Transform;
        var beforeNormals = WorldFaceNormals(obj);
        var beforeContours = MeshSlicer.PolygonsAt(
            new MeshSlicer.PreparedMesh(obj.Mesh, obj.Transform.ToMatrix()), 1.0);
        var settings = PrintSettings.Default with { LayerHeight = 0.5f, AntiAliasing = false };
        var beforeSlice = Slicer.Slice([obj], document.Printer, settings);

        document.MirrorSelection(axis);

        Assert.NotSame(originalMesh, obj.Mesh);
        var afterContours = MeshSlicer.PolygonsAt(
            new MeshSlicer.PreparedMesh(obj.Mesh, obj.Transform.ToMatrix()), 1.0);
        Assert.Equal(MeshSlicer.AreaMm2(beforeContours), MeshSlicer.AreaMm2(afterContours), 5);
        Assert.All(afterContours, contour => Assert.True(Clipper.Area(contour) > 0));
        var afterSlice = Slicer.Slice([obj], document.Printer, settings);
        Assert.Equal(beforeSlice.Layers.Select(layer => layer.LitPixels),
            afterSlice.Layers.Select(layer => layer.LitPixels));

        var afterNormals = WorldFaceNormals(obj);
        for (var triangle = 0; triangle < beforeNormals.Length; triangle++)
            Assert.True(Vector3.Dot(Reflect(beforeNormals[triangle], axis), afterNormals[triangle]) > 0.9999f);

        var mirroredMesh = obj.Mesh;
        var mirroredTransform = obj.Transform;
        Assert.True(document.Undo());
        Assert.Same(originalMesh, obj.Mesh);
        Assert.Equal(beforeTransform, obj.Transform);
        Assert.True(document.Redo());
        Assert.Same(mirroredMesh, obj.Mesh);
        Assert.Equal(mirroredTransform, obj.Transform);
    }

    [Fact]
    public void MirrorUsesWholeSelectionBoundsAndAutoDropsInTheSameUndoStep()
    {
        var document = new Document();
        var left = new SceneObject("left", AsymmetricPrism())
        {
            Transform = Transform.Identity with { Translation = new Vector3(-12f, -1f, 9f) },
        };
        var right = new SceneObject("right", AsymmetricPrism())
        {
            Transform = Transform.Identity with { Translation = new Vector3(8f, -1f, 9f) },
        };
        document.AddObject(left);
        document.AddObject(right);
        document.Select(left);
        document.Select(right, additive: true);
        var beforeLeft = left.WorldBounds.Center.X;
        var beforeRight = right.WorldBounds.Center.X;

        document.MirrorSelection(ObjectMirrorAxis.X);

        Assert.Equal(beforeRight, left.WorldBounds.Center.X, 4);
        Assert.Equal(beforeLeft, right.WorldBounds.Center.X, 4);
        Assert.Equal(0f, left.WorldBounds.Min.Z, 5);
        Assert.Equal(0f, right.WorldBounds.Min.Z, 5);
        Assert.Equal("Mirror 2 objects X", document.History.UndoName);
        Assert.True(document.Undo());
        Assert.Equal(9f, left.WorldBounds.Min.Z, 5);
        Assert.Equal(9f, right.WorldBounds.Min.Z, 5);
    }

    [Fact]
    public void MirrorKeepsAnInvertibleTransformAndReflectsWorldGeometry()
    {
        var document = new Document { PlacementMode = PlacementMode.Off };
        var obj = new SceneObject("transformed", AsymmetricPrism())
        {
            Transform = new Transform(new Vector3(3f, -4f, 2f),
                Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 6f),
                new Vector3(2f, 0.5f, 1.5f)),
        };
        document.AddObject(obj);
        var beforeTransform = obj.Transform;
        var centreX = obj.WorldBounds.Center.X;
        var beforePoints = obj.Mesh.Positions
            .Select(point => Vector3.Transform(point, beforeTransform.ToMatrix())).ToArray();

        document.MirrorSelection(ObjectMirrorAxis.X);

        Assert.Equal(beforeTransform, obj.Transform);
        var afterPoints = obj.Mesh.Positions
            .Select(point => Vector3.Transform(point, obj.Transform.ToMatrix())).ToArray();
        for (var i = 0; i < beforePoints.Length; i++)
        {
            Assert.Equal(2f * centreX - beforePoints[i].X, afterPoints[i].X, 4);
            Assert.Equal(beforePoints[i].Y, afterPoints[i].Y, 4);
            Assert.Equal(beforePoints[i].Z, afterPoints[i].Z, 4);
        }
    }

    private static Vector3[] WorldFaceNormals(SceneObject obj)
    {
        var normals = new Vector3[obj.Mesh.TriangleCount];
        var world = obj.Transform.ToMatrix();
        for (var triangle = 0; triangle < normals.Length; triangle++)
        {
            obj.Mesh.GetTriangle(triangle, out var a, out var b, out var c);
            a = Vector3.Transform(a, world);
            b = Vector3.Transform(b, world);
            c = Vector3.Transform(c, world);
            normals[triangle] = Vector3.Normalize(Vector3.Cross(b - a, c - a));
        }
        return normals;
    }

    private static Vector3 Reflect(Vector3 normal, ObjectMirrorAxis axis) => axis switch
    {
        ObjectMirrorAxis.X => normal with { X = -normal.X },
        ObjectMirrorAxis.Y => normal with { Y = -normal.Y },
        _ => normal with { Z = -normal.Z },
    };

    private static Mesh AsymmetricPrism()
    {
        Vector3[] positions =
        [
            new(0, 0, 0), new(4, 0, 0), new(0, 2, 0),
            new(0, 0, 2), new(4, 0, 2), new(0, 2, 2),
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
}
