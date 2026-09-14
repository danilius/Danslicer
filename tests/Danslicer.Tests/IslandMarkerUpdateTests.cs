using System.Numerics;
using Danslicer.App.ViewModels;
using Danslicer.Core.Config;
using Danslicer.Core.Geometry;
using Danslicer.Core.Scene;
using Danslicer.Core.Supports;

namespace Danslicer.Tests;

public sealed class IslandMarkerUpdateTests
{
    [Fact]
    public async Task SupportDetectedIslandsUsesMarkersAndUndoRestoresThem()
    {
        var vm = new MainViewModel();
        vm.Document.PlacementMode = PlacementMode.Off;
        vm.Document.SupportSettings = vm.Document.SupportSettings with { UseBaseGrid = false };
        var boxes = TwoBoxes();
        var obj = new SceneObject("raised islands", new Mesh(
            boxes.Positions.Select(p => p + new Vector3(0, 0, 8)).ToArray(), boxes.Indices));
        vm.Document.AddObject(obj);
        vm.SelectedObject = obj;
        Assert.False(vm.GenerateIslandSupportsCommand.CanExecute(null));
        await vm.DetectIslandsCommand.ExecuteAsync(null);
        Assert.Equal(2, vm.DetectedIslands.Count);
        Assert.True(vm.GenerateIslandSupportsCommand.CanExecute(null));

        var other = new SceneObject("other", TwoBoxes());
        vm.Document.AddObject(other);
        vm.SelectedObject = other;
        Assert.False(vm.GenerateIslandSupportsCommand.CanExecute(null));
        vm.SelectedObject = obj;
        Assert.True(vm.GenerateIslandSupportsCommand.CanExecute(null));

        // A fresh automatic detection would now discard both islands. The button must
        // consume the already displayed results instead.
        vm.Document.SupportSettings = vm.Document.SupportSettings with { MinIslandAreaMm2 = 1000 };
        await vm.GenerateIslandSupportsCommand.ExecuteAsync(null);
        Assert.Equal(2, vm.Document.Supports.Nodes.Count(n => n.Type == SupportNodeType.Tip));
        Assert.Empty(vm.DetectedIslands);
        Assert.False(vm.GenerateIslandSupportsCommand.CanExecute(null));
        vm.Document.Undo();
        Assert.Equal(2, vm.DetectedIslands.Count);
        Assert.True(vm.GenerateIslandSupportsCommand.CanExecute(null));
        vm.ClearIslandDetectionCommand.Execute(null);
        Assert.False(vm.GenerateIslandSupportsCommand.CanExecute(null));
    }

    [Fact]
    public async Task SupportEditsHideOnlyTheSupportedIslandAndRestoreItAfterRemoval()
    {
        var vm = new MainViewModel();
        vm.Document.PlacementMode = PlacementMode.Off;
        var obj = new SceneObject("two islands", TwoBoxes());
        vm.Document.AddObject(obj);
        vm.SelectedObject = obj;
        await vm.DetectIslandsCommand.ExecuteAsync(null);
        var original = vm.DetectedIslands.ToArray();
        Assert.Equal(2, original.Length);

        var graph = vm.Document.Supports;
        var bottom = new SupportNode { Type = SupportNodeType.Junction, Position = new(1, 1, 0) };
        var top = new SupportNode { Type = SupportNodeType.Junction, Position = new(1, 1, 2) };
        graph.AddNode(bottom);
        graph.AddNode(top);
        var member = new SupportSegment
        {
            Type = SupportSegmentType.Trunk, NodeA = bottom.Id, NodeB = top.Id, Diameter = 0.6f
        };
        graph.AddSegment(member);

        Assert.Equal(original.Where(i => i.X > 5), vm.DetectedIslands);
        vm.Document.NotifyTransientChange();
        Assert.Single(vm.DetectedIslands);
        await vm.DetectIslandsCommand.ExecuteAsync(null);
        Assert.Single(vm.DetectedIslands);
        graph.RemoveSegment(member.Id);
        Assert.Equal(original.Select(i => (i.Position, i.AreaMm2, i.LayerIndex)),
            vm.DetectedIslands.Select(i => (i.Position, i.AreaMm2, i.LayerIndex)));

        vm.ClearIslandDetectionCommand.Execute(null);
        graph.AddSegment(member);
        Assert.Empty(vm.DetectedIslands);
    }

    [Fact]
    public async Task MovingDetectedModelInvalidatesCachedMarkers()
    {
        var vm = new MainViewModel();
        vm.Document.PlacementMode = PlacementMode.Off;
        var obj = new SceneObject("two islands", TwoBoxes());
        vm.Document.AddObject(obj);
        vm.SelectedObject = obj;
        await vm.DetectIslandsCommand.ExecuteAsync(null);
        Assert.Equal(2, vm.DetectedIslands.Count);
        obj.Transform = obj.Transform with { Translation = Vector3.UnitX };
        vm.Document.NotifyTransientChange();
        Assert.Empty(vm.DetectedIslands);
        Assert.False(vm.GenerateIslandSupportsCommand.CanExecute(null));
    }

    private static Mesh TwoBoxes()
    {
        Vector3[] points =
        [
            new(0, 0, 2), new(2, 0, 2), new(2, 2, 2), new(0, 2, 2),
            new(0, 0, 3), new(2, 0, 3), new(2, 2, 3), new(0, 2, 3)
        ];
        int[] indices = [0, 3, 2, 0, 2, 1, 4, 5, 6, 4, 6, 7,
            0, 1, 5, 0, 5, 4, 2, 3, 7, 2, 7, 6, 0, 4, 7, 0, 7, 3, 1, 2, 6, 1, 6, 5];
        return new Mesh(points.Concat(points.Select(p => p + new Vector3(10, 0, 0))).ToArray(),
            indices.Concat(indices.Select(i => i + 8)).ToArray());
    }
}
