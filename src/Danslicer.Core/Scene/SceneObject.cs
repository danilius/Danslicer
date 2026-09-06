using System.ComponentModel;
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
public sealed class SceneObject : INotifyPropertyChanged
{
    public Guid Id { get; }
    public string Name { get; set; }
    public Mesh Mesh { get; internal set; }
    public Transform Transform { get; set; } = Transform.Identity;

    private RenderState _renderState = RenderState.Normal;

    /// <summary>
    /// How the object is drawn, hidden included. This one property raises change notification —
    /// the object list's per-model show/hide toggles bind to it, and they must follow an undo as
    /// well as a click. The rest of the class is plain: notification is here because a control
    /// needs it, not as a general policy for scene objects.
    /// </summary>
    public RenderState RenderState
    {
        get => _renderState;
        set
        {
            if (_renderState == value) return;
            _renderState = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RenderState)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsVisible)));
        }
    }

    /// <summary>The form the toggles want: hidden is the only state that is not visible.</summary>
    public bool IsVisible => _renderState != RenderState.Hidden;

    public event PropertyChangedEventHandler? PropertyChanged;

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
