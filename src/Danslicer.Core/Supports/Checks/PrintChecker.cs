using System.Numerics;
using Danslicer.Core.Geometry;
using Danslicer.Core.Supports.Generation;

namespace Danslicer.Core.Supports.Checks;

/// <summary>
/// DESIGN.md §8.10 print checks. Pure analysis: findings only, no geometry mutation.
/// Meshes are world-space. Deterministic: no RNG; findings sorted by kind, layer, position.
/// </summary>
public static class PrintChecker
{
    public static IReadOnlyList<CheckFinding> Check(
        IReadOnlyList<Mesh> objects,
        SupportGraph? supports = null,
        PrintCheckParameters? parameters = null)
    {
        ArgumentNullException.ThrowIfNull(objects);
        var p = parameters ?? PrintCheckParameters.Default;
        var findings = new List<CheckFinding>();

        var queries = new List<TriangleBvh>(objects.Count);
        for (int i = 0; i < objects.Count; i++)
        {
            var mesh = objects[i];
            queries.Add(MeshAnalysis.For(mesh).Bvh);
            var layers = LayerStack.Slice(mesh, p.LayerHeightMm);
            SuctionCupDetector.Find(layers, p, i, findings);

            foreach (var island in IslandFinder.Find(
                         layers, mesh.Bounds.Min.Z, p.LayerHeightMm, p.MinIslandAreaMm2, p.PlateZ, p.OverhangAngleDegrees))
            {
                findings.Add(new CheckFinding
                {
                    Kind = CheckKind.Island,
                    Severity = CheckSeverity.Info,
                    ObjectIndex = i,
                    LayerFrom = island.LayerIndex,
                    LayerTo = island.LayerIndex,
                    Point = island.Centroid,
                    AreaMm2 = island.AreaMm2,
                });
            }

            BoundsChecker.CheckMesh(mesh, i, p, findings);
        }

        if (supports is not null)
        {
            ProximityChecker.SupportSupport(supports, p.SupportSupportThresholdMm, findings);
            ProximityChecker.SupportModel(supports, queries, p.SupportModelThresholdMm, findings);
            BoundsChecker.CheckSupports(supports, p, findings);
        }

        ProximityChecker.ObjectObject(queries, p.ObjectObjectThresholdMm, findings);

        findings.Sort(Compare);
        return findings;
    }

    private static int Compare(CheckFinding a, CheckFinding b)
    {
        var k = a.Kind.CompareTo(b.Kind);
        if (k != 0) return k;
        var s = a.Severity.CompareTo(b.Severity);
        if (s != 0) return s;
        var o = (a.ObjectIndex ?? -1).CompareTo(b.ObjectIndex ?? -1);
        if (o != 0) return o;
        var lf = (a.LayerFrom ?? -1).CompareTo(b.LayerFrom ?? -1);
        if (lf != 0) return lf;
        var pa = a.Point ?? a.PointA ?? Vector3.Zero;
        var pb = b.Point ?? b.PointA ?? Vector3.Zero;
        var x = pa.X.CompareTo(pb.X);
        if (x != 0) return x;
        var y = pa.Y.CompareTo(pb.Y);
        if (y != 0) return y;
        return pa.Z.CompareTo(pb.Z);
    }
}
