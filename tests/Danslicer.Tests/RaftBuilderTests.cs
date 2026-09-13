using System.Numerics;
using Clipper2Lib;
using Danslicer.Core.Slicing;
using Danslicer.Core.Supports.Rafts;
using Xunit;

namespace Danslicer.Tests;

/// <summary>Rafts spec (2026-09-09): Plate silhouette, Web discs and bars, the sloped edge.</summary>
public class RaftBuilderTests
{
    private static readonly RaftParameters Plate = new()
    {
        Type = RaftType.Plate, Thickness = 1f, EdgeAngleDegrees = 45f,
        DiscDiameter = 5f, Margin = 2f, BridgingDistance = 8f,
    };

    private static readonly RaftParameters Web = new()
    {
        Type = RaftType.Web, Thickness = 1f, EdgeAngleDegrees = 45f,
        DiscDiameter = 5f, BarWidth = 4f, MaxBarLength = 15f,
    };

    private static double AreaMm2(Paths64 paths) => MeshSlicer.AreaMm2(paths);

    private static int Pieces(Paths64 paths) =>
        Clipper.Union(paths, FillRule.NonZero).Count(p => Clipper.IsPositive(p));

    [Fact]
    public void PlateUnderOneFootIsADiscOfTheFootPlusTheMargin()
    {
        var outline = RaftBuilder.TopOutline([Vector2.Zero], Plate);

        var radius = Plate.DiscDiameter / 2 + Plate.Margin;
        Assert.Equal(Math.PI * radius * radius, AreaMm2(outline), Math.PI * radius * radius * 0.02);
    }

    [Fact]
    public void PlateBridgesAGapUpToTheBridgingDistanceAndNotBeyond()
    {
        // Two 5 mm discs 20 mm apart, margin 2: the grown discs leave an 11 mm gap.
        Vector2[] feet = [new(0, 0), new(20, 0)];

        Assert.Equal(1, Pieces(RaftBuilder.TopOutline(feet, Plate with { BridgingDistance = 12f })));
        Assert.Equal(2, Pieces(RaftBuilder.TopOutline(feet, Plate with { BridgingDistance = 4f })));
    }

    [Fact]
    public void PlateFillsTheInsideOfARingOfFeet()
    {
        // Eight feet on a 12 mm radius ring, 9.2 mm apart: bridged, and the hole in the middle
        // (about 8 mm across after the margin) closes too, so the plate is one solid piece.
        var feet = Enumerable.Range(0, 8)
            .Select(i => new Vector2(12 * MathF.Cos(i * MathF.PI / 4), 12 * MathF.Sin(i * MathF.PI / 4)))
            .ToList();

        var outline = Clipper.Union(RaftBuilder.TopOutline(feet, Plate), FillRule.NonZero);

        Assert.Single(outline);
        Assert.True(Clipper.IsPositive(outline[0]));
    }

    [Fact]
    public void WebUnderOneFootIsJustItsDisc()
    {
        var outline = RaftBuilder.TopOutline([Vector2.Zero], Web);

        var radius = Web.DiscDiameter / 2;
        Assert.Equal(Math.PI * radius * radius, AreaMm2(outline), Math.PI * radius * radius * 0.02);
    }

    [Fact]
    public void WebJoinsTwoFeetWithABarOfTheConfiguredWidth()
    {
        Vector2[] feet = [new(0, 0), new(10, 0)];

        var outline = RaftBuilder.TopOutline(feet, Web);

        Assert.Equal(1, Pieces(outline));
        // Two discs plus the bar between their edges (10 − 5 = 5 mm long, 4 mm wide) less the
        // slivers where the bar overlaps the discs: a lower bound of two discs plus the bar's
        // clear middle, an upper bound of everything counted twice.
        var disc = Math.PI * 2.5 * 2.5;
        var area = AreaMm2(outline);
        Assert.InRange(area, 2 * disc + 5 * 4 * 0.9, 2 * disc + 10 * 4);
    }

