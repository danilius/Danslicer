using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;


namespace Danslicer.App.Controls.Refresh;

/// <summary>In-window host that retains existing settings subtrees and their bindings.</summary>
public sealed class WorkspacePopout : ContentControl
{
    public static readonly StyledProperty<Control?> PlacementTargetProperty = AvaloniaProperty.Register<WorkspacePopout, Control?>(nameof(PlacementTarget));
    public Control? PlacementTarget { get => GetValue(PlacementTargetProperty); set => SetValue(PlacementTargetProperty, value); }
    public string Title { get; set; } = "Settings";
    public ToolPopout Shell { get; private set; } = null!;
    public event EventHandler? Opened;
    public event EventHandler? Closed;
    public bool IsOpen
    {
        get => IsVisible;
        set
        {
            if (IsVisible == value) return;
            IsVisible = value;
            if (Shell is not null) Shell.IsVisible = true;
            if (value) Opened?.Invoke(this, EventArgs.Empty);
            else Closed?.Invoke(this, EventArgs.Empty);
        }
    }
    public WorkspacePopout()
    {
        IsVisible = false;
        HorizontalAlignment = HorizontalAlignment.Left;
        VerticalAlignment = VerticalAlignment.Top;
    }
    public void Initialize(Action close)
    {
        var body = Content as Control;
        Content = null;
        if (body is Border border)
        {
            border.Width = double.NaN; border.MaxHeight = double.PositiveInfinity;
            border.Margin = new Thickness(0); border.Padding = new Thickness(0);
            border.BorderThickness = new Thickness(0);
            border.HorizontalAlignment = HorizontalAlignment.Stretch;
            border.VerticalAlignment = VerticalAlignment.Stretch;
        }
        Shell = new ToolPopout { Title = Title, Body = body, Margin = new Thickness(0), Width = 340, Focusable = true };
        Shell.CloseRequested += (_, _) => close();
        Content = Shell;
    }
}

