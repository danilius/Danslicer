using Danslicer.Core.Geometry;
using Danslicer.Core.Supports;

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
    public Guid Id { get; }
    public string Name { get; set; }
    public Mesh Mesh { get; internal set; }
    public Transform Transform { get; set; } = Transform.Identity;
    public RenderState RenderState { get; set; } = RenderState.Normal;

    /// <summary>
    /// Painted support region and keep-clean faces for this object (DESIGN 8.3). Empty by
    /// default, which means "every face" — an unpainted object generates exactly as it did
    /// before regions existed. Face indices address THIS object's mesh, so replacing the mesh
    /// invalidates them; go through <see cref="Danslicer.Core.Document"/> to change it, which
    /// makes the edit undoable.
    /// </summary>
    public ObjectSupportRegions Regions { get; set; } = ObjectSupportRegions.Empty;

    public SceneObject(string name, Mesh mesh, Guid? id = null)
    {
        Name = name;
        Mesh = mesh;
        Id = id ?? Guid.NewGuid();
    }

    public Aabb WorldBounds => Mesh.Bounds.Transform(Transform.ToMatrix());
}
