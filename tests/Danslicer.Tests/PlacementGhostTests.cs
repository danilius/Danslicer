using System.Numerics;
using Danslicer.Core;
using Danslicer.Core.Config;
using Danslicer.Core.Geometry;
using Danslicer.Core.Scene;
using Danslicer.Core.Supports;
using Danslicer.Core.Supports.Routing;
using Xunit;

namespace Danslicer.Tests;

/// <summary>
/// The T placement ghost is a graph built from the preview edit. A preview that branches onto
/// an existing trunk references a node outside the edit; the ghost must borrow it, not throw
/// (crash log 2026-09-09: "Both segment endpoints must be in the graph").
/// </summary>
public class PlacementGhostTests
{
    private static (Document Document, SceneObject Object) Slab()
    {
        var document = new Document { PlacementMode = PlacementMode.Off };
        document.SupportSettings = document.SupportSettings with
        {
            BaseGridPitch = 6f, AutoBracing = false, AutoParenting = false, PreferExistingTrunks = true,
        };
        var obj = new SceneObject("slab", new Mesh(
            [new(-30, -30, 20), new(30, -30, 20), new(-30, 30, 20)], [0, 1, 2]));
        document.AddObject(obj);
        document.Select(obj);
        return (document, obj);
    }

    [Fact]
    public void AnEditThatJoinsAnExistingTrunkBorrowsThatNodeForTheGhost()
    {
        var existing = new SupportGraph();
        var trunkTop = new SupportNode { Type = SupportNodeType.Junction, Position = new Vector3(0, 0, 10) };
        existing.AddNode(trunkTop);
        var tip = new SupportNode { Type = SupportNodeType.Tip, Position = new Vector3(4, 0, 15) };
        var branch = new SupportSegment { Type = SupportSegmentType.Branch, NodeA = tip.Id, NodeB = trunkTop.Id };
        var edit = new SupportGraphEdit([tip], [branch], []);

        var ghost = edit.ToGraph(existing);

        Assert.True(ghost.TryGetNode(trunkTop.Id, out var borrowed));
        Assert.NotSame(trunkTop, borrowed);
        Assert.Equal(trunkTop.Position, borrowed.Position);
        Assert.Single(ghost.Segments);
        Assert.Single(ghost.Nodes, n => n.Id == tip.Id);
    }

    [Fact]
    public void ASegmentWithAnUnknownEndIsLeftOutInsteadOfThrowing()
    {
        var tip = new SupportNode { Type = SupportNodeType.Tip, Position = new Vector3(4, 0, 15) };
        var dangling = new SupportSegment { Type = SupportSegmentType.Branch, NodeA = tip.Id, NodeB = Guid.NewGuid() };
        var edit = new SupportGraphEdit([tip], [dangling], []);

        var ghost = edit.ToGraph(new SupportGraph());

        Assert.Single(ghost.Nodes);
        Assert.Empty(ghost.Segments);
    }

    [Fact]
    public void PreviewingNextToAStandingSupportMakesADrawableGhost()
    {
        var (document, obj) = Slab();
        Assert.True(document.AddManualSupport(obj, new Vector3(0, 0, 20), -Vector3.UnitZ));
        var standing = document.Supports.NodeCount;

        // Close enough to prefer the existing trunk; whatever the router chooses, the ghost builds.
        var edit = document.PreviewManualSupport(obj, new Vector3(3, 0, 20), -Vector3.UnitZ, out _);

        Assert.NotNull(edit);
        var ghost = edit.ToGraph(document.Supports);
        Assert.NotEmpty(ghost.Segments);
        Assert.Equal(standing, document.Supports.NodeCount); // a preview adds nothing
    }
}
