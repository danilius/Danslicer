using System.Numerics;

namespace Danslicer.Core.Supports;

/// <summary>Screen-space hit testing for an ordered projection of a circular base rim.</summary>
public static class SupportDiscPicking
{
    /// <summary>
    /// Returns a centre-distance score normalised by the projected rim size when the point lies
    /// inside the projected disc polygon; otherwise null. Rim samples must be ordered around the
    /// source circle. Normalisation makes overlapping discs comparable despite perspective.
    /// </summary>
    public static float? NormalizedScore(Vector2 point, Vector2 centre,
        IReadOnlyList<Vector2> rim)
    {
        if (rim.Count < 3) return null;
        var sign = 0;
        var radiusSum = 0f;
        for (var i = 0; i < rim.Count; i++)
        {
            var a = rim[i];
            var b = rim[(i + 1) % rim.Count];
            var cross = Cross(b - a, point - a);
            if (MathF.Abs(cross) > 1e-4f)
            {
                var edgeSign = MathF.Sign(cross);
                if (sign != 0 && edgeSign != sign) return null;
                sign = edgeSign;
            }
            radiusSum += Vector2.Distance(centre, a);
        }

        var meanRadius = radiusSum / rim.Count;
        return meanRadius > 1e-4f ? Vector2.Distance(point, centre) / meanRadius : null;
    }

    private static float Cross(Vector2 a, Vector2 b) => a.X * b.Y - a.Y * b.X;
}
