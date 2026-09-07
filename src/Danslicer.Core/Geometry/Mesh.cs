using System.Numerics;

namespace Danslicer.Core.Geometry;

/// <summary>
/// Immutable indexed triangle mesh. Positions are welded (shared between triangles),
/// face normals are derived from winding.
/// </summary>
public sealed class Mesh
{
    public Vector3[] Positions { get; }
    public int[] Indices { get; }
    public Vector3[] FaceNormals { get; }
    public Aabb Bounds { get; }

    public int TriangleCount => Indices.Length / 3;
    public int VertexCount => Positions.Length;

    public Mesh(Vector3[] positions, int[] indices)
    {
        if (indices.Length % 3 != 0)
            throw new ArgumentException("Index count must be a multiple of three.", nameof(indices));

        Positions = positions;
        Indices = indices;
        Bounds = Aabb.FromPoints(positions);

        FaceNormals = new Vector3[indices.Length / 3];
        for (int t = 0; t < FaceNormals.Length; t++)
        {
            var a = positions[indices[t * 3]];
            var b = positions[indices[t * 3 + 1]];
            var c = positions[indices[t * 3 + 2]];
            var n = Vector3.Cross(b - a, c - a);
            var len = n.Length();
            FaceNormals[t] = len > 1e-12f ? n / len : Vector3.UnitZ;
        }
    }

    public void GetTriangle(int index, out Vector3 a, out Vector3 b, out Vector3 c)
    {
        a = Positions[Indices[index * 3]];
        b = Positions[Indices[index * 3 + 1]];
        c = Positions[Indices[index * 3 + 2]];
    }

    /// <summary>Area-weighted vertex normals.</summary>
    public Vector3[] ComputeVertexNormals()
    {
        var normals = new Vector3[Positions.Length];
        for (int t = 0; t < TriangleCount; t++)
        {
            GetTriangle(t, out var a, out var b, out var c);
            var weighted = Vector3.Cross(b - a, c - a); // length = 2 * area
            normals[Indices[t * 3]] += weighted;
            normals[Indices[t * 3 + 1]] += weighted;
            normals[Indices[t * 3 + 2]] += weighted;
        }
        for (int i = 0; i < normals.Length; i++)
        {
            var len = normals[i].Length();
            normals[i] = len > 1e-12f ? normals[i] / len : Vector3.UnitZ;
        }
        return normals;
    }

    /// <summary>
    /// Translation that sits the lowest point at Z = 0 and the XY AABB centre at the origin.
    /// No rotation or scaling. Empty meshes yield zero.
    /// </summary>
    public Vector3 SeatTranslation
    {
        get
        {
            if (VertexCount == 0) return Vector3.Zero;
            var b = Bounds;
            return new Vector3(-b.Center.X, -b.Center.Y, -b.Min.Z);
        }
    }

    /// <summary>
    /// The mesh moved by <see cref="SeatTranslation"/>: XY centred on the origin, lowest point at
    /// Z = 0. Files are authored anywhere (the roof gripper sits 3 m from its origin), and an
    /// object whose mesh keeps that offset shows it in every position field (user report
    /// 2026-09-08: "coordinates are probably in pixels"); baking it in at import makes the
    /// transform's translation the model's actual place on the plate.
    /// </summary>
    public Mesh Seated() => Translated(SeatTranslation);

    /// <summary>New mesh with every vertex shifted by <paramref name="delta"/>. Shares indices.</summary>
    public Mesh Translated(Vector3 delta)
    {
        if (delta == Vector3.Zero) return this;
        var positions = new Vector3[Positions.Length];
        for (int i = 0; i < Positions.Length; i++)
            positions[i] = Positions[i] + delta;
        return new Mesh(positions, Indices);
    }

    /// <summary>
    /// Applies a local-space matrix and reverses every triangle. A reflection changes handedness,
    /// so reversing the winding keeps outward faces and counter-clockwise slice contours.
    /// </summary>
    public Mesh Reflected(Matrix4x4 localReflection)
    {
        var positions = new Vector3[Positions.Length];
        for (var i = 0; i < Positions.Length; i++)
            positions[i] = Vector3.Transform(Positions[i], localReflection);

        var indices = new int[Indices.Length];
        for (var triangle = 0; triangle < TriangleCount; triangle++)
        {
            var offset = triangle * 3;
            indices[offset] = Indices[offset];
            indices[offset + 1] = Indices[offset + 2];
            indices[offset + 2] = Indices[offset + 1];
        }
        return new Mesh(positions, indices);
    }

    /// <summary>
    /// Builds a mesh from a triangle soup, welding vertices that are exactly equal.
    /// </summary>
    public static Mesh FromTriangleSoup(ReadOnlySpan<Vector3> vertices)
    {
        if (vertices.Length % 3 != 0)
            throw new ArgumentException("Vertex count must be a multiple of three.", nameof(vertices));

        var lookup = new Dictionary<Vector3, int>(vertices.Length);
        var positions = new List<Vector3>(vertices.Length / 2);
        var indices = new int[vertices.Length];

        for (int i = 0; i < vertices.Length; i++)
        {
            var v = vertices[i];
            if (!lookup.TryGetValue(v, out var index))
            {
                index = positions.Count;
                positions.Add(v);
                lookup.Add(v, index);
            }
            indices[i] = index;
        }

        return new Mesh(positions.ToArray(), indices);
    }
}
