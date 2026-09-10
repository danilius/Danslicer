using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Danslicer.App.Configuration;
using Danslicer.App.Controls;
using Danslicer.App.Controls.Refresh;
using Danslicer.App.ViewModels;

namespace Danslicer.App.Views;

public partial class MainWindow
{
    private async Task CheckSupportEditorRefinements(string directory)
    {
        static void Require(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
        var originalDevice = Viewport.SpaceMouseDevice;
        var vm = new SupportPresetEditorViewModel(AppConfig.Current.ActiveSupportPresetName, () => null);
        var editor = new SupportPresetEditorWindow(vm);
        editor.Show(this); editor.Activate();
        await Task.Delay(250); editor.UpdateLayout();
        var preview = editor.GetVisualDescendants().OfType<ViewportControl>().Single();
        Require(ViewportControl.SpaceMouseOwner == preview, "Editor did not acquire exclusive SpaceMouse ownership");
        Require(!Viewport.SpaceMouseConnected, "Main retained competing SpaceMouse connection");
        var hardware = preview.SpaceMouseConnected;
        Require(originalDevice is null || ReferenceEquals(originalDevice, preview.SpaceMouseDevice), "Editor recreated the driver connection");
        var sections = editor.GetVisualDescendants().OfType<ReorderableExpander>().ToArray();
        Require(sections.Length >= 6 && !editor.GetVisualDescendants().OfType<Expander>().Any(), "Legacy editor headers remain");
        var first = sections[0]; var panel = (Panel)first.Parent!;
        var grip = first.GetVisualDescendants().OfType<Button>().Single(b => (AutomationProperties.GetName(b) ?? "").StartsWith("Reorder "));
        grip.RaiseEvent(new KeyEventArgs { RoutedEvent = KeyDownEvent, Key = Key.Down, KeyModifiers = KeyModifiers.Alt });
        Require(panel.Children[1] == first, "Editor section reorder failed");
        await Task.Delay(100); editor.UpdateLayout();
        var otherGrip = ((ReorderableExpander)panel.Children[0]).GetVisualDescendants().OfType<Button>()
            .Single(b => (AutomationProperties.GetName(b) ?? "").StartsWith("Reorder "));
        var start = grip.TranslatePoint(new Point(grip.Bounds.Width / 2, grip.Bounds.Height / 2), editor)!.Value;
        var end = otherGrip.TranslatePoint(new Point(otherGrip.Bounds.Width / 2, otherGrip.Bounds.Height / 2 - 2), editor)!.Value;
        var pointer = new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, true);
        var pressed = new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed);
        var savedOrder = File.ReadAllBytes(AppConfig.WorkspacePath);
        grip.RaiseEvent(new PointerPressedEventArgs(grip, pointer, editor, start, 1, pressed, KeyModifiers.None, 1));
        grip.RaiseEvent(new PointerEventArgs(PointerMovedEvent, grip, pointer, editor, end, 2, pressed, KeyModifiers.None));
        Require(panel.Children[0] == first, "Editor pointer gripper crossing did not swap");
        Require(File.ReadAllBytes(AppConfig.WorkspacePath).SequenceEqual(savedOrder), "Editor persisted order during drag");
        grip.RaiseEvent(new PointerReleasedEventArgs(grip, pointer, editor, end, 3,
            new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonReleased), KeyModifiers.None, MouseButton.Left));
        var expectedOrder = panel.Children.OfType<ReorderableExpander>().Select(s => s.SectionId).ToArray();
        SaveWorkspacePreferences();
        Require(WorkspacePreferences.Load(AppConfig.WorkspacePath).SectionOrder["SupportPresetEditor/Sections"].SequenceEqual(expectedOrder),
            "Main save lost committed editor section order");
        var range = editor.GetVisualDescendants().OfType<FilledNumericSlider>().Single(f => f.PreviewProperty == "SupportBaseDiameter");
        Require(range.Maximum == 25, "Base diameter practical maximum missing");
        await Task.Delay(100); editor.UpdateLayout();
        var content = (Control)editor.Content!;
        using (var bitmap = new RenderTargetBitmap(new PixelSize((int)content.Bounds.Width, (int)content.Bounds.Height)))
        {
            bitmap.Render(content); bitmap.Save(System.IO.Path.Combine(directory, "support-editor.png"), PngBitmapEncoderOptions.Default);
        }
        editor.Close(); Activate();
        await Task.Delay(250);
        Require(ViewportControl.SpaceMouseOwner == Viewport && !preview.SpaceMouseConnected, "Main SpaceMouse ownership not restored after editor close");
        Require(!hardware || Viewport.SpaceMouseConnected, "Main failed to reconnect the available SpaceMouse driver");
        Require(originalDevice is null || ReferenceEquals(originalDevice, Viewport.SpaceMouseDevice), "Return to main recreated the driver connection");
        File.WriteAllText(System.IO.Path.Combine(directory, "editor-refinements-ok.txt"),
            $"Preset editor uses refresh headers; keyboard and pointer gripper reorder pass, no mid-drag writes, release persists and main save preserves editor order; base slider max 25mm. Exclusive SpaceMouse ownership transfers to editor and returns to main on close. Driver connected in editor: {hardware}; main after close: {Viewport.SpaceMouseConnected}. Physical device movement not simulated. Offscreen image omits GL. Border-width native live/persist/cancel checks run in live-numeric-ok.txt.\n");
    }
}
