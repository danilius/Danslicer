using System.Numerics;

namespace Danslicer.Core.Supports.Routing;

/// <summary>
/// Square / hexagonal lattice in the XY plane, using the same option shape and the same
/// coordinate convention as <see cref="GridSupportRouter"/>. Placement projects these
/// verticals up; routing drops bases down onto the same points.
/// </summary>
public static class BaseLattice
{
    /// <summary>
    /// Plate-origin-aligned square points within a circular reach, nearest first. Keeping lattice
    /// shape, alignment and ordering here isolates the three provisional grid decisions.
    /// </summary>
    public static IEnumerable<Vector2> NearestSquarePoints(Vector2 point, float spacing,
        float maxDistance)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(spacing);
        ArgumentOutOfRangeException.ThrowIfNegative(maxDistance);
        var ix0 = (int)MathF.Floor((point.X - maxDistance) / spacing);
        var ix1 = (int)MathF.Ceiling((point.X + maxDistance) / spacing);
        var iy0 = (int)MathF.Floor((point.Y - maxDistance) / spacing);
        var iy1 = (int)MathF.Ceiling((point.Y + maxDistance) / spacing);
        var maxDistanceSquared = maxDistance * maxDistance + 1e-4f;
        return Enumerable.Range(iy0, iy1 - iy0 + 1)
            .SelectMany(iy => Enumerable.Range(ix0, ix1 - ix0 + 1)
                .Select(ix => new Vector2(ix * spacing, iy * spacing)))
            .Where(candidate => Vector2.DistanceSquared(point, candidate) <= maxDistanceSquared)
            .OrderBy(candidate => Vector2.DistanceSquared(point, candidate))
            .ThenBy(candidate => candidate.X)
            .ThenBy(candidate => candidate.Y);
    }

    public static Vector2 WorldToLocal(Vector2 world, GridRoutingOptions options)
    {
        var radians = -options.RotationDegrees * MathF.PI / 180;
        return Rotate(world - options.Offset, radians);
    }

    public static Vector2 LocalToWorld(Vector2 local, GridRoutingOptions options)
    {
        var radians = options.RotationDegrees * MathF.PI / 180;
        return Rotate(local, radians) + options.Offset;
    }

    /// <summary>
    /// Lattice XY points (world) whose local cells cover the world-space AABB, in X-then-Y order.
    /// </summary>
    public static IEnumerable<Vector2> WorldPointsCovering(Vector2 worldMin, Vector2 worldMax, GridRoutingOptions options)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.Spacing);
        var corners = new[]
        {
            new Vector2(worldMin.X, worldMin.Y),
            new Vector2(worldMax.X, worldMin.Y),
            new Vector2(worldMin.X, worldMax.Y),
            new Vector2(worldMax.X, worldMax.Y),
        };
        var locals = corners.Select(c => WorldToLocal(c, options)).ToArray();
        var lmin = new Vector2(locals.Min(p => p.X), locals.Min(p => p.Y));
        var lmax = new Vector2(locals.Max(p => p.X), locals.Max(p => p.Y));
        var pad = options.Spacing * 1e-4f;
        lmin -= new Vector2(pad);
        lmax += new Vector2(pad);

        var localPoints = options.Lattice == BaseLatticeType.Hexagonal
            ? HexCovering(lmin, lmax, options.Spacing)
            : SquareCovering(lmin, lmax, options.Spacing);

        return localPoints
            .Select(p => LocalToWorld(p, options))
            .OrderBy(p => p.X)
            .ThenBy(p => p.Y);
    }

    public static IEnumerable<Vector2> SquareRing(Vector2 point, float spacing, int rings)
    {
        var x = (int)MathF.Round(point.X / spacing);
        var y = (int)MathF.Round(point.Y / spacing);
        for (var ring = 0; ring <= rings; ring++)
            for (var iy = y - ring; iy <= y + ring; iy++)
                for (var ix = x - ring; ix <= x + ring; ix++)
                    if (ring == 0 || Math.Max(Math.Abs(ix - x), Math.Abs(iy - y)) == ring)
                        yield return new Vector2(ix * spacing, iy * spacing);
    }

    public static IEnumerable<Vector2> HexRing(Vector2 point, float spacing, int rings)
    {
        var rowHeight = spacing * MathF.Sqrt(3) * 0.5f;
        var row = (int)MathF.Round(point.Y / rowHeight);
        var column = (int)MathF.Round(point.X / spacing - (row & 1) * 0.5f);
        for (var ring = 0; ring <= rings; ring++)
            for (var r = row - ring; r <= row + ring; r++)
                for (var q = column - ring; q <= column + ring; q++)
                    if (ring == 0 || Math.Max(Math.Abs(q - column), Math.Abs(r - row)) == ring)
                        yield return new Vector2((q + (r & 1) * 0.5f) * spacing, r * rowHeight);
    }

    private static IEnumerable<Vector2> SquareCovering(Vector2 min, Vector2 max, float spacing)
    {
        var x0 = (int)MathF.Floor(min.X / spacing);
        var x1 = (int)MathF.Ceiling(max.X / spacing);
        var y0 = (int)MathF.Floor(min.Y / spacing);
        var y1 = (int)MathF.Ceiling(max.Y / spacing);
        for (var iy = y0; iy <= y1; iy++)
        for (var ix = x0; ix <= x1; ix++)
            yield return new Vector2(ix * spacing, iy * spacing);
    }

    private static IEnumerable<Vector2> HexCovering(Vector2 min, Vector2 max, float spacing)
    {
        var rowHeight = spacing * MathF.Sqrt(3) * 0.5f;
        var r0 = (int)MathF.Floor(min.Y / rowHeight);
        var r1 = (int)MathF.Ceiling(max.Y / rowHeight);
        for (var r = r0; r <= r1; r++)
        {
            var odd = (r & 1) * 0.5f;
            var q0 = (int)MathF.Floor(min.X / spacing - odd);
            var q1 = (int)MathF.Ceiling(max.X / spacing - odd);
            for (var q = q0; q <= q1; q++)
                yield return new Vector2((q + odd) * spacing, r * rowHeight);
        }
    }

    private static Vector2 Rotate(Vector2 p, float radians)
    {
        var c = MathF.Cos(radians);
        var s = MathF.Sin(radians);
        return new Vector2(p.X * c - p.Y * s, p.X * s + p.Y * c);
    }
}
