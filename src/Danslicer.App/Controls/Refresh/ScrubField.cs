using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Danslicer.Core.Utilities;
using System.Globalization;

namespace Danslicer.App.Controls.Refresh;

/// <summary>Rectangular numeric field. Click/Enter types expressions, drag scrubs, Shift is fine,
/// Escape cancels. Value changes only at commit; EditCommitted is one undo boundary.</summary>
public class ScrubField : UserControl
{
    public static readonly StyledProperty<double> ValueProperty = AvaloniaProperty.Register<ScrubField, double>(nameof(Value), defaultBindingMode: BindingMode.TwoWay);
    public static readonly StyledProperty<double> MinimumProperty = AvaloniaProperty.Register<ScrubField, double>(nameof(Minimum), 0);
    public static readonly StyledProperty<double> MaximumProperty = AvaloniaProperty.Register<ScrubField, double>(nameof(Maximum), 100);
    public static readonly StyledProperty<double> StepProperty = AvaloniaProperty.Register<ScrubField, double>(nameof(Step), 0.01);
    public static readonly StyledProperty<string> UnitProperty = AvaloniaProperty.Register<ScrubField, string>(nameof(Unit), "mm");
    public static readonly StyledProperty<bool> IsLockedProperty = AvaloniaProperty.Register<ScrubField, bool>(nameof(IsLocked));
    public static readonly StyledProperty<bool> CanLockProperty = AvaloniaProperty.Register<ScrubField, bool>(nameof(CanLock));
    public double Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public double Minimum { get => GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }
    public double Maximum { get => GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
    public double Step { get => GetValue(StepProperty); set => SetValue(StepProperty, value); }
    public string Unit { get => GetValue(UnitProperty); set => SetValue(UnitProperty, value); }
    public bool IsLocked { get => GetValue(IsLockedProperty); set => SetValue(IsLockedProperty, value); }
    public bool CanLock { get => GetValue(CanLockProperty); set => SetValue(CanLockProperty, value); }
    public UnitKind UnitKind { get; set; } = UnitKind.Length;
    public string Format { get; set; } = "0.00";
    public bool IsInteger { get; set; }
    public event EventHandler<NumericCommittedEventArgs>? EditCommitted;
    private readonly Border _surface;
    private readonly Border _fill;
    private readonly TextBlock _label;
    private readonly TextBox _editor;
    private readonly Button _lock;
    private NumericEditSession? _session;
    private IPointer? _capturedPointer;
    private bool _editing;
    private double _displayValue;
    protected virtual bool ShowFill => false;

    public ScrubField()
    {
        Focusable = true;
        MinWidth = 110;
        Height = 24;
        FontSize = 12;
        _fill = new Border { Background = RefreshPalette.Fill, HorizontalAlignment = HorizontalAlignment.Left, IsHitTestVisible = false };
        _label = new TextBlock { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Foreground = RefreshPalette.Text, IsHitTestVisible = false };
        _editor = new TextBox { IsVisible = false, HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(4, 0), MinHeight = 22 };
        var layers = new Grid();
        layers.Children.Add(_fill); layers.Children.Add(_label); layers.Children.Add(_editor);
        _surface = new Border { Background = RefreshPalette.Field, BorderBrush = RefreshPalette.Edge, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3), ClipToBounds = true, Child = layers };
        _lock = new Button { Width = 26, Height = 24, MinHeight = 0, Padding = new Thickness(4), IsVisible = false };
        ToolTip.SetTip(_lock, "Lock this value against editing");
        _lock.Click += (_, _) => { Cancel(); SetCurrentValue(IsLockedProperty, !IsLocked); };
        var root = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        root.Children.Add(_surface); Grid.SetColumn(_lock, 1); root.Children.Add(_lock); Content = root;
        _surface.PointerPressed += Pressed;
        _surface.PointerMoved += Moved;
        _surface.PointerReleased += Released;
        _surface.PointerCaptureLost += (_, _) => { if (_session is not null) Cancel(); };
        _surface.SizeChanged += (_, _) => Refresh();
        _editor.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { Cancel(); Focus(); e.Handled = true; }
            else if (e.Key == Key.Enter) { if (CommitText()) Focus(); e.Handled = true; }
        };
        _editor.LostFocus += (_, _) => { if (_editing && !CommitText()) Cancel(); };
        GotFocus += (_, _) => _surface.BorderBrush = RefreshPalette.Accent;
        LostFocus += (_, _) => _surface.BorderBrush = RefreshPalette.Edge;
        Refresh();
    }

    protected override void OnAttachedToLogicalTree(LogicalTreeAttachmentEventArgs e)
    {
        base.OnAttachedToLogicalTree(e);
        if (Parent is Grid row && row.Children.OfType<TextBlock>().FirstOrDefault() is { Text: { } label }
            && string.IsNullOrEmpty(AutomationProperties.GetName(this)))
            AutomationProperties.SetName(this, label);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (_surface is null) return;
        if (change.Property == ValueProperty || change.Property == IsLockedProperty || change.Property == IsEnabledProperty) Cancel();
        Refresh();
    }

    private void Refresh()
    {
        _displayValue = _session?.Preview ?? Value;
        _label.Text = $"{_displayValue.ToString(Format, CultureInfo.InvariantCulture)}{(Unit.Length > 0 ? " " + Unit : "")}";
        _fill.Width = ShowFill && Maximum > Minimum ? Math.Max(0, _surface.Bounds.Width - 2) * Math.Clamp((_displayValue - Minimum) / (Maximum - Minimum), 0, 1) : 0;
        _lock.IsVisible = CanLock;
        _lock.Content = RefreshIcons.Create(IsLocked ? "lock" : "unlock");
        AutomationProperties.SetName(_lock, IsLocked ? "Unlock value" : "Lock value");
        _surface.Opacity = IsLocked || !IsEnabled ? 0.5 : 1;
    }

    private void Pressed(object? sender, PointerPressedEventArgs e)
    {
        if (_editing || IsLocked || !e.GetCurrentPoint(_surface).Properties.IsLeftButtonPressed) return;
        Focus();
        _session = new NumericEditSession(Value, e.GetPosition(this).X);
        _capturedPointer = e.Pointer;
        e.Pointer.Capture(_surface); e.Handled = true;
    }
    private void Moved(object? sender, PointerEventArgs e)
    {
        if (_session is null || Maximum < Minimum) return;
        _session.Move(e.GetPosition(this).X, Step, e.KeyModifiers.HasFlag(KeyModifiers.Shift), Minimum, Maximum);
        Refresh();
    }
    private void Released(object? sender, PointerReleasedEventArgs e)
    {
        if (_session is null) return;
        var session = _session; _session = null;
        _capturedPointer = null;
        e.Pointer.Capture(null);
        if (session.IsDragging) Commit(session.Preview); else BeginText();
        e.Handled = true;
    }
    private void BeginText()
    {
        if (IsLocked) return;
        _editing = true; _editor.Text = Value.ToString("G", CultureInfo.InvariantCulture);
        _editor.IsVisible = true; _editor.Focus(); _editor.SelectAll();
    }
    private bool CommitText()
    {
        if (!NumericEditSession.TryExpression(_editor.Text ?? "", UnitKind, Minimum, Maximum, out var value))
        {
            _editor.BorderBrush = Brushes.IndianRed;
            ToolTip.SetTip(_editor, $"Enter a valid expression between {Minimum} and {Maximum} {Unit}.");
            return false;
        }
        _editing = false; _editor.IsVisible = false; _editor.ClearValue(TextBox.BorderBrushProperty);
        Commit(value); return true;
    }
    private void Commit(double value)
    {
        if (IsInteger) value = Math.Clamp(Math.Round(value), Minimum, Maximum);
        var old = Value;
        SetCurrentValue(ValueProperty, value); Refresh();
        if (old != value) EditCommitted?.Invoke(this, new NumericCommittedEventArgs(old, value));
    }
    protected void Cancel()
    {
        _session = null; _editing = false; _editor.IsVisible = false;
        var pointer = _capturedPointer; _capturedPointer = null;
        pointer?.Capture(null);
        _editor.ClearValue(TextBox.BorderBrushProperty);
        ToolTip.SetTip(_editor, null);
        Refresh();
    }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled) return;
        if (e.Key == Key.Escape && (_session is not null || _editing)) { Cancel(); e.Handled = true; }
        else if (!IsLocked && e.Key is Key.Enter or Key.F2) { BeginText(); e.Handled = true; }
        else if (!IsLocked && e.Key is Key.Left or Key.Right or Key.Up or Key.Down && Maximum >= Minimum)
        {
            var direction = e.Key is Key.Left or Key.Down ? -1 : 1;
            Commit(Math.Clamp(Value + direction * Step * (!IsInteger && e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 0.1 : 1), Minimum, Maximum));
            e.Handled = true;
        }
    }
}

/// <summary>Filled rectangular slider, with its value and unit centered inside the field.</summary>
public sealed class FilledNumericSlider : ScrubField
{
    protected override bool ShowFill => true;
}
