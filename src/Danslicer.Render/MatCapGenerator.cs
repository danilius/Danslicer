using System.Numerics;

namespace Danslicer.Render;

public enum MatCapStyle
{
    Clay,
    Metal,
    Pearl,
}

/// <summary>
/// Procedural MatCap sphere textures, generated on the CPU at startup so no binary assets ship in
/// the repository. Authored around mid-grey: the composite pass multiplies the lookup by the base
/// colour and by 2, so a 0.5 sample reproduces the base colour unchanged. Pixels outside the unit
/// disc take the rim shading so bilinear filtering near the silhouette never bleeds black.
/// </summary>
public static class MatCapGenerator
{
    public const int Size = 256;

    /// <summary>Tightly packed RGB8 rows, bottom row first (matching GL texture upload order).</summary>
    public static byte[] Generate(MatCapStyle style, int size = Size)
    {
        var data = new byte[size * size * 3];
        var k = 0;
        for (var row = 0; row < size; row++)
        {
            var ny = (row + 0.5f) / size * 2f - 1f;
            for (var col = 0; col < size; col++)
            {
                var nx = (col + 0.5f) / size * 2f - 1f;
                var (x, y) = (nx, ny);
                var r2 = x * x + y * y;
                float z;
                if (r2 > 1f)
                {
                    var r = MathF.Sqrt(r2);
                    x /= r;
                    y /= r;
                    z = 0f;
                }
                else
                {
                    z = MathF.Sqrt(1f - r2);
                }

                var color = Shade(style, new Vector3(x, y, z));
                data[k++] = ToByte(color.X);
                data[k++] = ToByte(color.Y);
                data[k++] = ToByte(color.Z);
            }
        }
        return data;
    }

    // The clay lights reuse the studio key/fill directions so switching Studio <-> MatCap Clay
    // keeps the same sense of where the light comes from.
    private static readonly Vector3 Key = Vector3.Normalize(new Vector3(0.45f, 0.55f, 0.70f));
    private static readonly Vector3 Fill = Vector3.Normalize(new Vector3(-0.70f, 0.10f, 0.45f));
    private static readonly Vector3 KeyHalf = Vector3.Normalize(Key + Vector3.UnitZ);

    private static Vector3 Shade(MatCapStyle style, Vector3 n) => style switch
    {
        MatCapStyle.Metal => ShadeMetal(n),
        MatCapStyle.Pearl => ShadePearl(n),
        _ => ShadeClay(n),
    };

    private static float Wrap(float ndotl)
    {
        var d = ndotl * 0.5f + 0.5f;
        return d * d;
    }

    private static Vector3 ShadeClay(Vector3 n)
    {
        var diffuse = Wrap(Vector3.Dot(n, Key)) * 0.75f + Wrap(Vector3.Dot(n, Fill)) * 0.30f + 0.08f;
        var spec = MathF.Pow(MathF.Max(Vector3.Dot(n, KeyHalf), 0f), 32f) * 0.15f;
        var tint = new Vector3(0.62f, 0.55f, 0.49f);
        return tint * diffuse + new Vector3(spec);
    }

    private static Vector3 ShadeMetal(Vector3 n)
    {
        var diffuse = Wrap(Vector3.Dot(n, Key)) * 0.38f + 0.08f;
        var spec = MathF.Pow(MathF.Max(Vector3.Dot(n, KeyHalf), 0f), 64f) * 1.1f;
        var bounceHalf = Vector3.Normalize(new Vector3(-0.45f, -0.35f, 0.60f));
        var bounce = MathF.Pow(MathF.Max(Vector3.Dot(n, bounceHalf), 0f), 14f) * 0.18f;
        var fresnel = MathF.Pow(1f - MathF.Max(n.Z, 0f), 3f) * 0.40f;
        var tint = new Vector3(0.82f, 0.87f, 0.97f);
        return tint * (diffuse + bounce + fresnel) + new Vector3(spec);
    }

    private static Vector3 ShadePearl(Vector3 n)
    {
        var body = 0.30f + Wrap(Vector3.Dot(n, Key)) * 0.42f;
        var spec = MathF.Pow(MathF.Max(Vector3.Dot(n, KeyHalf), 0f), 48f) * 0.28f;
        // Gentle iridescent drift across the sphere; the offsets stay small so the mean holds.
        var tint = new Vector3(
            0.94f + 0.10f * n.X,
            0.92f + 0.10f * n.Y,
            0.98f + 0.06f * n.Z);
        return tint * body + new Vector3(spec);
    }

    private static byte ToByte(float value) =>
        (byte)Math.Clamp((int)(value * 255f + 0.5f), 0, 255);
}
