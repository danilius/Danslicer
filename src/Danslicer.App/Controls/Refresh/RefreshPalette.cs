using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;

namespace Danslicer.App.Controls.Refresh;

/// <summary>Opt-in palette: existing application themes are untouched.</summary>
public static class RefreshPalette
{
    public static readonly IBrush Canvas = Brush.Parse("#191B1D");
    public static readonly IBrush Panel = Brush.Parse("#292C2F");
    public static readonly IBrush Header = Brush.Parse("#202225");
    public static readonly IBrush Field = Brush.Parse("#34373A");
    public static readonly IBrush Edge = Brush.Parse("#4A4D50");
    public static readonly IBrush Text = Brush.Parse("#E3E4E6");
    public static readonly IBrush Muted = Brush.Parse("#A8ADB3");
    public static readonly IBrush Accent = Brush.Parse("#EBA548");
    public static readonly IBrush Fill = Brush.Parse("#765322");

    public static ResourceDictionary CreateResources()
    {
        var resources = new ResourceDictionary();
        foreach (var key in new[] { "ButtonBackground", "ToggleButtonBackground", "TextControlBackground" }) resources[key] = Field;
        foreach (var key in new[] { "ButtonForeground", "ToggleButtonForeground", "ToggleButtonForegroundChecked", "TextControlForeground" }) resources[key] = Text;
        foreach (var key in new[] { "ButtonBackgroundPointerOver", "ToggleButtonBackgroundPointerOver" }) resources[key] = Edge;
        foreach (var key in new[] { "ToggleButtonBackgroundChecked", "ToggleButtonBackgroundCheckedPointerOver" }) resources[key] = Fill;
        foreach (var key in new[] { "CheckBoxCheckBackgroundFillChecked", "CheckBoxCheckBackgroundFillCheckedPointerOver", "TextControlBorderBrushFocused" }) resources[key] = Accent;
        return resources;
    }
}

/// <summary>Outline geometries on a 24-unit grid, reconstructed from the Charcoal study.</summary>
public static class RefreshIcons
{
    private static readonly IReadOnlyDictionary<string, string> Paths = new Dictionary<string, string>
    {
        ["select"] = "M5,3 L18,14 L12,14 L15,21 L12,22 L9,15 L5,19 Z",
        ["move"] = "M12,2 L12,22 M2,12 L22,12 M8,6 L12,2 L16,6 M8,18 L12,22 L16,18 M6,8 L2,12 L6,16 M18,8 L22,12 L18,16",
        ["rotate"] = "M20,8 A9,9 0 1 0 21,14 M20,2 L20,8 L14,8",
        ["scale"] = "M3,21 L21,3 M13,3 L21,3 L21,11 M3,13 L3,21 L11,21",
        ["objects"] = "M3,7 L12,2 L21,7 L21,17 L12,22 L3,17 Z M3,7 L12,12 L21,7 M12,12 L12,22",
        ["supports"] = "M12,3 L12,21 M5,7 L5,12 L12,17 L19,12 L19,7 M8,21 L16,21 M10,3 A2,2 0 1 0 14,3 A2,2 0 1 0 10,3 M3,5 A2,2 0 1 0 7,5 A2,2 0 1 0 3,5 M17,5 A2,2 0 1 0 21,5 A2,2 0 1 0 17,5",
        ["visibility"] = "M2,12 Q12,-1 22,12 Q12,25 2,12 Z M8,12 A4,4 0 1 0 16,12 A4,4 0 1 0 8,12",
        ["rafts"] = "M2,8 L12,3 L22,8 L12,13 Z M2,12 L12,17 L22,12 M2,16 L12,21 L22,16",
        ["close"] = "M5,5 L19,19 M19,5 L5,19",
        ["grip"] = "M8,5 L8,7 M16,5 L16,7 M8,11 L8,13 M16,11 L16,13 M8,17 L8,19 M16,17 L16,19",
        ["lock"] = "M7,10 L7,6 A5,5 0 0 1 17,6 L17,10 M5,10 L19,10 L19,21 L5,21 Z M12,14 L12,17",
        ["unlock"] = "M7,10 L7,6 A5,5 0 0 1 17,6 M5,10 L19,10 L19,21 L5,21 Z M12,14 L12,17"
    };
    public static Control Create(string name) => new Avalonia.Controls.Shapes.Path
    {
        Data = Geometry.Parse(Paths[name]), Stroke = RefreshPalette.Text, StrokeThickness = 1.5,
        Width = 20, Height = 20, Stretch = Stretch.Uniform, IsHitTestVisible = false
    };
}
