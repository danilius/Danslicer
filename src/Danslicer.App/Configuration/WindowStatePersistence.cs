using Avalonia;
using Avalonia.Controls;
using Danslicer.Core.Config;

namespace Danslicer.App.Configuration;

/// <summary>
/// Restores and saves a window's position, size and maximized state under a stable key in the
/// user configuration. Call <see cref="Track"/> in the window's constructor, after
/// InitializeComponent. The last NORMAL bounds are tracked while the window moves and resizes,
/// so closing while maximized still restores to a sensible un-maximized size next time.
/// </summary>
public static class WindowStatePersistence
{
    public static void Track(Window window, string key,
        ColumnDefinition? leftPanel = null, ColumnDefinition? rightPanel = null)
    {
        var saved = AppConfig.Current.Windows.TryGetValue(key, out var s) ? s : null;
        var normal = new WindowStateConfig
        {
            X = saved?.X ?? 0,
            Y = saved?.Y ?? 0,
            Width = saved is null ? window.Width : saved.Width,
            Height = saved is null ? window.Height : saved.Height,
        };

        if (saved is { Width: >= 200, Height: >= 150 })
        {
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Width = saved.Width;
            window.Height = saved.Height;
            window.Position = new PixelPoint(saved.X, saved.Y);
            if (saved.Maximized) window.WindowState = WindowState.Maximized;
            // Off-screen rescue (unplugged monitor): re-center once screens are known.
            window.Opened += (_, _) =>
            {
                var position = window.Position;
                var visible = window.Screens.All.Any(screen =>
                    screen.WorkingArea.Contains(position + new PixelVector(40, 40)));
                if (!visible && window.Screens.Primary is { } primary)
                    window.Position = primary.WorkingArea.Center - new PixelPoint(
                        (int)(window.Width / 2), (int)(window.Height / 2));
            };
        }

        if (leftPanel is not null && saved?.LeftPanelWidth >= leftPanel.MinWidth)
            leftPanel.Width = new GridLength(saved.LeftPanelWidth);
        if (rightPanel is not null && saved?.RightPanelWidth >= rightPanel.MinWidth)
            rightPanel.Width = new GridLength(saved.RightPanelWidth);

        window.PositionChanged += (_, e) =>
        {
            if (window.WindowState != WindowState.Normal) return;
            normal.X = e.Point.X;
            normal.Y = e.Point.Y;
        };
        window.SizeChanged += (_, _) =>
        {
            if (window.WindowState != WindowState.Normal) return;
            normal.Width = window.Width;
            normal.Height = window.Height;
        };
        window.Closing += (_, _) =>
        {
            normal.Maximized = window.WindowState == WindowState.Maximized;
            if (leftPanel is not null) normal.LeftPanelWidth = leftPanel.ActualWidth;
            if (rightPanel is not null) normal.RightPanelWidth = rightPanel.ActualWidth;
            AppConfig.Current.Windows[key] = normal;
            AppConfig.Save();
        };
    }
}
