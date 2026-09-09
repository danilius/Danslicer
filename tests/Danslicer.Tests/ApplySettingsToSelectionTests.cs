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
        var untouched = document.Supports.Nodes.First(n => n.Type == SupportNodeType.Base);
        var baseDiameterBefore = untouched.BaseDiameter;
        document.SelectSupportElements([trunk.Id, tip.Id]);
        document.SupportSettings = document.SupportSettings with { TrunkDiameter = 2.5f, TipDiameter = 0.9f, BaseDiameter = 9f };

        var changed = document.ApplySupportSettingsToSelection();

        Assert.Equal(2, changed);
        Assert.Equal(2.5f, trunk.Diameter);
        Assert.Equal(0.9f, tip.TipDiameter);
        Assert.Equal(baseDiameterBefore, untouched.BaseDiameter); // not selected, not touched
        Assert.Equal("Apply support settings", document.History.UndoName);

        document.Undo();
        Assert.NotEqual(2.5f, trunk.Diameter);
        Assert.NotEqual(0.9f, tip.TipDiameter);
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
