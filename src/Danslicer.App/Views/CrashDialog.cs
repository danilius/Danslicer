using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Danslicer.Core.Diagnostics;

namespace Danslicer.App.Views;

/// <summary>
/// Shown when a UI-thread exception was caught and the app kept running. Says what failed,
/// where the log is, and that the document may be half-changed; offers to save the project
/// under a new name so the session's work is not lost, or to carry on.
/// </summary>
internal sealed class CrashDialog : Window
{
    private CrashDialog(Exception exception)
    {
        Title = "Something went wrong";
        Width = 520;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = this.FindResource("AppSurface") as IBrush ?? Brushes.Transparent;
        var text = this.FindResource("AppTextPrimary") as IBrush;
        var faint = this.FindResource("AppTextFaint") as IBrush;

        var saveAs = new Button { Content = "Save project as…", MinWidth = 140, IsDefault = true };
        var carryOn = new Button { Content = "Continue", MinWidth = 92, IsCancel = true };
        saveAs.Click += (_, _) => Close(true);
        carryOn.Click += (_, _) => Close(false);

        Content = new StackPanel
        {
            Margin = new Thickness(18),
            Spacing = 12,
            Children =
            {
                new TextBlock
                {
                    Text = $"{exception.GetType().Name}: {exception.Message}",
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = text,
                    FontWeight = FontWeight.SemiBold,
                },
                new TextBlock
                {
                    Text = "The app is still running, but the last action failed part-way and the " +
                           "document may be inconsistent. Saving under a new name keeps your work " +
                           "without overwriting the last good file.",
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = text,
                },
                new SelectableTextBlock
                {
                    Text = $"Details were written to {CrashLog.CurrentPath}",
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = faint,
                    FontSize = 12,
                },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Children = { carryOn, saveAs },
                },
            },
        };
    }

    /// <summary>True when the user chose to save the project under a new name.</summary>
    public static async Task<bool> ShowAsync(Window owner, Exception exception) =>
        await new CrashDialog(exception).ShowDialog<bool>(owner);
}
