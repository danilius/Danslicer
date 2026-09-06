using System.Numerics;
using System.Runtime.CompilerServices;
using Danslicer.Core.Geometry;
using Danslicer.Core.Supports.Generation;

namespace Danslicer.Core.Supports;

/// <summary>
/// The set-producing half of support painting (DESIGN 8.3, stage 2): pure functions from a mesh
/// and a face set to a new face set. Nothing here mutates a document, touches a renderer or knows
/// what a selection is for — the caller wraps a result in one undoable
/// <see cref="Document.SetSupportRegions"/> edit, which is what makes a whole grow-or-shrink a
/// single undo step rather than one per face.
///
/// <para>Every operation is a function of its arguments alone, so a live-adjustable threshold is
/// just the same call again with a different number: the UI never has to keep incremental state
/// it could get out of step with.</para>
///
/// <para>Results are returned sorted, and the sorted set is what callers persist. Face sets live
/// in hash containers whose iteration order is not defined, and a project file that changed
/// byte-for-byte between two runs of the same operations would be a nuisance to review.</para>
/// </summary>
public static class SupportRegionSelection
{
    /// <summary>
    /// Grows outward from <paramref name="seeds"/> across shared edges, crossing an edge only
    /// while the dihedral angle between its two faces is at most
    /// <paramref name="maxDihedralDegrees"/>. Zero degrees selects only exactly-coplanar
    /// neighbours; the default patch threshold (1°) reproduces "click a planar patch".
    ///
    /// <para>The angle is measured between face normals, so it is the turn between the faces: a
    /// flat continuation is 0°, and the corner of a cube is 90°.</para>
    /// </summary>
    public static IReadOnlySet<int> GrowByDihedral(Mesh mesh, IEnumerable<int> seeds,
        float maxDihedralDegrees)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(seeds);
        var adjacency = FaceAdjacency(mesh);
        // Cosine decreases with the angle, so "turns no more than the threshold" is "dot of at
        // least cos(threshold)". The tolerance keeps a pair that is coplanar to float precision
        // on the inside of a 0° threshold.
        var minDot = MathF.Cos(Math.Clamp(maxDihedralDegrees, 0f, 180f) * MathF.PI / 180f) - 1e-5f;

