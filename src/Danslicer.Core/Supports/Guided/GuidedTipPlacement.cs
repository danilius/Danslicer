using System.Numerics;
using Danslicer.Core.Geometry;
using Danslicer.Core.Supports.Generation;

namespace Danslicer.Core.Supports.Guided;

/// <summary>
/// Turns the samples of a guided gesture into tip candidates (SUPPORT-GEOMETRY-SPEC "Guided tip
/// placement", stage 1 of the shared pipeline). Pure: a world-space mesh, sample points with
/// their faces, the placement parameters, and optionally the graph already in the document.
/// </summary>
public static class GuidedTipPlacement
{
    /// <summary>
    /// One candidate per sample, its inward normal taken from the sample's face. A sample within
    /// <see cref="TipPlacementParameters.MinSpacingMm"/> of a candidate already taken from the
    /// same gesture is dropped. When <paramref name="existing"/> is given the gesture is
    /// existing-aware: a sample within <see cref="TipPlacementParameters.ExistingTipClearanceMm"/>
    /// of a tip already in the document is dropped too. Pass null to ignore existing supports,
    /// which is the default (user decision 2026-09-07): whether a gesture defers to what is
    /// already there is the user's explicit choice, never the tool's.
    /// </summary>
    public static IReadOnlyList<TipCandidate> Candidates(Mesh mesh,
        IReadOnlyList<(Vector3 Point, int Face)> samples, TipPlacementParameters parameters,
        SupportGraph? existing = null, float? duplicateRadiusMm = null,
        IReadOnlyList<Vector3>? keepClearOf = null)
    {
        var existingTips = existing?.Nodes
            .Where(node => node.Type == SupportNodeType.Tip)
            .Select(node => node.Position).ToList() ?? [];
        // Samples are spaced along the surface, so around a bend two of them are closer in a
        // straight line than the pitch (a 30° fillet on the roof gripper left a 5 mm gap when
        // the radius was the pitch itself). The duplicate radius is therefore only ever meant
        // to fold together the same point sampled twice — a shared origin, a polygon corner —
        // and defaults to half the spacing, less a hair for rounding.
        var duplicate = MathF.Max(0f, (duplicateRadiusMm ?? parameters.MinSpacingMm * 0.5f) - 1e-3f);
        var duplicateSquared = duplicate * duplicate;
        var clearance = MathF.Max(0f, (parameters.ExistingTipClearanceMm ?? parameters.MinSpacingMm) - 1e-3f);
        var clearanceSquared = clearance * clearance;
        var taken = new List<Vector3>(keepClearOf ?? []);
        var result = new List<TipCandidate>(samples.Count);
        foreach (var (point, face) in samples)
        {
            if ((uint)face >= (uint)mesh.TriangleCount) continue;
            if (existingTips.Any(tip => Vector3.DistanceSquared(tip, point) < clearanceSquared)) continue;
            if (taken.Any(tip => Vector3.DistanceSquared(tip, point) < duplicateSquared)) continue;
            taken.Add(point);
            result.Add(new TipCandidate(point, -mesh.FaceNormals[face], parameters.TipDiameterMm,
                Score: 5f, TipStrategy.Guided, face, parameters.TipShape, parameters.ConeLengthMm,
                parameters.BallDiameterMm, parameters.PenetrationDepthMm, parameters.TipNormalLeadInMm));
        }
        return result;
    }
}
