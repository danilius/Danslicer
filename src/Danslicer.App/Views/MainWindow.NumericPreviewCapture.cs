using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Danslicer.App.Configuration;
using Danslicer.App.Controls.Refresh;
using Danslicer.App.ViewModels;
using Danslicer.Core;
using Danslicer.Core.Config;
using Danslicer.Core.Supports;

namespace Danslicer.App.Views;

public partial class MainWindow
{
    private async Task CheckLiveNumericPreviews(string directory)
    {
        static void Require(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
        static void Key(Control c, Key key) => c.RaiseEvent(new KeyEventArgs { RoutedEvent = KeyDownEvent, Key = key });
        var pointer = new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, true);
        var pressed = new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed);
        var released = new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonReleased);
        var report = new List<string>();
        var configPath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(AppConfig.WorkspacePath)!, "config.json");
        (Border Surface, Point Start, Point End) Start(ScrubField field, Window window, double fraction = 0.75)
        {
            var surface = field.GetVisualDescendants().OfType<Border>().First();
            var start = surface.TranslatePoint(new Point(12, 12), window)!.Value;
            var end = surface.TranslatePoint(new Point(1 + (surface.Bounds.Width - 2) * fraction, 12), window)!.Value;
            surface.RaiseEvent(new PointerPressedEventArgs(surface, pointer, window, start, 1, pressed, KeyModifiers.None, 1));
            surface.RaiseEvent(new PointerEventArgs(PointerMovedEvent, surface, pointer, window, end, 2, pressed, KeyModifiers.None));
            return (surface, start, end);
        }
        void Release((Border Surface, Point Start, Point End) drag, Window window) =>
            drag.Surface.RaiseEvent(new PointerReleasedEventArgs(drag.Surface, pointer, window, drag.End, 3, released, KeyModifiers.None, MouseButton.Left));
        async Task Check(ScrubField field, Window window, Func<double> model, string name)
        {
            field.BringIntoView(); await Task.Delay(80); window.UpdateLayout();
            var original = model(); var saves = AppConfig.SaveCount;
            var bytes = File.ReadAllBytes(configPath);
            var undo = 0; void Changed() => undo++;
            ViewModel!.Document.History.Changed += Changed;
            var drag = Start(field, window);
            Require(model() != original && AppConfig.SaveCount == saves && undo == 0, $"{name}: scene setting must change before release, zero save/undo");
            Require(File.ReadAllBytes(configPath).SequenceEqual(bytes), name + ": wrote config during drag");
            Key(field, Avalonia.Input.Key.Escape);
            Require(model() == original && pointer.Captured is null && AppConfig.SaveCount == saves, name + ": Escape failed");
            drag = Start(field, window); pointer.Capture(null);
            Require(model() == original && AppConfig.SaveCount == saves, name + ": capture cancellation failed");
            drag = Start(field, window); var final = model(); Release(drag, window);
            Require(model() == final && AppConfig.SaveCount == saves + 1 && undo == 0, name + ": release must save once without document undo");
            ViewModel.Document.History.Changed -= Changed;
            report.Add(name + ": native pointer preview before release, no config writes/undo, Escape/capture restoration, one release save passed.");
        }
        Width = 1200; Height = 800;
        if (!ViewSettingsPopup.IsOpen) ViewSettingsButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Task.Delay(150); UpdateLayout();
        PopShadowMode.SelectedIndex = (int)ModelShadowMode.Working;
        await Check(PopAoStrength, this, () => AppConfig.Current.Viewport.AmbientOcclusionStrength, "View AO strength");
        await Check(PopAoRadius, this, () => AppConfig.Current.Viewport.AmbientOcclusionRadiusMm, "View AO radius");
        await Check(PopShadowStrength, this, () => AppConfig.Current.Viewport.WorkingShadowStrength, "View shadow strength");
        await Check(PopShadowSoftness, this, () => AppConfig.Current.Viewport.WorkingShadowSoftnessMm, "View shadow softness");
        // A mode switch mid-drag restores the captured mode's setting and drops capture.
        var shadowOriginal = AppConfig.Current.Viewport.WorkingShadowStrength;
        var modeDrag = Start(PopShadowStrength, this, 0.25);
        Require(AppConfig.Current.Viewport.WorkingShadowStrength != shadowOriginal, "Mode-switch fixture did not preview");
        PopShadowMode.SelectedIndex = (int)ModelShadowMode.Presentation;
        Require(pointer.Captured is null && AppConfig.Current.Viewport.WorkingShadowStrength == shadowOriginal, "Shadow mode switch leaked preview");
        ViewSettingsPopup.IsOpen = false;
        OnPreferencesClick(this, new RoutedEventArgs());
        var preferences = _configWindow!;
        await Task.Delay(150);
        foreach (var field in preferences.GetVisualDescendants().OfType<FilledNumericSlider>().Where(f => f.PreviewProperty is not null).ToArray())
        {
            var property = field.PreviewProperty!;
            if (property.StartsWith("Support") && !property.StartsWith("SupportGizmo")) continue; // Shared support hosts are separately exercised.
            var member = typeof(ConfigViewModel).GetProperty(property)!;
            // Avoid matching the exact final value from the View popout checks.
            var value = Convert.ToDouble(member.GetValue(ViewModel!.SupportSettings));
            if (Math.Abs(value - NumericEditSession.RoundValue(field.Minimum + (field.Maximum - field.Minimum) * 0.75, field.IsInteger)) < 0.001)
                member.SetValue(ViewModel.SupportSettings, Convert.ChangeType(field.Minimum, member.PropertyType));
            await Check(field, preferences, () => Convert.ToDouble(member.GetValue(ViewModel!.SupportSettings)), "Preferences " + property);
        }
        var reflection = preferences.GetVisualDescendants().OfType<FilledNumericSlider>().Single(f => f.PreviewProperty == "PlateReflectionStrength");
        reflection.BringIntoView(); await Task.Delay(80); preferences.UpdateLayout();
        var reflectionOriginal = AppConfig.Current.Viewport.PlateReflectionStrength;
        Start(reflection, preferences, 0.25);
        Require(AppConfig.Current.Viewport.PlateReflectionStrength != reflectionOriginal, "Close fixture did not preview");
        AppConfig.Save(); // An unrelated save must serialize committed values, then resume preview.
        Require(UserConfig.Load(configPath).Viewport.PlateReflectionStrength == reflectionOriginal
            && AppConfig.Current.Viewport.PlateReflectionStrength != reflectionOriginal, "Unrelated config save leaked or erased live preview");
        preferences.Close();
        Require(AppConfig.Current.Viewport.PlateReflectionStrength == reflectionOriginal && pointer.Captured is null, "Window close leaked preview");
        report.Add("Shadow mode switch and Preferences close cancel active preview.");

        var vm = ViewModel!;
        vm.ViewMode = WorkspaceMode.Support;
        vm.Document.Select(vm.Objects[0]);
        var obj = vm.Objects[0];
        var foot = new SupportNode { Type = SupportNodeType.Base, Position = System.Numerics.Vector3.Zero, Origin = SupportOrigin.ManualFor(obj.Id) };
        vm.Document.Supports.AddNode(foot);
        vm.Document.AddRaftToSelection();
        if (!RaftsToolPopup.IsOpen) RaftsToolButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Task.Delay(150); UpdateLayout();
        var raft = RaftsToolPopup.GetVisualDescendants().OfType<FilledNumericSlider>().First();
        var oldRaft = obj.Raft; var raftSaves = AppConfig.SaveCount;
        var history = 0; void History() => history++;
        vm.Document.History.Changed += History;
        var raftDrag = Start(raft, this);
        // Background dispatcher work can run later than wall-clock delays under native GL load.
        // Keep the production 100ms coalescing interval; await its observable result, bounded.
        for (var attempt = 0; attempt < 40 && obj.Raft == oldRaft; attempt++) await Task.Delay(50);
        Require(obj.Raft != oldRaft && AppConfig.SaveCount == raftSaves && history == 0,
            $"Raft must preview geometry before release without saves/undo: original={oldRaft}; current={obj.Raft}; field={raft.Value}; saves={AppConfig.SaveCount - raftSaves}; history={history}; capture={pointer.Captured}");
        var finalRaft = obj.Raft;
        Release(raftDrag, this);
        Require(obj.Raft == finalRaft && AppConfig.SaveCount == raftSaves + 1 && history == 1, "Raft release must commit once from original");
        vm.UndoCommand.Execute(null); Require(obj.Raft == oldRaft, "Raft undo lost original");
        vm.RedoCommand.Execute(null); Require(obj.Raft == finalRaft, "Raft redo lost final");
        var committedRaft = obj.Raft;
        Start(raft, this, 0.25); Key(raft, Avalonia.Input.Key.Escape); await Task.Delay(220);
        Require(obj.Raft == committedRaft, "Cancelled queued raft preview ran later");
        raftDrag = Start(raft, this, 0.25);
        for (var attempt = 0; attempt < 40 && obj.Raft == committedRaft; attempt++) await Task.Delay(50);
        Require(obj.Raft != committedRaft, "Raft capture-loss fixture did not preview");
        pointer.Capture(null); await Task.Delay(150);
        Require(obj.Raft == committedRaft, "Raft capture loss did not restore geometry");
        vm.Document.History.Changed -= History;
        report.Add("Selected raft: coalesced native scene preview, zero pre-release writes/undo, one commit, undo/redo and pending/applied cancellation passed.");
        vm.ViewMode = WorkspaceMode.Layout;
        vm.Document.Select(obj);
        if (!TransformToolPopup.IsOpen) TransformToolButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Task.Delay(120); UpdateLayout();
        var position = TransformPopupContent.GetVisualDescendants().OfType<ModelScrubField>().Single(f => f.Field == vm.Position[0]);
        var originalTransform = obj.Transform;
        Start(position, this);
        Require(obj.Transform != originalTransform, "Selection-change fixture did not preview");
        vm.Document.ClearSelection();
        Require(obj.Transform == originalTransform && pointer.Captured is null, "Selection change failed to restore captured original object");
        vm.Document.Select(obj);
        Start(position, this);
        vm.ViewMode = WorkspaceMode.Support;
        Require(obj.Transform == originalTransform && pointer.Captured is null, "Workspace mode change leaked transform preview");
        report.Add("Selection/workspace-mode changes restore the original transform and release capture; unrelated config save excludes preview.");
        vm.ViewMode = WorkspaceMode.Layout; vm.Document.Select(obj);
        if (!TransformToolPopup.IsOpen) TransformToolButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Task.Delay(120); UpdateLayout();
        Start(position, this);
        Require(obj.Transform != originalTransform, "Detach fixture did not preview");
        var parent = (Panel)position.Parent!; var fieldIndex = parent.Children.IndexOf(position);
        parent.Children.Remove(position);
        Require(obj.Transform == originalTransform && pointer.Captured is null, "Detach did not cancel model preview");
        parent.Children.Insert(fieldIndex, position);
        report.Add("Control detach restores original scene and releases capture.");

        await CheckLayerNavigation();
        report.Add("Native slice-navigation thumb: live value, Escape/capture restoration, release retains final session value.");
        File.WriteAllLines(System.IO.Path.Combine(directory, "live-numeric-ok.txt"), report);
    }

    private async Task CheckLayerNavigation()
    {
        static void Require(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
        static void Key(Control c, Key key) => c.RaiseEvent(new KeyEventArgs { RoutedEvent = KeyDownEvent, Key = key });
        var pointer = new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, true);
        var pressed = new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed);
        var released = new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonReleased);
        var navigation = new Controls.PreviewLayerSlider { Minimum = 0, Maximum = 100, Value = 20, Width = 300 };
        var navigationWindow = new Window { Width = 360, Height = 110, Content = navigation, Title = "Isolated layer navigation check" };
        navigationWindow.Show(this); await Task.Delay(120); navigationWindow.UpdateLayout();
        var thumb = navigation.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.Thumb>().Single();
        var thumbPoint = thumb.TranslatePoint(new Point(thumb.Bounds.Width / 2, thumb.Bounds.Height / 2), navigationWindow)!.Value;
        void Navigate()
        {
            thumb.RaiseEvent(new PointerPressedEventArgs(thumb, pointer, navigationWindow, thumbPoint, 1, pressed, KeyModifiers.None, 1));
            // Routed-event injection bypasses the input manager's gesture recognizer capture.
            // Supply that native capture explicitly; this is not a physical-input test.
            pointer.Capture(thumb);
            thumb.RaiseEvent(new PointerEventArgs(PointerMovedEvent, thumb, pointer, navigationWindow, thumbPoint + new Vector(50, 0), 2, pressed, KeyModifiers.None));
        }
        Navigate(); Require(navigation.Value != 20, "Native layer navigation did not move");
        Key(navigation, Avalonia.Input.Key.Escape); Require(navigation.Value == 20 && pointer.Captured is null, "Layer navigation Escape did not restore");
        await Task.Delay(100); navigationWindow.UpdateLayout();
        Navigate(); var captured = pointer.Captured?.GetType().Name; pointer.Capture(null);
        Require(navigation.Value == 20, $"Layer navigation capture loss did not restore: value={navigation.Value}, captured={captured}");
        Navigate(); var finalLayer = navigation.Value;
        thumb.RaiseEvent(new PointerReleasedEventArgs(thumb, pointer, navigationWindow, thumbPoint + new Vector(50, 0), 3, released, KeyModifiers.None, MouseButton.Left));
        Require(navigation.Value == finalLayer, "Layer navigation release lost value");
        navigationWindow.Close();
    }
}
