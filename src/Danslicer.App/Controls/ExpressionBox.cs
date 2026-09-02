using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Danslicer.App.Controls;

/// <summary>
/// TextBox that commits its binding on Enter, reverts on Escape, and selects all on focus.
/// Bind Text with UpdateSourceTrigger=LostFocus (the default) so typing does not commit per keystroke.
/// </summary>
public sealed class ExpressionBox : TextBox
{
    protected override Type StyleKeyOverride => typeof(TextBox);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            BindingOperations.GetBindingExpressionBase(this, TextProperty)?.UpdateSource();
            SelectAll();
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Escape)
        {
            BindingOperations.GetBindingExpressionBase(this, TextProperty)?.UpdateTarget();
            SelectAll();
            e.Handled = true;
            return;
        }
        base.OnKeyDown(e);
    }

    protected override void OnGotFocus(FocusChangedEventArgs e)
    {
        base.OnGotFocus(e);
        SelectAll();
    }
}
