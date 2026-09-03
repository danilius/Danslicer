using System.Diagnostics;
using System.Numerics;
using Danslicer.Core.Geometry;
using Danslicer.Core.Supports.Checks;
using Xunit.Abstractions;

namespace Danslicer.Tests;

public class MeshAnalysisTests
{
    private readonly ITestOutputHelper _output;
    public MeshAnalysisTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void CacheReturnsTheSameInstanceAndIsThreadSafe()
    {
        var mesh = Meshes.Box(4, 4, 4);
        var a = MeshAnalysis.For(mesh);
        var b = MeshAnalysis.For(mesh);
        Assert.Same(a, b);

        var hits = new MeshAnalysis[Math.Max(4, Environment.ProcessorCount)];
        Parallel.For(0, hits.Length, i => hits[i] = MeshAnalysis.For(mesh));
        Assert.All(hits, h => Assert.Same(a, h));
    }

    [Fact]
    public void DistinctMeshesHaveDistinctAnalyses()
    {
        var a = Meshes.Box(4, 4, 4);
        var b = Meshes.Box(4, 4, 4);
        Assert.NotSame(MeshAnalysis.For(a), MeshAnalysis.For(b));
    }

    [Fact]
    public void CubeHasSixPlanarPatchesTwelveSharpEdgesAndUnitCornerCurvature()
    {
        var mesh = Meshes.Box(10, 10, 10);
        var analysis = MeshAnalysis.For(mesh);

        Assert.Equal(6, analysis.Patches.Count);
        Assert.All(analysis.Patches, p => Assert.Equal(2, p.Triangles.Count));
        Assert.Equal(12, analysis.SharpEdges.Count);

        for (int v = 0; v < mesh.VertexCount; v++)
        {
            Assert.True(analysis.IsCorner(v));
            Assert.Equal(1f, analysis.Curvature[v], 3);
        }
    }

    [Fact]
    public void BvhRayCastAgreesWithBruteForce()
    {
        var mesh = Meshes.UvSphere(radius: 5, center: new Vector3(1, -2, 4), slices: 24, stacks: 16);
        var bvh = MeshAnalysis.For(mesh).Bvh;
        var rng = new Random(20260903);
        for (int i = 0; i < 200; i++)
        {
            var origin = new Vector3(rng.NextSingle() * 20 - 10, rng.NextSingle() * 20 - 10, rng.NextSingle() * 20 - 10);
            var dir = Vector3.Normalize(new Vector3(rng.NextSingle() * 2 - 1, rng.NextSingle() * 2 - 1, rng.NextSingle() * 2 - 1));
            var ray = new Ray(origin, dir);
            var bruteT = bvh.BruteForceRayCast(ray, out var bruteTri);
            var hit = bvh.RayCast(ray, out var tri, out var t);
            if (float.IsPositiveInfinity(bruteT))
            {
                Assert.False(hit);
            }
            else
            {
                Assert.True(hit);
                Assert.Equal(bruteTri, tri);
                Assert.Equal(bruteT, t, 4);
            }
        }
    }

    [Fact]
    public void BvhClosestPointAndSegmentAgreeWithBruteForce()
    {
        var mesh = Meshes.FloatingBox(8, 6, 4, z: 2);
        var bvh = MeshAnalysis.For(mesh).Bvh;
        var rng = new Random(7);
        for (int i = 0; i < 150; i++)
        {
            var p = new Vector3(rng.NextSingle() * 16 - 4, rng.NextSingle() * 12 - 3, rng.NextSingle() * 10 - 1);
            var dBvh = bvh.ClosestPoint(p, out var onBvh, out var triBvh);
            var dBrute = bvh.BruteForceClosestPoint(p, out var onBrute, out var triBrute);
            Assert.Equal(dBrute, dBvh, 4);
            Assert.Equal(triBrute, triBvh);
            Assert.InRange(Vector3.Distance(onBvh, onBrute), 0, 1e-4f);

            var q = p + new Vector3(rng.NextSingle() * 2 - 1, rng.NextSingle() * 2 - 1, rng.NextSingle() * 2 - 1);
            var dsBvh = bvh.ClosestToSegment(p, q, out var sBvh, out var mBvh, out _);
            var dsBrute = bvh.BruteForceClosestToSegment(p, q, out var sBrute, out var mBrute);
            Assert.Equal(dsBrute, dsBvh, 4);
            Assert.InRange(Vector3.Distance(sBvh, sBrute), 0, 1e-4f);
            Assert.InRange(Vector3.Distance(mBvh, mBrute), 0, 1e-4f);
        }
    }

