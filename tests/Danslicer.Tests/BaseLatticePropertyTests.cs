using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Danslicer.Core.Supports.Routing;
using Xunit;

namespace Danslicer.Tests;

public class BaseLatticePropertyTests
{
    [Fact]
    public void DistancesAreNonDecreasing()
    {
        var point = new Vector2(3.2f, -7.9f);
        var spacing = 5f;
        var maxDistance = 40f;
        var points = BaseLattice.NearestSquarePoints(point, spacing, maxDistance).ToList();

        float previousDistanceSquared = 0;
        foreach (var candidate in points)
        {
            var distanceSquared = Vector2.DistanceSquared(point, candidate);
            Assert.True(distanceSquared >= previousDistanceSquared);
            previousDistanceSquared = distanceSquared;
        }
    }

    [Fact]
    public void AllPointsAreAlignedAndInRange()
    {
        var point = new Vector2(3.2f, -7.9f);
        var spacing = 5f;
        var maxDistance = 40f;
        var points = BaseLattice.NearestSquarePoints(point, spacing, maxDistance).ToList();

        foreach (var candidate in points)
        {
            Assert.True(Math.Abs(candidate.X % spacing) < 1e-3f);
            Assert.True(Math.Abs(candidate.Y % spacing) < 1e-3f);
            Assert.True(Vector2.Distance(point, candidate) <= maxDistance + 1e-2f);
        }
    }

    [Fact]
    public void MatchesBruteForceEnumeration()
    {
        var point = new Vector2(11.7f, 4.1f);
        var spacing = 7f;
        var maxDistance = 25f;
        var actualPoints = BaseLattice.NearestSquarePoints(point, spacing, maxDistance)
            .Select(v => (X: (int)Math.Round(v.X), Y: (int)Math.Round(v.Y)))
            .OrderBy(t => t.X).ThenBy(t => t.Y)
            .ToList();

        var ix0 = (int)MathF.Floor((point.X - maxDistance) / spacing);
        var ix1 = (int)MathF.Ceiling((point.X + maxDistance) / spacing);
        var iy0 = (int)MathF.Floor((point.Y - maxDistance) / spacing);
        var iy1 = (int)MathF.Ceiling((point.Y + maxDistance) / spacing);
        var maxDistanceSquared = maxDistance * maxDistance + 1e-4f;

        var expectedPoints = Enumerable.Range(iy0, iy1 - iy0 + 1)
            .SelectMany(iy => Enumerable.Range(ix0, ix1 - ix0 + 1)
                .Select(ix => new Vector2(ix * spacing, iy * spacing)))
            .Where(candidate => Vector2.DistanceSquared(point, candidate) <= maxDistanceSquared)
            .Select(v => (X: (int)Math.Round(v.X), Y: (int)Math.Round(v.Y)))
            .OrderBy(t => t.X).ThenBy(t => t.Y)
            .ToList();

        Assert.Equal(expectedPoints, actualPoints);
    }

    [Fact]
    public void IsDeterministic()
    {
        var point = new Vector2(3.2f, -7.9f);
        var spacing = 5f;
        var maxDistance = 40f;
        var firstCall = BaseLattice.NearestSquarePoints(point, spacing, maxDistance).ToList();
        var secondCall = BaseLattice.NearestSquarePoints(point, spacing, maxDistance).ToList();

        Assert.Equal(firstCall, secondCall);
    }

    [Fact]
    public void TinyRadiusYieldsOnlyExactPoint()
    {
        var point = new Vector2(8f, -12f);
        var spacing = 4f;
        var maxDistance = 0.5f;
        var points = BaseLattice.NearestSquarePoints(point, spacing, maxDistance).ToList();

        Assert.Single(points);
        Assert.Equal(point, points[0]);
    }
}
