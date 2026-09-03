using System.Windows.Input;
using Danslicer.Core;

namespace Danslicer.Tests;

public sealed class ModeScopedCommandTests
{
    public static TheoryData<WorkspaceMode[]> ShortcutScopes => new()
    {
        { [WorkspaceMode.Layout] },                    // Drop, lay flat, snapping.
        { [WorkspaceMode.Support] },                   // Generate, support-only actions.
        { [WorkspaceMode.Slicing] },                   // Slice and export.
        { [WorkspaceMode.Layout, WorkspaceMode.Support] }, // Hide/unhide.
    };

    [Theory]
    [MemberData(nameof(ShortcutScopes))]
    public void ShortcutFiresOnlyInItsDeclaredModes(WorkspaceMode[] allowedModes)
    {
        var current = WorkspaceMode.Layout;
        var executions = 0;
        var command = new ModeScopedCommand(
            new TestCommand(() => executions++), () => current, allowedModes);

        foreach (var mode in Enum.GetValues<WorkspaceMode>())
        {
            current = mode;
            command.NotifyModeChanged();

            Assert.Equal(allowedModes.Contains(mode), command.IsAvailable);
            command.Execute(null);
        }

        Assert.Equal(allowedModes.Length, executions);
    }

    [Fact]
    public void InnerCanExecuteStillAppliesInsideAnAllowedMode()
    {
        var enabled = false;
        var executions = 0;
        var command = new ModeScopedCommand(
            new TestCommand(() => executions++, () => enabled),
            () => WorkspaceMode.Support,
            WorkspaceMode.Support);

        command.Execute(null);
        enabled = true;
        command.Execute(null);

        Assert.Equal(1, executions);
    }

    private sealed class TestCommand(Action execute, Func<bool>? canExecute = null) : ICommand
    {
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? parameter) => canExecute?.Invoke() ?? true;
        public void Execute(object? parameter) => execute();
    }
}
