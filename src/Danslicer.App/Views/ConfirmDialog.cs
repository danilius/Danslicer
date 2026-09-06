using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Danslicer.App.Views;

/// <summary>
/// A small yes/no modal, built in code because it is two lines of text and two buttons and does
/// not earn a .axaml file. Themed through the same DynamicResource tokens as the rest of the app,
/// so it follows the chosen palette.
/// </summary>
internal sealed class ConfirmDialog : Window
{
    private ConfirmDialog(string title, string message, string confirmText, string cancelText)
    {
        Title = title;
        Width = 380;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = this.FindResource("AppSurface") as IBrush ?? Brushes.Transparent;

        var confirm = new Button { Content = confirmText, MinWidth = 92, IsDefault = true };
        var cancel = new Button { Content = cancelText, MinWidth = 92, IsCancel = true };
        confirm.Click += (_, _) => Close(true);
        cancel.Click += (_, _) => Close(false);

        Content = new StackPanel
        {
            Margin = new Thickness(18),
            Spacing = 16,
            Children =
            {
                new TextBlock
                {
                    Text = message,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = this.FindResource("AppTextPrimary") as IBrush,
                },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Children = { cancel, confirm },
                },
            },
        };
    }

    /// <summary>True when the user confirmed. Closing the dialog any other way is a "no".</summary>
    public static async Task<bool> AskAsync(Window owner, string title, string message,
        string confirmText = "Discard", string cancelText = "Cancel") =>
        await new ConfirmDialog(title, message, confirmText, cancelText)
            .ShowDialog<bool>(owner);
}
