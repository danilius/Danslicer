using System.Numerics;

namespace Danslicer.Core.Supports;

/// <summary>One linearly tapered section of a cone-tip body.</summary>
internal readonly record struct TipBodySection(
    Vector3 Start, Vector3 End, float StartRadius, float EndRadius);

/// <summary>
/// Shared centreline and taper construction for rendered and sliced cone tips. A configured
/// lead-in leaves the contact along its outward surface normal before bending toward the graph
/// junction; zero keeps the historical straight centreline exactly.
/// </summary>
public static class TipBodyGeometry
{
    private const float Epsilon = 1e-6f;

    /// <summary>
    /// How much of the junction radius the tip's base ring actually carries. The tip grows out of
    /// a ball of the parent's diameter centred on the junction, and a base ring of exactly that
    /// radius has its rim *on* the ball — and so outside the tessellated one, where it shows
    /// through as a disc standing proud of the parent. A few percent in clears the facets of both
    /// the ball's rings and its stacks; the ball itself fills the difference, in the print and on
    /// screen alike.
    /// </summary>
    private const float JunctionInset = 0.94f;

    /// <summary>
    /// Length of the run over which the base ring is drawn in, as a fraction of the junction
    /// radius. Kept short so the whole of it is buried in the ball and the member keeps its
    /// configured profile everywhere the eye and the resin can reach it.
    /// </summary>
    private const float JunctionInsetRun = 0.25f;

    public static IReadOnlyList<Vector3> Centerline(Vector3 contact, Vector3 surfaceNormal,
        Vector3 junction, float requestedLeadIn)
    {
        var delta = junction - contact;
        var available = delta.Length();
        if (available < Epsilon || !float.IsFinite(requestedLeadIn) || requestedLeadIn <= 0)
            return [contact, junction];

        var normalLengthSquared = surfaceNormal.LengthSquared();
        if (!float.IsFinite(normalLengthSquared) || normalLengthSquared < Epsilon * Epsilon)
            return [contact, junction];

        var leadIn = MathF.Min(requestedLeadIn, available);
        var bend = contact + surfaceNormal / MathF.Sqrt(normalLengthSquared) * leadIn;
        if (Vector3.DistanceSquared(bend, contact) < Epsilon * Epsilon ||
            Vector3.DistanceSquared(bend, junction) < Epsilon * Epsilon)
            return [contact, junction];
        return [contact, bend, junction];
    }

    internal static IReadOnlyList<TipBodySection> Sections(SupportNode tip, SupportNode other,
        float neckRadius, float junctionRadius, bool embedContact)
    {
        var points = Centerline(tip.Position, tip.SurfaceNormal, other.Position,
            tip.TipNormalLeadIn);
        var distances = new float[points.Count];
        for (var i = 1; i < points.Count; i++)
            distances[i] = distances[i - 1] + Vector3.Distance(points[i - 1], points[i]);

        var totalLength = distances[^1];
        if (totalLength < Epsilon) return [];
        var coneLength = MathF.Min(MathF.Max(tip.ConeLength, 0f), totalLength);
        if (coneLength <= 0) return [];

        var contactRadius = MathF.Max(tip.TipDiameter * 0.5f, 0f);
        var neck = MathF.Max(neckRadius, 0f);
        var junction = MathF.Max(junctionRadius, 0f);
        var insetRun = MathF.Min(junction * JunctionInsetRun, totalLength * 0.5f);
        var insetStart = totalLength - insetRun;

        // Ascending, so a bend crossed by both splits keeps its knots in order along the path.
        var splits = new List<float>(2);
        if (coneLength < insetStart - Epsilon) splits.Add(coneLength);
        if (insetRun > Epsilon) splits.Add(insetStart);

        var knots = new List<(Vector3 Point, float Distance)> { (points[0], 0f) };
        for (var i = 1; i < points.Count; i++)
        {
            var startDistance = distances[i - 1];
            var endDistance = distances[i];
            foreach (var split in splits)
                if (split > startDistance + Epsilon && split < endDistance - Epsilon)
                    knots.Add((Vector3.Lerp(points[i - 1], points[i],
                        (split - startDistance) / (endDistance - startDistance)), split));
            knots.Add((points[i], endDistance));
        }

        var sections = new List<TipBodySection>(knots.Count - 1);
        for (var i = 1; i < knots.Count; i++)
        {
            var start = knots[i - 1];
            var end = knots[i];
            sections.Add(new TipBodySection(start.Point, end.Point,
                RadiusAt(start.Distance), RadiusAt(end.Distance)));
        }

        if (embedContact && tip.PenetrationDepth > 0 && sections.Count > 0)
        {
            var normal = points.Count > 2
                ? Vector3.Normalize(points[1] - points[0])
                : Vector3.Normalize(points[^1] - points[0]);
            sections[0] = sections[0] with
            {
                Start = tip.Position - normal * tip.PenetrationDepth,
            };
        }
        return sections;

        // The configured taper: contact to neck over the cone, then on to the junction.
        float BaseRadiusAt(float distance)
        {
            if (coneLength >= totalLength - Epsilon)
                return Lerp(contactRadius, junction, distance / totalLength);
            if (distance <= coneLength)
                return Lerp(contactRadius, neck, distance / coneLength);
            return Lerp(neck, junction, (distance - coneLength) / (totalLength - coneLength));
        }

        float RadiusAt(float distance)
        {
            if (insetRun <= Epsilon || distance <= insetStart) return BaseRadiusAt(distance);
            return Lerp(BaseRadiusAt(insetStart), junction * JunctionInset,
                (distance - insetStart) / insetRun);
        }
    }

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;
}
