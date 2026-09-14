using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Danslicer.App.Controls;
using Danslicer.Core;

namespace Danslicer.App.Views;

public partial class MainWindow
{
    private async Task CheckGuidedSelector(string directory)
    {
        static void Require(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
        var pointer = new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, true);
        var pressed = new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed);
        var released = new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonReleased);
        var button = GuidedToolButton;
        var origin = button.TranslatePoint(default, this)!.Value;
        var point = origin + new Vector(10, 10);
        void Press() => button.RaiseEvent(new PointerPressedEventArgs(button, pointer, this, point, 0, pressed, KeyModifiers.None, 1));
        void Release() => button.RaiseEvent(new PointerReleasedEventArgs(button, pointer, this, point, 1, released, KeyModifiers.None, MouseButton.Left));
        Require(_selectedGuidedTool == ViewportControl.GuidedTool.Place, "Selector must default to Place");
        Require(button.Bounds.Top > ObjectsToolButton.Bounds.Top && button.Bounds.Top < SupportsToolButton.Bounds.Top,
            "Placement selector must follow Objects");
        Press();
        Require(_guidedHoldTimer.IsEnabled, "Pointer press did not start hold timer");
        await Task.Delay(700);
        Require(_guidedSelector.IsOpen, $"Holding placement button must open tool list (timer={_guidedHoldTimer.IsEnabled}, opened={_guidedHoldOpened}, pointer={_guidedPointer is not null})");
        Release();
        Require(_guidedSelector.IsOpen, "Releasing a hold must leave the list open");
        Require(_guidedChoices.Count == Enum.GetValues<ViewportControl.GuidedTool>().Length, "Missing guided tool");
        var list = (Control)_guidedSelector.Content!;
        using (var bitmap = new RenderTargetBitmap(new PixelSize((int)list.Bounds.Width, (int)list.Bounds.Height), new Vector(96, 96)))
        {
            bitmap.Render(list);
            bitmap.Save(System.IO.Path.Combine(directory, "support-tool-icons.png"), PngBitmapEncoderOptions.Default);
        }
        _guidedChoices[ViewportControl.GuidedTool.Line].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Require(!_guidedSelector.IsOpen && _selectedGuidedTool == ViewportControl.GuidedTool.Line, "Selection must update button and dismiss list");
        ViewModel!.ViewMode = WorkspaceMode.Layout;
        ViewModel.ViewMode = WorkspaceMode.Support;
        Require(_selectedGuidedTool == ViewportControl.GuidedTool.Line, "Workspace switch lost chosen tool");
        UpdateLayout();
        origin = button.TranslatePoint(default, this)!.Value;
        point = origin + new Vector(button.Bounds.Width - 3, button.Bounds.Height - 3);
        Press(); Release();
        Require(_guidedSelector.IsOpen, "Corner must open list immediately");
        ((Control)_guidedSelector.Content!).RaiseEvent(new KeyEventArgs { RoutedEvent = KeyDownEvent, Key = Key.Escape });
        Require(!_guidedSelector.IsOpen, "Escape must dismiss tool list");
        _selectedGuidedTool = ViewportControl.GuidedTool.Place;
        UpdateGuidedTool();
        CheckGuidedToolSwitching();
    }

    private void CheckGuidedToolSwitching()
    {
        static void Require(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
        void Key(Key key, KeyModifiers modifiers = KeyModifiers.None) => Viewport.RaiseEvent(
            new KeyEventArgs { RoutedEvent = KeyDownEvent, Key = key, KeyModifiers = modifiers });
        void Check(ViewportControl.GuidedTool? expected)
        {
            Require(Viewport.ActiveGuidedTool == expected, $"Expected active tool {expected}, got {Viewport.ActiveGuidedTool}");
            Require(Viewport.IsPlacingSupports == (expected == ViewportControl.GuidedTool.Place), "Place survived a tool switch");
            Require(ReferenceEquals(GuidedToolButton.Background, expected is null ? Avalonia.Media.Brushes.Transparent : Controls.Refresh.RefreshPalette.Fill),
                "Toolbar highlight does not match actual active tool");
            if (expected is { } active)
            {
                Require(_selectedGuidedTool == active, "Shortcut did not update displayed tool");
                Require(ReferenceEquals(_guidedChoices[active].Background, Controls.Refresh.RefreshPalette.Fill), "Active list icon is not highlighted");
            }
        }
        var doc = ViewModel!.Document;
        if (doc.SupportTarget is null)
        {
            var mesh = new Core.Geometry.Mesh(
                [new(0, 0, 10), new(20, 0, 10), new(0, 20, 10), new(0, 0, 30)],
                [0, 2, 1, 0, 1, 3, 1, 2, 3, 2, 0, 3]);
            var target = new Core.Scene.SceneObject("Tool switch check", mesh);
            doc.AddObject(target);
            doc.Select(target);
        }
        Viewport.StartGuidedTool(ViewportControl.GuidedTool.Place);
        Check(ViewportControl.GuidedTool.Place);
        foreach (var tool in new[] { ViewportControl.GuidedTool.Line, ViewportControl.GuidedTool.Polygon,
                     ViewportControl.GuidedTool.Edge, ViewportControl.GuidedTool.Ring, ViewportControl.GuidedTool.Contour })
        {
            _guidedChoices[tool].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(tool);
            Key(Avalonia.Input.Key.Escape);
            Check(null);
            Viewport.StartGuidedTool(ViewportControl.GuidedTool.Place);
            Check(ViewportControl.GuidedTool.Place);
        }
        Key(Avalonia.Input.Key.L); Check(ViewportControl.GuidedTool.Line);
        Key(Avalonia.Input.Key.P); Check(ViewportControl.GuidedTool.Polygon);
        Key(Avalonia.Input.Key.T); Check(ViewportControl.GuidedTool.Place);
        Key(Avalonia.Input.Key.T); Check(null);
        Key(Avalonia.Input.Key.L); Check(ViewportControl.GuidedTool.Line);
        Key(Avalonia.Input.Key.Enter); Check(null);
        Key(Avalonia.Input.Key.T);
        Key(Avalonia.Input.Key.D); Check(null);
        Key(Avalonia.Input.Key.L);
        Key(Avalonia.Input.Key.D, KeyModifiers.Shift); Check(null);
        Key(Avalonia.Input.Key.T);
        ViewModel.ViewMode = WorkspaceMode.Layout; Check(null);
        ViewModel.ViewMode = WorkspaceMode.Support; Check(null);
    }
}
