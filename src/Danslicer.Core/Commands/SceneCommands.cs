using Danslicer.Core.Scene;

namespace Danslicer.Core.Commands;

public sealed class AddObjectCommand : IDocumentCommand
{
    private readonly Scene.Scene _scene;
    private readonly SceneObject _object;

    public AddObjectCommand(Scene.Scene scene, SceneObject obj)
    {
        _scene = scene;
        _object = obj;
    }

    public string Name => $"Add {_object.Name}";
    public void Execute() => _scene.Add(_object);
    public void Undo() => _scene.Remove(_object);
}

public sealed class RemoveObjectCommand : IDocumentCommand
{
    private readonly Scene.Scene _scene;
    private readonly SceneObject _object;
    private int _index;

    public RemoveObjectCommand(Scene.Scene scene, SceneObject obj)
    {
        _scene = scene;
        _object = obj;
    }

    public string Name => $"Delete {_object.Name}";
    public void Execute() => _index = _scene.Remove(_object);
    public void Undo() => _scene.Insert(_index, _object);
}

public sealed class SetTransformCommand : IDocumentCommand
{
    private readonly SceneObject _object;
    private readonly Transform _before;
    private readonly Transform _after;

    public SetTransformCommand(SceneObject obj, Transform before, Transform after, string name = "Transform")
    {
        _object = obj;
        _before = before;
        _after = after;
        Name = name;
    }

    public string Name { get; }
    public void Execute() => _object.Transform = _after;
    public void Undo() => _object.Transform = _before;
}

public sealed class SetRenderStateCommand : IDocumentCommand
{
    private readonly SceneObject _object;
    private readonly RenderState _before;
    private readonly RenderState _after;

    public SetRenderStateCommand(SceneObject obj, RenderState after, string name)
    {
        _object = obj;
        _before = obj.RenderState;
        _after = after;
        Name = name;
    }

    public string Name { get; }
    public void Execute() => _object.RenderState = _after;
    public void Undo() => _object.RenderState = _before;
}
