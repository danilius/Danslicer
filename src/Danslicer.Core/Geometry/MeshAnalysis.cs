using System.Numerics;
using System.Runtime.CompilerServices;

namespace Danslicer.Core.Geometry;

/// <summary>
/// One planar patch: a connected set of triangles whose dihedral angles stay below the
/// clustering threshold (DESIGN.md §5.2, default 1°).
/// </summary>
public readonly record struct PlanarPatch(int Id, Vector3 Normal, float AreaMm2, IReadOnlyList<int> Triangles);

/// <summary>
/// Lazily computed, cached-per-<see cref="Mesh"/> derived data (DESIGN.md §4.3 / §5.2):
/// adjacency, angle-defect curvature, sharp edges, planar patches, and a triangle BVH.
/// Keyed off the mesh instance with a <see cref="ConditionalWeakTable{TKey,TValue}"/> so an
/// unreferenced mesh's analysis can be collected. Construction is thread-safe.
/// Do not add fields to <see cref="Mesh"/>.
/// </summary>
public sealed class MeshAnalysis
{
    public const float DefaultSharpEdgeDegrees = 30f;
    public const float DefaultPlanarDegrees = 1f;

    private static readonly ConditionalWeakTable<Mesh, MeshAnalysis> Cache = new();

    private readonly Mesh _mesh;
    private readonly HashSet<int>[] _neighbors;
    private readonly List<int>[] _facesOf;
    private readonly Dictionary<(int A, int B), List<int>> _edgeFaces;
    private readonly float[] _curvature;
    private readonly int[] _patchId;
    private readonly PlanarPatch[] _patches;
    private readonly HashSet<(int A, int B)> _sharpEdgesDefault;
    private readonly bool[] _isCornerDefault;

    public Mesh Mesh => _mesh;
    public IReadOnlyList<float> Curvature => _curvature;
    public IReadOnlyList<int> PatchId => _patchId;
    public IReadOnlyList<PlanarPatch> Patches => _patches;
    public TriangleBvh Bvh { get; }

    /// <summary>Sharp edges at <see cref="DefaultSharpEdgeDegrees"/> (30°).</summary>
    public IReadOnlySet<(int A, int B)> SharpEdges => _sharpEdgesDefault;

    internal IReadOnlyList<HashSet<int>> VertexNeighbors => _neighbors;
    internal IReadOnlyList<List<int>> FacesOfVertex => _facesOf;
    internal IReadOnlyDictionary<(int A, int B), List<int>> EdgeFaces => _edgeFaces;

    private MeshAnalysis(Mesh mesh)
    {
        _mesh = mesh;
        var n = mesh.VertexCount;
        _neighbors = new HashSet<int>[n];
        _facesOf = new List<int>[n];
        for (int i = 0; i < n; i++)
        {
            _neighbors[i] = new HashSet<int>();
            _facesOf[i] = new List<int>(6);
        }

        _edgeFaces = new Dictionary<(int A, int B), List<int>>(Math.Max(1, mesh.TriangleCount * 2));
        for (int t = 0; t < mesh.TriangleCount; t++)
        {
            int ia = mesh.Indices[t * 3], ib = mesh.Indices[t * 3 + 1], ic = mesh.Indices[t * 3 + 2];
            _facesOf[ia].Add(t);
            _facesOf[ib].Add(t);
            _facesOf[ic].Add(t);
            AddEdge(_edgeFaces, ia, ib, t);
            AddEdge(_edgeFaces, ib, ic, t);
            AddEdge(_edgeFaces, ic, ia, t);
            _neighbors[ia].Add(ib); _neighbors[ia].Add(ic);
            _neighbors[ib].Add(ia); _neighbors[ib].Add(ic);
            _neighbors[ic].Add(ia); _neighbors[ic].Add(ib);
        }

        _curvature = ComputeCurvature(mesh, _facesOf, _edgeFaces);
        (_patchId, _patches) = ComputePlanarPatches(mesh, _edgeFaces, DefaultPlanarDegrees);
        (_sharpEdgesDefault, _isCornerDefault) = ComputeSharp(mesh, _edgeFaces, DefaultSharpEdgeDegrees);
        Bvh = TriangleBvh.Build(mesh);
    }

