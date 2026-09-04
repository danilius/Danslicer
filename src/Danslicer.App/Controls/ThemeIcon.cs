using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Danslicer.App.Configuration;
using Danslicer.Core.Config;

namespace Danslicer.App.Controls;

/// <summary>
/// Shows Forge vector geometry while preserving an optional Classic text glyph. Menu icons omit
/// the fallback and therefore reserve no visible icon content in the original theme.
/// </summary>
public sealed class ThemeIcon : ContentControl
{
    public static readonly StyledProperty<Geometry?> DataProperty =
        AvaloniaProperty.Register<ThemeIcon, Geometry?>(nameof(Data));
    public static readonly StyledProperty<string?> ClassicTextProperty =
        AvaloniaProperty.Register<ThemeIcon, string?>(nameof(ClassicText));
    public static readonly StyledProperty<double> IconSizeProperty =
        AvaloniaProperty.Register<ThemeIcon, double>(nameof(IconSize), 16);

    static ThemeIcon() => AffectsMeasure<ThemeIcon>(DataProperty, ClassicTextProperty, IconSizeProperty);

    public ThemeIcon()
    {
        Focusable = false;
        HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center;
        VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center;
        AttachedToVisualTree += (_, _) =>
        {
            ThemeManager.ThemeChanged += Refresh;
            Refresh();
        };
        DetachedFromVisualTree += (_, _) => ThemeManager.ThemeChanged -= Refresh;
        PropertyChanged += (_, e) =>
        {
            if (e.Property == DataProperty || e.Property == ClassicTextProperty || e.Property == IconSizeProperty)
                Refresh();
        };
        Refresh();
    }

    public Geometry? Data { get => GetValue(DataProperty); set => SetValue(DataProperty, value); }
    public string? ClassicText { get => GetValue(ClassicTextProperty); set => SetValue(ClassicTextProperty, value); }
    public double IconSize { get => GetValue(IconSizeProperty); set => SetValue(IconSizeProperty, value); }

    private void Refresh()
    {
        if (ThemeManager.CurrentTheme == AppTheme.Forge && Data is not null)
        {
            IsVisible = true;
            Content = new PathIcon { Data = Data, Width = IconSize, Height = IconSize };
            return;
        }

        IsVisible = !string.IsNullOrEmpty(ClassicText);
        Content = ClassicText;
    }
}
