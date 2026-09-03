using System.Numerics;
using Danslicer.Core.Geometry;
using Danslicer.Core.Supports;
using Danslicer.Core.Supports.Checks;

namespace Danslicer.Tests;

public class PrintCheckTests
{
    private static PrintCheckParameters P(
        float layer = 0.2f,
        float minSuction = 5f,
        float drain = 0.8f,
        float support = 1f,
        float model = 0.5f,
        float objects = 1f,
        float minIsland = 0.5f) => new()
    {
        LayerHeightMm = layer,
        MinSuctionVolumeMm3 = minSuction,
        DrainOpeningMm = drain,
        SupportSupportThresholdMm = support,
        SupportModelThresholdMm = model,
        ObjectObjectThresholdMm = objects,
        MinIslandAreaMm2 = minIsland,
    };

    private static IReadOnlyList<CheckFinding> Run(Mesh mesh, PrintCheckParameters? p = null, SupportGraph? g = null) =>
        PrintChecker.Check([mesh], g, p ?? P());

    [Fact]
    public void InvertedCupIsASuctionCup()
    {
        var mesh = OpenBottomCup(outer: 20, inner: 16, height: 12, roof: 2);
        var findings = Run(mesh);
        var cups = findings.Where(f => f.Kind == CheckKind.SuctionCup).ToList();
        Assert.NotEmpty(cups);
        var cup = cups[0];
        Assert.Equal(CheckSeverity.Warning, cup.Severity);
        Assert.True(cup.VolumeMm3 > 100, $"volume {cup.VolumeMm3}");
        Assert.NotNull(cup.Point);
        Assert.InRange(cup.Point!.Value.X, -8, 8);
        Assert.InRange(cup.Point.Value.Y, -8, 8);
        Assert.True(cup.LayerTo >= cup.LayerFrom);
    }

    [Fact]
    public void DrainHolePreventsSuctionCup()
    {
        var mesh = OpenBottomCup(outer: 20, inner: 16, height: 12, roof: 2, drainPlusX: true);
        var findings = Run(mesh);
        Assert.DoesNotContain(findings, f => f.Kind == CheckKind.SuctionCup);
    }

    [Fact]
    public void TubeOpenAtBothEndsIsNotASuctionCup()
    {
        var mesh = SquareTube(outer: 12, inner: 8, height: 20);
        var findings = Run(mesh);
        Assert.DoesNotContain(findings, f => f.Kind == CheckKind.SuctionCup);
    }

    [Fact]
    public void SolidBoxIsNotASuctionCup()
    {
        var mesh = Meshes.Box(10, 10, 10);
        var findings = Run(mesh);
        Assert.DoesNotContain(findings, f => f.Kind == CheckKind.SuctionCup);
        Assert.DoesNotContain(findings, f => f.Kind == CheckKind.Island);
    }

    [Fact]
    public void NarrowGapStillCountsAsNearlyClosed()
    {
        // 0.4 mm slot through the +X wall. A 1.5 mm drain threshold seals it; 0.2 mm leaves it open.
        var mesh = OpenBottomCupWithSlot(outer: 20, inner: 16, height: 12, roof: 2, slot: 0.4f);
        Assert.Contains(Run(mesh, P(drain: 1.5f)), f => f.Kind == CheckKind.SuctionCup);
        Assert.DoesNotContain(Run(mesh, P(drain: 0.2f)), f => f.Kind == CheckKind.SuctionCup);
    }

    [Fact]
    public void FloatingBoxSurfacesAsIslandCheck()
    {
        var mesh = Meshes.FloatingBox(10, 10, 10, z: 5);
        var findings = Run(mesh, P(layer: 0.05f));
        var islands = findings.Where(f => f.Kind == CheckKind.Island).ToList();
        Assert.NotEmpty(islands);
        Assert.All(islands, f =>
        {
            Assert.Equal(CheckSeverity.Info, f.Severity);
            Assert.True(f.AreaMm2 > 50);
            Assert.NotNull(f.LayerFrom);
        });
    }

