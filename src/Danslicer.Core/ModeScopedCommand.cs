using System.ComponentModel;
using System.Windows.Input;

namespace Danslicer.Core;

/// <summary>
/// Wraps an application command with the workspace modes in which it is available. The same
/// declaration drives command execution and UI visibility, so menus and shortcuts cannot drift.
/// </summary>
public sealed class ModeScopedCommand : ICommand, INotifyPropertyChanged
{
    private readonly ICommand _inner;
    private readonly Func<WorkspaceMode> _currentMode;
    private readonly HashSet<WorkspaceMode> _allowedModes;

    public ModeScopedCommand(
        ICommand inner,
        Func<WorkspaceMode> currentMode,
        params WorkspaceMode[] allowedModes)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(currentMode);
        if (allowedModes.Length == 0)
            throw new ArgumentException("At least one workspace mode is required.", nameof(allowedModes));

        _inner = inner;
        _currentMode = currentMode;
        _allowedModes = [.. allowedModes];
        _inner.CanExecuteChanged += (_, _) => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }

    public bool IsAvailable => _allowedModes.Contains(_currentMode());

    public event EventHandler? CanExecuteChanged;
    public event PropertyChangedEventHandler? PropertyChanged;

    public bool CanExecute(object? parameter) => IsAvailable && _inner.CanExecute(parameter);

    public void Execute(object? parameter)
    {
        if (CanExecute(parameter)) _inner.Execute(parameter);
    }

    /// <summary>Notifies bindings after the owning view model changes workspace.</summary>
    public void NotifyModeChanged()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsAvailable)));
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}
