using System.Numerics;

namespace Danslicer.Core.Supports.Generation;

/// <summary>Uniform 3D hash for radius queries. Cell size is the query radius, so neighbours are ±1 cell.</summary>
internal sealed class PointGrid
{
    private readonly float _cell;
    private readonly Dictionary<(int X, int Y, int Z), List<Vector3>> _cells = new();

    public PointGrid(float cellSize) => _cell = MathF.Max(cellSize, 1e-4f);

    public void Add(Vector3 p)
    {
        var key = Key(p);
        if (!_cells.TryGetValue(key, out var list))
            _cells[key] = list = new List<Vector3>(4);
        list.Add(p);
    }

    public bool AnyWithin(Vector3 p, float radius)
    {
        var r2 = radius * radius;
        var reach = Math.Max(1, (int)MathF.Ceiling(radius / _cell));
        var c = Key(p);
        for (int dz = -reach; dz <= reach; dz++)
        for (int dy = -reach; dy <= reach; dy++)
        for (int dx = -reach; dx <= reach; dx++)
        {
            if (!_cells.TryGetValue((c.X + dx, c.Y + dy, c.Z + dz), out var list)) continue;
            foreach (var q in list)
            {
                if (Vector3.DistanceSquared(p, q) < r2) return true;
            }
        }
        return false;
    }

    private (int X, int Y, int Z) Key(Vector3 p) => (
        (int)MathF.Floor(p.X / _cell),
        (int)MathF.Floor(p.Y / _cell),
        (int)MathF.Floor(p.Z / _cell));
}
