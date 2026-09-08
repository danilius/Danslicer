namespace Danslicer.Core.Commands;

/// <summary>
/// A command built the first time it executes, for a step whose inputs only exist once an
/// earlier command in the same composite has run. Parenting's second pass removes trunks the
/// first pass adds: constructed up front, the removal would look those trunks up in a graph
/// that does not have them yet (crash seen 2026-09-08). Undo and redo delegate to the command
/// built on the first execute.
/// </summary>
public sealed class DeferredCommand(string name, Func<IDocumentCommand> factory) : IDocumentCommand
{
    private IDocumentCommand? _inner;

    public string Name => name;

    public void Execute()
    {
        _inner ??= factory();
        _inner.Execute();
    }

    public void Undo() => _inner?.Undo();
}
