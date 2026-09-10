using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Danslicer.App.Configuration;
using Danslicer.App.Controls.Refresh;
using Danslicer.Core;
using Danslicer.Core.Config;

namespace Danslicer.App.Views;

public partial class MainWindow
{
    private async Task CheckSettingsIntegration(string directory)
    {
        static void Require(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
        static void Key(Control control, Key key, KeyModifiers modifiers = KeyModifiers.None) =>
            control.RaiseEvent(new KeyEventArgs { RoutedEvent = KeyDownEvent, Key = key, KeyModifiers = modifiers });
        static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        static TextBox Edit(ScrubField field, string text)
        {
            field.Focus(); Key(field, Avalonia.Input.Key.Enter);
            var box = field.GetVisualDescendants().OfType<TextBox>().Single(); box.Text = text; return box;
        }
        async Task Layout() { await Task.Delay(150); UpdateLayout(); PositionWorkspacePopouts(); UpdateLayout(); }
        var vm = ViewModel!;
        Width = 1200; Height = 800; _workspaceToolbar.ShowLabels = false;
        vm.ViewMode = WorkspaceMode.Layout; vm.Document.Select(vm.Objects[0]);
        Click(TransformToolButton); await Layout();
        var position = TransformPopupContent.GetVisualDescendants().OfType<ModelScrubField>().Single(f => f.Field == vm.Position[0]);
        var original = position.Value;
        var surface = position.GetVisualDescendants().OfType<Border>().First();
        var pointer = new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, true);
        var point = surface.TranslatePoint(new Point(20, 12), this)!.Value;
        var pressed = new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed);
        var commits = 0; position.EditCommitted += (_, _) => commits++;
        surface.RaiseEvent(new PointerPressedEventArgs(surface, pointer, this, point, 0, pressed, KeyModifiers.None, 1));
        for (var i = 1; i <= 5; i++) surface.RaiseEvent(new PointerEventArgs(PointerMovedEvent, surface, pointer, this, point + new Vector(i * 10, 0), (ulong)i, pressed, KeyModifiers.None));
        Require(vm.Position[0].Value == original && commits == 0, "Drag previews must not apply to production model");
        surface.RaiseEvent(new PointerReleasedEventArgs(surface, pointer, this, point + new Vector(50, 0), 6, new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonReleased), KeyModifiers.None, MouseButton.Left));
        Require(commits == 1 && vm.Position[0].Value != original, "One transform commit per pointer gesture");
        vm.UndoCommand.Execute(null);
        Require(vm.Position[0].Value == original && position.Value == original, "Single undo must restore entire transform gesture");
        surface.RaiseEvent(new PointerPressedEventArgs(surface, pointer, this, point, 7, pressed, KeyModifiers.None, 1));
        surface.RaiseEvent(new PointerEventArgs(PointerMovedEvent, surface, pointer, this, point + new Vector(50, 0), 8, pressed, KeyModifiers.Shift));
        Key(position, Avalonia.Input.Key.Escape);
        Require(position.Value == original && commits == 1 && pointer.Captured is null && TransformToolPopup.IsOpen, "Production drag Escape cancels before popout dismissal");
        var box = Edit(position, "1cm + 2mm"); Key(box, Avalonia.Input.Key.Enter);
        Require(position.Value == 12, "Production unit expression failed"); vm.UndoCommand.Execute(null);
        box = Edit(position, "bad expression"); Key(box, Avalonia.Input.Key.Enter);
        Require(box.IsVisible && box.IsFocused, "Invalid expression must retain editor/focus");
        Key(box, Avalonia.Input.Key.Escape); Require(position.Value == original, "Invalid expression cancellation changed model");
        box = Edit(position, "14"); Viewport.Focus();
        Require(position.Value == 14, "Focus loss did not commit once"); vm.UndoCommand.Execute(null);

        vm.ViewMode = WorkspaceMode.Support; Click(SupportsToolButton); await Layout();
        var tip = SupportsToolPopup.GetVisualDescendants().OfType<FilledNumericSlider>().First();
        var saves = 0; void Saved() => saves++;
        vm.SupportSettings.Saved += Saved;
        box = Edit(tip, "0.05cm + 0.1mm"); Key(box, Avalonia.Input.Key.Enter);
        Require(Math.Abs(vm.SupportSettings.SupportTipDiameter - 0.6) < 0.00001 && saves == 1, "Support expression must call existing saving setter exactly once");
        box = Edit(tip, "1001"); Key(box, Avalonia.Input.Key.Enter);
        Require(saves == 1 && box.IsVisible, "Out-of-range support setting applied"); Key(box, Avalonia.Input.Key.Escape);
        var same = tip.Value; box = Edit(tip, same.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
        Key(box, Avalonia.Input.Key.Enter); Require(saves == 1, "Unchanged edit must not save twice");
        vm.SupportSettings.Saved -= Saved;
        var count = SupportsToolPopup.GetVisualDescendants().OfType<FilledNumericSlider>().First(f => f.IsInteger);
        count.BringIntoView(); await Layout();
        box = Edit(count, "3.6"); Key(box, Avalonia.Input.Key.Enter);
        Require(count.Value == 4, "Integer field must normalize at commit");
        Key(count, Avalonia.Input.Key.Right, KeyModifiers.Shift);
        Require(count.Value == 5, "Integer keyboard fine modifier must still make a whole step");
        tip.BringIntoView(); await Layout();
        var supportSection = SupportsToolPopup.GetLogicalDescendants().OfType<ReorderableExpander>().First();
        var grip = supportSection.GetVisualDescendants().OfType<Button>().Single(b => (AutomationProperties.GetName(b) ?? "").StartsWith("Reorder "));
        var sectionPanel = (Panel)supportSection.Parent!;
        var orderBeforeDrag = File.ReadAllText(AppConfig.WorkspacePath);
        point = grip.TranslatePoint(new Point(12, 12), this)!.Value;
        var swapDistance = sectionPanel.Children[1].Bounds.Center.Y - supportSection.Bounds.Center.Y + 10;
        void StartSwap()
        {
            grip.RaiseEvent(new PointerPressedEventArgs(grip, pointer, this, point, 20, pressed, KeyModifiers.None, 1));
            grip.RaiseEvent(new PointerEventArgs(PointerMovedEvent, grip, pointer, this, point + new Vector(0, swapDistance), 21, pressed, KeyModifiers.None));
            Require(sectionPanel.Children[1] == supportSection, "Production section must swap during drag");
            Require(File.ReadAllText(AppConfig.WorkspacePath) == orderBeforeDrag, "Live swap must not persist before drop");
        }
        StartSwap();
        // Moving back across the neighbour restores its slot without ending the gesture.
        grip.RaiseEvent(new PointerEventArgs(PointerMovedEvent, grip, pointer, this, point - new Vector(0, 10), 22, pressed, KeyModifiers.None));
        Require(sectionPanel.Children[0] == supportSection && pointer.Captured == grip, "Live reverse swap lost position/capture");
        Key(grip, Avalonia.Input.Key.Escape);
        StartSwap(); pointer.Capture(null);
        Require(sectionPanel.Children[0] == supportSection && File.ReadAllText(AppConfig.WorkspacePath) == orderBeforeDrag, "Capture loss must restore original order without saving");
        StartSwap(); Key(grip, Avalonia.Input.Key.Escape);
        Require(sectionPanel.Children[0] == supportSection && File.ReadAllText(AppConfig.WorkspacePath) == orderBeforeDrag, "Escape must restore original order without saving");
        StartSwap();
        grip.RaiseEvent(new PointerReleasedEventArgs(grip, pointer, this, point + new Vector(0, swapDistance), 23, new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonReleased), KeyModifiers.None, MouseButton.Left));
        Require(sectionPanel.Children[1] == supportSection && File.ReadAllText(AppConfig.WorkspacePath) != orderBeforeDrag, "Drop must persist final order");
        Key(grip, Avalonia.Input.Key.Up, KeyModifiers.Alt);
        Key(grip, Avalonia.Input.Key.Down, KeyModifiers.Alt); supportSection.IsExpanded = false;
        var resize = SupportsToolPopup.GetVisualDescendants().OfType<Border>().Single(b => AutomationProperties.GetName(b) == "Resize popout width");
        Key(resize, Avalonia.Input.Key.Right);
        var savedWidth = SupportsToolPopup.Shell.Width;
        point = resize.TranslatePoint(new Point(3, 15), this)!.Value;
        resize.RaiseEvent(new PointerPressedEventArgs(resize, pointer, this, point, 10, pressed, KeyModifiers.None, 1));
        resize.RaiseEvent(new PointerEventArgs(PointerMovedEvent, resize, pointer, this, point + new Vector(80, 0), 11, pressed, KeyModifiers.None));
        Key(resize, Avalonia.Input.Key.Escape);
        Require(SupportsToolPopup.Shell.Width == savedWidth && WorkspacePreferences.Load(AppConfig.WorkspacePath).Width(SupportsToolPopup.Name!, 0) == savedWidth,
            "Cancelled resize must not persist transient width");
        _workspaceToolbar.ShowLabels = true;
        var loaded = WorkspacePreferences.Load(AppConfig.WorkspacePath);
        Require(loaded.ShowToolbarLabels && loaded.Width(SupportsToolPopup.Name!, 0) == savedWidth && loaded.RecentProjects.Count > 0, "Workspace preferences did not round trip");
        // Construct another production window to verify actual restoration, including section identity/order.
        var restored = new MainWindow();
        Require(restored._workspaceToolbar.ShowLabels && restored.SupportsToolPopup.Shell.Width == savedWidth, "New window did not restore preferences");
        var restoredSections = restored.SupportsToolPopup.GetLogicalDescendants().OfType<ReorderableExpander>().ToArray();
        Require(restoredSections[1].SectionId == supportSection.SectionId && !restoredSections[1].IsExpanded, "New window did not restore section order/expansion");
        restored.Close();
        supportSection.IsExpanded = true; Key(grip, Avalonia.Input.Key.Up, KeyModifiers.Alt);
        _workspaceToolbar.ShowLabels = false; await Layout();
        Capture("settings-support.png");
        Click(RaftsToolButton); await Layout();
        var raft = RaftsToolPopup.GetVisualDescendants().OfType<FilledNumericSlider>().First();
        box = Edit(raft, "0.2cm"); Key(box, Avalonia.Input.Key.Enter);
        Require(vm.SupportSettings.SupportRaftThickness == 2, "Available raft thickness unit expression failed");
        Capture("settings-raft.png");
        var cap = AppConfig.Current.Viewport.CapInterior;
        vm.CapInterior = false;
        var configPath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(AppConfig.WorkspacePath)!, "config.json");
        var config = UserConfig.Load(configPath);
        Require(!config.Viewport.CapInterior && Math.Abs(config.Supports.TipDiameter - 0.6) < 0.00001 && config.Supports.RaftThickness == 2, "Settings/save cap-off compatibility round trip failed");
        vm.CapInterior = cap;
        File.WriteAllText(System.IO.Path.Combine(directory, "settings-ok.txt"), "Production transform pointer previews/single commit/undo/cancel, unit expressions, invalid and out-of-range rejection, focus-loss commit, support setter called once, raft thickness, durable workspace restoration and config/cap-off round trips passed using isolated temporary configuration. Visibility uses its existing display modes and switches; no numeric opacity parameter is invented. Support config settings retain existing immediate-save semantics (no document undo was present).\n");
        void Capture(string name)
        {
            var content = (Control)Content!;
            using var bitmap = new RenderTargetBitmap(new PixelSize((int)content.Bounds.Width, (int)content.Bounds.Height));
            bitmap.Render(content); bitmap.Save(System.IO.Path.Combine(directory, name), PngBitmapEncoderOptions.Default);
        }
    }
}
