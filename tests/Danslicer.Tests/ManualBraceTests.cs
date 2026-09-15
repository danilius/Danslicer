using System.Numerics;
using Danslicer.Core;
using Danslicer.Core.Geometry;
using Danslicer.Core.Scene;
using Danslicer.Core.Supports;
using Danslicer.Core.Supports.Routing;

namespace Danslicer.Tests;

public sealed class ManualBraceTests
{
    private static (Document Document, Guid A, Guid B) Fixture()
    {
        var doc = new Document();
        var model = new SceneObject("slab", new Mesh([new(-30,-30,50), new(30,-30,50), new(0,30,50)], [0,2,1]));
        doc.AddObject(model); doc.Select(model);
        Guid Add(float x)
        {
            var a = new SupportNode { Type = SupportNodeType.Base, Position = new(x,0,0), Origin = SupportOrigin.ManualFor(model.Id) };
            var b = new SupportNode { Type = SupportNodeType.Junction, Position = new(x,0,30), Origin = a.Origin };
            var s = new SupportSegment { Type = SupportSegmentType.Trunk, NodeA = a.Id, NodeB = b.Id, Origin = a.Origin };
            doc.Supports.AddNode(a); doc.Supports.AddNode(b); doc.Supports.AddSegment(s);
            return s.Id;
        }
        return (doc, Add(0), Add(10));
    }

    [Fact]
    public void PreviewIsDetachedAndEachBraceHasItsOwnUndoStep()
    {
        var (doc, a, b) = Fixture();
        var original = doc.Supports.Segments.Select(s => s.Id).ToArray();
        var edit = doc.PreviewManualBrace(a, new(0.3f,0,20), b, new(10.2f,0,10), out var refusal);
        Assert.NotNull(edit); Assert.Null(refusal);
        Assert.Equal(original, doc.Supports.Segments.Select(s => s.Id));
        Assert.Equal(new Vector3(0,0,20), edit.AddedNodes[0].Position);
        Assert.True(doc.AddManualBrace(a, new(0,0,20), b, new(10,0,10), out _));
        var first = doc.Supports.Segments.Single(s => s.Type == SupportSegmentType.Bracing).Id;
        Assert.True(doc.AddManualBrace(a, new(0,0,10), b, new(10,0,2), out _));
        Assert.Equal(2, doc.Supports.Segments.Count(s => s.Type == SupportSegmentType.Bracing));
        Assert.True(doc.Undo());
        Assert.Equal(first, doc.Supports.Segments.Single(s => s.Type == SupportSegmentType.Bracing).Id);
        Assert.True(doc.Undo()); Assert.Equal(original, doc.Supports.Segments.Select(s => s.Id));
        Assert.True(doc.Redo()); Assert.True(doc.Redo());
        Assert.Equal(2, doc.Supports.Segments.Count(s => s.Type == SupportSegmentType.Bracing));
    }

    [Fact]
    public void RejectsDuplicatesSameSupportAndCrossingBraces()
    {
        var (doc, a, b) = Fixture();
        doc.SupportSettings.ManualBraceAvoidSupports = true;
        Assert.Null(doc.PreviewManualBrace(a, new(0,0,20), a, new(0,0,10), out _));
        Assert.True(doc.AddManualBrace(a, new(0,0,20), b, new(10,0,10), out _));
        Assert.False(doc.AddManualBrace(b, new(10,0,10), a, new(0,0,20), out _));
        Assert.False(doc.AddManualBrace(a, new(0,0,10), b, new(10,0,20), out _));
        Assert.Single(doc.Supports.Segments, s => s.Type == SupportSegmentType.Bracing);
    }

    [Fact]
    public void RejectsModelCollisionAndHiddenCarrier()
    {
        var (doc, a, b) = Fixture();
        var obstacles = new LinearCollisionScene();
        obstacles.AddSphere(new(5,0,15), 2);
        Assert.Null(ManualBrace.Plan(doc.Supports, doc.SupportTarget!.Id, a, new(0,0,20), b, new(10,0,10),
            doc.SupportSettings, obstacles, out _));
        doc.Supports.GetSegment(a).Hidden = true;
        Assert.Null(doc.PreviewManualBrace(a, new(0,0,20), b, new(10,0,10), out _));
    }

    [Fact]
    public void ManualSettingsAllowCrossingsAndControlDiameterIndependently()
    {
        var (doc, a, b) = Fixture();
        doc.SupportSettings.ManualBraceDiameter = 0.8f;
        doc.SupportSettings.BracingDiameter = 3f;
        Assert.True(doc.AddManualBrace(a, new(0,0,20), b, new(10,0,10), out _));
        Assert.True(doc.AddManualBrace(a, new(0,0,10), b, new(10,0,20), out _));
        Assert.All(doc.Supports.Segments.Where(s => s.Type == SupportSegmentType.Bracing),
            s => Assert.Equal(0.8f, s.Diameter));
        var obstacles = new LinearCollisionScene(); obstacles.AddSphere(new(5,0,12), 5);
        Assert.Null(ManualBrace.Plan(doc.Supports, doc.SupportTarget!.Id, a, new(0,0,17), b, new(10,0,7),
            doc.SupportSettings, obstacles, out _));
        doc.SupportSettings.ManualBraceAvoidModels = false;
        Assert.NotNull(ManualBrace.Plan(doc.Supports, doc.SupportTarget.Id, a, new(0,0,17), b, new(10,0,7),
            doc.SupportSettings, obstacles, out _));
    }

    [Fact]
    public void CanBraceMembersOfTheSameConnectedStructure()
    {
        var (doc, a, b) = Fixture();
        doc.Supports.AddSegment(new SupportSegment { Type = SupportSegmentType.Branch,
            NodeA = doc.Supports.GetSegment(a).NodeB, NodeB = doc.Supports.GetSegment(b).NodeB });
        Assert.True(doc.AddManualBrace(a, new(0,0,20), b, new(10,0,10), out _));
    }

    [Fact]
    public void SnapChoosesNearest45DegreeIntersectionAndRejectsOutOfRange()
    {
        Assert.Equal(new Vector3(10,0,10), ManualBrace.Snap45(new(0,0,20), new(10,0,0), new(10,0,40), new(10,0,12)));
        Assert.Equal(new Vector3(10,0,30), ManualBrace.Snap45(new(0,0,20), new(10,0,0), new(10,0,40), new(10,0,29)));
        Assert.Null(ManualBrace.Snap45(new(0,0,20), new(10,0,15), new(10,0,25), new(10,0,21)));
        var snapped = ManualBrace.Snap45(new(0,0,20), new(5,0,0), new(15,0,40), new(10,0,10));
        Assert.NotNull(snapped);
        var delta = snapped.Value - new Vector3(0,0,20);
        Assert.Equal(MathF.Abs(delta.Z), new Vector2(delta.X, delta.Y).Length(), 4);
    }
}
