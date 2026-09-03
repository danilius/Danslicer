using System.Numerics;
using Danslicer.Core.Geometry;

namespace Danslicer.Core.Supports.Generation;

/// <summary>Small deterministic meshes that exercise distinct support-placement cases.</summary>
public static class SupportPreviewShapes
{
    public static IReadOnlyList<string> Names { get; } =
    [
        "Overhang table / bridge",
        "Sphere",
        "Fine-teeth comb",
        "Dome underside",
        "Pocketed plate",
        "Current selection",
    ];

    public static Mesh Create(string name)
    {
        var builder = new Builder();
        switch (name)
        {
            case "Overhang table / bridge":
                builder.Box(new(-12, -6, 14), new(12, 6, 16));
                builder.Box(new(-12, -6, 0), new(-9, 6, 14));
                builder.Box(new(9, -6, 0), new(12, 6, 14));
                break;
            case "Sphere":
                builder.Sphere(new(0, 0, 14), 7, 24, 14);
                break;
            case "Fine-teeth comb":
                builder.Box(new(-12, -2, 15), new(12, 2, 18));
                for (var x = -11f; x <= 11f; x += 2f)
                    builder.Box(new(x - 0.25f, -1.5f, 10), new(x + 0.25f, 1.5f, 15));
                break;
            case "Dome underside":
                builder.Dome(11, 12, 18, 28, 8);
                break;
            case "Pocketed plate":
                builder.Box(new(-12, -9, 16), new(12, 9, 18));
                builder.Box(new(-12, -9, 8), new(-9, 9, 16));
                builder.Box(new(9, -9, 8), new(12, 9, 16));
                builder.Box(new(-9, -9, 8), new(9, -6, 16));
                builder.Box(new(-9, 6, 8), new(9, 9, 16));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(name), name,
                    "Current selection requires a document object and is not procedural.");
        }
        return builder.Build();
    }

    private sealed class Builder
    {
        private readonly List<Vector3> _positions = [];
        private readonly List<int> _indices = [];

        public void Box(Vector3 min, Vector3 max)
        {
            var start = _positions.Count;
            _positions.AddRange(
            [
                new(min.X, min.Y, min.Z), new(max.X, min.Y, min.Z),
                new(max.X, max.Y, min.Z), new(min.X, max.Y, min.Z),
                new(min.X, min.Y, max.Z), new(max.X, min.Y, max.Z),
                new(max.X, max.Y, max.Z), new(min.X, max.Y, max.Z),
            ]);
            int[] faces =
            [
                0, 2, 1, 0, 3, 2,
                4, 5, 6, 4, 6, 7,
                0, 1, 5, 0, 5, 4,
                1, 2, 6, 1, 6, 5,
                2, 3, 7, 2, 7, 6,
                3, 0, 4, 3, 4, 7,
            ];
            _indices.AddRange(faces.Select(index => start + index));
        }

        public void Sphere(Vector3 center, float radius, int slices, int stacks)
        {
            var start = _positions.Count;
            for (var stack = 0; stack <= stacks; stack++)
            {
                var phi = MathF.PI * stack / stacks;
                var z = MathF.Cos(phi);
                var ring = MathF.Sin(phi);
                for (var slice = 0; slice <= slices; slice++)
                {
                    var theta = 2 * MathF.PI * slice / slices;
                    _positions.Add(center + radius * new Vector3(
                        ring * MathF.Cos(theta), ring * MathF.Sin(theta), z));
                }
            }
            for (var stack = 0; stack < stacks; stack++)
            for (var slice = 0; slice < slices; slice++)
            {
                var a = start + stack * (slices + 1) + slice;
                var b = a + slices + 1;
                _indices.AddRange([a, b, a + 1, a + 1, b, b + 1]);
            }
        }

        public void Dome(float radius, float lowZ, float topZ, int slices, int rings)
        {
            var undersideStart = _positions.Count;
            _positions.Add(new Vector3(0, 0, lowZ));
            for (var ring = 1; ring <= rings; ring++)
            {
                var r = radius * ring / rings;
                var z = lowZ + (topZ - lowZ) * (r * r / (radius * radius));
                for (var slice = 0; slice < slices; slice++)
                {
                    var angle = 2 * MathF.PI * slice / slices;
                    _positions.Add(new Vector3(r * MathF.Cos(angle), r * MathF.Sin(angle), z));
                }
            }
            for (var slice = 0; slice < slices; slice++)
                _indices.AddRange([undersideStart, undersideStart + 1 + (slice + 1) % slices,
                    undersideStart + 1 + slice]);
            for (var ring = 1; ring < rings; ring++)
            for (var slice = 0; slice < slices; slice++)
            {
                var inner = undersideStart + 1 + (ring - 1) * slices + slice;
                var innerNext = undersideStart + 1 + (ring - 1) * slices + (slice + 1) % slices;
                var outer = undersideStart + 1 + ring * slices + slice;
                var outerNext = undersideStart + 1 + ring * slices + (slice + 1) % slices;
                _indices.AddRange([inner, outerNext, outer, inner, innerNext, outerNext]);
            }

            var topCenter = _positions.Count;
            _positions.Add(new Vector3(0, 0, topZ));
            var outerStart = undersideStart + 1 + (rings - 1) * slices;
            for (var slice = 0; slice < slices; slice++)
                _indices.AddRange([topCenter, outerStart + slice,
                    outerStart + (slice + 1) % slices]);
        }

        public Mesh Build() => new(_positions.ToArray(), _indices.ToArray());
    }
}