    /// <summary>Cached analysis for <paramref name="mesh"/>. Thread-safe; one instance per mesh.</summary>
    public static MeshAnalysis For(Mesh mesh)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        return Cache.GetValue(mesh, static m => new MeshAnalysis(m));
    }

    /// <summary>Builds analysis without touching the cache. Used for cold-path timings.</summary>
    public static MeshAnalysis Compute(Mesh mesh)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        return new MeshAnalysis(mesh);
    }

    public TriangleBvh BvhForTriangles(IReadOnlyCollection<int> triangles) =>
        TriangleBvh.Build(_mesh, triangles);

    public IReadOnlySet<(int A, int B)> SharpEdgesAt(float degrees)
    {
        if (MathF.Abs(degrees - DefaultSharpEdgeDegrees) < 1e-6f) return _sharpEdgesDefault;
        return ComputeSharp(_mesh, _edgeFaces, degrees).Edges;
    }

    public bool IsCorner(int vertex, float sharpEdgeDegrees = DefaultSharpEdgeDegrees)
    {
        if ((uint)vertex >= (uint)_mesh.VertexCount)
            throw new ArgumentOutOfRangeException(nameof(vertex));
        if (MathF.Abs(sharpEdgeDegrees - DefaultSharpEdgeDegrees) < 1e-6f)
            return _isCornerDefault[vertex];
        var sharp = SharpEdgesAt(sharpEdgeDegrees);
        var count = 0;
        foreach (var n in _neighbors[vertex])
        {
            var key = vertex < n ? (vertex, n) : (n, vertex);
            if (sharp.Contains(key)) count++;
        }
        return count >= 3;
    }

    public bool TryGetEdgeFaces(int a, int b, out List<int> faces) =>
        _edgeFaces.TryGetValue(a < b ? (a, b) : (b, a), out faces!);

    public float MaxVertexCurvature(int face)
    {
        int ia = _mesh.Indices[face * 3], ib = _mesh.Indices[face * 3 + 1], ic = _mesh.Indices[face * 3 + 2];
        return MathF.Max(_curvature[ia], MathF.Max(_curvature[ib], _curvature[ic]));
    }

    private static (HashSet<(int A, int B)> Edges, bool[] IsCorner) ComputeSharp(
        Mesh mesh, Dictionary<(int A, int B), List<int>> edgeFaces, float degrees)
    {
        var minDot = MathF.Cos(degrees * MathF.PI / 180f);
        var sharp = new HashSet<(int A, int B)>();
        var sharpCount = new int[mesh.VertexCount];
        foreach (var (edge, faces) in edgeFaces)
        {
            bool isSharp;
            if (faces.Count < 2)
            {
                isSharp = true;
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

        var isCorner = new bool[mesh.VertexCount];
        for (int i = 0; i < isCorner.Length; i++)
            isCorner[i] = sharpCount[i] >= 3;
        return (sharp, isCorner);
    }

    private static (int[] PatchId, PlanarPatch[] Patches) ComputePlanarPatches(
        Mesh mesh, Dictionary<(int A, int B), List<int>> edgeFaces, float degrees)
    {
        var minDot = MathF.Cos(degrees * MathF.PI / 180f);
        var patchId = new int[mesh.TriangleCount];
        Array.Fill(patchId, -1);
        var patches = new List<PlanarPatch>();
        var stack = new Stack<int>();
        var members = new List<int>();

        for (int seed = 0; seed < mesh.TriangleCount; seed++)
        {
            if (patchId[seed] >= 0) continue;
            members.Clear();
            stack.Push(seed);
            patchId[seed] = patches.Count;
            var id = patches.Count;
            var normalSum = Vector3.Zero;
            float area = 0;
            while (stack.Count > 0)
            {
                var t = stack.Pop();
                members.Add(t);
                mesh.GetTriangle(t, out var a, out var b, out var c);
                var twice = Vector3.Cross(b - a, c - a);
                area += twice.Length() * 0.5f;
                normalSum += mesh.FaceNormals[t] * (twice.Length() * 0.5f);

                int ia = mesh.Indices[t * 3], ib = mesh.Indices[t * 3 + 1], ic = mesh.Indices[t * 3 + 2];
                Push(ia, ib);
                Push(ib, ic);
                Push(ic, ia);

                void Push(int u, int v)
                {
                    var key = u < v ? (u, v) : (v, u);
                    if (!edgeFaces.TryGetValue(key, out var faces)) return;
                    foreach (var next in faces)
                    {
                        if (patchId[next] >= 0) continue;
                        var dot = Vector3.Dot(mesh.FaceNormals[t], mesh.FaceNormals[next]);
                        if (dot < minDot) continue;
                        patchId[next] = id;
                        stack.Push(next);
                    }
                }
            }

            var nlen = normalSum.Length();
            var normal = nlen > 1e-12f ? normalSum / nlen : mesh.FaceNormals[seed];
            patches.Add(new PlanarPatch(id, normal, area, members.ToArray()));
        }

        return (patchId, patches.ToArray());
    }

    /// <summary>
    /// Angle-defect Gaussian curvature, scaled so a cube corner (defect π/2) maps to 1.
    /// Ridges that are not corners still score via a dihedral boost (same formula as the
    /// original per-call MeshFeatures so placement scores stay stable).
    /// </summary>
    private static float[] ComputeCurvature(
        Mesh mesh, List<int>[] facesOf, Dictionary<(int A, int B), List<int>> edgeFaces)
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

    private static void AddEdge(Dictionary<(int A, int B), List<int>> map, int a, int b, int face)
    {
        var key = a < b ? (a, b) : (b, a);
        if (!map.TryGetValue(key, out var list)) map[key] = list = new List<int>(2);
        list.Add(face);
    }
}
