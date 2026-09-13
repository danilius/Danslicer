using System.Numerics;
using Danslicer.Core;
using Danslicer.Core.Config;
using Danslicer.Core.Geometry;
using Danslicer.Core.Scene;
using Danslicer.Core.Supports;
using Danslicer.Core.Supports.Generation;

namespace Danslicer.Tests;

/// <summary>
/// Auto-parenting (SUPPORT-GEOMETRY-SPEC "Auto-parenting", user directive 2026-09-08): a
/// placement is parented at once with its neighbours within the trunk search range, as one
/// undo step with the placement; off, supports stay single until J.
/// </summary>
public sealed class AutoParentingTests
{
    private static Mesh Box(Vector3 min, Vector3 max)
    {
        var p = new[]
        {
            new Vector3(min.X, min.Y, min.Z), new(max.X, min.Y, min.Z),
            new(max.X, max.Y, min.Z), new(min.X, max.Y, min.Z),
            new(min.X, min.Y, max.Z), new(max.X, min.Y, max.Z),
            new(max.X, max.Y, max.Z), new(min.X, max.Y, max.Z),
        };
        int[] indices =
        [
            0, 3, 2, 0, 2, 1, 4, 5, 6, 4, 6, 7,
            0, 1, 5, 0, 5, 4, 2, 3, 7, 2, 7, 6,
            0, 4, 7, 0, 7, 3, 1, 2, 6, 1, 6, 5,
        ];
        return new Mesh(p, indices);
    }

    private static (Document Document, SceneObject Slab) FloatingSlab(bool autoParenting, bool grid = false)
    {
        var document = new Document();
        var slab = new SceneObject("slab", Box(new Vector3(-30, -30, 20), new Vector3(30, 30, 26)));
        document.AddObject(slab);
        document.Select(slab);
        document.SupportSettings = new SupportConfig
        {
            UseBaseGrid = grid, BaseGridPitch = 6f, IndependentManualSupports = true, AutoParenting = autoParenting,
        };
        return (document, slab);
    }

    private static int Count(Document d, SupportNodeType type) => d.Supports.Nodes.Count(n => n.Type == type);

    private static List<Guid> Ids(Document d) =>
        d.Supports.Nodes.Select(n => n.Id).Concat(d.Supports.Segments.Select(s => s.Id)).OrderBy(id => id).ToList();

    [Fact]
    public void ASecondSupportNearTheFirstSharesItsTrunk()
    {
        var (document, slab) = FloatingSlab(autoParenting: true);
        Assert.True(document.AddManualSupport(slab, new Vector3(0, 0, 20), -Vector3.UnitZ, out _, out var first));
        Assert.Null(first); // nothing to parent with yet
        Assert.Equal(1, Count(document, SupportNodeType.Base));

        Assert.True(document.AddManualSupport(slab, new Vector3(3, 0, 20), -Vector3.UnitZ, out _, out var second));

        Assert.NotNull(second);
        Assert.Equal(1, second.Placed);
        Assert.Equal(1, second.Trunks);
        Assert.Equal(2, Count(document, SupportNodeType.Tip));
        Assert.Equal(1, Count(document, SupportNodeType.Base));
    }

    [Fact]
    public void PlacementAndParentingUndoTogether()
    {
        var (document, slab) = FloatingSlab(autoParenting: true);
        Assert.True(document.AddManualSupport(slab, new Vector3(0, 0, 20), -Vector3.UnitZ));
        var afterFirst = Ids(document);
        Assert.True(document.AddManualSupport(slab, new Vector3(3, 0, 20), -Vector3.UnitZ, out _, out var parenting));
        Assert.NotNull(parenting);
        Assert.Equal("Add support", document.History.UndoName);

        Assert.True(document.Undo());

        Assert.Equal(afterFirst, Ids(document));
        Assert.Equal(1, Count(document, SupportNodeType.Tip));
        Assert.True(document.Redo());
        Assert.Equal(2, Count(document, SupportNodeType.Tip));
        Assert.Equal(1, Count(document, SupportNodeType.Base));
    }

    [Fact]
    public void OffLeavesSupportsSingle()
    {
        var (document, slab) = FloatingSlab(autoParenting: false);
        Assert.True(document.AddManualSupport(slab, new Vector3(0, 0, 20), -Vector3.UnitZ));
        Assert.True(document.AddManualSupport(slab, new Vector3(3, 0, 20), -Vector3.UnitZ, out _, out var parenting));

        Assert.Null(parenting);
        Assert.Equal(2, Count(document, SupportNodeType.Base));
    }

    [Fact]
    public void ASupportOutOfRangeIsLeftAlone()
    {
        var (document, slab) = FloatingSlab(autoParenting: true);
        document.SupportSettings = document.SupportSettings with { ParentingTrunkRange = 5f };
        Assert.True(document.AddManualSupport(slab, new Vector3(-20, 0, 20), -Vector3.UnitZ));
        var far = Ids(document);

        Assert.True(document.AddManualSupport(slab, new Vector3(20, 0, 20), -Vector3.UnitZ, out _, out var parenting));

        Assert.Null(parenting);
        Assert.Equal(2, Count(document, SupportNodeType.Base));
        Assert.All(far, id => Assert.True(document.Supports.TryGetNode(id, out _) || document.Supports.TryGetSegment(id, out _)));
    }

    [Fact]
    public void AGuidedRunIsParentedInOneStep()
    {
        var (document, slab) = FloatingSlab(autoParenting: true);
        var candidates = Enumerable.Range(0, 5)
            .Select(i => new TipCandidate(new Vector3(i * 2.5f - 5f, 0, 20), Vector3.UnitZ, 0.4f, 1f,
                TipStrategy.Island, 0, TipShape: SupportTipShape.Cone, ConeLength: 2f, BallDiameter: 1.6f))
            .ToList();

        var placed = document.PlaceGuidedTips(slab, candidates, "Support line", out var refused, out var parenting);

        Assert.Equal(5, placed);
        Assert.Equal(0, refused);
        Assert.NotNull(parenting);
        Assert.Equal(5, parenting.Placed);
        Assert.True(parenting.Trunks < 5, $"{parenting.Trunks} trunks for 5 tips");
        Assert.Equal(parenting.Trunks, Count(document, SupportNodeType.Base));
        Assert.Equal("Support line", document.History.UndoName);
        Assert.True(document.Undo());
        Assert.Empty(document.Supports.Nodes);
    }
}
