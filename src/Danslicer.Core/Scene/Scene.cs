using Danslicer.Core.Geometry;

namespace Danslicer.Core.Scene;

/// <summary>The set of objects on the build plate. Mutated only through commands.</summary>
public sealed class Scene
{
    private readonly List<SceneObject> _objects = new();

    public IReadOnlyList<SceneObject> Objects => _objects;

    public event Action<SceneObject>? ObjectAdded;
    public event Action<SceneObject>? ObjectRemoved;

    internal void Add(SceneObject obj)
    {
        _objects.Add(obj);
        ObjectAdded?.Invoke(obj);
    }

    internal void Insert(int index, SceneObject obj)
    {
        _objects.Insert(Math.Clamp(index, 0, _objects.Count), obj);
        ObjectAdded?.Invoke(obj);
    }

    internal int Remove(SceneObject obj)
    {
        var index = _objects.IndexOf(obj);
        if (index >= 0)
        {
            _objects.RemoveAt(index);
            ObjectRemoved?.Invoke(obj);
        }
        return index;
    }

    internal void ReplaceWith(IEnumerable<SceneObject> objects)
    {
        foreach (var existing in _objects.ToList())
            Remove(existing);
        foreach (var obj in objects)
            Add(obj);
    }

    public Aabb WorldBounds
    {
        get
        {
            var bounds = Aabb.Empty;
            foreach (var o in _objects)
                if (o.RenderState != RenderState.Hidden)
                    bounds = bounds.Union(o.WorldBounds);
            return bounds;
        }
    }
}
