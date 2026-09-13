using System.Numerics;
using Danslicer.Core.Geometry;

namespace Danslicer.Core.Supports;

/// <summary>
/// The brush half of support painting (DESIGN 8.3, stage 3): which faces one dab of a brush of
/// radius r, centred on a point of the surface, covers. Pure geometry, like
/// <see cref="SupportRegionSelection"/> — a stroke is just a sequence of dabs unioned together,
/// which is what lets the caller commit a whole stroke as one undo step.
///
/// <para>Adjacency comes from <see cref="SupportRegionSelection"/>'s per-mesh cache, so a drag
/// does not rebuild it on every dab.</para>
///
/// <para><b>Distance is straight-line, but spread is across the surface.</b> A face joins the dab
/// only if it is edge-connected to the face under the cursor AND its nearest point is within the
/// radius. True geodesic distance would be the ideal, and this is not it: on a surface that folds
/// back within the radius — the inside of a tight curl, say — this reaches round the fold, where
/// a geodesic brush would have to travel further and might not arrive. What it will NOT do is
/// bleed through a thin wall onto the far side, because the far side is not edge-connected to the
/// near one; that is the failure that would actually cost the user work, and the connectivity
/// requirement is there to prevent it.</para>
/// </summary>
public static class SupportRegionBrush
{
    /// <summary>
    /// The faces covered by one dab: the seed face, and everything reachable from it across
    /// shared edges whose closest point to <paramref name="centre"/> lies within
    /// <paramref name="radiusMm"/>.
    ///
    /// <para>The seed is always included, however small the radius — a click with a tiny brush
    /// paints the face you clicked rather than nothing at all.</para>
    /// </summary>
    public static IReadOnlySet<int> FacesWithin(Mesh mesh, Vector3 centre, float radiusMm, int seedFace)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        var result = new SortedSet<int>();
        if ((uint)seedFace >= (uint)mesh.TriangleCount) return result;

        var adjacency = SupportRegionSelection.FaceAdjacencyOf(mesh);
        var radiusSquared = MathF.Max(radiusMm, 0f) * MathF.Max(radiusMm, 0f);
        var stack = new Stack<int>();
        result.Add(seedFace);
        stack.Push(seedFace);

        while (stack.Count > 0)
        {
            var face = stack.Pop();
            foreach (var next in adjacency[face])
            {
                if (result.Contains(next)) continue;
                mesh.GetTriangle(next, out var a, out var b, out var c);
                var closest = TriangleQueries.ClosestPointOnTriangle(centre, a, b, c);
                if (Vector3.DistanceSquared(closest, centre) > radiusSquared) continue;
                result.Add(next);
                stack.Push(next);
            }
        }
        return result;
    }
}

/// <summary>
/// One brush stroke in progress: the faces its dabs have covered so far, and the region the
/// stroke started from. Held by the view model between pointer-down and pointer-up.
///
/// <para>The stroke exists so that a drag is ONE undo step. Each dab updates the object's region
/// so the user sees the paint appear, but the undoable edit is committed once, from
/// <see cref="Before"/> to the final set — a stroke that painted 400 faces and undid in 400 steps
/// would be a bug, not a feature.</para>
/// </summary>
public sealed class SupportRegionStroke(ObjectSupportRegions before, bool keepClean, bool erasing)
{
    private readonly HashSet<int> _touched = [];

    /// <summary>The region as it was before the stroke started: the undo target.</summary>
    public ObjectSupportRegions Before { get; } = before;

    /// <summary>Which of the two face sets the stroke edits.</summary>
    public bool KeepClean { get; } = keepClean;

    /// <summary>True for an erasing stroke; a stroke never changes direction mid-drag.</summary>
    public bool Erasing { get; } = erasing;

    /// <summary>Every face any dab of this stroke has covered.</summary>
    public IReadOnlySet<int> Touched => _touched;

    public bool Add(IReadOnlySet<int> faces)
    {
        var before = _touched.Count;
        _touched.UnionWith(faces);
        return _touched.Count != before;
    }

    /// <summary>
    /// The region this stroke produces: the set it started from, with everything the stroke
    /// touched added or removed. Erasing can only remove faces that were painted, so it cannot
    /// leave the region holding a face that is not on the mesh.
    /// </summary>
    public ObjectSupportRegions Apply()
    {
        var active = new HashSet<int>(KeepClean ? Before.KeepCleanFaces : Before.Faces);
        if (Erasing) active.ExceptWith(_touched);
        else active.UnionWith(_touched);
        return KeepClean
            ? ObjectSupportRegions.From(Before.Faces, active)
            : ObjectSupportRegions.From(active, Before.KeepCleanFaces);
    }
}
