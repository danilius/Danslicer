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
        SupportGraph? existing = null)
    {
        var existingTips = existing?.Nodes
            .Where(node => node.Type == SupportNodeType.Tip)
            .Select(node => node.Position).ToList() ?? [];
        // Samples sit exactly one pitch apart, and the pitch is usually the minimum spacing
        // itself; a hair of tolerance keeps rounding from dropping every second one.
        var minSpacing = MathF.Max(0f, parameters.MinSpacingMm - 1e-3f);
        var minSpacingSquared = minSpacing * minSpacing;
        var clearance = MathF.Max(0f, (parameters.ExistingTipClearanceMm ?? parameters.MinSpacingMm) - 1e-3f);
        var clearanceSquared = clearance * clearance;
        var taken = new List<Vector3>();
        var result = new List<TipCandidate>(samples.Count);
        foreach (var (point, face) in samples)
        {
            if ((uint)face >= (uint)mesh.TriangleCount) continue;
            if (existingTips.Any(tip => Vector3.DistanceSquared(tip, point) < clearanceSquared)) continue;
            if (taken.Any(tip => Vector3.DistanceSquared(tip, point) < minSpacingSquared)) continue;
            taken.Add(point);
            result.Add(new TipCandidate(point, -mesh.FaceNormals[face], parameters.TipDiameterMm,
                Score: 5f, TipStrategy.Guided, face, parameters.TipShape, parameters.ConeLengthMm,
                parameters.BallDiameterMm, parameters.PenetrationDepthMm, parameters.TipNormalLeadInMm));
        }
        return result;
    }
}
