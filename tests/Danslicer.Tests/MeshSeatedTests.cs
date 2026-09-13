using System.Numerics;
using Danslicer.Core;
using Danslicer.Core.Config;
using Danslicer.Core.Geometry;
using Danslicer.Core.Scene;

namespace Danslicer.Tests;

/// <summary>
/// Import seating (user report 2026-09-08): a file authored far from its origin must not show
/// that offset in the position fields, so the mesh is seated at import and a reload keeps the
/// model where it is.
/// </summary>
public sealed class MeshSeatedTests
{
    private static Mesh FarAwayTriangle() => new(
        [new Vector3(1000, 2000, 300), new(1010, 2000, 300), new(1000, 2010, 320)], [0, 1, 2]);

    [Fact]
    public void SeatedMeshIsCentredInXyWithItsLowestPointOnThePlate()
    {
        var seated = FarAwayTriangle().Seated();

        Assert.Equal(0f, seated.Bounds.Center.X, 4);
        Assert.Equal(0f, seated.Bounds.Center.Y, 4);
        Assert.Equal(0f, seated.Bounds.Min.Z, 4);
        Assert.Equal(new Vector3(10, 10, 20), seated.Bounds.Max - seated.Bounds.Min);
    }

    [Fact]
    public void SeatingAnAlreadySeatedMeshChangesNothing()
    {
        var seated = FarAwayTriangle().Seated();
        Assert.Same(seated, seated.Seated());
    }

    [Fact]
    public void ReloadWithACompensatedTransformKeepsTheModelWhereItWas()
    {
        // An object imported before seating: raw mesh, translation compensating the offset.
        var document = new Document();
        var raw = FarAwayTriangle();
        var obj = new SceneObject("far", raw)
        {
            Transform = Transform.Identity with { Translation = -raw.Bounds.Center + new Vector3(5, 0, 0) },
        };
        document.AddObject(obj);
        var worldBefore = raw.Positions.Select(p => Vector3.Transform(p, obj.Transform.ToMatrix())).ToArray();

        var seat = raw.SeatTranslation;
        var t = obj.Transform;
        var shift = Vector3.Transform(seat * t.Scale, t.Rotation);
        document.PlacementMode = PlacementMode.Off;
        document.ReloadObject(obj, raw.Translated(seat), t with { Translation = t.Translation - shift });

        var worldAfter = obj.Mesh.Positions.Select(p => Vector3.Transform(p, obj.Transform.ToMatrix())).ToArray();
        for (var i = 0; i < worldBefore.Length; i++)
            Assert.True(Vector3.Distance(worldBefore[i], worldAfter[i]) < 1e-3f, $"vertex {i} moved");
    }
}
