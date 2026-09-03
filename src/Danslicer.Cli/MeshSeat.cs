using System.Globalization;
using System.Numerics;
using Danslicer.Core.Geometry;

namespace Danslicer.Cli;

/// <summary>Shared --seat handling: drop to Z = 0, centre XY AABB on the origin.</summary>
internal static class MeshSeat
{
    private static readonly CultureInfo Ci = CultureInfo.InvariantCulture;

    public static (Mesh Mesh, Vector3 Offset) Apply(Mesh mesh)
    {
        var offset = mesh.SeatTranslation;
        return (mesh.Translated(offset), offset);
    }

    public static void WriteText(Vector3 offset) =>
        Console.WriteLine($"Seat offset:  {Fmt(offset.X)}, {Fmt(offset.Y)}, {Fmt(offset.Z)}");

    public static float[] Json(Vector3 offset) => [offset.X, offset.Y, offset.Z];

    private static string Fmt(float v) => v.ToString("0.###", Ci);
}
