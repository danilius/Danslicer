using System.Numerics;
using Danslicer.Core.Geometry;

namespace Danslicer.Core.Supports.Checks;

internal static class BoundsChecker
{
    public static void CheckMesh(Mesh mesh, int objectIndex, PrintCheckParameters p, List<CheckFinding> output)
    {
        var b = mesh.Bounds;
        var (min, max) = Volume(p);
        if (b.Min.Z < p.PlateZ - 1e-3f)
        {
            output.Add(Finding(CheckKind.BelowPlate, objectIndex, null, new Vector3(b.Center.X, b.Center.Y, b.Min.Z)));
        }
        if (b.Min.X < min.X - 1e-3f || b.Max.X > max.X + 1e-3f ||
            b.Min.Y < min.Y - 1e-3f || b.Max.Y > max.Y + 1e-3f ||
            b.Max.Z > max.Z + 1e-3f)
        {
            var pt = new Vector3(
                b.Min.X < min.X ? b.Min.X : b.Max.X > max.X ? b.Max.X : b.Center.X,
                b.Min.Y < min.Y ? b.Min.Y : b.Max.Y > max.Y ? b.Max.Y : b.Center.Y,
                b.Max.Z > max.Z ? b.Max.Z : b.Center.Z);
            output.Add(Finding(CheckKind.OutsideVolume, objectIndex, null, pt));
        }
    }

    public static void CheckSupports(SupportGraph graph, PrintCheckParameters p, List<CheckFinding> output)
    {
        var (min, max) = Volume(p);
        foreach (var node in graph.Nodes.OrderBy(n => n.Id))
        {
            if (node.Disabled) continue;
            var q = node.Position;
            if (q.Z < p.PlateZ - 1e-3f)
            {
                output.Add(Finding(CheckKind.BelowPlate, null, node.Id, q));
                continue;
            }
            if (q.X < min.X - 1e-3f || q.X > max.X + 1e-3f ||
                q.Y < min.Y - 1e-3f || q.Y > max.Y + 1e-3f ||
                q.Z > max.Z + 1e-3f)
            {
                output.Add(Finding(CheckKind.OutsideVolume, null, node.Id, q));
            }
        }

        foreach (var seg in graph.Segments.OrderBy(s => s.Id))
        {
            if (seg.Disabled) continue;
            var a = graph.GetNode(seg.NodeA);
            var b = graph.GetNode(seg.NodeB);
            if (a.Disabled || b.Disabled) continue;
            var r = seg.Diameter * 0.5f;
            var lo = Vector3.Min(a.Position, b.Position) - new Vector3(r, r, r);
            var hi = Vector3.Max(a.Position, b.Position) + new Vector3(r, r, r);
            if (lo.Z < p.PlateZ - 1e-3f)
            {
                // Bases rest on the plate; a capsule that only dips to z=0 is in volume.
                if (lo.Z < p.PlateZ - r - 1e-3f)
                    output.Add(Finding(CheckKind.BelowPlate, null, seg.Id, a.Position.Z <= b.Position.Z ? a.Position : b.Position));
            }
            if (lo.X < min.X - 1e-3f || hi.X > max.X + 1e-3f ||
                lo.Y < min.Y - 1e-3f || hi.Y > max.Y + 1e-3f ||
                hi.Z > max.Z + 1e-3f)
            {
                var pt = Vector3.Lerp(a.Position, b.Position, 0.5f);
                output.Add(Finding(CheckKind.OutsideVolume, null, seg.Id, pt));
            }
        }
    }

    private static (Vector3 Min, Vector3 Max) Volume(PrintCheckParameters p)
    {
        var half = new Vector3(p.BuildVolume.X * 0.5f, p.BuildVolume.Y * 0.5f, 0);
        var min = new Vector3(-half.X, -half.Y, p.PlateZ);
        var max = new Vector3(half.X, half.Y, p.PlateZ + p.BuildVolume.Z);
        return (min, max);
    }

    private static CheckFinding Finding(CheckKind kind, int? objectIndex, Guid? element, Vector3 point) => new()
    {
        Kind = kind,
        Severity = CheckSeverity.Error,
        ObjectIndex = objectIndex,
        ElementIdA = element,
        Point = point,
    };
}