    [Fact]
    public void BvhMeshMeshAgreesWithBruteForceAndWithBruteDistanceQuery()
    {
        var a = Meshes.Box(10, 10, 10);
        var b = Meshes.Box(10, 10, 10, new Vector3(10.4f, 0, 0));
        var bvhA = MeshAnalysis.For(a).Bvh;
        var bvhB = MeshAnalysis.For(b).Bvh;
        var dBvh = bvhA.ClosestTo(bvhB, out var pa, out var pb);
        var dBrute = bvhA.BruteForceClosestTo(bvhB, out var qa, out var qb);
        Assert.Equal(dBrute, dBvh, 4);
        Assert.InRange(dBvh, 0.39f, 0.41f);

        var queryA = new MeshDistanceQuery(a);
        var queryB = new MeshDistanceQuery(b);
        var dQuery = queryA.ClosestToMesh(queryB, out _, out _);
        Assert.Equal(dQuery, dBvh, 4);
        Assert.InRange(Vector3.Distance(pa, qa), 0, 1e-3f);
        Assert.InRange(Vector3.Distance(pb, qb), 0, 1e-3f);
    }

    [Fact]
    public void SubsetBvhOnlyQueriesThoseFaces()
    {
        var mesh = Meshes.FloatingBox(10, 10, 10, z: 5);
        var bottom = Enumerable.Range(0, mesh.TriangleCount).Where(t => mesh.FaceNormals[t].Z < -0.5f).ToHashSet();
        Assert.NotEmpty(bottom);
        var bvh = MeshAnalysis.For(mesh).BvhForTriangles(bottom);
        var p = new Vector3(5, 5, 0);
        var d = bvh.ClosestPoint(p, out var on, out var tri);
        Assert.Contains(tri, bottom);
        Assert.Equal(5f, on.Z, 3);
        Assert.Equal(5f, d, 3);
    }

    [Fact]
    public void SecondCacheHitIsFarFasterThanAColdBuildOnAMillionTriangleMesh()
    {
        // 708×708 quads = 1,000,448 triangles. Cold build includes adjacency, curvature, patches, BVH.
        var mesh = Meshes.Heightfield(708, size: 100f, amplitude: 2f);
        Assert.InRange(mesh.TriangleCount, 900_000, 1_100_000);
        _output.WriteLine($"triangles: {mesh.TriangleCount:N0}  vertices: {mesh.VertexCount:N0}");

        var cold = Stopwatch.StartNew();
        var first = MeshAnalysis.For(mesh);
        cold.Stop();
        _output.WriteLine($"cold For (fills cache): {cold.Elapsed.TotalMilliseconds:0.0} ms");

        var warm = Stopwatch.StartNew();
        var again = MeshAnalysis.For(mesh);
        warm.Stop();
        _output.WriteLine($"warm For: {warm.Elapsed.TotalMilliseconds:0.000} ms");

        Assert.Same(first, again);
        Assert.Equal(mesh.TriangleCount, first.Bvh.TriangleCount);
        Assert.True(warm.Elapsed < TimeSpan.FromMilliseconds(50),
            $"warm cache hit took {warm.Elapsed.TotalMilliseconds:0.000} ms; expected a dictionary lookup");
        Assert.True(cold.Elapsed > warm.Elapsed,
            "first For should pay for construction; warm For should not");
    }
}
