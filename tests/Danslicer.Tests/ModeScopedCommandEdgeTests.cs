using System;
using System.ComponentModel;
using System.Windows.Input;
using Danslicer.Core;
using Xunit;

namespace Danslicer.Tests;

public sealed class ModeScopedCommandEdgeTests
{
    [Fact]
    public void ConstructorThrowsArgumentExceptionForEmptyAllowedModes()
    {
        var inner = new TestCommand(() => { });
        var currentMode = () => WorkspaceMode.Layout;

        Assert.Throws<ArgumentException>(() => new ModeScopedCommand(inner, currentMode));
    }

    [Fact]
    public void ConstructorThrowsArgumentNullExceptionForNullInnerCommand()
    {
        Func<WorkspaceMode> currentMode = () => WorkspaceMode.Layout;

        Assert.Throws<ArgumentNullException>(() => new ModeScopedCommand(null!, currentMode, WorkspaceMode.Layout));
    }

    [Fact]
    public void ExecuteDoesNotInvokeInnerCommandInDisallowedMode()
    {
        var executions = 0;
        var currentMode = WorkspaceMode.Layout;
        var command = new ModeScopedCommand(
            new TestCommand(() => executions++),
            () => currentMode,
            WorkspaceMode.Support);

        command.Execute(null);
        Assert.Equal(0, executions);

        currentMode = WorkspaceMode.Support;
        command.NotifyModeChanged();
        command.Execute(null);
        Assert.Equal(1, executions);
    }

    [Fact]
    public void InnerCanExecuteChangedEventIsForwarded()
    {
        var canExecuteChangedFired = false;
        var inner = new TestCommand(() => { }, () => true);
        inner.CanExecuteChanged += (_, _) => canExecuteChangedFired = true;

        var command = new ModeScopedCommand(
            inner,
            () => WorkspaceMode.Layout,
            WorkspaceMode.Layout);

        inner.RaiseCanExecuteChanged();
        Assert.True(canExecuteChangedFired);
    }

    [Fact]
    public void NotifyModeChangedRaisesPropertyChangedAndCanExecuteChanged()
    {
        var propertyChangedFired = false;
        var canExecuteChangedFired = false;
        var command = new ModeScopedCommand(
            new TestCommand(() => { }),
            () => WorkspaceMode.Layout,
            WorkspaceMode.Layout);

        command.PropertyChanged += (_, e) => propertyChangedFired = e.PropertyName == nameof(command.IsAvailable);
        command.CanExecuteChanged += (_, _) => canExecuteChangedFired = true;

        command.NotifyModeChanged();
        Assert.True(propertyChangedFired);
        Assert.True(canExecuteChangedFired);
    }

    [Fact]
    public void CanExecuteIsFalseInAllowedModeWhenInnerCanExecuteIsFalse()
    {
        var inner = new TestCommand(() => { }, () => false);
        var command = new ModeScopedCommand(
            inner,
            () => WorkspaceMode.Layout,
            WorkspaceMode.Layout);

        Assert.False(command.CanExecute(null));
    }

    private sealed class TestCommand(Action execute, Func<bool>? canExecute = null) : ICommand
    {
        public event EventHandler? CanExecuteChanged;
        public bool CanExecute(object? parameter) => canExecute?.Invoke() ?? true;
        public void Execute(object? parameter) => execute();

        public void RaiseCanExecuteChanged()
        {
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
