using System.Numerics;
using Danslicer.Core;
using Danslicer.Core.Config;
using Danslicer.Core.Geometry;
using Danslicer.Core.Scene;
using Danslicer.Core.Supports;
using Xunit;

namespace Danslicer.Tests;

/// <summary>Editing a support setting with supports selected changes those supports at once (user, 2026-09-09).</summary>
public class ApplySettingsToSelectionTests
{
    private static (Document Document, SceneObject Object) Supported()
    {
        var document = new Document { PlacementMode = PlacementMode.Off };
        document.SupportSettings = document.SupportSettings with { BaseGridPitch = 20f };
        var obj = new SceneObject("slab", new Mesh(
            [new(0, 0, 20), new(40, 0, 20), new(0, 40, 20)], [0, 1, 2]));
        document.AddObject(obj);
        document.Select(obj);
        Assert.True(document.AddManualSupport(obj, new Vector3(20, 20, 20), -Vector3.UnitZ));
        return (document, obj);
    }

    [Fact]
    public void SelectedTrunkAndTipTakeTheNewDiameters()
    {
        var (document, _) = Supported();
        var trunk = document.Supports.Segments.First(s => s.Type == SupportSegmentType.Trunk);
        var tip = document.Supports.Nodes.First(n => n.Type == SupportNodeType.Tip);
        var foot = document.Supports.Nodes.First(n => n.Type == SupportNodeType.Base);
        document.SelectSupportElements([trunk.Id, tip.Id]);
        document.SupportSettings = document.SupportSettings with { TrunkDiameter = 2.5f, TipDiameter = 0.9f, BaseDiameter = 9f };

        var changed = document.ApplySupportSettingsToSelection();

        Assert.Equal(3, changed);
        Assert.Equal(2.5f, trunk.Diameter);
        Assert.Equal(0.9f, tip.TipDiameter);
        Assert.Equal(9f, foot.BaseDiameter);
        Assert.Equal("Apply support settings", document.History.UndoName);

        document.Undo();
        Assert.NotEqual(2.5f, trunk.Diameter);
        Assert.NotEqual(0.9f, tip.TipDiameter);
    }