    [Fact]
    public void DelaunayJoinsASquareWithItsSidesAndOneDiagonal()
    {
        Vector2[] feet = [new(0, 0), new(10, 0), new(10, 10), new(0, 10)];

        var pairs = RaftBuilder.Neighbours(feet, Web with { MaxBarLength = 0f });

        Assert.Equal(5, pairs.Count);
        Assert.Contains((0, 1), pairs);
        Assert.Contains((1, 2), pairs);
        Assert.Contains((2, 3), pairs);
        Assert.Contains((0, 3), pairs);
    }

    [Fact]
    public void DelaunayChainsCollinearFeet()
    {
        Vector2[] feet = [new(20, 0), new(0, 0), new(10, 0)];

        var pairs = RaftBuilder.Neighbours(feet, Web with { MaxBarLength = 0f });

        Assert.Equal([(0, 2), (1, 2)], pairs);
    }

    [Fact]
    public void MaxBarLengthDropsLongBars()
    {
        Vector2[] feet = [new(0, 0), new(10, 0), new(10, 10), new(0, 10)];

        var pairs = RaftBuilder.Neighbours(feet, Web with { MaxBarLength = 12f });
        Assert.Equal(4, pairs.Count); // the 14.1 mm diagonal goes, the 10 mm sides stay
        Assert.Empty(RaftBuilder.Neighbours([new(0, 0), new(30, 0)], Web));
        Assert.Single(RaftBuilder.Neighbours([new(0, 0), new(30, 0)], Web with { MaxBarLength = 0f }));
    }

    [Fact]
    public void TheLipGrowsFromNothingAtThePlateToItsWidthAtTheTop()
    {
        var outline = RaftBuilder.TopOutline([Vector2.Zero], Web);
        var foot = Web.DiscDiameter / 2;

        // 45°, 1 mm thick: the footprint itself at the plate, 0.5 mm wider halfway, 1 mm at the top.
        AssertDisc(RaftBuilder.SectionAt(outline, Web, 0.0), foot);
        AssertDisc(RaftBuilder.SectionAt(outline, Web, 0.5), foot + 0.5);
        AssertDisc(RaftBuilder.SectionAt(outline, Web, 0.999), foot + 0.999);
        Assert.Empty(RaftBuilder.SectionAt(outline, Web, 1.0));
        Assert.Empty(RaftBuilder.SectionAt(outline, Web, -0.01));
        Assert.Equal(1.0, RaftBuilder.LipWidth(Web), 6);

        // 90° = no lip.
        AssertDisc(RaftBuilder.SectionAt(outline, Web with { EdgeAngleDegrees = 90f }, 0.999), foot);
    }

    [Fact]
    public void TheLipIsOnTheOutsideOnly()
    {
        // Four feet in a square joined by bars enclose a void; the void keeps its size while the
        // outer contour grows, so the lip adds area outside and none inside.
        Vector2[] feet = [new(0, 0), new(20, 0), new(20, 20), new(0, 20)];
        var web = Web with { MaxBarLength = 25f, BarWidth = 2f };
        var outline = Clipper.Union(RaftBuilder.TopOutline(feet, web), FillRule.NonZero);
        var hole = Assert.Single(outline.Where(p => !Clipper.IsPositive(p)));

        var top = Clipper.Union(RaftBuilder.SectionAt(outline, web, 0.999), FillRule.NonZero);

        var topHole = Assert.Single(top.Where(p => !Clipper.IsPositive(p)));
        Assert.Equal(Math.Abs(Clipper.Area(hole)), Math.Abs(Clipper.Area(topHole)), Math.Abs(Clipper.Area(hole)) * 0.01);
        Assert.True(AreaMm2(top) > AreaMm2(outline));
    }

    [Fact]
    public void NoFeetMeansNoRaft()
    {
        Assert.Empty(RaftBuilder.TopOutline([], Plate));
        Assert.Empty(RaftBuilder.Neighbours([], Web));
    }

    private static void AssertDisc(Paths64 section, double radius)
    {
        var expected = Math.PI * radius * radius;
        Assert.Equal(expected, AreaMm2(section), expected * 0.02);
    }
}
