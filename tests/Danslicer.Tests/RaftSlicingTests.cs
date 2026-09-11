using System.Numerics;
using Danslicer.Core;
using Danslicer.Core.Config;
using Danslicer.Core.Geometry;
using Danslicer.Core.Printers;
using Danslicer.Core.Scene;
using Danslicer.Core.Slicing;
using Danslicer.Core.Supports;
using Danslicer.Core.Supports.Rafts;
using Xunit;

namespace Danslicer.Tests;

/// <summary>Rafts spec step 3: the raft slices under the feet and draws as a mesh.</summary>
public class RaftSlicingTests
{
    private static readonly PrinterDefinition Printer = PrinterDefinition.PhotonMonoX;
    private static readonly PrintSettings Settings = new() { LayerHeight = 0.1f, AntiAliasing = false };

    /// <summary>A slab at z = 20 with two manual supports, rafted when asked.</summary>
    private static (Document Document, SceneObject Object) Rafted(bool raft = true)
    {
        var document = new Document { PlacementMode = PlacementMode.Off };
        document.SupportSettings = document.SupportSettings with
        {
            BaseGridPitch = 20f, AutoBracing = false, RaftThickness = 1f, RaftEdgeAngleDegrees = 90f,
        };
        var obj = new SceneObject("slab", new Mesh(
            [new(-20, -20, 20), new(20, -20, 20), new(-20, 20, 20)], [0, 1, 2]));
        document.AddObject(obj);
        document.Select(obj);
        Assert.True(document.AddManualSupport(obj, new Vector3(-10, -10, 20), -Vector3.UnitZ));
        Assert.True(document.AddManualSupport(obj, new Vector3(5, -10, 20), -Vector3.UnitZ));
        if (raft) Assert.Equal(1, document.AddRaftToSelection());
        return (document, obj);
    }

    private static float LayerArea(SliceResult result, int index) => result.Layers[index].AreaMm2;

    [Fact]
    public void TheRaftPrintsUnderTheFeetAndOnlyUpToItsTop()
    {
        var (withRaft, obj) = Rafted();
        var (without, _) = Rafted(raft: false);

        var rafted = Slicer.Slice(withRaft.Scene.Objects, Printer, Settings, supports: withRaft.Supports);
        var bare = Slicer.Slice(without.Scene.Objects, Printer, Settings, supports: without.Supports);

        // The first layer carries the whole plate; the layer above the raft carries the trunks only.
        var raftArea = MeshSlicer.AreaMm2(RaftGeometry.Shape(withRaft.Supports, obj)!.Value.TopOutline);
        Assert.InRange(LayerArea(rafted, 0), raftArea * 0.98, raftArea * 1.02);
        Assert.True(LayerArea(rafted, 0) > LayerArea(bare, 0));
        var aboveRaft = (int)Math.Round(1f / Settings.LayerHeight) + 2;
        Assert.Equal(LayerArea(bare, aboveRaft), LayerArea(rafted, aboveRaft), 2);
        Assert.Equal(bare.LayerCount, rafted.LayerCount);
        Assert.True(rafted.VolumeMl > bare.VolumeMl);
        Assert.Equal(rafted.Layers.Sum(l => (double)l.AreaMm2) * Settings.LayerHeight / 1000,
            rafted.VolumeMl, 5);
    }

    [Fact]
    public void AHiddenModelTakesItsRaftWithIt()
    {
        var (document, obj) = Rafted();
        var other = new SceneObject("other", new Mesh(
            [new(30, 30, 0), new(40, 30, 0), new(30, 40, 0), new(30, 30, 5)], [0, 1, 2, 0, 2, 3, 0, 3, 1, 1, 3, 2]));
        document.AddObject(other);
        obj.RenderState = RenderState.Hidden;

        var result = Slicer.Slice(document.Scene.Objects, Printer, Settings, supports: document.Supports);

        Assert.Empty(RaftGeometry.Printable(document.Scene.Objects, document.Supports));
        // Only the tetrahedron's foot (a 50 mm² triangle, a little less one layer up) prints.
        Assert.InRange(LayerArea(result, 0), 40, 51);
    }

    [Fact]
    public void ARaftPastThePlateEdgeIsRefusedLikeAnyContent()
    {
        var (document, obj) = Rafted();
        document.SupportSettings = document.SupportSettings with { RaftMargin = 200f };
        document.AddRaftToSelection();

        var error = Assert.Throws<InvalidOperationException>(() =>
            Slicer.Slice(document.Scene.Objects, Printer, Settings, supports: document.Supports));
        Assert.Contains("past the plate", error.Message);
    }

    [Fact]
    public void TheRaftMeshSpansThePlateToTheTopAndFacesOutward()
    {
        // One foot: a convex raft, so "away from the centre at plate level" is outward for every
        // face (the lip wall leans out and down, the top up, the bottom flat).
        var parameters = new RaftParameters { Thickness = 1f, EdgeAngleDegrees = 45f, DiscDiameter = 5f };
        var shape = new RaftShape(parameters, RaftBuilder.TopOutline([Vector2.Zero], parameters));

        var mesh = RaftGeometry.BuildMesh(shape);

        Assert.NotNull(mesh);
        Assert.Equal(0f, mesh.Bounds.Min.Z, 4);
        Assert.Equal(1f, mesh.Bounds.Max.Z, 4);
        // Widest at the top: the lip pushes the footprint out by 1 mm at 45°, the bottom is the
        // footprint itself.
        var footprint = Clipper2Lib.Clipper.GetBounds(shape.TopOutline);
        Assert.Equal(footprint.right / MeshSlicer.UnitsPerMm + 1.0, mesh.Bounds.Max.X, 2);
        var bottomVertices = mesh.Positions.Where(p => p.Z < 1e-4f).ToList();
        Assert.NotEmpty(bottomVertices);
        Assert.Equal(footprint.right / MeshSlicer.UnitsPerMm, bottomVertices.Max(p => p.X), 2);
        // Every triangle's normal points away from the raft's centre: outward faces.
        var centre = mesh.Bounds.Center with { Z = 0f };
        var outward = 0;
        for (var i = 0; i < mesh.Indices.Length; i += 3)
        {
            var a = mesh.Positions[mesh.Indices[i]];
            var b = mesh.Positions[mesh.Indices[i + 1]];
            var c = mesh.Positions[mesh.Indices[i + 2]];
            var normal = Vector3.Cross(b - a, c - a);
            if (Vector3.Dot(normal, (a + b + c) / 3 - centre) >= 0) outward++;
        }
        Assert.Equal(mesh.Indices.Length / 3, outward);
    }

    [Fact]
    public void RaftsFollowTheDisplaySwitchAndTheMode()
    {
        var display = new SupportDisplayConfig();
        Assert.True(SupportDisplayPolicy.ShowsRafts(display));
        Assert.False(SupportDisplayPolicy.ShowsRafts(display with { ShowRafts = false }));
        Assert.False(SupportDisplayPolicy.ShowsRafts(display with { Mode = SupportDisplayMode.Lines }));
        Assert.True(SupportDisplayPolicy.ShowsRafts(display with { Mode = SupportDisplayMode.Transparent }));
        // Layout shows everything, rafts included.
        Assert.True(SupportDisplayPolicy.ForWorkspace(display with { ShowRafts = false }, isLayoutView: true).ShowRafts);
    }
}
