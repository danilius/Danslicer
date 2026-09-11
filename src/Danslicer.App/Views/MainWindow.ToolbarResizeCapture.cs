using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Danslicer.App.Configuration;
using Danslicer.App.Controls.Refresh;
using Danslicer.Core;

namespace Danslicer.App.Views;

public partial class MainWindow
{
    private async Task CheckToolbarResize(string directory)
    {
        static void Require(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
        var toolbar = _workspaceToolbar;
        var edge = toolbar.ResizeEdge;
        var pointer = new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, true);
        var pressed = new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed);
        var released = new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonReleased);
        Point start = default;
        var commits = 0;
        void Committed(object? sender, EventArgs e) => commits++;
        toolbar.LabelsCommitted += Committed;
        async Task Layout() { await Task.Delay(160); UpdateLayout(); PositionWorkspacePopouts(); UpdateLayout(); }
        void Key(Key key) => edge.RaiseEvent(new KeyEventArgs { RoutedEvent = KeyDownEvent, Key = key });
        void Begin()
        {
            start = edge.TranslatePoint(new Point(6, 20), this)!.Value;
            edge.RaiseEvent(new PointerPressedEventArgs(edge, pointer, this, start, 0, pressed, KeyModifiers.None, 1));
            Require(toolbar.IsResizing && ReferenceEquals(pointer.Captured, edge), "Toolbar edge did not capture pointer");
        }
        void Move(double delta) => edge.RaiseEvent(new PointerEventArgs(PointerMovedEvent, edge, pointer, this, start + new Vector(delta, 0), 1, pressed, KeyModifiers.None));
        void Release(double delta) => edge.RaiseEvent(new PointerReleasedEventArgs(edge, pointer, this, start + new Vector(delta, 0), 2, released, KeyModifiers.None, MouseButton.Left));
        void Capture(string name)
        {
            var content = (Control)Content!;
            using var bitmap = new RenderTargetBitmap(new PixelSize((int)content.Bounds.Width, (int)content.Bounds.Height), new Vector(96, 96));
            bitmap.Render(content); bitmap.Save(System.IO.Path.Combine(directory, name), PngBitmapEncoderOptions.Default);
        }
        bool SavedLabels() => WorkspacePreferences.Load(AppConfig.WorkspacePath).ShowToolbarLabels;
        void Anchored()
        {
            Require(TransformToolPopup.IsOpen, "Toolbar resize dismissed popout");
            Require(Math.Abs(TransformToolPopup.Bounds.X - toolbar.Bounds.Right - 8) < 1, "Open popout lost toolbar anchor");
            Require(TransformToolPopup.Bounds.Right <= ViewportSurface.Bounds.Width - 10, "Popout overflowed viewport");
        }
        toolbar.ShowLabels = false; SaveWorkspacePreferences(); await Layout(); commits = 0;
        Require(!toolbar.GetVisualDescendants().OfType<Button>().Any(b => AutomationProperties.GetName(b) == "Toolbar labels"), "Obsolete label toggle remains");
        Require(edge.Focusable && AutomationProperties.GetName(edge) == "Resize toolbar labels" && edge.Cursor?.ToString() == "SizeWestEast", "Resize edge accessibility/cursor missing");
        Require(edge.Bounds.Width == 12 && edge.Bounds.Height > 30, "Resize edge hit target too small");
        var toolRight = TransformToolButton.TranslatePoint(new Point(TransformToolButton.Bounds.Width, 0), toolbar)!.Value.X;
        var edgeLeft = edge.TranslatePoint(default, toolbar)!.Value.X;
        Require(toolRight <= edgeLeft, "Resize edge steals tool click area");
        Require(ToolTip.GetTip(TransformToolButton) is not null && TransformToolButton.Cursor is null, "Icon tooltip/cursor regressed");
        var before = File.ReadAllBytes(AppConfig.WorkspacePath);
        Begin(); Move(74); Require(!toolbar.ShowLabels, "Expanded below 60% threshold");
        Move(77); Require(toolbar.ShowLabels && commits == 0, "Expansion threshold/preview commit failed");
        Move(64); Require(toolbar.ShowLabels, "Hysteresis flickered at midpoint");
        Move(51); Require(toolbar.ShowLabels, "Collapsed above 40% threshold");
        Move(49); Require(!toolbar.ShowLabels, "Reversal failed to collapse");
        Move(110); await Layout(); Anchored(); Capture("toolbar-drag-preview.png");
        Require(File.ReadAllBytes(AppConfig.WorkspacePath).SequenceEqual(before), "Drag saved before release");
        SaveWorkspacePreferences(); Require(!SavedLabels(), "Unrelated save leaked toolbar preview");
        Release(126); await Layout(); Anchored();
        Require(toolbar.ShowLabels && commits == 1 && SavedLabels() && !toolbar.IsResizing && pointer.Captured is null, "Completed expansion did not save exactly once");
        Capture("toolbar-labelled.png");
        await CheckToolbarRestart(directory, true);
        // Restore through the real window initialization and compatible existing JSON preference.
        var restored = new MainWindow { DataContext = new ViewModels.MainViewModel() };
        Require(restored._workspaceToolbar.ShowLabels && restored._workspaceToolbar.Width == FloatingToolbar.LabelledWidth, "Reopened window lost committed toolbar mode");
        restored.Close();
        Begin(); Move(-74); Require(toolbar.ShowLabels, "Collapsed before threshold");
        Move(-77); Require(!toolbar.ShowLabels && SavedLabels(), "Collapse preview was persisted");
        Key(Avalonia.Input.Key.Escape); await Layout(); Anchored();
        Require(toolbar.ShowLabels && commits == 1 && pointer.Captured is null, "Escape failed to restore start without saving");
        Begin(); Move(-100); pointer.Capture(null); await Layout();
        Require(toolbar.ShowLabels && commits == 1, "Capture loss failed to cancel");
        Begin(); Move(-100); Release(-126); await Layout(); Anchored();
        Require(!toolbar.ShowLabels && commits == 2 && !SavedLabels(), "Left drag failed to persist collapse");
        await CheckToolbarRestart(directory, false);
        Begin(); Move(80); Move(0); Release(0);
        Require(!toolbar.ShowLabels && commits == 2, "Reversed no-op drag saved");
        // Release coordinates must be consumed even without a preceding move event.
        Begin(); Release(126); Require(toolbar.ShowLabels && commits == 3, "Release position was ignored");
        Key(Avalonia.Input.Key.Left); Require(!toolbar.ShowLabels && !SavedLabels() && commits == 4, "Keyboard Left did not commit");
        Key(Avalonia.Input.Key.Left); Require(commits == 4, "Repeated keyboard no-op saved");
        Key(Avalonia.Input.Key.Right); Require(toolbar.ShowLabels && SavedLabels() && commits == 5, "Keyboard Right did not commit");
        await Layout(); Begin(); Move(-100);
        ViewModel!.ViewMode = WorkspaceMode.Support; await Layout();
        Require(toolbar.ShowLabels && !toolbar.IsResizing && commits == 5 && SavedLabels(), "Mode switch leaked preview");
        ViewModel.ViewMode = WorkspaceMode.Layout; TransformToolPopup.IsOpen = true; await Layout();
        Begin(); Move(-100);
        ViewportSurface.Children.Remove(toolbar);
        Require(toolbar.ShowLabels && !toolbar.IsResizing && commits == 5, "Detach failed to cancel");
        ViewportSurface.Children.Add(toolbar); await Layout();
        Width = 640; Height = 480; await Layout();
        Begin(); Move(-126); await Layout(); Anchored(); Release(-126); await Layout();
        Begin(); Move(126); await Layout(); Anchored(); Release(126); await Layout();
        Require(toolbar.Bounds.Right <= ViewportSurface.Bounds.Width - 12, "Narrow toolbar overflow");
        Capture("toolbar-narrow-popout.png");
        Width = 1200; Height = 800; Key(Avalonia.Input.Key.Left); await Layout();
        Capture("toolbar-icons.png");
        toolbar.LabelsCommitted -= Committed;
        File.WriteAllText(System.IO.Path.Combine(directory, "toolbar-resize-ok.txt"),
            "Native MAIN routed pointer press/move/release: 60/40% thresholds, reversal/hysteresis, continuous width/opacity, release coordinates, no-op, Escape and capture-loss cancellation; 12-DIP nonoverlapping edge, horizontal cursor property, name/focus/Left/Right; commit-only JSON preference, unrelated-save isolation, window recreation, mode/detach cancellation; anchored open Transform popout at 1200x800 and 640x480. Screenshots omit GL composition. Physical pointer/cursor display, screen reader and actual monitor DPI not certified.");
    }
    private static async Task CheckToolbarRestart(string directory, bool expected)
    {
        var output = System.IO.Path.Combine(directory, "restart-" + expected);
        Directory.CreateDirectory(output);
        var snapshot = System.IO.Path.Combine(output, "workspace-ui.json");
        File.Copy(AppConfig.WorkspacePath, snapshot, true);
        var info = new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!)
        {
            UseShellExecute = false, CreateNoWindow = true,
            WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden
        };
        foreach (var argument in new[] { "--workspace-capture", output, "--toolbar-preference-probe", snapshot, expected.ToString() })
            info.ArgumentList.Add(argument);
        using var process = System.Diagnostics.Process.Start(info)!;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { process.Kill(true); throw new InvalidOperationException("Toolbar restart probe timed out"); }
        if (process.ExitCode != 0 || !File.Exists(System.IO.Path.Combine(output, "toolbar-restart-ok.txt")))
            throw new InvalidOperationException("Fresh MAIN toolbar preference probe failed: " + expected);
    }

    private void PrepareToolbarCloseCheck(string directory)
    {
        var toolbar = _workspaceToolbar;
        toolbar.ShowLabels = false; SaveWorkspacePreferences(); UpdateLayout();
        var edge = toolbar.ResizeEdge;
        var pointer = new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, true);
        var point = edge.TranslatePoint(new Point(6, 15), this)!.Value;
        var pressed = new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed);
        edge.RaiseEvent(new PointerPressedEventArgs(edge, pointer, this, point, 0, pressed, KeyModifiers.None, 1));
        edge.RaiseEvent(new PointerEventArgs(PointerMovedEvent, edge, pointer, this, point + new Vector(126, 0), 1, pressed, KeyModifiers.None));
        if (!toolbar.IsResizing || !toolbar.ShowLabels) throw new InvalidOperationException("Close fixture failed to preview");
        Closed += (_, _) =>
        {
            var ok = !toolbar.ShowLabels && !toolbar.IsResizing && pointer.Captured is null
                && !WorkspacePreferences.Load(AppConfig.WorkspacePath).ShowToolbarLabels;
            File.WriteAllText(System.IO.Path.Combine(directory, ok ? "toolbar-close-ok.txt" : "toolbar-close-error.txt"),
                ok ? "Closing actual MAIN during resize restored initial labels and retained saved preference." : "Closing leaked toolbar preview.");
            if (!ok) Environment.ExitCode = 1;
        };
    }

}
