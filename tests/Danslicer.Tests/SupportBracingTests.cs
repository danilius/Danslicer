using System.Numerics;
using Danslicer.Core;
using Danslicer.Core.Config;
using Danslicer.Core.Geometry;
using Danslicer.Core.IO;
using Danslicer.Core.Scene;
using Danslicer.Core.Supports;

namespace Danslicer.Tests;

/// <summary>
/// Bracing (SUPPORT-GEOMETRY-SPEC "Bracing"): ladders of braces between neighbouring trunks,
/// added on without splitting a trunk, as one undo step, gone with the supports they tie.
/// </summary>
public sealed class SupportBracingTests
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

    /// <summary>A floating slab at <paramref name="height"/> with single supports along X at <paramref name="pitch"/>.</summary>
    private static (Document Document, SceneObject Slab) SlabWithSingles(int count, float pitch, float height = 40,
        bool autoBracing = false)
    {
        var document = new Document();
        var slab = new SceneObject("slab", Box(new Vector3(-30, -30, height), new Vector3(30, 30, height + 6)));
        document.AddObject(slab);
        document.Select(slab);
        document.SupportSettings = new SupportConfig
        {
            UseBaseGrid = false, IndependentManualSupports = true, AutoParenting = false, AutoBracing = autoBracing,
        };
        for (var i = 0; i < count; i++)
            Assert.True(document.AddManualSupport(slab, new Vector3(i * pitch - (count - 1) * pitch / 2f, 0, height), -Vector3.UnitZ));
        return (document, slab);
    }

    private static int Braces(Document d) => d.Supports.Segments.Count(s => s.Type == SupportSegmentType.Bracing);
    private static int BraceEnds(Document d) => d.Supports.Nodes.Count(n => n.Type == SupportNodeType.BraceEnd);
    private static int Trunks(Document d) => d.Supports.Segments.Count(s => s.Type == SupportSegmentType.Trunk);

    /// <summary>Every brace end sits on a member and carries a brace: nothing dangles.</summary>
    private static void AssertBraceEndsSound(Document d)
    {
        foreach (var node in d.Supports.Nodes.Where(n => n.Type == SupportNodeType.BraceEnd))
        {
            Assert.NotNull(SupportBracing.CarrierOf(d.Supports, node));
            Assert.Contains(d.Supports.SegmentsAt(node.Id), s => s.Type == SupportSegmentType.Bracing);
        }
        foreach (var brace in d.Supports.Segments.Where(s => s.Type == SupportSegmentType.Bracing))
        {
            Assert.Equal(SupportNodeType.BraceEnd, d.Supports.GetNode(brace.NodeA).Type);
            Assert.Equal(SupportNodeType.BraceEnd, d.Supports.GetNode(brace.NodeB).Type);
        }
    }

    [Fact]
    public void BracingLaysALadderWithoutSplittingTrunks()
    {
        var (document, _) = SlabWithSingles(2, 6);
        var trunksBefore = Trunks(document);
        var elementsBefore = document.Supports.Nodes.Count + document.Supports.Segments.Count;

        var outcome = document.BraceSupports();

        Assert.NotNull(outcome);
        Assert.Equal(2, outcome.Operands);
        Assert.Equal(2, outcome.SupportsTied);
        // 40 mm trunks, first brace at 10 mm rising 6 mm, then every 15 mm: 10, 25 → two braces.
        Assert.Equal(2, outcome.Braces);
        Assert.Equal(2, Braces(document));
        Assert.Equal(4, BraceEnds(document));
        Assert.Equal(trunksBefore, Trunks(document));
        Assert.Equal(elementsBefore + 6, document.Supports.Nodes.Count + document.Supports.Segments.Count);
        AssertBraceEndsSound(document);
        // Each brace rises at 45° over the 6 mm gap and alternates sides (zigzag).
        var braces = document.Supports.Segments.Where(s => s.Type == SupportSegmentType.Bracing)
            .Select(s => (A: document.Supports.GetNode(s.NodeA).Position, B: document.Supports.GetNode(s.NodeB).Position))
            .OrderBy(b => MathF.Min(b.A.Z, b.B.Z)).ToList();
        foreach (var (a, b) in braces) Assert.Equal(6f, MathF.Abs(a.Z - b.Z), 2);
        Assert.NotEqual(braces[0].A.X, braces[1].A.X);
        Assert.Equal("Brace supports", document.History.UndoName);
        // A brace end is no support of its own.
        Assert.DoesNotContain(document.Supports.Supports(),
            c => c.Nodes.All(id => document.Supports.GetNode(id).Type == SupportNodeType.BraceEnd));
    }

    [Fact]
    public void BracingIsIdempotentAndOneUndoStep()
    {
        var (document, _) = SlabWithSingles(2, 6);
        document.BraceSupports();
        var braces = Braces(document);
        var after = document.Supports.Nodes.Count + document.Supports.Segments.Count;

        var again = document.BraceSupports();
        Assert.NotNull(again);
        Assert.Equal(0, again.Braces);
        Assert.Equal(after, document.Supports.Nodes.Count + document.Supports.Segments.Count);

        Assert.True(document.History.Undo());
        Assert.Equal(0, Braces(document));
        Assert.Equal(0, BraceEnds(document));
        Assert.True(document.History.Redo());
        Assert.Equal(braces, Braces(document));
        AssertBraceEndsSound(document);
    }

    [Fact]
    public void ShortSupportsAndDistantNeighboursAreNotBraced()
    {
        var (shortOnes, _) = SlabWithSingles(2, 6, height: 15);
        var outcome = shortOnes.BraceSupports();
        Assert.True(outcome is null || outcome.Braces == 0);
        Assert.Equal(0, Braces(shortOnes));

        var (distant, _) = SlabWithSingles(2, 14);
        outcome = distant.BraceSupports();
        Assert.True(outcome is null || outcome.Braces == 0);
        Assert.Equal(0, Braces(distant));
    }

    [Fact]
    public void DiagonalPatternLeansEveryBraceTheSameWay()
    {
        var (document, _) = SlabWithSingles(2, 6);
        document.SupportSettings = document.SupportSettings with { BracingPattern = BracingPattern.Diagonal };
        document.BraceSupports();
        var feet = document.Supports.Segments.Where(s => s.Type == SupportSegmentType.Bracing)
            .Select(s =>
            {
                var a = document.Supports.GetNode(s.NodeA).Position;
                var b = document.Supports.GetNode(s.NodeB).Position;
                return a.Z < b.Z ? a.X : b.X;
            }).Distinct().ToList();
        Assert.True(Braces(document) >= 2);
        Assert.Single(feet);
    }

    [Fact]
    public void UnbraceRemovesBracesAndTheirEndsAsOneStep()
    {
        var (document, _) = SlabWithSingles(3, 6);
        document.BraceSupports();
        Assert.True(Braces(document) > 0);
        var trunks = Trunks(document);

        var removed = document.UnbraceSupports();

        Assert.True(removed > 0);
        Assert.Equal(0, Braces(document));
        Assert.Equal(0, BraceEnds(document));
        Assert.Equal(trunks, Trunks(document));
        Assert.Equal("Unbrace supports", document.History.UndoName);
        Assert.True(document.History.Undo());
        Assert.Equal(removed, Braces(document));
        AssertBraceEndsSound(document);
    }

    [Fact]
    public void SelectBracesSelectsOnlyBracesAndDeleteRemovesTheirEnds()
    {
        var (document, _) = SlabWithSingles(3, 6);
        document.BraceSupports();
        var braces = Braces(document);
        Assert.True(braces > 0);

        Assert.Equal(braces, document.SelectBraces());
        Assert.All(document.SupportSelection, id => Assert.Equal(SupportSegmentType.Bracing, document.Supports.GetSegment(id).Type));

        document.DeleteSupportSelection();
        Assert.Equal(0, Braces(document));
        Assert.Equal(0, BraceEnds(document));
        Assert.Equal(3, document.Supports.Nodes.Count(n => n.Type == SupportNodeType.Tip));
    }

    [Fact]
    public void DeletingOneSupportTakesItsBracesAndKeepsTheOthers()
    {
        var (document, _) = SlabWithSingles(3, 6);
        document.BraceSupports();
        var middleTip = document.Supports.Nodes.Where(n => n.Type == SupportNodeType.Tip)
            .OrderBy(n => MathF.Abs(n.Position.X)).First();
        var component = document.Supports.Component(middleTip.Id);
        var bracesBefore = Braces(document);

        document.DeleteSupportElements(component.Nodes.Concat(component.Segments));

        Assert.Equal(2, document.Supports.Nodes.Count(n => n.Type == SupportNodeType.Tip));
        Assert.Equal(0, Braces(document)); // both ladders touched the middle trunk
        Assert.True(bracesBefore > 0);
        AssertBraceEndsSound(document);

        Assert.True(document.History.Undo());
        Assert.Equal(bracesBefore, Braces(document));
        AssertBraceEndsSound(document);
    }

    [Fact]
    public void ParentingTakesBracesDownWithTheTrunks()
    {
        var (document, _) = SlabWithSingles(3, 3);
        document.BraceSupports();
        document.SupportSettings = document.SupportSettings with { IndependentManualSupports = false, AutoBracing = false };

        var outcome = document.ParentSupports();

        Assert.NotNull(outcome);
        AssertBraceEndsSound(document);
        foreach (var node in document.Supports.Nodes.Where(n => n.Type == SupportNodeType.BraceEnd))
            Assert.True(document.Supports.TryGetSegment(SupportBracing.CarrierOf(document.Supports, node)!.Id, out _));
    }

    [Fact]
    public void BracedGraphsRoundTripThroughTheProjectFile()
    {
        var (document, _) = SlabWithSingles(3, 6);
        document.BraceSupports();
        var braces = Braces(document);
        var braceEnds = BraceEnds(document);
        Assert.True(braces > 0);
        var path = Path.Combine(Path.GetTempPath(), $"danslicer-bracing-{Guid.NewGuid():N}.{ProjectFile.Extension}");
        try
        {
            ProjectFile.Save(path, document, new ProjectViewState());
            var loaded = ProjectFile.Load(path).Document;
            Assert.Equal(braces, Braces(loaded));
            Assert.Equal(braceEnds, BraceEnds(loaded));
            AssertBraceEndsSound(loaded);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void AutoBracingFollowsGenerationInsideItsUndoStep()
    {
        var document = new Document();
        var slab = new SceneObject("slab", Box(new Vector3(-8, -8, 40), new Vector3(8, 8, 46)));
        document.AddObject(slab);
        document.Select(slab);
        document.SupportSettings = new SupportConfig { UseBaseGrid = true, BaseGridPitch = 6f, AutoBracing = true, Spacing = 4f };

        var summary = document.GenerateSupports(slab);

        Assert.True(summary.GeneratedTipCount >= 2);
        Assert.True(Braces(document) > 0, "generation should have braced its trunks");
        Assert.Equal("Generate supports", document.History.UndoName);
        AssertBraceEndsSound(document);
        Assert.True(document.History.Undo());
        Assert.Empty(document.Supports.Nodes);
    }
}