    [Fact]
    public void TwoCloseSupportsAreReported()
    {
        var g = new SupportGraph();
        var a1 = Pillar(g, new Vector3(0, 0, 0), new Vector3(0, 0, 10), 1.2f);
        var a2 = Pillar(g, new Vector3(1.5f, 0, 0), new Vector3(1.5f, 0, 10), 1.2f);
        _ = a1; _ = a2;
        var findings = PrintChecker.Check([Meshes.Box(1, 1, 1, new Vector3(50, 50, 0))], g, P(support: 1.0f));
        var hits = findings.Where(f => f.Kind == CheckKind.SupportProximity).ToList();
        Assert.NotEmpty(hits);
        Assert.True(hits[0].DistanceMm < 1.0f);
        Assert.NotNull(hits[0].PointA);
        Assert.NotNull(hits[0].PointB);
    }

    [Fact]
    public void ConnectedSupportsAreNotProximity()
    {
        var g = new SupportGraph();
        var baseN = Node(SupportNodeType.Base, 0, 0, 0);
        var j = Node(SupportNodeType.Junction, 0, 0, 8);
        var t = Node(SupportNodeType.Tip, 0, 0, 10);
        g.AddNode(baseN); g.AddNode(j); g.AddNode(t);
        g.AddSegment(new SupportSegment { Type = SupportSegmentType.Pillar, NodeA = baseN.Id, NodeB = j.Id, Diameter = 1.2f });
        g.AddSegment(new SupportSegment { Type = SupportSegmentType.Neck, NodeA = j.Id, NodeB = t.Id, Diameter = 0.8f });
        var findings = PrintChecker.Check([Meshes.Box(1, 1, 1, new Vector3(40, 40, 0))], g, P(support: 5f));
        Assert.DoesNotContain(findings, f => f.Kind == CheckKind.SupportProximity);
    }

    [Fact]
    public void SupportCloseToModelIsReportedExceptAtTheTip()
    {
        var cube = Meshes.Box(10, 10, 10, new Vector3(0, 0, 2));
        var g = new SupportGraph();
        // Pillar 0.3 mm from the +X face (x=10), not a tip contact.
        Pillar(g, new Vector3(10.3f, 5, 0), new Vector3(10.3f, 5, 8), 0.4f);
        var close = PrintChecker.Check([cube], g, P(model: 1.0f));
        Assert.Contains(close, f => f.Kind == CheckKind.SupportModelProximity);

        var gTip = new SupportGraph();
        var tip = Node(SupportNodeType.Tip, 5, 5, 2);
        tip.TipDiameter = 0.4f;
        var junction = Node(SupportNodeType.Junction, 5, 5, 0.5f);
        var b = Node(SupportNodeType.Base, 5, 5, 0);
        gTip.AddNode(tip); gTip.AddNode(junction); gTip.AddNode(b);
        gTip.AddSegment(new SupportSegment { Type = SupportSegmentType.Neck, NodeA = tip.Id, NodeB = junction.Id, Diameter = 0.8f });
        gTip.AddSegment(new SupportSegment { Type = SupportSegmentType.Pillar, NodeA = junction.Id, NodeB = b.Id, Diameter = 1.2f });
        var atTip = PrintChecker.Check([cube], gTip, P(model: 1.0f));
        Assert.DoesNotContain(atTip, f => f.Kind == CheckKind.SupportModelProximity);
    }

    [Fact]
    public void TwoCloseObjectsAreReported()
    {
        var a = Meshes.Box(10, 10, 10, new Vector3(0, 0, 0));
        var b = Meshes.Box(10, 10, 10, new Vector3(10.4f, 0, 0));
        var findings = PrintChecker.Check([a, b], null, P(objects: 1.0f));
        var hits = findings.Where(f => f.Kind == CheckKind.ObjectProximity).ToList();
        Assert.Single(hits);
        Assert.Equal(0, hits[0].ObjectIndex);
        Assert.Equal(1, hits[0].ObjectIndexB);
        Assert.True(hits[0].DistanceMm < 0.5f);
    }