    [Fact]
    public void TipDiameterEditDoesNotResetSelectedTrunkOrOtherTipSettings()
    {
        var (document, _) = Supported();
        var trunk = document.Supports.Segments.First(s => s.Type == SupportSegmentType.Trunk);
        var tip = document.Supports.Nodes.First(n => n.Type == SupportNodeType.Tip);
        trunk.Diameter = 0.75f;
        tip.BallDiameter = 0.15f;
        var originalTipDiameter = tip.TipDiameter;
        document.SelectSupportElements([trunk.Id, tip.Id]);
        var previous = document.SupportSettings with { };
        document.SupportSettings.TipDiameter = 0.9f;

        Assert.Equal(1, document.ApplySupportSettingsToSelection(previous));
        Assert.Equal(0.9f, tip.TipDiameter);
        Assert.Equal(0.75f, trunk.Diameter);
        Assert.Equal(0.15f, tip.BallDiameter);
        document.Undo();
        Assert.Equal(originalTipDiameter, tip.TipDiameter);
        Assert.Equal(0.75f, trunk.Diameter);
        document.Redo();
        Assert.Equal(0.9f, tip.TipDiameter);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SelectedTipBodyUpdatesContactNodeOnce(bool alsoSelectNode)
    {
        var (document, _) = Supported();
        var tip = document.Supports.Nodes.First(n => n.Type == SupportNodeType.Tip);
        var member = document.Supports.Segments.First(s => s.Type == SupportSegmentType.Tip);
        var diameter = member.Diameter;
        document.SelectSupportElements(alsoSelectNode ? [member.Id, tip.Id] : [member.Id]);
        var previous = document.SupportSettings with { };
        document.SupportSettings.TipDiameter = 0.9f;

        Assert.Equal(1, document.ApplySupportSettingsToSelection(previous));
        Assert.Equal(0.9f, tip.TipDiameter);
        Assert.Equal(diameter, member.Diameter);
    }

    [Fact]
    public void TipDiameterEditWithOnlyTrunkSelectedUpdatesItsTip()
    {
        var (document, _) = Supported();
        var trunk = document.Supports.Segments.First(s => s.Type == SupportSegmentType.Trunk);
        trunk.Diameter = 0.75f;
        document.SelectSupportElement(trunk.Id);
        var previous = document.SupportSettings with { };
        document.SupportSettings.TipDiameter = 0.9f;

        Assert.Equal(1, document.ApplySupportSettingsToSelection(previous));
        Assert.Equal(0.9f, document.Supports.Nodes.First(n => n.Type == SupportNodeType.Tip).TipDiameter);
        Assert.Equal(0.75f, trunk.Diameter);
    }

    [Theory]
    [InlineData("base")]
    [InlineData("trunk")]
    [InlineData("branch")]
    [InlineData("tip")]
    [InlineData("contact")]
    [InlineData("multiple")]
    public void AnySelectedPartEditsWholeSupportWithoutCrossingBraces(string selectedPart)
    {
        var document = new Document();
        var graph = document.Supports;
        (SupportNode Foot, SupportNode Tip, SupportSegment Trunk, SupportSegment Branch, SupportSegment TipBody) AddSupport(float x)
        {
            var foot = new SupportNode { Type = SupportNodeType.Base, Position = new(x, 0, 0) };
            var junction = new SupportNode { Type = SupportNodeType.Junction, Position = new(x, 0, 10) };
            var shoulder = new SupportNode { Type = SupportNodeType.Junction, Position = new(x + 2, 0, 15) };
            var tip = new SupportNode { Type = SupportNodeType.Tip, Position = new(x + 2, 0, 17) };
            foreach (var node in new[] { foot, junction, shoulder, tip }) graph.AddNode(node);
            var trunk = new SupportSegment { Type = SupportSegmentType.Trunk, NodeA = foot.Id, NodeB = junction.Id };
            var branch = new SupportSegment { Type = SupportSegmentType.Branch, NodeA = junction.Id, NodeB = shoulder.Id };
            var body = new SupportSegment { Type = SupportSegmentType.Tip, NodeA = shoulder.Id, NodeB = tip.Id };
            foreach (var member in new[] { trunk, branch, body }) graph.AddSegment(member);
            return (foot, tip, trunk, branch, body);
        }
        var support = AddSupport(0);
        var neighbor = AddSupport(10);
        var brace = new SupportSegment { Type = SupportSegmentType.Bracing,
            NodeA = support.Trunk.NodeB, NodeB = neighbor.Trunk.NodeB };
        graph.AddSegment(brace);
        var selected = selectedPart switch
        {
            "base" => support.Foot.Id,
            "trunk" => support.Trunk.Id,
            "branch" => support.Branch.Id,
            "tip" => support.TipBody.Id,
            _ => support.Tip.Id,
        };
        document.SelectSupportElements(selectedPart == "multiple"
            ? [support.Foot.Id, support.Trunk.Id, support.Tip.Id] : [selected]);
        var previous = document.SupportSettings with { };
        document.SupportSettings = previous with { BaseDiameter = 9, TrunkDiameter = 3,
            BranchDiameter = 2, TipDiameter = 0.8f };

        Assert.Equal(4, document.ApplySupportSettingsToSelection(previous));
        Assert.Equal(9f, support.Foot.BaseDiameter);
        Assert.Equal(3f, support.Trunk.Diameter);
        Assert.Equal(2f, support.Branch.Diameter);
        Assert.Equal(0.8f, support.Tip.TipDiameter);
        Assert.Equal(4f, neighbor.Foot.BaseDiameter);
        Assert.Equal(1.2f, neighbor.Trunk.Diameter);
        Assert.Equal(1.2f, neighbor.Branch.Diameter);
        Assert.Equal(0.4f, neighbor.Tip.TipDiameter);
        Assert.Equal(1.2f, brace.Diameter);
        document.Undo();
        Assert.Equal(4f, support.Foot.BaseDiameter);
        Assert.Equal(1.2f, support.Trunk.Diameter);
        Assert.Equal(1.2f, support.Branch.Diameter);
        Assert.Equal(0.4f, support.Tip.TipDiameter);
        document.Redo();
        Assert.Equal(9f, support.Foot.BaseDiameter);
        Assert.Equal(3f, support.Trunk.Diameter);
        Assert.Equal(2f, support.Branch.Diameter);
        Assert.Equal(0.8f, support.Tip.TipDiameter);
    }

    [Fact]
    public void NothingSelectedOrNothingDifferentIsNoStep()
    {
        var (document, _) = Supported();
        var undo = document.History.UndoName;

        Assert.Equal(0, document.ApplySupportSettingsToSelection());
        document.SelectAllSupportElements();
        Assert.Equal(0, document.ApplySupportSettingsToSelection()); // same settings the support was built with
        Assert.Equal(undo, document.History.UndoName);
    }
}
