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
