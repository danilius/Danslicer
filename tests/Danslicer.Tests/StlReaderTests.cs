using System.Numerics;
using System.Text;
using Danslicer.Core.Geometry;
using Danslicer.Core.IO;

namespace Danslicer.Tests;

public class StlReaderTests
{
    // A unit cube as 12 triangles, outward winding.
    private static readonly Vector3[] CubeSoup = BuildCube();

    private static Vector3[] BuildCube()
    {
        var p = new Vector3[8];
        for (int i = 0; i < 8; i++) p[i] = new Vector3(i & 1, (i >> 1) & 1, (i >> 2) & 1);
        int[][] faces =
        {
            new[] { 0, 2, 3, 0, 3, 1 }, // -Z
            new[] { 4, 5, 7, 4, 7, 6 }, // +Z
            new[] { 0, 1, 5, 0, 5, 4 }, // -Y
            new[] { 2, 6, 7, 2, 7, 3 }, // +Y
            new[] { 0, 4, 6, 0, 6, 2 }, // -X
            new[] { 1, 3, 7, 1, 7, 5 }, // +X
        };
        return faces.SelectMany(f => f).Select(i => p[i]).ToArray();
    }

    private static byte[] BinaryStl(Vector3[] soup)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write(new byte[80]);
        w.Write((uint)(soup.Length / 3));
        for (int t = 0; t < soup.Length / 3; t++)
        {
            w.Write(0f); w.Write(0f); w.Write(0f);
            for (int v = 0; v < 3; v++)
            {
                var p = soup[t * 3 + v];
                w.Write(p.X); w.Write(p.Y); w.Write(p.Z);
            }
            w.Write((ushort)0);
        }
        return ms.ToArray();
    }

    private static byte[] AsciiStl(Vector3[] soup)
    {
        var sb = new StringBuilder("solid cube\n");
        for (int t = 0; t < soup.Length / 3; t++)
        {
            sb.Append("  facet normal 0 0 0\n    outer loop\n");
            for (int v = 0; v < 3; v++)
            {
                var p = soup[t * 3 + v];
                sb.Append($"      vertex {p.X} {p.Y} {p.Z}\n");
            }
            sb.Append("    endloop\n  endfacet\n");
        }
        sb.Append("endsolid cube\n");
        return Encoding.ASCII.GetBytes(sb.ToString());
    }

    private static void AssertCube(Mesh mesh)
    {
        Assert.Equal(12, mesh.TriangleCount);
        Assert.Equal(8, mesh.VertexCount);
        Assert.Equal(Vector3.Zero, mesh.Bounds.Min);
        Assert.Equal(Vector3.One, mesh.Bounds.Max);
        // Outward normals: every face normal points away from the centre.
        for (int t = 0; t < mesh.TriangleCount; t++)
        {
            mesh.GetTriangle(t, out var a, out var b, out var c);
            var centroid = (a + b + c) / 3f - new Vector3(0.5f);
            Assert.True(Vector3.Dot(centroid, mesh.FaceNormals[t]) > 0, $"triangle {t} is inward facing");
        }
    }

    [Fact]
    public void ReadsBinary() => AssertCube(StlReader.Read(new MemoryStream(BinaryStl(CubeSoup))));

    [Fact]
    public void ReadsAscii() => AssertCube(StlReader.Read(new MemoryStream(AsciiStl(CubeSoup))));

    [Fact]
    public void BinaryWithSolidHeaderIsStillBinary()
    {
        var data = BinaryStl(CubeSoup);
        Encoding.ASCII.GetBytes("solid exported").CopyTo(data, 0);
        AssertCube(StlReader.Read(new MemoryStream(data)));
    }

    [Fact]
    public void RayHitsCube()
    {
        var mesh = StlReader.Read(new MemoryStream(BinaryStl(CubeSoup)));
        var ray = new Ray(new Vector3(0.5f, 0.5f, 5f), -Vector3.UnitZ);
        var t = ray.IntersectMesh(mesh, out _);
        Assert.NotNull(t);
        Assert.Equal(4f, t!.Value, 4);
        Assert.Null(new Ray(new Vector3(2f, 2f, 5f), -Vector3.UnitZ).IntersectMesh(mesh, out _));
    }
}