    [Fact]
    public void MeshBelowPlateIsAnError()
    {
        var mesh = Meshes.Box(10, 10, 10, new Vector3(0, 0, -3));
        var findings = Run(mesh);
        Assert.Contains(findings, f => f.Kind == CheckKind.BelowPlate && f.Severity == CheckSeverity.Error);
    }

    [Fact]
    public void MeshOutsideVolumeIsAnError()
    {
        var mesh = Meshes.Box(10, 10, 10, new Vector3(200, 0, 0));
        var findings = Run(mesh);
        Assert.Contains(findings, f => f.Kind == CheckKind.OutsideVolume && f.Severity == CheckSeverity.Error);
    }

    [Fact]
    public void SupportNodeBelowPlateIsAnError()
    {
        var g = new SupportGraph();
        g.AddNode(Node(SupportNodeType.Base, 0, 0, -2));
        var findings = PrintChecker.Check([Meshes.Box(4, 4, 4, new Vector3(0, 0, 1))], g, P());
        Assert.Contains(findings, f => f.Kind == CheckKind.BelowPlate && f.ElementIdA is not null);
    }

    [Fact]
    public void BvhSupportModelAgreesWithBruteForceDistanceQuery()
    {
        var cube = Meshes.Box(10, 10, 10, new Vector3(0, 0, 2));
        var g = new SupportGraph();
        Pillar(g, new Vector3(10.3f, 5, 0), new Vector3(10.3f, 5, 8), 0.4f);

        var bvh = MeshAnalysis.For(cube).Bvh;
        var brute = new MeshDistanceQuery(cube);
        var seg = g.Segments.Single();
        var na = g.GetNode(seg.NodeA);
        var nb = g.GetNode(seg.NodeB);

        var dBvh = bvh.ClosestToSegment(na.Position, nb.Position, out var sBvh, out var mBvh, out _);
        var dBrute = brute.ClosestToSegment(na.Position, nb.Position, out var sBrute, out var mBrute);
        Assert.Equal(dBrute, dBvh, 4);
        Assert.InRange(Vector3.Distance(sBvh, sBrute), 0, 1e-4f);
        Assert.InRange(Vector3.Distance(mBvh, mBrute), 0, 1e-4f);

        var findings = PrintChecker.Check([cube], g, P(model: 1.0f));
        Assert.Contains(findings, f => f.Kind == CheckKind.SupportModelProximity);
    }

    [Fact]
    public void SameInputsAreDeterministic()
    {
        var mesh = OpenBottomCup(20, 16, 12, 2);
        var a = Run(mesh);
        var b = Run(mesh);
        Assert.Equal(a, b);
    }

    private static SupportNode Node(SupportNodeType type, float x, float y, float z) =>
        new() { Type = type, Position = new Vector3(x, y, z) };

    private static SupportSegment Pillar(SupportGraph g, Vector3 from, Vector3 to, float diameter)
    {
        var a = new SupportNode { Type = SupportNodeType.Base, Position = from };
        var b = new SupportNode { Type = SupportNodeType.Junction, Position = to };
        g.AddNode(a);
        g.AddNode(b);
        var s = new SupportSegment { Type = SupportSegmentType.Pillar, NodeA = a.Id, NodeB = b.Id, Diameter = diameter };
        g.AddSegment(s);
        return s;
    }

    /// <summary>Hollow box open on -Z. <paramref name="drainPlusX"/> opens the whole +X side.</summary>
    internal static Mesh OpenBottomCup(
        float outer, float inner, float height, float roof, bool drainPlusX = false)
    {
        var oh = outer * 0.5f;
        var ih = inner * 0.5f;
        var z0 = 0f;
        var zRoof = height - roof;
        var zTop = height;
        var soup = new List<Vector3>();
        void Tri(Vector3 a, Vector3 b, Vector3 c, Vector3 outward)
        {
            var n = Vector3.Cross(b - a, c - a);
            if (Vector3.Dot(n, outward) < 0) (b, c) = (c, b);
            soup.Add(a); soup.Add(b); soup.Add(c);
        }
        void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 outward)
        {
            Tri(a, b, c, outward);
            Tri(a, c, d, outward);
        }

