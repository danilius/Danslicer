using System.Numerics;

namespace Danslicer.Core.Supports.Routing;

internal static class RoutingUtilities
{
    // A degenerate routing normal most commonly belongs to an underside contact. Treating its
    // inward direction as +Z makes the graph's negated, outward contact normal point downward.
    public static Vector3 SafeInwardNormal(Vector3 normal) => normal.LengthSquared() > 1e-12f
        ? Vector3.Normalize(normal) : Vector3.UnitZ;
}

internal sealed class DeterministicIds
{
    private readonly Random _random;

    public DeterministicIds(int seed) => _random = new Random(seed);

    public Guid Next()
    {
        Span<byte> bytes = stackalloc byte[16];
        _random.NextBytes(bytes);
        return new Guid(bytes);
    }
}
