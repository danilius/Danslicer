using Danslicer.Core.Scene;
using Danslicer.Core.Supports.Rafts;

namespace Danslicer.Core.Commands;

/// <summary>Sets or clears an object's raft (the parameters it holds). Paired with the base
/// strip or restore of its feet in one composite step by <see cref="Document"/>.</summary>
public sealed class SetObjectRaftCommand : IDocumentCommand
{
    private readonly SceneObject _object;
    private readonly RaftParameters? _before;
    private readonly RaftParameters? _after;

    public SetObjectRaftCommand(SceneObject obj, RaftParameters? after, string name)
    {
        _object = obj;
        _before = obj.Raft;
        _after = after;
        Name = name;
    }

    public string Name { get; }
    public void Execute() => _object.Raft = _after;
    public void Undo() => _object.Raft = _before;
}
