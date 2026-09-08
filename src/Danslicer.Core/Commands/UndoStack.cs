namespace Danslicer.Core.Commands;

public sealed class UndoStack
{
    private readonly Stack<IDocumentCommand> _undo = new();
    private readonly Stack<IDocumentCommand> _redo = new();

    public event Action? Changed;

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public string? UndoName => _undo.TryPeek(out var c) ? c.Name : null;
    public string? RedoName => _redo.TryPeek(out var c) ? c.Name : null;

    public void Execute(IDocumentCommand command)
    {
        command.Execute();
        _undo.Push(command);
        _redo.Clear();
        Changed?.Invoke();
    }

    /// <summary>
    /// Records a command whose effects were already applied incrementally. Used by long-running
    /// operations that must remain one undo step without replaying their mutations at completion.
    /// </summary>
    public void RecordExecuted(IDocumentCommand command)
    {
        _undo.Push(command);
        _redo.Clear();
        Changed?.Invoke();
    }

    /// <summary>
    /// Folds the last two executed commands into one undo step named <paramref name="name"/>.
    /// Auto-parenting runs as its own command after a placement and the two must undo together
    /// (one gesture, one step); merging after the fact keeps parenting out of every placement
    /// command. Does nothing with fewer than two commands.
    /// </summary>
    public bool MergeLastTwo(string name)
    {
        if (_undo.Count < 2) return false;
        var later = _undo.Pop();
        var earlier = _undo.Pop();
        _undo.Push(new CompositeCommand(name, [earlier, later]));
        Changed?.Invoke();
        return true;
    }

    public bool Undo()
    {
        if (!_undo.TryPop(out var command)) return false;
        command.Undo();
        _redo.Push(command);
        Changed?.Invoke();
        return true;
    }

    public bool Redo()
    {
        if (!_redo.TryPop(out var command)) return false;
        command.Execute();
        _undo.Push(command);
        Changed?.Invoke();
        return true;
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
        Changed?.Invoke();
    }
}
