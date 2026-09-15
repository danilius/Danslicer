using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Danslicer.App.Controls;
using Danslicer.App.ViewModels;
using Danslicer.Core.Config;
using Danslicer.Core.Geometry;
using Danslicer.Core.Scene;
using Danslicer.Core.Supports;

namespace Danslicer.App.Views;

public partial class MainWindow
{
    private void ConfigureStructureCapture()
    {
        var args = Environment.GetCommandLineArgs();
        var index = Array.IndexOf(args, "--structure-capture");
        if (index < 0 || index + 1 >= args.Length) return;
        var directory = args[index + 1];
        Opened += async (_, _) =>
        {
            try
            {
                Directory.CreateDirectory(directory);
                File.Delete(Path.Combine(directory, "structure-ok.txt"));
                File.Delete(Path.Combine(directory, "structure-error.txt"));
                await Task.Delay(250);
                var vm = ViewModel!;
                var document = vm.Document;
                var model = new SceneObject("Preview test slab", new Mesh(
                    [new(-30, -10, 60), new(30, -10, 60), new(0, 20, 60)], [0, 2, 1]));
                document.AddObject(model); document.Select(model);
                document.SupportSettings = new SupportConfig { UseBaseGrid = false, AutoParenting = false, AutoBracing = false };
                for (var i = 0; i < 13; i++)
                {
                    if (!document.AddManualSupport(model, new Vector3(i * 2 - 12, 0, 60), -Vector3.UnitZ))
                        throw new InvalidOperationException("Could not place fixture support");
                }
                vm.IsSupportView = true;
                Viewport.FrameAll();
                await Task.Delay(200);
                OnStructureToolClick(StructureToolButton, new RoutedEventArgs());
                await Task.Delay(150);
                Require(ReferenceEquals(StructureToolPopup.Content, _structureToolbar), "Structure is not a toolbar");
                Require(_structureToolbar.GetLogicalDescendants().OfType<Button>().Count() == 8, "Structure actions missing");
                _structureToolbar.ShowLabels = true;
                await Task.Delay(100);
                Save(_structureToolbar, "structure-toolbar-expanded.png");
                var manualSettingsButton = _structureToolbar.GetLogicalDescendants().OfType<Button>().Single(b => b.Flyout is Flyout);
                manualSettingsButton.Flyout!.ShowAt(manualSettingsButton);
                await Task.Delay(100);
                var manualSettingsBody = (StackPanel)((Flyout)manualSettingsButton.Flyout).Content!;
                var manualDiameter = manualSettingsBody.Children.OfType<NumericUpDown>().Single();
                manualDiameter.Value = 0.9m;
                await Task.Delay(100);
                Require(Math.Abs(document.SupportSettings.ManualBraceDiameter - 0.9f) < 0.001,
                    "Manual diameter settings binding failed");
                Save(manualSettingsBody, "manual-settings.png");
                manualDiameter.Value = 1.2m;
                manualSettingsButton.Flyout.Hide();
                _structureToolbar.ShowLabels = false;
                await Task.Delay(100);
                Save(_structureToolbar, "structure-toolbar-collapsed.png");
                Require(Math.Abs(_structureToolbar.Bounds.Width - Controls.Refresh.FloatingToolbar.IconWidth) < 1,
                    "Structure toolbar did not collapse");
                _structureToolbar.ShowLabels = true;
                OnStructureToolClick(StructureToolButton, new RoutedEventArgs());
                var manualBefore = document.Supports.Segments.Select(s => s.Id).Order().ToArray();
                var manualTrunks = document.Supports.Segments.Where(s => s.Type == SupportSegmentType.Trunk)
                    .OrderBy(s => document.Supports.GetNode(s.NodeA).Position.X).Take(2).ToArray();
                Vector2 ManualScreen(int index, float z)
                {
                    var world = document.Supports.GetNode(manualTrunks[index].NodeA).Position with { Z = z };
                    return Viewport.Camera.WorldToScreen(world, (float)Viewport.Bounds.Width, (float)Viewport.Bounds.Height)!.Value;
                }
                Viewport.ToggleManualBrace();
                Viewport.ManualBraceSnap45 = true;
                Viewport.UpdateManualBrace(ManualScreen(0, 20));
                Viewport.ClickManualBrace();
                Viewport.UpdateManualBrace(ManualScreen(1, 18));
                Viewport.ClickManualBrace();
                Require(document.Supports.Segments.Count(s => s.Type == SupportSegmentType.Bracing) == 1,
                    "Manual brace two-click placement failed: " + Viewport.StatusText);
                Viewport.ToggleManualBrace();
                document.Undo();
                Require(manualBefore.SequenceEqual(document.Supports.Segments.Select(s => s.Id).Order()),
                    "Manual brace undo changed existing supports");
                var trunk = document.Supports.Segments.First(s => s.Type == SupportSegmentType.Trunk);
                var centre = Vector3.Lerp(document.Supports.GetNode(trunk.NodeA).Position,
                    document.Supports.GetNode(trunk.NodeB).Position, 0.5f);
                var supportScreen = Viewport.Camera.WorldToScreen(centre, (float)Viewport.Bounds.Width, (float)Viewport.Bounds.Height)!.Value;
                var picked = Viewport.PickPlacementSurface(supportScreen, out var contact, out var normal, out var onSupport);
                Require(picked == model && onSupport, "T placement did not pick the support surface");
                Require(document.AddManualSupport(model, contact, normal, out _, out _, contactOnSupport: true),
                    "T placement on the picked support failed");
                document.Undo();
                var before = document.Supports.Nodes.Select(n => n.Id).Order().ToArray();
                Viewport.ParentSupports();
                await Task.Delay(1500);
                var window = _structurePreviewPanel ?? throw new InvalidOperationException("Parent button did not open preview");
                Require(TopLevel.GetTopLevel(window) == this && window.Parent == ViewportSurface, "Preview must be an in-viewport panel, not a separate window");
                Require(window.HorizontalAlignment == Avalonia.Layout.HorizontalAlignment.Right, "Preview must be anchored on the right");
                var settingsView = window.GetLogicalDescendants().OfType<StructureSettingsView>().Single();
                var config = (ConfigViewModel)settingsView.DataContext!;
                Require(config.IsCandelabra, "New parenting default is not Candelabra");
                Save(window, "parenting.png");
                Save((Control)Content!, "parenting-workspace.png");
                var oldWidth = Width; var oldHeight = Height;
                Width = 760; Height = 650;
                await Task.Delay(300);
                var panelCorner = window.TranslatePoint(new Point(window.Bounds.Width, window.Bounds.Height), ViewportSurface)!.Value;
                Require(panelCorner.X <= ViewportSurface.Bounds.Width && panelCorner.Y <= ViewportSurface.Bounds.Height, "Preview overflows a resized viewport");
                Save(window, "parenting-narrow.png");
                Width = oldWidth; Height = oldHeight;
                await Task.Delay(200);
                config.SupportCandelabraMaxTips = 4;
                await Task.Delay(2000);
                Require(before.SequenceEqual(document.Supports.Nodes.Select(n => n.Id).Order()), "Preview mutated document");
                Save(window, "parenting-small-groups.png");
                var apply = window.GetLogicalDescendants().OfType<Button>().Single(b => Equals(b.Content, "Apply"));
                Require(apply.IsEnabled, "Preview cannot apply");
                apply.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Require(document.Supports.Nodes.Count(n => n.Type == SupportNodeType.Base) >= 4, "Group limit did not apply");
                Require(document.Undo(), "Apply cannot undo");
                Require(before.SequenceEqual(document.Supports.Nodes.Select(n => n.Id).Order()), "Undo did not restore original graph");
                Viewport.BraceSupports();
                await Task.Delay(2000);
                window = _structurePreviewPanel ?? throw new InvalidOperationException("Brace button did not open preview");
                var bracingConfig = (ConfigViewModel)window.GetLogicalDescendants().OfType<StructureSettingsView>().Single().DataContext!;
                bracingConfig.SupportBracingClusterGapMm = 0;
                await Task.Delay(2000);
                Save(window, "bracing.png");
                var braceApply = window.GetLogicalDescendants().OfType<Button>().Single(b => Equals(b.Content, "Apply"));
                Require(braceApply.IsEnabled, $"Bracing unavailable: target={document.SupportTarget?.Name}, operands={document.StructureOperands().Count}, bases={document.Supports.Nodes.Count(n => n.Type == SupportNodeType.Base)}");
                window.Close();
                Require(before.SequenceEqual(document.Supports.Nodes.Select(n => n.Id).Order()), "Cancel mutated document");
                Require(!Viewport.StructurePreviewActive, "Cancel left viewport locked");
                Viewport.ParentSupports();
                await Task.Delay(300);
                window = _structurePreviewPanel!;
                config = (ConfigViewModel)window.GetLogicalDescendants().OfType<StructureSettingsView>().Single().DataContext!;
                config.SupportCandelabraMaxTips = 4;
                // Switch immediately, before the debounce timer and without clicking Apply.
                Viewport.BraceSupports();
                Require(document.Supports.Nodes.Count(n => n.Type == SupportNodeType.Base) >= 4 &&
                    document.Supports.Nodes.Count(n => n.Type == SupportNodeType.Base) < 13, "Switching to Brace discarded parenting");
                var parented = document.Supports.Nodes.Select(n => n.Id).Order().ToArray();
                window = _structurePreviewPanel!;
                config = (ConfigViewModel)window.GetLogicalDescendants().OfType<StructureSettingsView>().Single().DataContext!;
                config.SupportBracingClusterGapMm = 0;
                config.SupportBracingNeighbourDistanceMm = 30;
                Require(vm.UndoCommand.CanExecute(null), "Pending preview must enable Undo");
                Viewport.Focus();
                Viewport.RaiseEvent(new Avalonia.Input.KeyEventArgs
                {
                    RoutedEvent = Avalonia.Input.InputElement.KeyDownEvent,
                    Key = Avalonia.Input.Key.Z, KeyModifiers = Avalonia.Input.KeyModifiers.Control
                });
                Require(_structurePreviewPanel is null && !Viewport.StructurePreviewActive, "Undo did not close the preview");
                Require(parented.SequenceEqual(document.Supports.Nodes.Select(n => n.Id).Order()), "Undo Brace removed parenting");
                Require(document.History.RedoName == "Brace supports", "Undo Brace did not preserve Redo");
                vm.UndoCommand.Execute(null);
                Require(before.SequenceEqual(document.Supports.Nodes.Select(n => n.Id).Order()), "Second Undo did not restore independent supports");
                vm.RedoCommand.Execute(null);
                Require(parented.SequenceEqual(document.Supports.Nodes.Select(n => n.Id).Order()), "Redo did not restore parenting");
                vm.RedoCommand.Execute(null);
                Require(document.Supports.Segments.Any(s => s.Type == SupportSegmentType.Bracing), "Redo did not restore bracing");
                Viewport.ParentSupports();
                window = _structurePreviewPanel!;
                config = (ConfigViewModel)window.GetLogicalDescendants().OfType<StructureSettingsView>().Single().DataContext!;
                config.SupportCandelabraMaxTips = 1;
                await Task.Delay(1500);
                var selectionBeforeInspect = document.SupportSelection.Order().ToArray();
                var tipPosition = document.Supports.Nodes.First(n => n.Type == SupportNodeType.Tip).Position;
                var screen = Viewport.Camera.WorldToScreen(tipPosition, (float)Viewport.Bounds.Width, (float)Viewport.Bounds.Height)!.Value;
                Require(Viewport.InspectStructurePreviewAt(screen), "Orange support could not be picked in detached graph");
                Require(window.GetLogicalDescendants().OfType<TextBlock>().Any(t => t.Text?.StartsWith("Selected orange support") == true),
                    "Clicking orange support did not show its explanation");
                Require(selectionBeforeInspect.SequenceEqual(document.SupportSelection.Order()), "Inspecting preview mutated selection");
                window.Close();
                vm.IsLayoutView = true;
                AutoDropSettingsButton.Flyout!.ShowAt(AutoDropSettingsButton);
                await Task.Delay(150);
                var flyout = (Flyout)AutoDropSettingsButton.Flyout;
                Save((Control)flyout.Content!, "auto-drop.png");
                flyout.Hide();
                File.WriteAllText(Path.Combine(directory, "structure-ok.txt"), "Structure toolbar expanded/collapsed, support surface picking and placement, actual Parent and Brace entry points, Candelabra default, settings refresh, provisional graph, Apply, single Undo, Cancel, viewport cleanup and Auto Drop flyout passed. PNGs capture controls; native OpenGL is not included.");
            }
            catch (Exception ex)
            {
                Environment.ExitCode = 1;
                File.WriteAllText(Path.Combine(directory, "structure-error.txt"), ex.ToString());
            }
            finally { (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown(Environment.ExitCode); }
        };

        void Save(Control control, string name)
        {
            var target = control is Window w ? (Control)w.Content! : control;
            using var bitmap = new RenderTargetBitmap(new PixelSize((int)Math.Ceiling(target.Bounds.Width), (int)Math.Ceiling(target.Bounds.Height)));
            bitmap.Render(target); bitmap.Save(Path.Combine(directory, name), PngBitmapEncoderOptions.Default);
        }
        static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}
