using System.Numerics;
using Danslicer.Core.Geometry;

namespace Danslicer.Core.Supports.Generation;

/// <summary>
/// Per-mesh adjacency, curvature, and sharp-feature flags. Derived on demand for a placement
/// call; nothing is cached on <see cref="Mesh"/> (DESIGN.md §4.3 is a later change request).
/// </summary>
internal sealed class MeshFeatures
{
    private readonly Mesh _mesh;
    private readonly HashSet<int>[] _neighbors;
    private readonly List<int>[] _facesOf;
    private readonly Dictionary<(int A, int B), List<int>> _edgeFaces;
    private readonly float[] _curvature;
    private readonly HashSet<(int A, int B)> _sharpEdges;
    private readonly bool[] _isCorner;

    public IReadOnlyList<float> Curvature => _curvature;
    public IReadOnlySet<(int A, int B)> SharpEdges => _sharpEdges;
    public IReadOnlyList<bool> IsCorner => _isCorner;

    private MeshFeatures(
        Mesh mesh,
        HashSet<int>[] neighbors,
        List<int>[] facesOf,
        Dictionary<(int A, int B), List<int>> edgeFaces,
        float[] curvature,
        HashSet<(int A, int B)> sharpEdges,
        bool[] isCorner)
    {
        _mesh = mesh;
        _neighbors = neighbors;
        _facesOf = facesOf;
        _edgeFaces = edgeFaces;
        _curvature = curvature;
        _sharpEdges = sharpEdges;
        _isCorner = isCorner;
    }

    public static MeshFeatures Build(Mesh mesh, float sharpEdgeDegrees)
    {
        var n = mesh.VertexCount;
        var neighbors = new HashSet<int>[n];
        var facesOf = new List<int>[n];
        for (int i = 0; i < n; i++)
        {
            neighbors[i] = new HashSet<int>();
            facesOf[i] = new List<int>(6);
        }

        var edgeFaces = new Dictionary<(int A, int B), List<int>>(mesh.TriangleCount * 2);
        for (int t = 0; t < mesh.TriangleCount; t++)
        {
            int ia = mesh.Indices[t * 3], ib = mesh.Indices[t * 3 + 1], ic = mesh.Indices[t * 3 + 2];
            facesOf[ia].Add(t);
            facesOf[ib].Add(t);
            facesOf[ic].Add(t);
            AddEdge(edgeFaces, ia, ib, t);
            AddEdge(edgeFaces, ib, ic, t);
            AddEdge(edgeFaces, ic, ia, t);
            neighbors[ia].Add(ib); neighbors[ia].Add(ic);
            neighbors[ib].Add(ia); neighbors[ib].Add(ic);
            neighbors[ic].Add(ia); neighbors[ic].Add(ib);
        }

        var minDot = MathF.Cos(sharpEdgeDegrees * MathF.PI / 180f);
        var sharp = new HashSet<(int A, int B)>();
        var sharpCount = new int[n];
        foreach (var (edge, faces) in edgeFaces)
        {
            bool isSharp;
            if (faces.Count < 2)
            {
                isSharp = true; // boundary treated as a feature
            }
            else
            {
                var dot = Vector3.Dot(mesh.FaceNormals[faces[0]], mesh.FaceNormals[faces[1]]);
                isSharp = dot < minDot;
            }
            if (!isSharp) continue;
            sharp.Add(edge);
            sharpCount[edge.A]++;
            sharpCount[edge.B]++;
        }

        var isCorner = new bool[n];
        for (int i = 0; i < n; i++)
            isCorner[i] = sharpCount[i] >= 3;

        var curvature = ComputeCurvature(mesh, facesOf, edgeFaces);
        return new MeshFeatures(mesh, neighbors, facesOf, edgeFaces, curvature, sharp, isCorner);
    }

    public bool TryGetEdgeFaces(int a, int b, out List<int> faces) =>
        _edgeFaces.TryGetValue(a < b ? (a, b) : (b, a), out faces!);

    public float MaxVertexCurvature(int face)
    {
        int ia = _mesh.Indices[face * 3], ib = _mesh.Indices[face * 3 + 1], ic = _mesh.Indices[face * 3 + 2];
        return MathF.Max(_curvature[ia], MathF.Max(_curvature[ib], _curvature[ic]));
    }

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
            foreach (var t in _facesOf[v])
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
            foreach (var n in _neighbors[v])
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

