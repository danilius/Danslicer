using System.Numerics;

namespace Danslicer.Core.Geometry;

/// <summary>
/// Computes the rotation that lays a picked face flat on the build plate. The picked triangle is
/// grown into a cluster of connected triangles whose normals stay within a tolerance of the seed,
/// so a triangulated planar CAD face or a gently faceted surface acts as one face.
/// </summary>
public static class LayFlat
{
    public const float DefaultToleranceDegrees = 3f;

    /// <summary>
    /// Triangle indices of the connected, near-coplanar cluster around <paramref name="seedTriangle"/>.
    /// Connectivity is via shared edges; candidates are compared against the seed normal, not their
    /// neighbour, so the cluster cannot creep around fillets.
    /// </summary>
    public static List<int> Cluster(Mesh mesh, int seedTriangle, float toleranceDegrees = DefaultToleranceDegrees)
    {
        var seedNormal = mesh.FaceNormals[seedTriangle];
        var minDot = MathF.Cos(toleranceDegrees * MathF.PI / 180f);

        // Edge (as an ordered vertex pair) to adjacent triangles.
        var edges = new Dictionary<(int, int), List<int>>(mesh.TriangleCount * 3 / 2);
        for (int t = 0; t < mesh.TriangleCount; t++)
        {
            for (int e = 0; e < 3; e++)
            {
                var a = mesh.Indices[t * 3 + e];
                var b = mesh.Indices[t * 3 + (e + 1) % 3];
                var key = a < b ? (a, b) : (b, a);
                if (!edges.TryGetValue(key, out var list)) edges[key] = list = new List<int>(2);
                list.Add(t);
            }
        }

        var cluster = new List<int>();
        var visited = new HashSet<int> { seedTriangle };
        var queue = new Queue<int>();
        queue.Enqueue(seedTriangle);
        while (queue.Count > 0)
        {
            var t = queue.Dequeue();
            cluster.Add(t);
            for (int e = 0; e < 3; e++)
            {
                var a = mesh.Indices[t * 3 + e];
                var b = mesh.Indices[t * 3 + (e + 1) % 3];
                var key = a < b ? (a, b) : (b, a);
                foreach (var next in edges[key])
                {
                    // The test is against the seed normal, so a rejected triangle is rejected from
                    // every path and can be marked visited outright.
                    if (visited.Add(next) && Vector3.Dot(mesh.FaceNormals[next], seedNormal) >= minDot)
                        queue.Enqueue(next);
                }
            }
        }
        return cluster;
    }

    /// <summary>
    /// Area-weighted world-space normal of a triangle cluster. Normals are derived from transformed
    /// vertices, so non-uniform scale needs no inverse-transpose handling.
    /// </summary>
    public static Vector3 ClusterWorldNormal(Mesh mesh, IReadOnlyList<int> cluster, Matrix4x4 world)
    {
        var sum = Vector3.Zero;
        foreach (var t in cluster)
        {
            mesh.GetTriangle(t, out var a, out var b, out var c);
            var wa = Vector3.Transform(a, world);
            var wb = Vector3.Transform(b, world);
            var wc = Vector3.Transform(c, world);
            sum += Vector3.Cross(wb - wa, wc - wa); // length = 2 * area
        }
        var length = sum.Length();
        return length > 1e-12f ? sum / length : Vector3.UnitZ;
    }

    /// <summary>Shortest-arc rotation taking the world normal to straight down (-Z).</summary>
    public static Quaternion RotationToPlate(Vector3 worldNormal)
    {
        var n = Vector3.Normalize(worldNormal);
        var down = -Vector3.UnitZ;
        var dot = Math.Clamp(Vector3.Dot(n, down), -1f, 1f);
        if (dot > 1f - 1e-7f) return Quaternion.Identity;
        if (dot < -1f + 1e-7f) return Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI);
        var axis = Vector3.Normalize(Vector3.Cross(n, down));
        return Quaternion.CreateFromAxisAngle(axis, MathF.Acos(dot));
    }
}
