using System.Numerics;
using Danslicer.Core.Geometry;

namespace Danslicer.Core.Supports.Generation;

/// <summary>Why <see cref="ContactFaceFilter"/> dropped a candidate.</summary>
public enum ContactFaceRejectionReason
{
    /// <summary>The face normal was farther from straight down than <see cref="TipPlacementParameters.MaxContactFaceAngleDegrees"/> allows.</summary>
    Angle,

    /// <summary>The face passed the angle test, but the straight-down ray to the plate was blocked by the mesh.</summary>
    Occluded,
}

/// <summary>A candidate <see cref="ContactFaceFilter"/> dropped, and why. Nothing consumes this list yet
/// (a later "internal supports" feature will: contacts occluded from the plate are the population
/// a strut standing on a void floor instead of the plate would use), so it is reported rather than
/// silently discarded.</summary>
public readonly record struct RejectedContactFace(TipCandidate Candidate, ContactFaceRejectionReason Reason);

/// <summary>
/// Narrows which faces are eligible to receive a support contact at all (user request 2026-09-04:
/// chunky CAD parts such as the gripper test model should only receive supports from their lower
/// faces, with side supports added by hand). Applied to the candidate list after placement
/// (DESIGN.md §8.4 stage 1), not to the region: islands are discovered from the sliced layer stack
/// rather than the region, so a region-level filter would miss them. Filtering the candidates
/// instead is uniform across islands, local minima, overhangs, edges and corners.
/// <para>
/// Two independent settings, both on <see cref="TipPlacementParameters"/>:
/// <see cref="TipPlacementParameters.MaxContactFaceAngleDegrees"/> (angle from straight down; 90°,
/// the default, admits every downward-facing candidate — today's behaviour) and
/// <see cref="TipPlacementParameters.RequireContactSeesPlate"/> (an additional unobstructed
/// straight-down line of sight to the plate; off by default). A downward-facing candidate always
/// has a straight-down ray that geometrically reaches the plate plane unless something else in the
/// mesh is in the way, so "sees plate" is not a third exclusive mode — it composes with the angle
/// setting rather than replacing it.
/// </para>
/// A pure function of its inputs.
/// </summary>
public static class ContactFaceFilter
{
    /// <summary>
    /// Returns the subset of <paramref name="candidates"/> that pass both settings. Returns
    /// <paramref name="candidates"/> unchanged (same reference) when both are at their defaults
    /// (90°, sees-plate off), so the default path allocates nothing extra and stays byte-identical
    /// to today's output.
    /// </summary>
    public static IReadOnlyList<TipCandidate> Apply(
        IReadOnlyList<TipCandidate> candidates,
        Mesh mesh,
        TipPlacementParameters parameters) =>
        Apply(candidates, mesh, parameters, out _);

    /// <summary>As <see cref="Apply(IReadOnlyList{TipCandidate}, Mesh, TipPlacementParameters)"/>,
    /// additionally reporting every dropped candidate and why.</summary>
    public static IReadOnlyList<TipCandidate> Apply(
        IReadOnlyList<TipCandidate> candidates,
        Mesh mesh,
        TipPlacementParameters parameters,
        out IReadOnlyList<RejectedContactFace> rejected)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(parameters);

        var maxAngle = float.IsFinite(parameters.MaxContactFaceAngleDegrees)
            ? Math.Clamp(parameters.MaxContactFaceAngleDegrees, 0f, 90f)
            : 90f;
        var requireSeesPlate = parameters.RequireContactSeesPlate;

        // 90° admits every downward-facing candidate; with sees-plate off too, nothing is ever
        // rejected, so skip the pass entirely and hand back the exact same list.
        if (maxAngle >= 90f && !requireSeesPlate)
        {
            rejected = Array.Empty<RejectedContactFace>();
            return candidates;
        }
        if (candidates.Count == 0)
        {
            rejected = Array.Empty<RejectedContactFace>();
            return candidates;
        }

        var bvh = requireSeesPlate ? MeshAnalysis.For(mesh).Bvh : null;
        var kept = new List<TipCandidate>(candidates.Count);
        List<RejectedContactFace>? dropped = null;

        foreach (var candidate in candidates)
        {
            SupportGenerationMonitor.Check();
            // A painted region already says which faces the user wants supported; a global angle
            // rule must not overrule that, or painting a shallow face would silently do nothing.
            if (candidate.Strategy == TipStrategy.RegionGrid)
            {
                kept.Add(candidate);
                continue;
            }
            var normal = mesh.FaceNormals[candidate.FaceIndex];
            if (!IsWithinDownwardAngle(normal, maxAngle))
            {
                (dropped ??= []).Add(new RejectedContactFace(candidate, ContactFaceRejectionReason.Angle));
                continue;
            }
            if (bvh is not null && !SeesPlate(bvh, candidate, parameters))
            {
                (dropped ??= []).Add(new RejectedContactFace(candidate, ContactFaceRejectionReason.Occluded));
                continue;
            }
            kept.Add(candidate);
        }

        rejected = dropped ?? (IReadOnlyList<RejectedContactFace>)Array.Empty<RejectedContactFace>();
        return kept;
    }

    /// <summary>
    /// True when <paramref name="outwardNormal"/> is at or within <paramref name="maxAngleDegrees"/>
    /// of straight down (0,0,-1). This is the complement of
    /// <see cref="TipPlacementParameters.OverhangDegrees"/>, which measures from vertical instead:
    /// a face with 80° overhang (nearly horizontal, facing down) is only 10° from straight down.
    /// </summary>
    private static bool IsWithinDownwardAngle(Vector3 outwardNormal, float maxAngleDegrees)
    {
        var length = outwardNormal.Length();
        if (length <= 1e-12f) return false;
        // Component of the (unit) normal pointing straight down; cos(angle from straight down).
        var down = Math.Clamp(-outwardNormal.Z / length, -1f, 1f);
        var angleFromDown = MathF.Acos(down) * (180f / MathF.PI);
        return angleFromDown <= maxAngleDegrees;
    }

    /// <summary>
    /// Casts a ray straight down from the contact point and requires it to reach
    /// <see cref="TipPlacementParameters.PlateZ"/> without hitting the mesh again. Offsets the
    /// origin a small epsilon below the contact so the ray does not immediately re-hit the
    /// originating face.
    /// </summary>
    private static bool SeesPlate(TriangleBvh bvh, TipCandidate candidate, TipPlacementParameters parameters)
    {
        const float epsilon = 1e-3f;
        var remaining = candidate.Point.Z - epsilon - parameters.PlateZ;
        if (remaining <= 0f) return true;

        var origin = candidate.Point - new Vector3(0f, 0f, epsilon);
        var ray = new Ray(origin, -Vector3.UnitZ);
        if (bvh.RayCast(ray, out var face, out var t) && face >= 0 && t < remaining - epsilon)
            return false;
        return true;
    }
}