    private static void AddEdge(Dictionary<(int A, int B), List<int>> map, int a, int b, int face)
    {
        var key = a < b ? (a, b) : (b, a);
        if (!map.TryGetValue(key, out var list)) map[key] = list = new List<int>(2);
        list.Add(face);
    }

    /// <summary>
    /// Angle-defect Gaussian curvature, scaled so a cube corner (defect π/2) maps to 1.
    /// Ridges that are not corners still score via the defect at their vertices.
    /// </summary>
    private static float[] ComputeCurvature(Mesh mesh, List<int>[] facesOf, Dictionary<(int A, int B), List<int>> edgeFaces)
    {
        var n = mesh.VertexCount;
        var angleSum = new float[n];
        for (int t = 0; t < mesh.TriangleCount; t++)
        {
            int ia = mesh.Indices[t * 3], ib = mesh.Indices[t * 3 + 1], ic = mesh.Indices[t * 3 + 2];
            var a = mesh.Positions[ia];
            var b = mesh.Positions[ib];
            var c = mesh.Positions[ic];
            angleSum[ia] += CornerAngle(a, b, c);
            angleSum[ib] += CornerAngle(b, a, c);
            angleSum[ic] += CornerAngle(c, a, b);
        }

        var curvature = new float[n];
        const float cubeCornerDefect = MathF.PI / 2f;
        for (int v = 0; v < n; v++)
        {
            if (facesOf[v].Count == 0) continue;
            var defect = MathF.Abs(MathF.PI * 2f - angleSum[v]);
            curvature[v] = MathF.Min(1f, defect / cubeCornerDefect);
        }

        // Boost vertices on sharp convex ridges so organic and CAD ridges both light up.
        foreach (var (edge, faces) in edgeFaces)
        {
            if (faces.Count < 2) continue;
            var dot = Vector3.Dot(mesh.FaceNormals[faces[0]], mesh.FaceNormals[faces[1]]);
            var dihedral = MathF.Acos(Math.Clamp(dot, -1f, 1f));
            if (dihedral < 15f * MathF.PI / 180f) continue;
            var ridge = MathF.Min(1f, dihedral / (MathF.PI / 2f));
            curvature[edge.A] = MathF.Max(curvature[edge.A], ridge * 0.6f);
            curvature[edge.B] = MathF.Max(curvature[edge.B], ridge * 0.6f);
        }
        return curvature;
    }

    private static float CornerAngle(Vector3 vertex, Vector3 b, Vector3 c)
    {
        var u = Vector3.Normalize(b - vertex);
        var v = Vector3.Normalize(c - vertex);
        return MathF.Acos(Math.Clamp(Vector3.Dot(u, v), -1f, 1f));
    }

    public static Vector3 ClosestPointOnTriangle(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
    {
        var ab = b - a;
        var ac = c - a;
        var ap = p - a;
        var d1 = Vector3.Dot(ab, ap);
        var d2 = Vector3.Dot(ac, ap);
        if (d1 <= 0 && d2 <= 0) return a;

        var bp = p - b;
        var d3 = Vector3.Dot(ab, bp);
        var d4 = Vector3.Dot(ac, bp);
        if (d3 >= 0 && d4 <= d3) return b;

        var vc = d1 * d4 - d3 * d2;
        if (vc <= 0 && d1 >= 0 && d3 <= 0)
        {
            var v = d1 / (d1 - d3);
            return a + v * ab;
        }

        var cp = p - c;
        var d5 = Vector3.Dot(ab, cp);
        var d6 = Vector3.Dot(ac, cp);
        if (d6 >= 0 && d5 <= d6) return c;

        var vb = d5 * d2 - d1 * d6;
        if (vb <= 0 && d2 >= 0 && d6 <= 0)
        {
            var w = d2 / (d2 - d6);
            return a + w * ac;
        }

        var va = d3 * d6 - d5 * d4;
        if (va <= 0 && (d4 - d3) >= 0 && (d5 - d6) >= 0)
        {
            var w = (d4 - d3) / ((d4 - d3) + (d5 - d6));
            return b + w * (c - b);
        }

        var denom = 1f / (va + vb + vc);
        return a + ab * (vb * denom) + ac * (vc * denom);
    }
}
