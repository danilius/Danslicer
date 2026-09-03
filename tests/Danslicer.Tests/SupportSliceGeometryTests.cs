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
    public void SlicerUnionsSupportSectionsIntoTheLayers()
    {
        // A 10 x 10 x 5 box beside a vertical pillar (diameter 2) reaching above the box: the
        // sliced volume gains the pillar, and the pillar's cap extends the print height.
        var p = new Vector3[8];
        for (int i = 0; i < 8; i++) p[i] = new Vector3((i & 1) * 10, ((i >> 1) & 1) * 10, ((i >> 2) & 1) * 5);
        int[] idx =
        {
            0, 2, 3, 0, 3, 1,  4, 5, 7, 4, 7, 6,  0, 1, 5, 0, 5, 4,
            2, 6, 7, 2, 7, 3,  0, 4, 6, 0, 6, 2,  1, 3, 7, 1, 7, 5,
        };
        var obj = new Danslicer.Core.Scene.SceneObject("box", new Danslicer.Core.Geometry.Mesh(p, idx));
        obj.Transform = Danslicer.Core.Scene.Transform.Identity with { Translation = new Vector3(-5, -5, 0) };

        var graph = new SupportGraph();
        var bottom = new SupportNode { Type = SupportNodeType.Base, Position = new Vector3(20, 0, 0) };
        var top = new SupportNode { Type = SupportNodeType.Junction, Position = new Vector3(20, 0, 10) };
        graph.AddNode(bottom);
        graph.AddNode(top);
        graph.AddSegment(new SupportSegment { Type = SupportSegmentType.Pillar, NodeA = bottom.Id, NodeB = top.Id, Diameter = 2f });

        var settings = Danslicer.Core.Slicing.PrintSettings.Default with { LayerHeight = 0.5f };
        var plain = Danslicer.Core.Slicing.Slicer.Slice(new[] { obj }, Danslicer.Core.Printers.PrinterDefinition.PhotonMonoX, settings);
        var withSupports = Danslicer.Core.Slicing.Slicer.Slice(new[] { obj }, Danslicer.Core.Printers.PrinterDefinition.PhotonMonoX, settings, supports: graph);

        // Print height now reaches the pillar's cap top (11 mm), not the box top (5 mm).
        Assert.Equal(10, plain.LayerCount);
        Assert.Equal(22, withSupports.LayerCount);

        // Added volume is roughly the pillar's: pi * 1^2 * 11 ml/1000, within rasterisation slack.
        var added = withSupports.VolumeMl - plain.VolumeMl;
        Assert.InRange(added, 0.028, 0.040);

        // The pillar shows up in the footprint.
        Assert.True(withSupports.MaxX > 20.5);
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
