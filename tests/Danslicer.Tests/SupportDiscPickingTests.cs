using System.Numerics;
using Danslicer.Core.Supports;

namespace Danslicer.Tests;

public sealed class SupportDiscPickingTests
{
    [Fact]
    public void ProjectedEllipseIncludesMinorAxisAndRejectsOutsidePoint()
    {
        var centre = new Vector2(100, 100);
        var rim = Ellipse(centre, 40, 10);

        Assert.NotNull(SupportDiscPicking.NormalizedScore(new Vector2(100, 109), centre, rim));
        Assert.Null(SupportDiscPicking.NormalizedScore(new Vector2(100, 111), centre, rim));
    }

    [Fact]
    public void ScoreIsNormalisedAcrossDifferentProjectedSizes()
    {
        var centre = Vector2.Zero;
        var small = SupportDiscPicking.NormalizedScore(new Vector2(5, 0), centre,
            Ellipse(centre, 10, 10));
        var large = SupportDiscPicking.NormalizedScore(new Vector2(10, 0), centre,
            Ellipse(centre, 20, 20));

        Assert.Equal(small!.Value, large!.Value, 3);
    }

    private static IReadOnlyList<Vector2> Ellipse(Vector2 centre, float xRadius, float yRadius) =>
        Enumerable.Range(0, 24).Select(index =>
        {
            var angle = index * MathF.Tau / 24;
            return centre + new Vector2(MathF.Cos(angle) * xRadius, MathF.Sin(angle) * yRadius);
        }).ToList();
}
