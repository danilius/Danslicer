using System.Numerics;
using Danslicer.Core.Geometry;

namespace Danslicer.Core.Supports.Generation;

/// <summary>
/// Placement-facing view of <see cref="MeshAnalysis"/>: sharp-edge threshold, local minima,
/// overhang patches. Topology, curvature and the BVH come from the per-mesh cache.
/// </summary>
internal sealed class MeshFeatures
{
    private readonly Mesh _mesh;
    private readonly MeshAnalysis _analysis;
    private readonly IReadOnlySet<(int A, int B)> _sharpEdges;
    private readonly bool[] _isCorner;

    public MeshAnalysis Analysis => _analysis;
    public IReadOnlyList<float> Curvature => _analysis.Curvature;
    public IReadOnlySet<(int A, int B)> SharpEdges => _sharpEdges;
    public IReadOnlyList<bool> IsCorner => _isCorner;

    private MeshFeatures(Mesh mesh, MeshAnalysis analysis, IReadOnlySet<(int A, int B)> sharpEdges, bool[] isCorner)
    {
        _mesh = mesh;
        _analysis = analysis;
        _sharpEdges = sharpEdges;
        _isCorner = isCorner;
    }

    public static MeshFeatures Build(Mesh mesh, float sharpEdgeDegrees)
    {
        var analysis = MeshAnalysis.For(mesh);
        var sharp = analysis.SharpEdgesAt(sharpEdgeDegrees);
        var sharpCount = new int[mesh.VertexCount];
        foreach (var (a, b) in sharp)
        {
            sharpCount[a]++;
            sharpCount[b]++;
        }
        var isCorner = new bool[mesh.VertexCount];
        for (int i = 0; i < isCorner.Length; i++)
            isCorner[i] = sharpCount[i] >= 3;
        return new MeshFeatures(mesh, analysis, sharp, isCorner);
    }

    public bool TryGetEdgeFaces(int a, int b, out List<int> faces) =>
        _analysis.TryGetEdgeFaces(a, b, out faces!);

    public float MaxVertexCurvature(int face) => _analysis.MaxVertexCurvature(face);

    public TipStrategy FeatureAt(Vector3 point, int face, float edgeEpsilon)
    {
        int ia = _mesh.Indices[face * 3], ib = _mesh.Indices[face * 3 + 1], ic = _mesh.Indices[face * 3 + 2];
        var pa = _mesh.Positions[ia];
        var pb = _mesh.Positions[ib];
        var pc = _mesh.Positions[ic];
        var e2 = edgeEpsilon * edgeEpsilon;

        var nearA = Vector3.DistanceSquared(point, pa) <= e2;
        var nearB = Vector3.DistanceSquared(point, pb) <= e2;
        var nearC = Vector3.DistanceSquared(point, pc) <= e2;
        if (nearA && _isCorner[ia]) return TipStrategy.Corner;
        if (nearB && _isCorner[ib]) return TipStrategy.Corner;
        if (nearC && _isCorner[ic]) return TipStrategy.Corner;

        if (OnSharp(ia, ib, pa, pb, point, e2) ||
            OnSharp(ib, ic, pb, pc, point, e2) ||
            OnSharp(ic, ia, pc, pa, point, e2))
            return TipStrategy.Edge;

        return TipStrategy.Overhang;
    }

    /// <summary>
    /// Vertices that are local Z-minima among their neighbours, incident to a downward region
    /// face, and above the plate. Yielded in vertex-index order.
    /// </summary>
    public IEnumerable<(int Vertex, Vector3 Position, Vector3 OutwardNormal, int Face)> LocalMinima(
        HashSet<int> region, float plateZ, float layerHeight)
    {
        var cutoff = plateZ + layerHeight + 1e-4f;
        for (int v = 0; v < _mesh.VertexCount; v++)
        {
            var p = _mesh.Positions[v];
            if (p.Z <= cutoff) continue;

            int bestFace = -1;
            var bestDown = 0f;
            foreach (var t in _analysis.FacesOfVertex[v])
            {
                if (!region.Contains(t)) continue;
                var down = -_mesh.FaceNormals[t].Z;
                if (down > bestDown)
                {
                    bestDown = down;
                    bestFace = t;
                }
            }
            if (bestFace < 0 || bestDown <= 1e-4f) continue;

            var z = p.Z;
            var hasHigher = false;
            var isMin = true;
            foreach (var n in _analysis.VertexNeighbors[v])
            {
                var nz = _mesh.Positions[n].Z;
                if (nz < z - 1e-5f) { isMin = false; break; }
                if (nz > z + 1e-5f) hasHigher = true;
            }
            if (!isMin || !hasHigher) continue;

            yield return (v, p, _mesh.FaceNormals[bestFace], bestFace);
        }
    }

    /// <summary>Area in mm² of the connected overhang-face patch containing each region face.</summary>
    public float[] OverhangPatchArea(HashSet<int> region, TipPlacementParameters parameters)
    {
        var area = new float[_mesh.TriangleCount];
        var visited = new bool[_mesh.TriangleCount];
        var stack = new Stack<int>();
        var patch = new List<int>();

        foreach (var seed in region)
        {
            if (seed < 0 || seed >= _mesh.TriangleCount || visited[seed]) continue;
            if (!parameters.IsOverhang(_mesh.FaceNormals[seed])) continue;

            patch.Clear();
            stack.Push(seed);
            visited[seed] = true;
            float sum = 0;
            while (stack.Count > 0)
            {
                var t = stack.Pop();
                patch.Add(t);
                sum += TriangleArea(t);
                int ia = _mesh.Indices[t * 3], ib = _mesh.Indices[t * 3 + 1], ic = _mesh.Indices[t * 3 + 2];
                PushAdj(ia, ib);
                PushAdj(ib, ic);
                PushAdj(ic, ia);
            }
            foreach (var t in patch) area[t] = sum;

            void PushAdj(int a, int b)
            {
                if (!TryGetEdgeFaces(a, b, out var faces)) return;
                foreach (var next in faces)
                {
                    if (visited[next] || !region.Contains(next)) continue;
                    if (!parameters.IsOverhang(_mesh.FaceNormals[next])) continue;
                    visited[next] = true;
                    stack.Push(next);
                }
            }
        }
        return area;
    }

    private float TriangleArea(int t)
    {
        _mesh.GetTriangle(t, out var a, out var b, out var c);
        return Vector3.Cross(b - a, c - a).Length() * 0.5f;
    }

    private bool OnSharp(int ia, int ib, Vector3 pa, Vector3 pb, Vector3 point, float e2)
    {
        var key = ia < ib ? (ia, ib) : (ib, ia);
        if (!_sharpEdges.Contains(key)) return false;
        return DistanceSquaredToSegment(point, pa, pb) <= e2;
    }

    private static float DistanceSquaredToSegment(Vector3 p, Vector3 a, Vector3 b)
    {
        var ab = b - a;
        var len2 = ab.LengthSquared();
        if (len2 < 1e-16f) return Vector3.DistanceSquared(p, a);
        var t = Math.Clamp(Vector3.Dot(p - a, ab) / len2, 0f, 1f);
        var q = a + ab * t;
        return Vector3.DistanceSquared(p, q);
    }

    public static Vector3 ClosestPointOnTriangle(Vector3 p, Vector3 a, Vector3 b, Vector3 c) =>
        TriangleQueries.ClosestPointOnTriangle(p, a, b, c);
}
