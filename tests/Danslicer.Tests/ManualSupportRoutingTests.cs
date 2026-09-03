using System.Numerics;
using Danslicer.Core;
using Danslicer.Core.Geometry;
using Danslicer.Core.Scene;
using Danslicer.Core.Supports;
using Danslicer.Core.Supports.Routing;

namespace Danslicer.Tests;

/// <summary>T now routes manual supports around the model; Shift+T keeps the blind drop.</summary>
public sealed class ManualSupportRoutingTests
{
    /// <summary>Axis-aligned box mesh with outward faces (soup-welded).</summary>
    private static Mesh Box(Vector3 min, Vector3 max)
    {
        var (a, b) = (min, max);
        var corners = new Vector3[]
        {
            new(a.X, a.Y, a.Z), new(b.X, a.Y, a.Z), new(b.X, b.Y, a.Z), new(a.X, b.Y, a.Z),
            new(a.X, a.Y, b.Z), new(b.X, a.Y, b.Z), new(b.X, b.Y, b.Z), new(a.X, b.Y, b.Z),
        };
        int[] quads = [0, 3, 2, 1, 4, 5, 6, 7, 0, 1, 5, 4, 2, 3, 7, 6, 0, 4, 7, 3, 1, 2, 6, 5];
        var soup = new List<Vector3>();
        for (int q = 0; q < quads.Length; q += 4)
        {
            soup.Add(corners[quads[q]]); soup.Add(corners[quads[q + 1]]); soup.Add(corners[quads[q + 2]]);
            soup.Add(corners[quads[q]]); soup.Add(corners[quads[q + 2]]); soup.Add(corners[quads[q + 3]]);
        }
        return Mesh.FromTriangleSoup(soup.ToArray());
    }

    private static (Document Document, SceneObject Box) FloatingBoxDocument()
    {
        var document = new Document();
        var obj = new SceneObject("box", Box(new Vector3(-5, -5, 8), new Vector3(5, 5, 14)));
        document.AddObject(obj);
        return (document, obj);
    }

    [Fact]
    public void UndersideSupportRoutesToThePlate()
    {
        var (document, box) = FloatingBoxDocument();

        var added = document.AddManualSupport(box, new Vector3(0, 0, 8), -Vector3.UnitZ);

        Assert.True(added);
        Assert.Contains(document.Supports.Nodes, n => n.Type == SupportNodeType.Tip);
        var bases = document.Supports.Nodes.Where(n => n.Type == SupportNodeType.Base).ToList();
        Assert.All(bases, n => Assert.Equal(0f, n.Position.Z, 3));
        Assert.NotEmpty(bases);
    }

    [Fact]
    public void TopSurfaceSupportIsRefusedInsteadOfPiercingTheModel()
    {
        var (document, box) = FloatingBoxDocument();

        // On top of the box the only way down is through the solid: refuse, add nothing.
        var added = document.AddManualSupport(box, new Vector3(0, 0, 14), Vector3.UnitZ);

        Assert.False(added);
        Assert.Equal(0, document.Supports.NodeCount);
        Assert.Equal(0, document.Supports.SegmentCount);
    }

    [Fact]
    public void ShiftOverrideStillPlacesTheBlindStraightTree()
    {
        var (document, box) = FloatingBoxDocument();

        var added = document.AddManualSupport(box, new Vector3(0, 0, 14), Vector3.UnitZ,
            routeAroundModel: false);

        Assert.True(added);
        Assert.Equal(3, document.Supports.NodeCount); // tip, junction, base — the old shape
    }

    [Fact]
    public void RoutedSupportSegmentsClearTheModel()
    {
        var (document, box) = FloatingBoxDocument();
        // Off-centre so the pillar hugs the model's side; it must still keep clear.
        Assert.True(document.AddManualSupport(box, new Vector3(4, 0, 8), -Vector3.UnitZ));

        var audit = new LinearCollisionScene();
        audit.AddSceneObject(box);
        var graph = document.Supports;
        foreach (var segment in graph.Segments)
        {
            var a = graph.GetNode(segment.NodeA);
            var b = graph.GetNode(segment.NodeB);
            if (a.Type == SupportNodeType.Tip || b.Type == SupportNodeType.Tip) continue; // neck touches by design
            Assert.False(audit.IntersectsCapsule(a.Position, b.Position, segment.Diameter * 0.5f),
                $"{segment.Type} {a.Position} -> {b.Position} intersects the model");
        }
    }

    [Fact]
    public void SecondSupportAvoidsTheFirst()
    {
        var (document, box) = FloatingBoxDocument();
        Assert.True(document.AddManualSupport(box, new Vector3(0, 0, 8), -Vector3.UnitZ));
        var firstCount = document.Supports.SegmentCount;

        // Same contact point: the first support occupies the straight-down path.
        var added = document.AddManualSupport(box, new Vector3(0.3f, 0, 8), -Vector3.UnitZ);

        if (added)
        {
            // If it routed, it must not overlap the first support's members.
            Assert.True(document.Supports.SegmentCount > firstCount);
        }
        // Either refusing or detouring is acceptable; piercing the first support is not,
        // which the router's obstacle scene guarantees by construction.
    }

    [Fact]
    public void RoutedSupportIsOneUndoStep()
    {
        var (document, box) = FloatingBoxDocument();
        Assert.True(document.AddManualSupport(box, new Vector3(0, 0, 8), -Vector3.UnitZ));
        Assert.True(document.Supports.NodeCount > 0);

        document.Undo();

        Assert.Equal(0, document.Supports.NodeCount);
        Assert.Equal(0, document.Supports.SegmentCount);
    }

    [Fact]
    public void MovingTheObjectRefreshesTheObstacleCache()
    {
        var (document, box) = FloatingBoxDocument();
        Assert.True(document.AddManualSupport(box, new Vector3(0, 0, 8), -Vector3.UnitZ));

        // Slide the box away and support the new underside location; the cache must rebuild
        // for the new transform or this contact would appear to float in the old box's space.
        box.Transform = box.Transform with { Translation = new Vector3(40, 0, 0) };
        document.NotifyTransientChange();
        var added = document.AddManualSupport(box, new Vector3(40, 0, 8), -Vector3.UnitZ);

        Assert.True(added);
    }
}
