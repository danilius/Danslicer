using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Danslicer.Core;
using Danslicer.Core.IO;
using Danslicer.Core.Scene;

namespace Danslicer.App.Views;

public partial class MainWindow
{
    // Only called by --workspace-capture, after AppConfig has been isolated.
    private async Task CheckPrintWorkflow(string directory)
    {
        static void Require(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
        var vm = ViewModel!;
        vm.NewProject();
        vm.Document.Printer = vm.Document.Printer with
        {
            Id = "task05-fixture", IsBuiltIn = false, Name = "Task 05 fixture",
            DisplayWidthMm = 48, DisplayHeightMm = 30, ZTravelMm = 30,
            ResolutionX = 480, ResolutionY = 300,
        };
        vm.Document.PrintSettings = vm.Document.PrintSettings with { LayerHeight = 0.1f, AntiAliasing = false, XyCompensation = 0 };
        vm.RefreshPrinterOptions(notifyDocument: false);
        vm.PrintSettings.Refresh();
        vm.Document.SupportSettings = new Core.Config.SupportConfig();
        // Fresh imported closed tetrahedron; keep its original importer path under test.
        vm.ImportMesh(System.IO.Path.Combine(directory, "workspace-smoke.stl"));
        var obj = vm.Objects.Single();
        vm.Document.PlacementMode = Core.Config.PlacementMode.Off;
        var before = obj.Transform;
        var after = Transform.Identity with { Scale = new Vector3(0.3f), Translation = new Vector3(-3, -3, 5) };
        vm.Document.CommitTransform(obj, before, after, "Fixture placement");
        vm.UndoCommand.Execute(null); Require(obj.Transform == before, "Workflow transform undo failed");
        vm.RedoCommand.Execute(null); Require(obj.Transform == after, "Workflow transform redo failed");
        vm.Document.Select(obj);
        vm.ViewMode = WorkspaceMode.Support;
        Require(vm.GenerateSupportsScopedCommand.CanExecute(null), "Generate unavailable in Support");
        await vm.GenerateSupportsCommand.ExecuteAsync(null);
        var nodeCount = vm.Document.Supports.Nodes.Count;
        var segmentCount = vm.Document.Supports.Segments.Count;
        Require(nodeCount > 0 && segmentCount > 0 && !vm.IsGeneratingSupports,
            "Explicit support generation failed: " + vm.ViewportStatus);
        vm.UndoCommand.Execute(null);
        Require(vm.Document.Supports.Nodes.Count == 0, "Generation must undo in one step");
        vm.RedoCommand.Execute(null);
        Require(vm.Document.Supports.Nodes.Count == nodeCount, "Generation redo failed");
        vm.AddRaftScopedCommand.Execute(null);
        var raft = obj.Raft;
        Require(raft is not null, "Explicit Add raft failed");
        vm.UndoCommand.Execute(null); Require(obj.Raft is null, "Add raft undo failed");
        vm.RedoCommand.Execute(null); Require(obj.Raft == raft, "Add raft redo failed");
        Require(vm.LastSlice is null, "Placement/support/raft unexpectedly started slicing");
        var project = System.IO.Path.Combine(directory, "workflow.danslicer");
        var output = System.IO.Path.Combine(directory, "workflow.pwmx");
        vm.SaveProject(project, CaptureProjectViewState()); RememberProject();
        vm.NewProject(); OpenRecentProject(project);
        obj = vm.Objects.Single();
        Require(obj.Transform == after && obj.Raft == raft && obj.SourcePath is not null &&
            vm.Document.Supports.Nodes.Count == nodeCount && vm.Document.Supports.Segments.Count == segmentCount,
            $"Generated support/raft/transform project roundtrip failed: transform={obj.Transform} expected={after}; raft={obj.Raft} expected={raft}; nodes={vm.Document.Supports.Nodes.Count}/{nodeCount}; segments={vm.Document.Supports.Segments.Count}/{segmentCount}");
        Require(vm.Document.Printer.ResolutionX == 480 && vm.Document.PrintSettings.LayerHeight == 0.1f,
            $"Fixture printer/print settings roundtrip failed: {vm.Document.Printer}; {vm.Document.PrintSettings}");
        vm.ViewMode = WorkspaceMode.Slicing;
        Require(vm.SliceScopedCommand.CanExecute(null) && !vm.GenerateSupportsScopedCommand.CanExecute(null),
            "Print workflow mode scoping failed");
        await vm.SliceCommand.ExecuteAsync(null);
        var slice = vm.LastSlice;
        Require(slice is { LayerCount: > 0 } && vm.PreviewImage is not null && !vm.IsSlicing,
            "Explicit slice/preview failed: " + vm.ViewportStatus);
        Require(await vm.ExportAsync(output), "Explicit export failed: " + vm.ViewportStatus);
        Require(ReferenceEquals(slice, vm.LastSlice), "Unchanged export unexpectedly re-sliced");
        var exported = PhotonWorkshopFile.Read(output);
        Require(exported.Layers.Count == slice!.LayerCount && exported.ResolutionX == 480 && exported.ResolutionY == 300,
            "Export layer/header roundtrip failed");
        for (var i = 0; i < exported.Layers.Count; i++)
        {
            Require(exported.DecodeLayer(i).Count(pixel => pixel > 0) == exported.Layers[i].LitPixels,
                $"Export layer {i} decoded pixel count mismatch");
            var expected = new byte[480 * 300];
            slice.Layers[i].Decode(480, 300, expected);
            Require(exported.DecodeLayer(i).SequenceEqual(expected), $"Export layer {i} differs from sliced bitmap");
        }
        // The last tetrahedron section can be sub-pixel; witness raft, support and model
        // at known interior heights instead of requiring that terminal layer to be lit.
        Require(exported.Layers[0].LitPixels > 0 && exported.Layers[20].LitPixels > 0 && exported.Layers[60].LitPixels > 0,
            "Export missing raft, support or model interior data");
        vm.PreviewLayer = Math.Min(10, vm.PreviewLayerMax);
        await Task.Delay(180); UpdateLayout();
        var content = (Control)Content!;
        using (var bitmap = new RenderTargetBitmap(new PixelSize((int)content.Bounds.Width, (int)content.Bounds.Height)))
        {
            bitmap.Render(content);
            bitmap.Save(System.IO.Path.Combine(directory, "workflow-sliced.png"), PngBitmapEncoderOptions.Default);
        }
        var originalPrinter = vm.Document.Printer;
        vm.Document.Printer = originalPrinter with { Id = "goo-workflow-fixture", Name = "GOO workflow fixture", NativeFormat = "goo", FileExtension = "goo", FormatVersion = 3 };
        string gooOutput = System.IO.Path.Combine(directory, "workflow.goo");
        Require(await vm.ExportAsync(gooOutput), "Native GOO export through MainWindow failed: " + vm.ViewportStatus);
        Require(File.ReadAllBytes(gooOutput).AsSpan(0, 4).SequenceEqual("V3.0"u8), "MainWindow did not dispatch to GOO writer");
        Require(vm.LastSlice?.Printer.NativeFormat == "goo", "Export reused an old printer's slice");
        vm.Document.Printer = originalPrinter;
        File.WriteAllText(System.IO.Path.Combine(directory, "multi-brand-export-ok.txt"), "MainWindow export invalidated the old printer slice and produced a native GOO file from the generated support/raft scene.");
        File.WriteAllText(System.IO.Path.Combine(directory, "workflow-ok.txt"),
            $"Native MainWindow VM: imported STL; transform undo/redo; explicit generation ({nodeCount} nodes/{segmentCount} segments), one-step undo/redo; Add raft undo/redo; project reload preserving geometry, source path, transform and printer/print settings; explicit slicing ({slice.LayerCount} layers); preview navigation; export and decoding every layer/header passed. Every exported bitmap equals its sliced source; raft/support/model interior layers contain data; unchanged export reused slice. Temporary 480x300 printer fixture, not physical printer certification. No file picker/UVtools dialog exercised.\nExport SHA256: {Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(output)))}\n");
    }
}