        var wholeSideOpen = drainPlusX;

        // Outer -X, +Y, -Y always.
        Quad(new(-oh, oh, z0), new(-oh, oh, zTop), new(-oh, -oh, zTop), new(-oh, -oh, z0), -Vector3.UnitX);
        Quad(new(-oh, oh, z0), new(-oh, oh, zTop), new(oh, oh, zTop), new(oh, oh, z0), Vector3.UnitY);
        Quad(new(oh, -oh, z0), new(oh, -oh, zTop), new(-oh, -oh, zTop), new(-oh, -oh, z0), -Vector3.UnitY);
        // Inner -X, +Y, -Y.
        Quad(new(-ih, -ih, z0), new(-ih, -ih, zRoof), new(-ih, ih, zRoof), new(-ih, ih, z0), Vector3.UnitX);
        Quad(new(-ih, ih, z0), new(-ih, ih, zRoof), new(ih, ih, zRoof), new(ih, ih, z0), -Vector3.UnitY);
        Quad(new(ih, -ih, z0), new(ih, -ih, zRoof), new(-ih, -ih, zRoof), new(-ih, -ih, z0), Vector3.UnitY);

        if (wholeSideOpen)
        {
            Quad(new(ih, ih, z0), new(oh, ih, z0), new(oh, ih, zRoof), new(ih, ih, zRoof), Vector3.UnitY);
            Quad(new(oh, -ih, z0), new(ih, -ih, z0), new(ih, -ih, zRoof), new(oh, -ih, zRoof), -Vector3.UnitY);
            Quad(new(oh, -oh, zRoof), new(oh, -oh, zTop), new(oh, oh, zTop), new(oh, oh, zRoof), Vector3.UnitX);
        }
        else
        {
            Quad(new(oh, -oh, z0), new(oh, -oh, zTop), new(oh, oh, zTop), new(oh, oh, z0), Vector3.UnitX);
            Quad(new(ih, -ih, z0), new(ih, ih, z0), new(ih, ih, zRoof), new(ih, -ih, zRoof), -Vector3.UnitX);
        }

        Quad(new(-oh, -oh, zTop), new(oh, -oh, zTop), new(oh, oh, zTop), new(-oh, oh, zTop), Vector3.UnitZ);
        Quad(new(-ih, -ih, zRoof), new(-ih, ih, zRoof), new(ih, ih, zRoof), new(ih, -ih, zRoof), -Vector3.UnitZ);

        // Bottom rim.
        Quad(new(-oh, oh, z0), new(-oh, -oh, z0), new(-ih, -ih, z0), new(-ih, ih, z0), -Vector3.UnitZ);
        Quad(new(-oh, oh, z0), new(oh, oh, z0), new(ih, ih, z0), new(-ih, ih, z0), -Vector3.UnitZ);
        Quad(new(oh, -oh, z0), new(-oh, -oh, z0), new(-ih, -ih, z0), new(ih, -ih, z0), -Vector3.UnitZ);
        if (!wholeSideOpen)
            Quad(new(oh, -oh, z0), new(oh, oh, z0), new(ih, ih, z0), new(ih, -ih, z0), -Vector3.UnitZ);

        if (wholeSideOpen)
        {
            // Roof underside between inner +X and outer, north and south of the opening.
            Quad(new(ih, ih, zRoof), new(oh, ih, zRoof), new(oh, oh, zRoof), new(ih, oh, zRoof), Vector3.UnitZ);
            Quad(new(ih, -oh, zRoof), new(oh, -oh, zRoof), new(oh, -ih, zRoof), new(ih, -ih, zRoof), Vector3.UnitZ);
        }

