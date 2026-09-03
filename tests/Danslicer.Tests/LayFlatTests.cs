using System.Numerics;
using Danslicer.Core;
using Danslicer.Core.Geometry;
using Danslicer.Core.Scene;

namespace Danslicer.Tests;

public class LayFlatTests
{
    // Unit cube with outward winding, welded: 12 triangles, 8 vertices.
    private static Mesh Cube()
    {
        var p = new Vector3[8];
        for (int i = 0; i < 8; i++) p[i] = new Vector3(i & 1, (i >> 1) & 1, (i >> 2) & 1);
        int[] indices =
        {
            0, 2, 3, 0, 3, 1, // -Z
            4, 5, 7, 4, 7, 6, // +Z
            0, 1, 5, 0, 5, 4, // -Y
            2, 6, 7, 2, 7, 3, // +Y
            0, 4, 6, 0, 6, 2, // -X
            1, 3, 7, 1, 7, 5, // +X
        };
        return new Mesh(p, indices);
    }

    [Fact]
    public void ClusterStopsAtSharpEdges()
    {
        var mesh = Cube();
        for (int seed = 0; seed < mesh.TriangleCount; seed++)
        {
            var cluster = LayFlat.Cluster(mesh, seed);
            // Each cube face is two coplanar triangles; the cluster must not cross the 90° edges.
            Assert.Equal(2, cluster.Count);
            Assert.Contains(seed, cluster);
            Assert.Equal(mesh.FaceNormals[cluster[0]], mesh.FaceNormals[cluster[1]]);
        }
    }

    [Fact]
    public void RotationToPlateMapsNormalDown()
    {
        foreach (var n in new[]
        {
            Vector3.UnitZ, -Vector3.UnitZ, Vector3.UnitX, Vector3.UnitY,
            Vector3.Normalize(new Vector3(1, 2, 3)), Vector3.Normalize(new Vector3(-0.2f, 0.9f, -0.5f)),
        })
        {
            var q = LayFlat.RotationToPlate(n);
            var rotated = Vector3.Transform(n, q);
            Assert.Equal(-1f, rotated.Z, 5);
        }
    }

    [Fact]
    public void LayFlatOnFaceRestsPickedFaceOnPlate()
    {
        var doc = new Document();
        var obj = new SceneObject("cube", Cube());
        // Tilt the cube and lift it so nothing is aligned to start with.
        obj.Transform = Transform.Identity with
        {
            EulerDegrees = new Vector3(30, 20, 10),
            Translation = new Vector3(5, -3, 7),
        };
        doc.AddObject(obj);

        // Seed triangle 2 belongs to the +Z face; afterwards that face must be the bottom.
        doc.LayFlatOnFace(obj, 2);

        var world = obj.Transform.ToMatrix();
        var mesh = obj.Mesh;
        // The +Z face is vertices 4..7; all four must sit on the plate.
        for (int i = 4; i < 8; i++)
        {
            var z = Vector3.Transform(mesh.Positions[i], world).Z;
            Assert.Equal(0f, z, 4);
        }
        // And nothing may dip below the plate.
        foreach (var p in mesh.Positions)
            Assert.True(Vector3.Transform(p, world).Z > -1e-4f);

        // One undo step restores the original pose.
        Assert.Equal("Lay flat on face", doc.History.UndoName);
    }
}
