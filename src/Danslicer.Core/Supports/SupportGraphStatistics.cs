using System.Numerics;

namespace Danslicer.Core.Supports;

/// <summary>Small analytic measurements used by diagnostics and the support-preset preview.</summary>
public sealed record SupportGraphStatistics(int MiniCount, int BaseCount, float MaxLeanDegrees,
    double EstimatedVolumeMm3)
{
    public static SupportGraphStatistics Calculate(SupportGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        var volume = 0d;
        var maxLean = 0f;
        foreach (var segment in graph.Segments.Where(segment => !segment.Disabled))
        {
            var a = graph.GetNode(segment.NodeA).Position;
            var b = graph.GetNode(segment.NodeB).Position;
            var delta = b - a;
            var length = delta.Length();
            if (length <= 1e-6f) continue;
            var radius = Math.Max(0d, segment.Diameter * 0.5d);
            volume += Math.PI * radius * radius * length;
            var cosFromVertical = Math.Clamp(MathF.Abs(delta.Z) / length, 0f, 1f);
            maxLean = MathF.Max(maxLean, MathF.Acos(cosFromVertical) * 180f / MathF.PI);
        }

        foreach (var node in graph.Nodes.Where(node => !node.Disabled))
        {
            if (node.Type == SupportNodeType.Base && node.BaseShape != SupportBaseShape.None)
            {
                var baseRadius = Math.Max(0d, node.BaseDiameter * 0.5d);
                volume += Math.PI * baseRadius * baseRadius * Math.Max(0d, node.BaseHeight);
                if (node.BaseShape == SupportBaseShape.DiscCone)
                {
                    var memberRadius = IncidentRadius(graph, node);
                    volume += FrustumVolume(baseRadius, memberRadius, node.BaseConeHeight);
                }
            }
            else if (node.Type == SupportNodeType.Tip && node.TipShape == SupportTipShape.Cone)
            {
                var contactRadius = Math.Max(0d, node.TipDiameter * 0.5d);
                var memberRadius = IncidentRadius(graph, node);
                volume += FrustumVolume(contactRadius, memberRadius,
                    node.ConeLength + node.PenetrationDepth);
                var ballRadius = Math.Max(0d, node.BallDiameter * 0.5d);
                volume += 4d / 3d * Math.PI * ballRadius * ballRadius * ballRadius;
            }
        }

        return new SupportGraphStatistics(
            graph.Segments.Count(segment => !segment.Disabled &&
                segment.Type == SupportSegmentType.MiniSupport),
            graph.Nodes.Count(node => !node.Disabled && node.Type == SupportNodeType.Base),
            maxLean,
            volume);
    }

    private static double IncidentRadius(SupportGraph graph, SupportNode node) =>
        graph.SegmentsAt(node.Id)
            .Where(segment => !segment.Disabled)
            .Select(segment => Math.Max(0d, segment.Diameter * 0.5d))
            .DefaultIfEmpty(0d)
            .Max();

    private static double FrustumVolume(double radiusA, double radiusB, double height) =>
        Math.PI * Math.Max(0d, height) *
        (radiusA * radiusA + radiusA * radiusB + radiusB * radiusB) / 3d;
}
