using System.Numerics;
using Clipper2Lib;
using Danslicer.Core.Slicing;
using Danslicer.Core.Supports;

namespace Danslicer.Tests;

public class SupportSliceGeometryTests
{
    private static double AreaMm2(Paths64 paths) =>
        MeshSlicer.AreaMm2(Clipper.Union(paths, FillRule.NonZero));

    /// <summary>A 64-gon inscribed in the exact curve is ~0.16% small; allow 0.5%.</summary>
    private static void AssertAreaNear(double expected, Paths64 paths)
    {
        var actual = AreaMm2(paths);
        Assert.True(Math.Abs(actual - expected) <= expected * 0.005,
            $"expected area ~{expected:0.####}, got {actual:0.####}");
    }

    [Fact]
    public void VerticalPillarSlicesToItsCircle()
    {
        var paths = new Paths64();
        SupportSliceGeometry.CapsuleSection(new Vector3(0, 0, 0), new Vector3(0, 0, 20), radius: 1.0, z: 10, paths);
        AssertAreaNear(Math.PI, paths);
    }

    [Fact]
    public void LeaningPillarSlicesToAWiderEllipse()
    {
        // 45-degree lean: ellipse area = pi * r * (r / cos 45) = pi * r^2 * sqrt(2).
        var paths = new Paths64();
        SupportSliceGeometry.CapsuleSection(new Vector3(0, 0, 0), new Vector3(20, 0, 20), radius: 1.0, z: 10, paths);
        AssertAreaNear(Math.PI * Math.Sqrt(2), paths);
    }

    [Fact]
    public void CapsRoundOffTheEnds()
    {
        // Above the top node the section is only the cap sphere: radius sqrt(1 - 0.5^2).
        var paths = new Paths64();
        SupportSliceGeometry.CapsuleSection(new Vector3(0, 0, 0), new Vector3(0, 0, 10), radius: 1.0, z: 10.5, paths);
        AssertAreaNear(Math.PI * (1.0 - 0.25), paths);

        // Well past the cap: nothing.
        var empty = new Paths64();
        SupportSliceGeometry.CapsuleSection(new Vector3(0, 0, 0), new Vector3(0, 0, 10), radius: 1.0, z: 11.5, empty);
        Assert.Empty(empty);
    }

    [Fact]
    public void HorizontalMemberSlicesThroughItsCaps()
    {
        // A horizontal brace at z = 5 cut at its own height: two cap circles at the ends
        // (the analytic body of a horizontal cylinder is left to the caps by design).
        var paths = new Paths64();
        SupportSliceGeometry.CapsuleSection(new Vector3(-5, 0, 5), new Vector3(5, 0, 5), radius: 1.0, z: 5, paths);
        Assert.Equal(2, paths.Count);
    }

    [Fact]
    public void GraphSectionsSkipDisabledButNotHidden()
    {
        var g = new SupportGraph();
        var a = new SupportNode { Type = SupportNodeType.Base, Position = Vector3.Zero };
        var b = new SupportNode { Type = SupportNodeType.Junction, Position = new Vector3(0, 0, 20) };
        g.AddNode(a);
        g.AddNode(b);
        var s = new SupportSegment { Type = SupportSegmentType.Pillar, NodeA = a.Id, NodeB = b.Id, Diameter = 2.0f };
        g.AddSegment(s);

        s.Hidden = true;
        Assert.NotEmpty(SupportSliceGeometry.SectionsAt(g, 10));

        s.Disabled = true;
        Assert.Empty(SupportSliceGeometry.SectionsAt(g, 10));
    }
}
