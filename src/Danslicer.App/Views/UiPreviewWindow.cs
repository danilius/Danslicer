using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Danslicer.App.Controls.Refresh;
using Danslicer.Core.Utilities;

namespace Danslicer.App.Views;

/// <summary>Native control gallery; no project, renderer or application settings are created.</summary>
public sealed class UiPreviewWindow : Window
{
    private readonly StackPanel _sections = new() { Spacing = 6 };
    private readonly Stack<(ScrubField Field, double Value)> _undo = [];
    private readonly TextBlock _status = new() { Text = "Ready · drag fields · Shift for fine adjustment · click or Enter for expressions", Foreground = RefreshPalette.Muted, TextWrapping = TextWrapping.Wrap };
    private readonly ToolPopout _popout = new() { Title = "Supports · control preview" };
    private readonly FloatingToolbar _toolbar = new();
    private string? _selected;

    public UiPreviewWindow(string? captureDirectory = null)
    {
        Title = "Danslicer · UI controls preview"; Width = 1040; Height = 760; MinWidth = 480; MinHeight = 400;
        Background = RefreshPalette.Canvas; Foreground = RefreshPalette.Text; FontSize = 12;
        Resources = RefreshPalette.CreateResources();
        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto"), Background = RefreshPalette.Canvas };
        var top = new WrapPanel { Margin = new Thickness(16), Orientation = Orientation.Horizontal };
        top.Children.Add(new TextBlock { Text = "DANSLICER  /  Native control study", FontSize = 18, Margin = new Thickness(0, 4, 24, 8) });
        var mode = new CheckBox { Content = "Toolbar labels", IsChecked = true, Margin = new Thickness(0, 0, 16, 0) };
        mode.IsCheckedChanged += (_, _) => _toolbar.ShowLabels = mode.IsChecked == true;
        top.Children.Add(mode);
        var undo = new Button { Content = "Undo edit", Margin = new Thickness(0, 0, 8, 0) };
        undo.Click += (_, _) =>
        {
            if (_undo.TryPop(out var edit)) { edit.Field.Value = edit.Value; _status.Text = $"Undo restored {edit.Value:0.00} {edit.Field.Unit}"; }
        };
        top.Children.Add(undo);
        root.Children.Add(top);
        var viewport = new Grid(); Grid.SetRow(viewport, 1); root.Children.Add(viewport);
        viewport.Children.Add(new TextBlock
        {
            Text = "CONTROL PREVIEW\n\nNative Avalonia controls\nNo scene or renderer attached\n\nClick outside the popout: it stays open.\nUse its close button, toolbar toggle or Escape.",
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(24), Foreground = RefreshPalette.Muted, TextWrapping = TextWrapping.Wrap, MaxWidth = 270,
            IsHitTestVisible = false
        });
        var overlay = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), HorizontalAlignment = HorizontalAlignment.Stretch };
        overlay.Children.Add(_toolbar); Grid.SetColumn(_popout, 1); overlay.Children.Add(_popout); viewport.Children.Add(overlay);
        void ConstrainPopout()
        {
            var available = Math.Max(0, viewport.Bounds.Width - _toolbar.Bounds.Width - 48);
            _popout.MinWidth = Math.Min(240, available); _popout.MaxWidth = available;
            _toolbar.MaxHeight = Math.Max(0, viewport.Bounds.Height - 32); _popout.MaxHeight = Math.Max(0, viewport.Bounds.Height - 32);
        }
        viewport.SizeChanged += (_, _) => ConstrainPopout();
        _toolbar.SizeChanged += (_, _) => ConstrainPopout();
        foreach (var (id, label, icon) in new[] { ("select", "Select", "select"), ("move", "Move", "move"), ("rotate", "Rotate", "rotate"), ("scale", "Scale", "scale"), ("objects", "Objects", "objects"), ("supports", "Supports", "supports"), ("visibility", "Visibility", "visibility"), ("rafts", "Rafts", "rafts") })
        {
            _toolbar.AddTool(new RefreshTool(id, label, icon, () =>
            {
                if (_selected == id && _popout.IsVisible) _popout.RequestClose();
                else { _selected = id; _toolbar.Select(id); _popout.Title = $"{label} · control preview"; _popout.IsVisible = true; }
            }));
        }
        _popout.CloseRequested += (_, _) => { _selected = null; _toolbar.Select(null); _toolbar.Focus(); };
        _selected = "supports"; _toolbar.Select(_selected);
        var body = new StackPanel { Spacing = 8 };
        body.Children.Add(new TextBlock { Text = "Illustrative values · no model changes", Foreground = RefreshPalette.Muted, TextWrapping = TextWrapping.Wrap });
        body.Children.Add(_sections);
        var contacts = Section("contacts", "Contacts");
        contacts.Children.Add(Row("Tip diameter", Field(0.30, 0, 3)));
        contacts.Children.Add(Row("Contact depth", Field(0.20, 0, 2)));
        var branches = Section("branches", "Branches");
        branches.Children.Add(Row("Diameter", Field(1.20, 0, 10)));
        var angle = new FilledNumericSlider { Value = 45, Minimum = 0, Maximum = 90, Step = 0.5, Unit = "°", UnitKind = UnitKind.Angle, Format = "0.0" };
        Track(angle); branches.Children.Add(Row("Angle", angle));
        var locks = Section("locks", "Locks and validation");
        var locked = Field(2.5, 0, 20); locked.CanLock = true; locked.IsLocked = true;
        locks.Children.Add(Row("Fixed diameter", locked));
        var disabled = Field(0.8, 0, 10); disabled.IsEnabled = false;
        locks.Children.Add(Row("Disabled", disabled));
        locks.Children.Add(Row("Long descriptive parameter label", Field(12, -100, 100)));
        var help = Section("help", "Interaction guide");
        help.Children.Add(new TextBlock { Text = "Drag 4 px to scrub. Shift adjusts at one tenth speed. Click or Enter to type (for example 1cm + 2mm). Enter commits; Escape cancels. Invalid/out-of-range input stays in the editor until corrected or cancelled. Arrow keys step. Each committed gesture adds one undo entry.\n\nDrag the round grip to move the entire section. Sections swap as you cross a neighbour; release to keep the order. Escape restores the starting order. Alt+Up/Down reorders with the keyboard. Drag the popout's right edge to resize it.", TextWrapping = TextWrapping.Wrap, Foreground = RefreshPalette.Muted });
        _popout.Body = body;
        var footer = new Border { Background = RefreshPalette.Header, Padding = new Thickness(16, 10), Child = _status };
        Grid.SetRow(footer, 2); root.Children.Add(footer); Content = root;
        KeyDown += (_, e) => { if (!e.Handled && e.Key == Key.Escape && _popout.IsVisible) { _popout.RequestClose(); e.Handled = true; } };
        if (captureDirectory is not null)
            Opened += async (_, _) =>
            {
                await Task.Delay(700);
                try
                {
                    Directory.CreateDirectory(captureDirectory);
                    File.Delete(System.IO.Path.Combine(captureDirectory, "capture-ok.txt"));
                    File.Delete(System.IO.Path.Combine(captureDirectory, "capture-error.txt"));
                    await RunInteractionChecks(angle, locked, captureDirectory);
                    await Task.Delay(200);
                    foreach (var scale in new[] { 1.0, 1.5, 2.0 })
                    {
                        using var bitmap = new RenderTargetBitmap(new PixelSize((int)(root.Bounds.Width * scale), (int)(root.Bounds.Height * scale)), new Vector(96 * scale, 96 * scale));
                        bitmap.Render(root); bitmap.Save(System.IO.Path.Combine(captureDirectory, $"preview-{scale * 100:0}.png"), PngBitmapEncoderOptions.Default);
                    }
                    mode.IsChecked = false; Width = 520; Height = 540;
                    await Task.Delay(250);
                    using var narrow = new RenderTargetBitmap(new PixelSize((int)root.Bounds.Width, (int)root.Bounds.Height));
                    narrow.Render(root); narrow.Save(System.IO.Path.Combine(captureDirectory, "preview-narrow.png"), PngBitmapEncoderOptions.Default);
                    File.WriteAllText(System.IO.Path.Combine(captureDirectory, "capture-ok.txt"), "Native window opened. Routed pointer and keyboard checks passed: numeric commit/cancel/lock/expression/undo, whole-section live swap/pointer anchoring/cancel/settle, popout width resize/clamp/cancel, expansion and dismissal. Rendered at 100/150/200% pixel density, plus narrow icon toolbar, mid-drag and wide popout. This is not a monitor DPI or physical pointer test.");
                }
                catch (Exception ex) { Environment.ExitCode = 1; File.WriteAllText(System.IO.Path.Combine(captureDirectory, "capture-error.txt"), ex.ToString()); }
                finally { Close(); }
            };
    }
    private async Task RunInteractionChecks(ScrubField angle, ScrubField locked, string captureDirectory)
    {
        static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        static void Key(Control control, Avalonia.Input.Key key, KeyModifiers modifiers = KeyModifiers.None)
            => control.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, KeyModifiers = modifiers });
        var commits = 0;
        angle.EditCommitted += Count;
        void Count(object? sender, NumericCommittedEventArgs e) => commits++;
        var initial = angle.Value;
        Key(angle, Avalonia.Input.Key.Right);
        Require(angle.Value == initial + angle.Step && commits == 1 && _undo.Count == 1, "Keyboard step must create one undo entry");
        angle.Value = initial; _undo.Clear();
        var surface = (Border)((Grid)angle.Content!).Children[0];
        var pointer = new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, true);
        var position = surface.TranslatePoint(new Point(20, 16), this)!.Value;
        var pressed = new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed);
        surface.RaiseEvent(new PointerPressedEventArgs(surface, pointer, this, position, 0, pressed, KeyModifiers.None, 1));
        surface.RaiseEvent(new PointerEventArgs(InputElement.PointerMovedEvent, surface, pointer, this, position + new Vector(10, 0), 1, pressed, KeyModifiers.None));
        Require(angle.Value == initial, "Gallery with no scene-preview host keeps Value until commit");
        surface.RaiseEvent(new PointerReleasedEventArgs(surface, pointer, this, position + new Vector(10, 0), 2, new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonReleased), KeyModifiers.None, MouseButton.Left));
        var expectedTrackValue = NumericEditSession.RoundValue(angle.Minimum + 29 / (surface.Bounds.Width - 2) * (angle.Maximum - angle.Minimum));
        Require(angle.Value == expectedTrackValue && commits == 2 && _undo.Count == 1, "Filled slider must commit actual track-mapped value once");
        angle.Value = initial; _undo.Clear(); commits = 1;
        surface.RaiseEvent(new PointerPressedEventArgs(surface, pointer, this, position, 3, pressed, KeyModifiers.None, 1));
        surface.RaiseEvent(new PointerEventArgs(InputElement.PointerMovedEvent, surface, pointer, this, position + new Vector(20, 0), 4, pressed, KeyModifiers.None));
        Key(angle, Avalonia.Input.Key.Escape);
        Require(angle.Value == initial && pointer.Captured is null && commits == 1, "Cancel must restore original and release pointer capture");
        Key(locked, Avalonia.Input.Key.Right);
        Require(locked.Value == 2.5, "Locked field changed");
        Key(angle, Avalonia.Input.Key.Enter);
        var editor = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(angle).OfType<TextBox>().Single();
        editor.Text = "30 + 10"; Key(editor, Avalonia.Input.Key.Enter);
        Require(angle.Value == 40 && !editor.IsVisible && commits == 2, "Expression must commit once");
        angle.Value = 45;
        Key(angle, Avalonia.Input.Key.Enter); editor.Text = "12"; Key(editor, Avalonia.Input.Key.Escape);
        Require(angle.Value == 45 && !editor.IsVisible && _popout.IsVisible, "Escape must cancel edit before dismissing popout");
        Key(angle, Avalonia.Input.Key.Enter); editor.Text = "1 / 0"; Key(editor, Avalonia.Input.Key.Enter);
        Require(angle.Value == 45 && editor.IsVisible, "Invalid expression was accepted");
        Key(editor, Avalonia.Input.Key.Escape);
        var section = (ReorderableExpander)_sections.Children[0];
        section.IsExpanded = false; Require(!section.IsExpanded, "Collapse failed"); section.IsExpanded = true;
        var grip = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(section).OfType<Button>()
            .Single(b => AutomationProperties.GetName(b)?.StartsWith("Reorder") == true);
        Key(grip, Avalonia.Input.Key.Down, KeyModifiers.Alt);
        Require(_sections.Children[1] == section, "Keyboard reorder failed");
        Key(grip, Avalonia.Input.Key.Up, KeyModifiers.Alt);
        _sections.UpdateLayout();
        position = grip.TranslatePoint(new Point(12, 12), this)!.Value;
        var neighbour = (ReorderableExpander)_sections.Children[1];
        var neighbourGrip = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(neighbour).OfType<Button>().Single(b => AutomationProperties.GetName(b)?.StartsWith("Reorder") == true);
        var midpointDistance = neighbourGrip.TranslatePoint(new Point(0, 13), this)!.Value.Y - grip.TranslatePoint(new Point(0, 13), this)!.Value.Y;
        var dragDistance = midpointDistance + 0.5;
        var originalSectionTop = section.Bounds.Y;
        grip.RaiseEvent(new PointerPressedEventArgs(grip, pointer, this, position, 5, pressed, KeyModifiers.None, 1));
        grip.RaiseEvent(new PointerEventArgs(InputElement.PointerMovedEvent, grip, pointer, this, position + new Vector(0, midpointDistance - 0.5), 6, pressed, KeyModifiers.None));
        Require(_sections.Children[0] == section, "Must not swap before gripper midpoint");
        grip.RaiseEvent(new PointerEventArgs(InputElement.PointerMovedEvent, grip, pointer, this, position + new Vector(0, dragDistance), 6, pressed, KeyModifiers.None));
        Require(_sections.Children[1] == section && Math.Abs(section.Bounds.Y + ((TranslateTransform)section.RenderTransform!).Y - originalSectionTop - dragDistance) < 0.01 && section.IsExpanded, "Section must swap before release and remain anchored to pointer");
        var displacedTransform = (TranslateTransform)neighbour.RenderTransform!;
        var transition = displacedTransform.Transitions!.OfType<Avalonia.Animation.DoubleTransition>().Single();
        Require(transition.Duration == TimeSpan.FromMilliseconds(250) && transition.Easing is Avalonia.Animation.Easings.CubicEaseOut,
            "Displaced section must use 250ms deceleration");
        await Task.Delay(70);
        var duringAnimation = displacedTransform.Y;
        Require(duringAnimation > 0, "Displaced section must animate rather than jump");
        Capture("preview-drag.png");
        await Task.Delay(300);
        Require(displacedTransform.Y == 0, "Displaced section must finish in its new slot");
        grip.RaiseEvent(new PointerReleasedEventArgs(grip, pointer, this, position + new Vector(0, dragDistance), 7, new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonReleased), KeyModifiers.None, MouseButton.Left));
        Require(_sections.Children[1] == section && pointer.Captured is null, "Pointer reorder must insert at drop position");
        Key(grip, Avalonia.Input.Key.Up, KeyModifiers.Alt); _sections.UpdateLayout();
        grip.RaiseEvent(new PointerPressedEventArgs(grip, pointer, this, position, 8, pressed, KeyModifiers.None, 1));
        grip.RaiseEvent(new PointerEventArgs(InputElement.PointerMovedEvent, grip, pointer, this, position + new Vector(0, dragDistance), 9, pressed, KeyModifiers.None));
        Require(_sections.Children[1] == section, "Cancel test must start with a live swap");
        Key(grip, Avalonia.Input.Key.Escape);
        Require(_sections.Children[0] == section && pointer.Captured is null && _popout.IsVisible, "Escape must cancel reorder without closing popout");
        await Task.Delay(200);
        Require(((TranslateTransform)section.RenderTransform!).Y == 0, "Section must settle back into layout");
        var resize = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(_popout).OfType<Border>()
            .Single(b => AutomationProperties.GetName(b) == "Resize popout width");
        var originalWidth = _popout.Width;
        Key(resize, Avalonia.Input.Key.Right); Require(_popout.Width == originalWidth + 10, "Keyboard resize failed");
        _popout.Width = originalWidth; _popout.UpdateLayout();
        position = resize.TranslatePoint(new Point(3, 15), this)!.Value;
        resize.RaiseEvent(new PointerPressedEventArgs(resize, pointer, this, position, 10, pressed, KeyModifiers.None, 1));
        resize.RaiseEvent(new PointerEventArgs(InputElement.PointerMovedEvent, resize, pointer, this, position + new Vector(100, 0), 11, pressed, KeyModifiers.None));
        resize.RaiseEvent(new PointerReleasedEventArgs(resize, pointer, this, position + new Vector(100, 0), 12, new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonReleased), KeyModifiers.None, MouseButton.Left));
        Require(_popout.Width == originalWidth + 100 && pointer.Captured is null, "Pointer resize failed");
        _popout.UpdateLayout(); Capture("preview-wide.png");
        resize.RaiseEvent(new PointerPressedEventArgs(resize, pointer, this, position, 13, pressed, KeyModifiers.None, 1));
        resize.RaiseEvent(new PointerEventArgs(InputElement.PointerMovedEvent, resize, pointer, this, position + new Vector(10000, 0), 14, pressed, KeyModifiers.None));
        Require(_popout.Width == _popout.MaxWidth, "Resize must respect available viewport width");
        Key(resize, Avalonia.Input.Key.Escape);
        Require(_popout.Width == originalWidth + 100 && pointer.Captured is null, "Resize cancellation failed");
        _popout.Width = originalWidth;
        Key(_popout, Avalonia.Input.Key.Escape); Require(!_popout.IsVisible, "Popout Escape failed");
        _popout.IsVisible = true; _selected = "supports"; _toolbar.Select(_selected);
        angle.EditCommitted -= Count; _undo.Clear();
        _status.Text = "Ready · drag fields · Shift for fine adjustment · click or Enter for expressions";
        void Capture(string name)
        {
            var content = (Control)Content!;
            using var bitmap = new RenderTargetBitmap(new PixelSize((int)content.Bounds.Width, (int)content.Bounds.Height));
            bitmap.Render(content); bitmap.Save(System.IO.Path.Combine(captureDirectory, name), PngBitmapEncoderOptions.Default);
        }
    }
    private StackPanel Section(string id, string title)
    {
        var content = new StackPanel { Spacing = 6 };
        var section = new ReorderableExpander(id, title) { Body = content };
        section.MoveRequested += (_, direction) =>
        {
            var index = _sections.Children.IndexOf(section); var destination = Math.Clamp(index + direction, 0, _sections.Children.Count - 1);
            if (destination == index) return;
            _sections.Children.RemoveAt(index); _sections.Children.Insert(destination, section);
            _status.Text = $"Moved {title} to position {destination + 1}";
        };
        _sections.Children.Add(section); return content;
    }
    private ScrubField Field(double value, double minimum, double maximum)
    {
        var field = new ScrubField { Value = value, Minimum = minimum, Maximum = maximum };
        Track(field); return field;
    }
    private void Track(ScrubField field) => field.EditCommitted += (_, e) =>
    {
        _undo.Push((field, e.OldValue)); _status.Text = $"Committed {e.OldValue:0.00} → {e.NewValue:0.00} {field.Unit} · {_undo.Count} undo entries";
    };
    private static Control Row(string label, ScrubField field)
    {
        AutomationProperties.SetName(field, label);
        ToolTip.SetTip(field, $"{label}: drag to scrub, Shift for fine; click to type an expression");
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,1.15*") };
        row.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 8, 0), Foreground = RefreshPalette.Text });
        Grid.SetColumn(field, 1); row.Children.Add(field); return row;
    }
}
