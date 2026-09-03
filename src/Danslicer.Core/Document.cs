using System.Numerics;
using Danslicer.Core.Commands;
using Danslicer.Core.Supports;
using Danslicer.Core.Printers;
using Danslicer.Core.Scene;
using Danslicer.Core.Slicing;

namespace Danslicer.Core;

/// <summary>
/// The single model behind the application: scene, selection, printer and undo history.
/// Mutations go through <see cref="Execute"/>; selection is transient and not undoable.
/// </summary>
public sealed class Document
{
    private readonly HashSet<SceneObject> _selection = new();

    public Scene.Scene Scene { get; } = new();
    public SupportGraph Supports { get; } = new();
    public UndoStack History { get; } = new();
    public PrinterDefinition Printer { get; set; } = PrinterDefinition.PhotonMonoX;
    public PrintSettings PrintSettings { get; set; } = PrintSettings.Default;

    public IReadOnlyCollection<SceneObject> Selection => _selection;

    private readonly HashSet<Guid> _supportSelection = new();
    /// <summary>Selected support element ids; nodes and segments share one id space.</summary>
    public IReadOnlyCollection<Guid> SupportSelection => _supportSelection;

    /// <summary>Raised after any command, undo or redo, and after selection changes.</summary>
    public event Action? Changed;
    public event Action? SelectionChanged;
    public event Action? SupportSelectionChanged;

    public Document()
    {
        History.Changed += () => Changed?.Invoke();
        Supports.Changed += () =>
        {
            // Drop selection ids whose elements are gone (e.g. after undo of an add).
            var stale = _supportSelection.Where(id => !Supports.TryGetNode(id, out _) && !Supports.TryGetSegment(id, out _)).ToList();
            if (stale.Count > 0)
            {
                foreach (var id in stale) _supportSelection.Remove(id);
                SupportSelectionChanged?.Invoke();
            }
            Changed?.Invoke(); // support edits invalidate a stale slice
        };

        Scene.ObjectRemoved += obj =>
        {
            if (_selection.Remove(obj)) SelectionChanged?.Invoke();
        };
    }

    public void Execute(IDocumentCommand command) => History.Execute(command);
    public bool Undo() => History.Undo();
    public bool Redo() => History.Redo();

    /// <summary>Raise Changed for transient edits (e.g. live drag) that bypass the command stack.</summary>
    public void NotifyTransientChange() => Changed?.Invoke();

    public void Select(SceneObject obj, bool additive = false)
    {
        if (!additive) _selection.Clear();
        _selection.Add(obj);
        SelectionChanged?.Invoke();
    }

    public void ToggleSelection(SceneObject obj)
    {
        if (!_selection.Remove(obj)) _selection.Add(obj);
        SelectionChanged?.Invoke();
    }

    public void SelectAll()
    {
        _selection.Clear();
        foreach (var o in Scene.Objects)
            if (o.RenderState != RenderState.Hidden) _selection.Add(o);
        SelectionChanged?.Invoke();
    }

    public void ClearSelection()
    {
        if (_selection.Count == 0) return;
        _selection.Clear();
        SelectionChanged?.Invoke();
    }

    public bool IsSelected(SceneObject obj) => _selection.Contains(obj);

    public bool IsSupportSelected(Guid id) => _supportSelection.Contains(id);

    public void SelectSupportElement(Guid id, bool additive = false)
    {
        if (additive)
        {
            if (!_supportSelection.Remove(id)) _supportSelection.Add(id);
        }
        else
        {
            _supportSelection.Clear();
            _supportSelection.Add(id);
        }
        SupportSelectionChanged?.Invoke();
    }

    public void ClearSupportSelection()
    {
        if (_supportSelection.Count == 0) return;
        _supportSelection.Clear();
        SupportSelectionChanged?.Invoke();
    }

    /// <summary>Deletes the selected support elements; a deleted node takes its segments. One undo step.</summary>
    public void DeleteSupportSelection()
    {
        if (_supportSelection.Count == 0) return;
        var nodes = _supportSelection.Where(id => Supports.TryGetNode(id, out _)).ToList();
        var segments = _supportSelection.Where(id => Supports.TryGetSegment(id, out _)).ToList();
        _supportSelection.Clear();
        SupportSelectionChanged?.Invoke();
        if (nodes.Count == 0 && segments.Count == 0) return;
        Execute(new RemoveSupportElementsCommand(Supports, nodes, segments));
    }

    public void AddObject(SceneObject obj)
    {
        Execute(new AddObjectCommand(Scene, obj));
        Select(obj);
    }

    public void DeleteSelection()
    {
        if (_selection.Count == 0) return;
        var commands = _selection.Select(o => (IDocumentCommand)new RemoveObjectCommand(Scene, o)).ToList();
        Execute(new CompositeCommand(commands.Count == 1 ? commands[0].Name : $"Delete {commands.Count} objects", commands));
    }

