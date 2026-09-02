using Danslicer.Core.Geometry;

namespace Danslicer.Core.Scene;

public enum RenderState
{
    Normal,
    Ghosted,
    Highlighted,
    Hidden,
}

/// <summary>A placed instance of a mesh on the build plate.</summary>
public sealed class SceneObject
{
    public Guid Id { get; } = Guid.NewGuid();
    public string Name { get; set; }
    public Mesh Mesh { get; }
    public Transform Transform { get; set; } = Transform.Identity;
    public RenderState RenderState { get; set; } = RenderState.Normal;

    public SceneObject(string name, Mesh mesh)
    {
        Name = name;
        Mesh = mesh;
    }

    public Aabb WorldBounds => Mesh.Bounds.Transform(Transform.ToMatrix());
}
