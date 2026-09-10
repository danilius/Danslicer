using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Danslicer.App.Controls;
using Danslicer.Core.Config;

namespace Danslicer.App.Views;

public partial class MainWindow
{
    private async Task CheckIsolationEditor(string directory)
    {
        static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        static void Key(Control control, Key key) => control.RaiseEvent(new KeyEventArgs { RoutedEvent = KeyDownEvent, Key = key });
        async Task Layout() { await Task.Delay(80); UpdateLayout(); PositionIsolationEditor(); }
        var clip = ViewModel!.SupportClip;
        Require(InspectionContent.Background is null && InspectionContent.BorderThickness == default(Thickness), "Isolation must have no enclosing panel background or border");
        Require(IsolationResetButton.HorizontalAlignment == Avalonia.Layout.HorizontalAlignment.Center, "Reset must be centered");
        Require(IsolationCapCheckBox.Content?.ToString() == "Cap", "Checkbox label must be Cap");
        var cube = Danslicer.Render.ViewCube.Rect((int)(Viewport.Bounds.Width * RenderScaling), (int)(Viewport.Bounds.Height * RenderScaling), RenderScaling, Configuration.AppConfig.Current.Viewport.ViewCubeSizePixels);
        var cubeBottom = ((int)(Viewport.Bounds.Height * RenderScaling) - cube.Y) / RenderScaling;
        Require(!Configuration.AppConfig.Current.Viewport.ViewCubeEnabled || InspectionContent.Margin.Top >= cubeBottom + 12, "Isolation must clear the view cube");
        Require(InspectionContent.ZIndex > SupportsToolPopup.ZIndex, "Isolation must remain above tool popouts");
        Require(InspectionContent.IsEffectivelyVisible, "Support isolation rail must always show");
        Require(!_workspaceToolbar.GetVisualDescendants().OfType<Button>().Any(b => AutomationProperties.GetName(b) == "Layer isolation"), "Isolation must not have a toolbar button");
        Require(!IsolationCapCheckBox.IsThreeState && new ViewportConfig().CapInterior, "Cap interior must default on and have two states");
        clip.LowerZ = clip.MaximumZ * 0.2; clip.UpperZ = clip.MaximumZ * 0.8;
        await Layout();
        var pointer = new Pointer(81, PointerType.Mouse, true);
        Point Position(LayerRangeSliderThumb thumb) => IsolationSlider.TranslatePoint(IsolationSlider.ThumbCenter(thumb), this)!.Value;
        void Hover(Point point) => IsolationSlider.RaiseEvent(new PointerEventArgs(PointerMovedEvent, IsolationSlider, pointer, this, point, 1, new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.Other), KeyModifiers.None));
        Hover(Position(LayerRangeSliderThumb.Upper)); await Layout();
        Require(_isolationEditor.IsVisible && ReferenceEquals(_isolationMmInput.DataContext, clip.UpperMmField), "Upper handle hover must expose upper editor");
        var oldTop = Canvas.GetTop(_isolationEditor);
        var oldUpper = clip.UpperZ;
        var origin = Position(LayerRangeSliderThumb.Upper);
        var pressed = new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed);
        IsolationSlider.RaiseEvent(new PointerPressedEventArgs(IsolationSlider, pointer, this, origin, 2, pressed, KeyModifiers.None, 1));
        IsolationSlider.RaiseEvent(new PointerEventArgs(PointerMovedEvent, IsolationSlider, pointer, this, origin + new Vector(0, 35), 3, pressed, KeyModifiers.None));
        await Layout();
        Require(clip.IsDragging && clip.UpperZ < oldUpper && Canvas.GetTop(_isolationEditor) > oldTop, "Editor must follow dragged handle");
        Key(IsolationSlider, Avalonia.Input.Key.Escape);
        Require(!clip.IsDragging && pointer.Captured is null && Math.Abs(clip.UpperZ - oldUpper) < 1e-6, "Escape must cancel slider drag and release capture");
        origin = Position(LayerRangeSliderThumb.Upper);
        IsolationSlider.RaiseEvent(new PointerPressedEventArgs(IsolationSlider, pointer, this, origin, 4, pressed, KeyModifiers.None, 1));
        IsolationSlider.RaiseEvent(new PointerEventArgs(PointerMovedEvent, IsolationSlider, pointer, this, origin + new Vector(0, 20), 5, pressed, KeyModifiers.None));
        IsolationSlider.RaiseEvent(new PointerReleasedEventArgs(IsolationSlider, pointer, this, origin + new Vector(0, 20), 6, new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonReleased), KeyModifiers.None, MouseButton.Left));
        Require(!clip.IsDragging && pointer.Captured is null && clip.UpperZ < oldUpper, "Released drag must retain height and release capture");
        Hover(Position(LayerRangeSliderThumb.Lower)); await Layout();
        Require(ReferenceEquals(_isolationLayerInput.DataContext, clip.LowerField), "Lower hover must target lower limit");
        // Traverse the gap; dismissal is delayed long enough to enter and focus the card.
        Hover(IsolationSlider.TranslatePoint(new Point(-25, -25), this)!.Value);
        await Task.Delay(100);
        Require(_isolationEditor.IsVisible, "Editor closed while crossing gap");
        _isolationMmInput.Focus(); await Task.Delay(350);
        Require(_isolationEditor.IsVisible, "Focused editor must remain open");
        _isolationMmInput.Text = "0.2cm"; Key(_isolationMmInput, Avalonia.Input.Key.Enter);
        Require(Math.Abs(clip.LowerZ - 2) < 1e-6, "mm expression must update clip height");
        _isolationMmInput.Text = "invalid"; Key(_isolationMmInput, Avalonia.Input.Key.Enter);
        Require(Math.Abs(clip.LowerZ - 2) < 1e-6, "Invalid height changed range");
        Key(_isolationMmInput, Avalonia.Input.Key.Escape);
        Require(_isolationEditor.IsVisible && _isolationMmInput.Text == clip.LowerMmField.Text, "Escape must revert editor before dismissing it");
        _isolationLayerInput.Focus(); _isolationLayerInput.Text = "60"; Key(_isolationLayerInput, Avalonia.Input.Key.Enter);
        Require(Math.Abs(clip.LowerZ - 60 * clip.LayerHeightMm) < 1e-6, "Layer editor conversion failed");
        Viewport.Focus();
        // DispatcherTimer uses background priority: rendering a cold native window can delay it.
        // Keep the production 300ms grace and the earlier 100ms gap assertion; allow bounded dispatch latency.
        var dismissDeadline = DateTime.UtcNow.AddSeconds(2);
        while (_isolationEditor.IsVisible && DateTime.UtcNow < dismissDeadline) await Task.Delay(50);
        Require(!_isolationEditor.IsVisible && InspectionContent.IsVisible, $"Only hover card should dismiss on exit: hovered={_isolationHandleHovered}, dragging={IsolationSlider.IsDragging}, pointer={_isolationEditor.IsPointerOver}, focused={_isolationEditor.IsKeyboardFocusWithin}, rail={InspectionContent.IsVisible}");
        IsolationSlider.Focus(); Key(IsolationSlider, Avalonia.Input.Key.Right); Key(IsolationSlider, Avalonia.Input.Key.Enter);
        Require(_isolationLayerInput.IsFocused && ReferenceEquals(_isolationLayerInput.DataContext, clip.UpperField), "Keyboard must reach upper editor");
        IsolationSlider.Focus(); Key(IsolationSlider, Avalonia.Input.Key.Left);
        var oldLower = clip.LowerZ; Key(IsolationSlider, Avalonia.Input.Key.Up);
        Require(Math.Abs(clip.LowerZ - oldLower - clip.LayerHeightMm) < 1e-6, "Keyboard step should move one layer");
        await Layout(); Capture("workspace-isolation.png");
        var oldWidth = Width; var oldHeight = Height;
        Width = 640; Height = 480; await Layout();
        var bottomRight = _isolationEditor.TranslatePoint(new Point(_isolationEditor.Bounds.Width, _isolationEditor.Bounds.Height), IsolationEditorLayer)!.Value;
        Require(bottomRight.X <= IsolationEditorLayer.Bounds.Width && bottomRight.Y <= IsolationEditorLayer.Bounds.Height && Canvas.GetTop(_isolationEditor) >= 0, "Narrow isolation editor overflows viewport");
        Capture("workspace-isolation-narrow.png");
        Width = oldWidth; Height = oldHeight;
        Key(IsolationSlider, Avalonia.Input.Key.Escape); Viewport.Focus(); clip.Reset(); await Layout();
        void Capture(string name)
        {
            var content = (Control)Content!;
            using var bitmap = new RenderTargetBitmap(new PixelSize((int)content.Bounds.Width, (int)content.Bounds.Height));
            bitmap.Render(content); bitmap.Save(System.IO.Path.Combine(directory, name), PngBitmapEncoderOptions.Default);
        }
    }
}
