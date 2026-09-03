using System.Numerics;
using Danslicer.Core.Geometry;

namespace Danslicer.Tests;

public class MeshSeatTests
{
    [Fact]
    public void SeatTranslationDropsToPlateAndCentresXy()
    {
        var mesh = Box(new Vector3(100, 200, 50), new Vector3(110, 230, 80));
        var offset = mesh.SeatTranslation;
        Assert.Equal(new Vector3(-105, -215, -50), offset);

        var seated = mesh.Translated(offset);
        Assert.Equal(0, seated.Bounds.Min.Z, 5);
        Assert.Equal(0, seated.Bounds.Center.X, 5);
        Assert.Equal(0, seated.Bounds.Center.Y, 5);
        Assert.Equal(mesh.Bounds.Size, seated.Bounds.Size);
        Assert.Equal(mesh.TriangleCount, seated.TriangleCount);
    }

    [Fact]
    public void AlreadySeatedMeshHasZeroOffset()
    {
        var mesh = Box(new Vector3(-5, -5, 0), new Vector3(5, 5, 10));
        Assert.Equal(Vector3.Zero, mesh.SeatTranslation);
        Assert.Same(mesh, mesh.Translated(Vector3.Zero));
    }

    private static Mesh Box(Vector3 min, Vector3 max)
    {
        var (a, b) = (min, max);
        var corners = new Vector3[]
        {
            new(a.X, a.Y, a.Z), new(b.X, a.Y, a.Z), new(b.X, b.Y, a.Z), new(a.X, b.Y, a.Z),
            new(a.X, a.Y, b.Z), new(b.X, a.Y, b.Z), new(b.X, b.Y, b.Z), new(a.X, b.Y, b.Z),
        };
        int[] quads =
        [
            0, 3, 2, 1, 4, 5, 6, 7, 0, 1, 5, 4, 2, 3, 7, 6, 0, 4, 7, 3, 1, 2, 6, 5,
        ];
        var soup = new List<Vector3>();
        for (int q = 0; q < quads.Length; q += 4)
        {
            soup.Add(corners[quads[q]]); soup.Add(corners[quads[q + 1]]); soup.Add(corners[quads[q + 2]]);
            soup.Add(corners[quads[q]]); soup.Add(corners[quads[q + 2]]); soup.Add(corners[quads[q + 3]]);
        }
        return Mesh.FromTriangleSoup(soup.ToArray());
    }
}
