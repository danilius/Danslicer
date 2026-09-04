using System.Numerics;
using Clipper2Lib;
using Danslicer.Core.Slicing;

namespace Danslicer.Core.Supports.Generation;

/// <summary>
/// Converts isolated fine-feature contacts after density clustering has had first refusal.
/// Islands already carry their first-appearance area. For local minima, a horizontal slice
/// 0.5 mm above the contact is a cheap, deterministic proxy for the supported feature's section.
/// </summary>
internal static class FineFeatureMiniClassifier
{
    internal const float LocalMinimumProbeHeightMm = 0.5f;

    public static IReadOnlyList<TipCandidate> Measure(
        IReadOnlyList<TipCandidate> candidates,
        IReadOnlyList<SliceLayer> layers,
        TipPlacementParameters parameters)
    {
        if (!parameters.EnableMiniSupports)
            return candidates;

        var output = candidates.ToArray();
        foreach (var item in candidates.Select((candidate, index) => (Candidate: candidate, Index: index))
                     .Where(item => item.Candidate.Strategy == TipStrategy.LocalMinimum))
        {
            var area = LocalComponentArea(layers, item.Candidate.Point,
                parameters.LayerHeightMm, LocalMinimumProbeHeightMm);
            if (area is { } measured && float.IsFinite(measured) && measured > 0)
                output[item.Index] = item.Candidate with { FineFeatureAreaMm2 = measured };
        }
        return output;
    }

    public static IReadOnlyList<TipCandidate> Apply(
        IReadOnlyList<TipCandidate> candidates,
        TipPlacementParameters parameters)
    {
        if (!parameters.EnableMiniSupports ||
            !float.IsFinite(parameters.FineFeatureMaxAreaMm2) ||
            parameters.FineFeatureMaxAreaMm2 <= 0)
            return candidates;

        var output = candidates.ToArray();
        var nextClusterId = candidates.Where(candidate => candidate.MiniClusterId is not null)
            .Select(candidate => candidate.MiniClusterId!.Value)
            .DefaultIfEmpty(0).Max() + 1;
        var eligible = candidates.Select((candidate, index) => (Candidate: candidate, Index: index))
            .Where(item => item.Candidate.MiniClusterId is null &&
                           item.Candidate.Strategy is TipStrategy.Island or TipStrategy.LocalMinimum)
            .OrderBy(item => item.Candidate.Point.X)
            .ThenBy(item => item.Candidate.Point.Y)
            .ThenBy(item => item.Candidate.Point.Z)
            .ThenBy(item => item.Candidate.FaceIndex)
            .ThenBy(item => item.Index);

        foreach (var item in eligible)
        {
            if (item.Candidate.FineFeatureAreaMm2 is not { } measured ||
                !float.IsFinite(measured) || measured <= 0)
                continue;

            if (measured > parameters.FineFeatureMaxAreaMm2)
                continue;

            output[item.Index] = item.Candidate with
            {
                Strategy = TipStrategy.MiniCluster,
                TipDiameter = parameters.MiniSupportTipDiameterMm,
                TipShape = SupportTipShape.Cone,
                ConeLength = parameters.MiniSupportConeLengthMm,
                BallDiameter = 0f,
                PenetrationDepth = 0f,
                MiniClusterId = nextClusterId++,
                MiniClusterCenter = item.Candidate.Point,
                MiniClusterSourceStrategy = item.Candidate.Strategy,
                IsFineFeatureMini = true,
                FallbackTipDiameter = item.Candidate.TipDiameter,
                FallbackTipShape = item.Candidate.TipShape,
                FallbackConeLength = item.Candidate.ConeLength,
                FallbackBallDiameter = item.Candidate.BallDiameter,
            };
        }

        return output;
    }

    private static float? LocalComponentArea(
        IReadOnlyList<SliceLayer> layers,
        Vector3 contact,
        float layerHeight,
        float probeHeight)
    {
        if (layers.Count == 0 || !float.IsFinite(layerHeight) || layerHeight <= 1e-6f)
            return null;
        var targetZ = contact.Z + probeHeight;
        var layerIndex = (int)MathF.Round(targetZ / layerHeight - 0.5f);
        if ((uint)layerIndex >= (uint)layers.Count) return null;

        var point = new Point64(
            (long)Math.Round(contact.X * MeshSlicer.UnitsPerMm),
            (long)Math.Round(contact.Y * MeshSlicer.UnitsPerMm));
        var polygons = layers[layerIndex].Polygons;
        var scale = MeshSlicer.UnitsPerMm * MeshSlicer.UnitsPerMm;
        double? best = null;
        foreach (var outer in polygons.Where(path => Clipper.Area(path) > 0))
        {
            if (Clipper.PointInPolygon(point, outer) == PointInPolygonResult.IsOutside) continue;
            var net = Clipper.Area(outer);
            var pointInHole = false;
            foreach (var hole in polygons.Where(path => Clipper.Area(path) < 0))
            {
                if (hole.Count == 0 ||
                    Clipper.PointInPolygon(hole[0], outer) == PointInPolygonResult.IsOutside)
                    continue;
                net += Clipper.Area(hole);
                if (Clipper.PointInPolygon(point, hole) != PointInPolygonResult.IsOutside)
                    pointInHole = true;
            }
            if (pointInHole || net <= 0) continue;
            var area = net / scale;
            if (best is null || area < best) best = area;
        }
        return best is { } value ? (float)value : null;
    }
}