        return Mesh.FromTriangleSoup(soup.ToArray());
    }

    /// <summary>
    /// C-shaped cup with two panels that leave only <paramref name="slot"/> mm of gap on +X.
    /// </summary>
    internal static Mesh OpenBottomCupWithSlot(float outer, float inner, float height, float roof, float slot)
    {
        var oh = outer * 0.5f;
        var ih = inner * 0.5f;
        var hy = slot * 0.5f;
        var z0 = 0f;
        var zr = height - roof;
        var soup = new List<Vector3>();
        var cMesh = OpenBottomCup(outer, inner, height, roof, drainPlusX: true);
        for (int t = 0; t < cMesh.TriangleCount; t++)
        {
            cMesh.GetTriangle(t, out var a, out var b, out var c);
            soup.Add(a); soup.Add(b); soup.Add(c);
        }
        void Tri(Vector3 a, Vector3 b, Vector3 c, Vector3 outward)
        {
            var n = Vector3.Cross(b - a, c - a);
            if (Vector3.Dot(n, outward) < 0) (b, c) = (c, b);
            soup.Add(a); soup.Add(b); soup.Add(c);
        }
        void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 outward)
        {
            Tri(a, b, c, outward);
            Tri(a, c, d, outward);
        }
        // Outer +X panels span the full outer half-width so the wall-thickness corners close.
        Quad(new(oh, hy, z0), new(oh, oh, z0), new(oh, oh, zr), new(oh, hy, zr), Vector3.UnitX);
        Quad(new(oh, -oh, z0), new(oh, -hy, z0), new(oh, -hy, zr), new(oh, -oh, zr), Vector3.UnitX);
        Quad(new(ih, hy, z0), new(ih, hy, zr), new(ih, ih, zr), new(ih, ih, z0), -Vector3.UnitX);
        Quad(new(ih, -ih, z0), new(ih, -ih, zr), new(ih, -hy, zr), new(ih, -hy, z0), -Vector3.UnitX);
        Quad(new(ih, hy, z0), new(oh, hy, z0), new(oh, hy, zr), new(ih, hy, zr), -Vector3.UnitY);
        Quad(new(oh, -hy, z0), new(ih, -hy, z0), new(ih, -hy, zr), new(oh, -hy, zr), Vector3.UnitY);
        return Mesh.FromTriangleSoup(soup.ToArray());
    }

    internal static Mesh SquareTube(float outer, float inner, float height)
    {
        var oh = outer * 0.5f;
        var ih = inner * 0.5f;
        var soup = new List<Vector3>();
        void Tri(Vector3 a, Vector3 b, Vector3 c, Vector3 outward)
        {
            var n = Vector3.Cross(b - a, c - a);
            if (Vector3.Dot(n, outward) < 0) (b, c) = (c, b);
            soup.Add(a); soup.Add(b); soup.Add(c);
        }
        void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 outward)
        {
            Tri(a, b, c, outward);
            Tri(a, c, d, outward);
        }
        float z0 = 0, z1 = height;
        Quad(new(oh, -oh, z0), new(oh, -oh, z1), new(oh, oh, z1), new(oh, oh, z0), Vector3.UnitX);
        Quad(new(-oh, oh, z0), new(-oh, oh, z1), new(-oh, -oh, z1), new(-oh, -oh, z0), -Vector3.UnitX);
        Quad(new(-oh, oh, z0), new(-oh, oh, z1), new(oh, oh, z1), new(oh, oh, z0), Vector3.UnitY);
        Quad(new(oh, -oh, z0), new(oh, -oh, z1), new(-oh, -oh, z1), new(-oh, -oh, z0), -Vector3.UnitY);
        Quad(new(ih, -ih, z0), new(ih, ih, z0), new(ih, ih, z1), new(ih, -ih, z1), -Vector3.UnitX);
        Quad(new(-ih, -ih, z0), new(-ih, -ih, z1), new(-ih, ih, z1), new(-ih, ih, z0), Vector3.UnitX);
        Quad(new(-ih, ih, z0), new(-ih, ih, z1), new(ih, ih, z1), new(ih, ih, z0), -Vector3.UnitY);
        Quad(new(ih, -ih, z0), new(ih, -ih, z1), new(-ih, -ih, z1), new(-ih, -ih, z0), Vector3.UnitY);
        return Mesh.FromTriangleSoup(soup.ToArray());
    }
}
