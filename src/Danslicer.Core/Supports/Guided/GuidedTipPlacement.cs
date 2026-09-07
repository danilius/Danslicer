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
    /// <see cref="TipPlacementParameters.MinSpacingMm"/> of an existing tip is dropped — that is
    /// how a line started on an existing tip does not put a second cone on the same spot — and
    /// so is one that close to a candidate already taken from the same gesture.
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
        var taken = new List<Vector3>();
        var result = new List<TipCandidate>(samples.Count);
        foreach (var (point, face) in samples)
        {
            if ((uint)face >= (uint)mesh.TriangleCount) continue;
            if (existingTips.Any(tip => Vector3.DistanceSquared(tip, point) < minSpacingSquared)) continue;
            if (taken.Any(tip => Vector3.DistanceSquared(tip, point) < minSpacingSquared)) continue;
            taken.Add(point);
            result.Add(new TipCandidate(point, -mesh.FaceNormals[face], parameters.TipDiameterMm,
                Score: 5f, TipStrategy.Guided, face, parameters.TipShape, parameters.ConeLengthMm,
                parameters.BallDiameterMm, parameters.PenetrationDepthMm, parameters.TipNormalLeadInMm));
        }
        return result;
    }
}