        var result = new HashSet<int>();
        var stack = new Stack<int>();
        foreach (var seed in seeds)
        {
            if (!InRange(mesh, seed) || !result.Add(seed)) continue;
            stack.Push(seed);
        }
        while (stack.Count > 0)
        {
            var face = stack.Pop();
            foreach (var next in adjacency[face])
            {
                if (result.Contains(next)) continue;
                if (Vector3.Dot(mesh.FaceNormals[face], mesh.FaceNormals[next]) < minDot) continue;
                result.Add(next);
                stack.Push(next);
            }
        }
        return Sorted(result);
    }

    /// <summary>
    /// Every face that leans further from vertical than <paramref name="overhangDegrees"/>,
    /// optionally restricted to <paramref name="limitTo"/>.
    ///
    /// <para>The convention is generation's, deliberately and exactly: the angle is measured from
    /// vertical, so 0° is a vertical wall and 90° a horizontal underside, upward-facing faces
    /// score 0°, and the comparison is a strict greater-than — a face at exactly the threshold is
    /// NOT an overhang. Anything else would select a different set of faces from the one
    /// generation then places tips on, which is the one thing this operation must not do. It
    /// calls <see cref="TipPlacementParameters.IsOverhang"/> rather than re-deriving it.</para>
    /// </summary>
    public static IReadOnlySet<int> FacingDown(Mesh mesh, float overhangDegrees,
        IReadOnlySet<int>? limitTo = null)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        var parameters = TipPlacementParameters.Default with { OverhangAngleDegrees = overhangDegrees };
        var result = new HashSet<int>();
        for (var face = 0; face < mesh.TriangleCount; face++)
        {
            if (limitTo is not null && !limitTo.Contains(face)) continue;
            if (parameters.IsOverhang(mesh.FaceNormals[face])) result.Add(face);
        }
        return Sorted(result);
    }

    /// <summary>Every face of the mesh that is not in <paramref name="faces"/>.</summary>
    public static IReadOnlySet<int> Invert(Mesh mesh, IReadOnlySet<int> faces)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(faces);
        var result = new HashSet<int>();
        for (var face = 0; face < mesh.TriangleCount; face++)
            if (!faces.Contains(face)) result.Add(face);
        return Sorted(result);
    }

    /// <summary>The set plus every face sharing an edge with it.</summary>
    public static IReadOnlySet<int> Grow(Mesh mesh, IReadOnlySet<int> faces)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(faces);
        var adjacency = FaceAdjacency(mesh);
        var result = new HashSet<int>(faces.Where(face => InRange(mesh, face)));
        foreach (var face in faces)
        {
            if (!InRange(mesh, face)) continue;
            foreach (var next in adjacency[face]) result.Add(next);
        }
        return Sorted(result);
    }

    /// <summary>
    /// The set without its boundary: a face survives only if every neighbour across its edges is
    /// also in the set. Grow after Shrink restores an interior region, which is what makes the
    /// pair usable for tidying a ragged brush stroke.
    /// </summary>
    public static IReadOnlySet<int> Shrink(Mesh mesh, IReadOnlySet<int> faces)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(faces);
        var adjacency = FaceAdjacency(mesh);
        var result = new HashSet<int>();
        foreach (var face in faces)
        {
            if (!InRange(mesh, face)) continue;
            var interior = true;
            foreach (var next in adjacency[face])
            {
                if (faces.Contains(next)) continue;
                interior = false;
                break;
            }
            if (interior) result.Add(face);
        }
        return Sorted(result);
    }

    /// <summary>
    /// Everything edge-connected to <paramref name="seeds"/>, whatever the angles between faces.
    /// This is <see cref="GrowByDihedral"/> with no angle limit, and says so by calling it.
    /// </summary>
    public static IReadOnlySet<int> Connected(Mesh mesh, IEnumerable<int> seeds) =>
        GrowByDihedral(mesh, seeds, 180f);

    /// <summary>
    /// Cached per mesh, because hover preview calls these operations on every mouse move and
    /// rebuilding the adjacency of a hundred thousand triangles at 60 Hz is the difference
    /// between a live highlight and a slideshow. Keyed weakly, so an unloaded mesh's table goes
    /// with it.
    /// </summary>
    private static readonly ConditionalWeakTable<Mesh, List<int>[]> AdjacencyCache = new();

    private static List<int>[] FaceAdjacency(Mesh mesh) =>
        AdjacencyCache.GetValue(mesh, BuildFaceAdjacency);

    /// <summary>The same cached adjacency, for the brush — one table per mesh, not two.</summary>
    internal static IReadOnlyList<IReadOnlyList<int>> FaceAdjacencyOf(Mesh mesh) => FaceAdjacency(mesh);

    /// <summary>
    /// Faces sharing an edge, for every face of the mesh. Built from the mesh indices rather than
    /// through <see cref="MeshAnalysis"/>, whose cache also carries curvature, planar patches and
    /// a BVH that none of these operations need.
    /// </summary>
    private static List<int>[] BuildFaceAdjacency(Mesh mesh)
    {
        var edges = new Dictionary<(int A, int B), List<int>>();
        for (var face = 0; face < mesh.TriangleCount; face++)
        {
            int a = mesh.Indices[face * 3], b = mesh.Indices[face * 3 + 1], c = mesh.Indices[face * 3 + 2];
            Add(a, b, face);
            Add(b, c, face);
            Add(c, a, face);
        }

        var adjacency = new List<int>[mesh.TriangleCount];
        for (var face = 0; face < adjacency.Length; face++) adjacency[face] = [];
        foreach (var (_, faces) in edges)
        {
            for (var i = 0; i < faces.Count; i++)
                for (var j = 0; j < faces.Count; j++)
                    if (i != j && !adjacency[faces[i]].Contains(faces[j]))
                        adjacency[faces[i]].Add(faces[j]);
        }
        // Sorted so a traversal visits neighbours in the same order on every run.
        foreach (var list in adjacency) list.Sort();
        return adjacency;

        void Add(int u, int v, int face)
        {
            var key = u < v ? (u, v) : (v, u);
            if (!edges.TryGetValue(key, out var list)) edges[key] = list = [];
            list.Add(face);
        }
    }

    private static bool InRange(Mesh mesh, int face) => (uint)face < (uint)mesh.TriangleCount;

    private static IReadOnlySet<int> Sorted(HashSet<int> faces) => new SortedSet<int>(faces);
}
