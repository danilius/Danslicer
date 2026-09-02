namespace Danslicer.Core.Commands;

/// <summary>
/// A reversible mutation of the document. Every change to scene, supports or settings goes through one
/// so that the UI, the CLI and scripting share a single code path and a single undo stack.
/// </summary>
public interface IDocumentCommand
{
    string Name { get; }
    void Execute();
    void Undo();
}

/// <summary>Executes several commands as one undo step.</summary>
public sealed class CompositeCommand : IDocumentCommand
{
    private readonly IReadOnlyList<IDocumentCommand> _commands;

    public CompositeCommand(string name, IReadOnlyList<IDocumentCommand> commands)
    {
        Name = name;
        _commands = commands;
    }

    public string Name { get; }
    public int Count => _commands.Count;

    public void Execute()
    {
        foreach (var c in _commands) c.Execute();
    }

    public void Undo()
    {
        for (int i = _commands.Count - 1; i >= 0; i--) _commands[i].Undo();
    }
}