    /// <summary>Moves each selected object so its lowest point sits on the plate.</summary>
    public void DropSelectionToPlate()
    {
        var commands = new List<IDocumentCommand>();
        foreach (var o in _selection)
        {
            var minZ = o.WorldBounds.Min.Z;
            if (MathF.Abs(minZ) < 1e-6f) continue;
            var before = o.Transform;
            var after = before with { Translation = before.Translation with { Z = before.Translation.Z - minZ } };
            commands.Add(new SetTransformCommand(o, before, after, "Drop to plate"));
        }
        if (commands.Count > 0) Execute(new CompositeCommand("Drop to plate", commands));
    }

    /// <summary>
    /// Adds a simple manual support under a picked surface point: a tip at the contact, a tapered
    /// neck dropping vertically to a junction, and a pillar straight down to a base on the plate.
    /// Contacts too close to the plate get a single tip-to-base pillar. One undo step.
    /// </summary>
    public void AddManualSupport(SceneObject obj, Vector3 contact, Vector3 surfaceNormal)
    {
        const float neckLength = 2f;
        const float neckDiameter = 0.8f;
        const float pillarDiameter = 1.2f;

        var tip = new SupportNode
        {
            Type = SupportNodeType.Tip,
            Position = contact,
            SurfaceNormal = surfaceNormal,
            ContactObjectId = obj.Id,
        };
        var baseNode = new SupportNode
        {
            Type = SupportNodeType.Base,
            Position = contact with { Z = 0 },
        };

        var nodes = new List<SupportNode> { tip, baseNode };
        var segments = new List<SupportSegment>();
        if (contact.Z > neckLength * 1.5f)
        {
            var junction = new SupportNode
            {
                Type = SupportNodeType.Junction,
                Position = contact with { Z = contact.Z - neckLength },
            };
            nodes.Add(junction);
            segments.Add(new SupportSegment
            {
                Type = SupportSegmentType.Neck, NodeA = tip.Id, NodeB = junction.Id, Diameter = neckDiameter,
            });
            segments.Add(new SupportSegment
            {
                Type = SupportSegmentType.Pillar, NodeA = junction.Id, NodeB = baseNode.Id, Diameter = pillarDiameter,
            });
        }
        else
        {
            segments.Add(new SupportSegment
            {
                Type = SupportSegmentType.Pillar, NodeA = tip.Id, NodeB = baseNode.Id, Diameter = pillarDiameter,
            });
        }

        Execute(new AddSupportElementsCommand(Supports, nodes, segments));
    }

    /// <summary>Hides the selected objects and deselects them. One undo step.</summary>
    public void HideSelection()
    {
        if (_selection.Count == 0) return;
        var commands = _selection
            .Select(o => (IDocumentCommand)new SetRenderStateCommand(o, RenderState.Hidden, $"Hide {o.Name}"))
            .ToList();
        ClearSelection();
        Execute(new CompositeCommand(commands.Count == 1 ? commands[0].Name : $"Hide {commands.Count} objects", commands));
    }

    /// <summary>Returns every hidden object to normal. One undo step.</summary>
    public void UnhideAll()
    {
        var commands = Scene.Objects
            .Where(o => o.RenderState == RenderState.Hidden)
            .Select(o => (IDocumentCommand)new SetRenderStateCommand(o, RenderState.Normal, $"Unhide {o.Name}"))
            .ToList();
        if (commands.Count == 0) return;
        Execute(new CompositeCommand(commands.Count == 1 ? commands[0].Name : "Unhide all", commands));
    }

    /// <summary>
    /// Rotates the object so the picked face (grown into its near-coplanar cluster) points straight
    /// down, then rests it on the plate. One undo step. Rotation pivots on the world-bounds centre.
    /// </summary>
    public void LayFlatOnFace(SceneObject obj, int seedTriangle)
    {
        var mesh = obj.Mesh;
        var before = obj.Transform;
        var cluster = Geometry.LayFlat.Cluster(mesh, seedTriangle);
        var normal = Geometry.LayFlat.ClusterWorldNormal(mesh, cluster, before.ToMatrix());
        var arc = Geometry.LayFlat.RotationToPlate(normal);

        var pivot = obj.WorldBounds.Center;
        var after = before with
        {
            Rotation = Rotations.Compose(before.Rotation, arc),
            Translation = Vector3.Transform(before.Translation - pivot, arc) + pivot,
        };

        // Exact drop: the axis-aligned WorldBounds of a rotated box overestimates, which would leave
        // the face floating above the plate, so scan the vertices under the new matrix.
        var m = after.ToMatrix();
        var minZ = float.PositiveInfinity;
        foreach (var p in mesh.Positions)
        {
            var z = Vector3.Transform(p, m).Z;
            if (z < minZ) minZ = z;
        }
        after = after with { Translation = after.Translation with { Z = after.Translation.Z - minZ } };

        if (after == before) return;
        Execute(new SetTransformCommand(obj, before, after, "Lay flat on face"));
    }
}
