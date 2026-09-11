using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Danslicer.App.Controls;
using Danslicer.App.Controls.Refresh;
using Danslicer.Core;

namespace Danslicer.App.Views;

public partial class MainWindow
{
    // Opt-in evidence from the real main window, real VM and production bindings.
    private void ConfigureWorkspaceCapture()
    {
        var args = Environment.GetCommandLineArgs();
        var index = Array.IndexOf(args, "--workspace-capture");
        if (index < 0 || index + 1 >= args.Length) return;
        var directory = System.IO.Path.GetFullPath(args[index + 1]);
        Opened += async (_, _) =>
        {
            Directory.CreateDirectory(directory);
            File.Delete(System.IO.Path.Combine(directory, "workspace-ok.txt"));
            File.Delete(System.IO.Path.Combine(directory, "workspace-error.txt"));
            try
            {
                await Task.Delay(600);
                var probeIndex = Array.IndexOf(args, "--toolbar-preference-probe");
                if (probeIndex >= 0)
                {
                    var expected = bool.Parse(args[probeIndex + 2]);
                    if (_workspaceToolbar.ShowLabels != expected)
                        throw new InvalidOperationException("Fresh process did not restore committed toolbar labels");
                    File.WriteAllText(System.IO.Path.Combine(directory, "toolbar-restart-ok.txt"), $"Fresh MAIN process restored toolbar labels={expected} from compatible workspace JSON.");
                    return;
                }
                await CheckWorkspace(directory);
                File.WriteAllText(System.IO.Path.Combine(directory, "workspace-ok.txt"),
                    "Real MainWindow/VM: STL import, selected object transform expression/undo, duplicate/undo, toolbar labels, mode scoping, all 12 popouts and persistent panel-free Support isolation rail with cube clearance/centered Reset/Cap label, handle-hover layer/mm editing, pointer drag commit/cancel and delayed dismissal, real print/settings bindings, editor Escape, viewport Escape, invoking-button focus, resize bounds, section reorder and expansion, durable project open/save history, failed-open status, narrow 640x480 layout and 100/150/200 density captures passed. Offscreen rendering omits the native OpenGL composition surface; no physical input, monitor transition or screen-reader claim.");
            }
            catch (Exception ex)
            {
                Environment.ExitCode = 1;
                File.WriteAllText(System.IO.Path.Combine(directory, "workspace-error.txt"), ex.ToString());
            }
            finally { if (Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime lifetime) lifetime.Shutdown(Environment.ExitCode); else Close(); }
        };
    }
    private async Task CheckWorkspace(string directory)
    {
        static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        static void Key(Control target, Key key, KeyModifiers modifiers = KeyModifiers.None) =>
            target.RaiseEvent(new KeyEventArgs { RoutedEvent = KeyDownEvent, Key = key, KeyModifiers = modifiers });
        async Task Layout() { await Task.Delay(120); UpdateLayout(); PositionWorkspacePopouts(); UpdateLayout(); }
        void Capture(string name, double scale = 1)
        {
            var content = (Control)Content!;
            using var bitmap = new RenderTargetBitmap(new PixelSize((int)(content.Bounds.Width * scale), (int)(content.Bounds.Height * scale)), new Vector(96 * scale, 96 * scale));
            bitmap.Render(content); bitmap.Save(System.IO.Path.Combine(directory, name), PngBitmapEncoderOptions.Default);
        }
        var vm = ViewModel!;
        var meshPath = System.IO.Path.Combine(directory, "workspace-smoke.stl");
        var vertices = new System.Numerics.Vector3[] { new(0, 0, 0), new(20, 0, 0), new(0, 20, 0), new(0, 0, 20) };
        var triangles = new[] { 0, 2, 1, 0, 1, 3, 1, 2, 3, 2, 0, 3 };
        var stl = new System.Text.StringBuilder("solid workspace\n");
        for (var t = 0; t < triangles.Length; t += 3)
        {
            stl.AppendLine("facet normal 0 0 0\nouter loop");
            for (var j = 0; j < 3; j++)
            {
                var v = vertices[triangles[t + j]];
                stl.AppendLine(FormattableString.Invariant($"vertex {v.X} {v.Y} {v.Z}"));
            }
            stl.AppendLine("endloop\nendfacet");
        }
        stl.AppendLine("endsolid workspace");
        File.WriteAllText(meshPath, stl.ToString());
        vm.ImportMesh(meshPath);
        Require(vm.Objects.Count == 1 && vm.SelectedObject is not null, "Native STL import/selection failed");
        Viewport.FrameAll();
        Width = 1200; Height = 800;
        vm.ViewMode = WorkspaceMode.Layout;
        await Layout();
        Require(HeaderAutoDrop.IsEffectivelyVisible && HeaderAutoDrop.IsEnabled, "Auto drop missing from Layout header");
        var autoDrop = vm.AutoDropEnabled;
        HeaderAutoDrop.IsChecked = !autoDrop;
        Require(vm.AutoDropEnabled != autoDrop, "Header Auto drop binding failed");
        HeaderAutoDrop.IsChecked = autoDrop;
        var tabsCentre = WorkspaceTabs.TranslatePoint(new Point(WorkspaceTabs.Bounds.Width / 2, 0), WorkspaceHeader)!.Value.X;
        Require(Math.Abs(tabsCentre - WorkspaceHeader.Bounds.Width / 2) < 1, "Workspace tabs are not centered");
        Require(WorkspaceGrid.ColumnDefinitions.Count <= 1, "Permanent settings columns remain");
        Require(_workspacePopouts.Count == 12, "Settings host missing");
        Click(ObjectsToolButton); await Layout();
        Require(ObjectsToolPopup.IsOpen, "Objects tool failed");
        Require(ReferenceEquals(ObjectsPopupContent.DataContext, vm), "Rehost lost VM binding");
        Click(TransformToolButton); await Layout();
        Require(!ObjectsToolPopup.IsOpen && TransformToolPopup.IsOpen, "Tools must be exclusive");
        var position = TransformPopupContent.GetVisualDescendants().OfType<ModelScrubField>()
            .Single(e => ReferenceEquals(e.Field, vm.Position[0]));
        var previous = vm.Position[0].Text;
        position.Focus(); Key(position, Avalonia.Input.Key.Enter);
        var positionEditor = position.GetVisualDescendants().OfType<TextBox>().Single();
        positionEditor.Text = "10 + 2"; Key(positionEditor, Avalonia.Input.Key.Enter);
        Require(vm.Position[0].Text == "12", "Production transform expression failed");
        vm.UndoCommand.Execute(null);
        Require(vm.Position[0].Text == previous, "Production transform undo failed");
        vm.DuplicateScopedCommand.Execute(null); Require(vm.Objects.Count == 2, "Duplicate command failed");
        vm.UndoCommand.Execute(null); Require(vm.Objects.Count == 1, "Duplicate undo failed");
        vm.Document.Select(vm.Objects[0]);
        await Layout();
        Require(position.IsEffectivelyVisible && position.Bounds.Height >= 24, "Transform field not visible after undo");
        await CheckToolbarResize(directory);
        Capture("workspace-layout.png");
        _workspaceToolbar.ShowLabels = true; await Layout();
        Capture("workspace-labels.png");
        Key(TransformToolPopup.Shell, Avalonia.Input.Key.Escape);
        Require(!TransformToolPopup.IsOpen && TransformToolButton.IsFocused, "Dismiss must return invoking tool focus");
        Click(_layoutTool); await Layout();
        var placement = LayoutOptions.GetVisualDescendants().OfType<ModelScrubField>().Single();
        placement.Focus(); Key(placement, Avalonia.Input.Key.Enter);
        var editor = placement.GetVisualDescendants().OfType<TextBox>().Single();
        editor.Focus(); var before = vm.PlacementHeight.Text;
        editor.Text = "12345"; Key(editor, Avalonia.Input.Key.Escape);
        Require(LayoutPopout.IsOpen && vm.PlacementHeight.Text == before, "Editor Escape must precede dismissal");
        Viewport.Focus(); Key(Viewport, Avalonia.Input.Key.Escape);
        Require(!LayoutPopout.IsOpen, "Escape from viewport must dismiss popout");
        vm.Document.Select(vm.Objects[0]);
        vm.ViewMode = WorkspaceMode.Support; await Layout();
        await CheckIsolationEditor(directory);
        Require(!TransformToolButton.IsVisible && SupportsToolButton.IsVisible && !_printTool.IsVisible, "Support tool scoping failed");
        foreach (var popup in new[] { ObjectsToolPopup, SupportsToolPopup, StructureToolPopup, GuidedToolPopup, RegionToolPopup, VisibilityToolPopup, RaftsToolPopup, ViewSettingsPopup })
        {
            Click((Button)popup.PlacementTarget!); await Layout();
            Require(popup.IsOpen && popup.Shell.Bounds.Height > 28, $"Cannot open {popup.Title}");
            Require(ReferenceEquals(popup.Shell.Body!.DataContext, vm), $"Binding lost for {popup.Title}");
            if (popup == SupportsToolPopup) Capture("workspace-support.png");
            if (popup == ViewSettingsPopup)
            {
                var config = Configuration.AppConfig.Current.Viewport;
                var originalAo = config.AmbientOcclusionEnabled;
                var originalReflection = config.PlateReflectionsEnabled;
                var originalCap = config.CapInterior;
                PopAo.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                PopReflections.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var configPath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Configuration.AppConfig.WorkspacePath)!, "config.json");
                var saved = Core.Config.UserConfig.Load(configPath).Viewport;
                Require(saved.AmbientOcclusionEnabled != originalAo && saved.PlateReflectionsEnabled != originalReflection,
                    "AO/reflection view toggles did not save");
                Require(saved.CapInterior == originalCap, "View effects changed saved Cap choice");
                Require(PopAo.IsChecked == AoMenuItem.IsChecked && PopReflections.IsChecked == ReflectionsMenuItem.IsChecked,
                    "View menu and popout effect state disagree");
                PopAo.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                PopReflections.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var oldStrength = config.AmbientOcclusionStrength;
                var oldRadius = config.AmbientOcclusionRadiusMm;
                void EditAo(ScrubField field, string expression)
                {
                    field.Focus(); Key(field, Avalonia.Input.Key.Enter);
                    var input = field.GetVisualDescendants().OfType<TextBox>().Single();
                    input.Text = expression; Key(input, Avalonia.Input.Key.Enter);
                }
                EditAo(PopAoStrength, "0.1 + 0.1");
                EditAo(PopAoRadius, "0.3 cm");
                Require(Math.Abs(config.AmbientOcclusionStrength - 0.2f) < 0.0001 && Math.Abs(config.AmbientOcclusionRadiusMm - 3) < 0.0001,
                    "AO popout expressions/units failed");
                saved = Core.Config.UserConfig.Load(configPath).Viewport;
                Require(saved.AmbientOcclusionStrength == config.AmbientOcclusionStrength && saved.AmbientOcclusionRadiusMm == 3,
                    "AO popout values were not persisted");
                EditAo(PopAoStrength, oldStrength.ToString(System.Globalization.CultureInfo.InvariantCulture));
                EditAo(PopAoRadius, oldRadius.ToString(System.Globalization.CultureInfo.InvariantCulture));
                var originalShadows = config.ModelShadows;
                var workingStrength = config.WorkingShadowStrength;
                var workingSoftness = config.WorkingShadowSoftnessMm;
                var presentationStrength = config.PresentationShadowStrength;
                var presentationSoftness = config.PresentationShadowSoftnessMm;
                PopShadowMode.SelectedIndex = (int)Core.Config.ModelShadowMode.Working;
                EditAo(PopShadowStrength, "0.1 + 0.2");
                EditAo(PopShadowSoftness, "0.08 cm");
                PopShadowMode.SelectedIndex = (int)Core.Config.ModelShadowMode.Presentation;
                EditAo(PopShadowStrength, "0.6");
                EditAo(PopShadowSoftness, "0.2 cm");
                PopShadowMode.SelectedIndex = (int)Core.Config.ModelShadowMode.Off;
                Require(!PopShadowStrength.IsEnabled && !PopShadowSoftness.IsEnabled, "Off shadow fields enabled");
                saved = Core.Config.UserConfig.Load(configPath).Viewport;
                Require(saved.ModelShadows == Core.Config.ModelShadowMode.Off && saved.WorkingShadowStrength == 0.3f && saved.WorkingShadowSoftnessMm == 0.8f && saved.PresentationShadowStrength == 0.6f && saved.PresentationShadowSoftnessMm == 2f, "Shadow settings did not persist independently");
                PopShadowMode.SelectedIndex = (int)Core.Config.ModelShadowMode.Working;
                Require(Math.Abs(PopShadowStrength.Value - 0.3) < 0.0001 && Math.Abs(PopShadowSoftness.Value - 0.8) < 0.0001, "Working values lost on mode switch");
                Require(config.AmbientOcclusionStrength == oldStrength && config.AmbientOcclusionRadiusMm == oldRadius && config.CapInterior == originalCap, "Shadows changed AO/cap settings");
                config.WorkingShadowStrength = workingStrength; config.WorkingShadowSoftnessMm = workingSoftness;
                config.PresentationShadowStrength = presentationStrength; config.PresentationShadowSoftnessMm = presentationSoftness;
                config.ModelShadows = originalShadows; ApplyRenderPathChange();
                await Layout();
                File.WriteAllText(System.IO.Path.Combine(directory, "shadows-ok.txt"), "Off/Working/Presentation switching, independent saved strengths/softness, expressions, cm conversion, disabled Off controls and AO/cap independence passed. Original settings restored in isolated config.");
                Capture("workspace-view-effects.png");
                File.WriteAllText(System.IO.Path.Combine(directory, "view-effects-ok.txt"), "AO/reflection toggles save, menu/popout states agree, saved Cap unchanged, original effect choices restored in isolated configuration.");
            }
            Key(popup.Shell, Avalonia.Input.Key.Escape);
            Require(!popup.IsOpen, $"Cannot close {popup.Title}");
        }
        // Open detector through policy only: don't start a potentially lengthy detection job.
        ToggleViewportPopup(_islandDetectionPopupState); await Layout();
        Require(IslandDetectionToolPopup.IsOpen, "Detector settings missing");
        CloseWorkspacePopout(IslandDetectionToolPopup);
        vm.ViewMode = WorkspaceMode.Slicing; await Layout();
        Require(!SupportsToolButton.IsVisible && _printTool.IsVisible && !ViewSettingsButton.IsVisible, "Slicing tool scoping failed");
        Click(_printTool); await Layout();
        Require(PrintPopout.IsOpen && ReferenceEquals(PrintPopout.Shell.Body!.DataContext, vm), "Print bindings lost");
        var print = (StackPanel)PrintPopout.Shell.Body!;
        var first = (ReorderableExpander)print.Children[0];
        var grip = first.GetVisualDescendants().OfType<Button>().Single(b => (AutomationProperties.GetName(b) ?? "").StartsWith("Reorder "));
        Key(grip, Avalonia.Input.Key.Down, KeyModifiers.Alt);
        Require(print.Children[1] == first, "Production section reorder failed");
        Key(grip, Avalonia.Input.Key.Up, KeyModifiers.Alt);
        first.IsExpanded = false; Require(!first.IsExpanded, "Collapse failed"); first.IsExpanded = true;
        await Layout();
        foreach (var scale in new[] { 1.0, 1.5, 2.0 }) Capture($"workspace-slicing-{scale * 100:0}.png", scale);
        Width = 640; Height = 480; await Layout();
        var resize = PrintPopout.GetVisualDescendants().OfType<Border>().Single(b => AutomationProperties.GetName(b) == "Resize popout width");
        for (var i = 0; i < 100; i++) Key(resize, Avalonia.Input.Key.Right);
        await Layout();
        var corner = PrintPopout.TranslatePoint(new Point(PrintPopout.Bounds.Width, PrintPopout.Bounds.Height), ViewportSurface)!.Value;
        Require(corner.X <= ViewportSurface.Bounds.Width && corner.Y <= ViewportSurface.Bounds.Height, "Narrow popout overflow");
        tabsCentre = WorkspaceTabs.TranslatePoint(new Point(WorkspaceTabs.Bounds.Width / 2, 0), WorkspaceHeader)!.Value.X;
        Require(Math.Abs(tabsCentre - WorkspaceHeader.Bounds.Width / 2) < 1, "Narrow workspace tabs are not centered");
        Require(HeaderAutoDrop.IsEffectivelyVisible, "Narrow Auto drop disappeared");
        Capture("workspace-narrow.png");
        // Project round trip uses the same successful-open path as the dropdown.
        var path = System.IO.Path.Combine(directory, "workspace-smoke.danslicer");
        vm.SaveProject(path, CaptureProjectViewState()); RememberProject();
        Require(_recentProjects.Contains(path), "Saved project missing from recents");
        OpenRecentProject(path); Require(vm.ProjectPath == path, "Recent project open failed");
        Click(ProjectDropdown);
        Require(ProjectDropdown.ContextMenu!.Items.OfType<MenuItem>().First().Header?.ToString() == "Open another project…",
            "Project menu text is malformed");
        ProjectDropdown.ContextMenu.Close();
        OpenRecentProject(path + ".missing"); Require(vm.ViewportStatus.StartsWith("Open failed:"), "Missing recent project must report failure");
        await CheckSettingsIntegration(directory);
        await CheckLiveNumericPreviews(directory);
        await CheckPrintWorkflow(directory);
        await CheckSupportEditorRefinements(directory);
        File.Delete(path);
        File.Delete(meshPath);
        PrepareToolbarCloseCheck(directory);
    }
}
