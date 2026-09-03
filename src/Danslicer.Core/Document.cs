using System.Numerics;
using Danslicer.Core.Commands;
using Danslicer.Core.Config;
using Danslicer.Core.Geometry;
using Danslicer.Core.Supports;
using Danslicer.Core.Supports.Routing;
using Danslicer.Core.Supports.Generation;
using Danslicer.Core.Printers;
using Danslicer.Core.Scene;
using Danslicer.Core.Slicing;

namespace Danslicer.Core;

public sealed record SupportGenerationSummary(int CandidateCount, int GeneratedTipCount,
    int UnroutedTipCount);

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
    public PlacementMode PlacementMode { get; set; } = PlacementMode.AutoDrop;
    public float PlacementHeightMm { get; set; } = 5f;

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

    /// <summary>
    /// Selects the whole support containing the element: its connected component with bracing
    /// excluded, per the design's whole-support mode.
    /// </summary>
    public void SelectSupportComponent(Guid elementId, bool additive = false)
    {
        Guid seed;
        if (Supports.TryGetNode(elementId, out var node)) seed = node.Id;
        else if (Supports.TryGetSegment(elementId, out var segment)) seed = segment.NodeA;
        else return;

        var (nodes, segments) = Supports.Component(seed);
        if (!additive) _supportSelection.Clear();
        foreach (var id in nodes) _supportSelection.Add(id);
        foreach (var id in segments) _supportSelection.Add(id);
        SupportSelectionChanged?.Invoke();
    }

    public void ClearSupportSelection()
    {
        if (_supportSelection.Count == 0) return;
        _supportSelection.Clear();
        SupportSelectionChanged?.Invoke();
    }

    /// <summary>
    /// Deletes the selected support elements; a deleted node takes its segments. Fragments left
    /// with no tip or no base are useless residue (a pillar supporting nothing, a floating neck),
    /// so they are pruned in the same command. One undo step. Revisit the no-base rule when bare
    /// tips awaiting manual routing (DESIGN.md §8.8) become a feature.
    /// </summary>
    public void DeleteSupportSelection()
    {
        if (_supportSelection.Count == 0) return;
        var nodes = _supportSelection.Where(id => Supports.TryGetNode(id, out _)).ToHashSet();
        var segments = _supportSelection.Where(id => Supports.TryGetSegment(id, out _)).ToHashSet();
        _supportSelection.Clear();
        SupportSelectionChanged?.Invoke();
        if (nodes.Count == 0 && segments.Count == 0) return;
        AddOrphanedFragments(nodes, segments);
        Execute(new RemoveSupportElementsCommand(Supports, nodes, segments));
    }

    /// <summary>
    /// Extends a support removal with every remaining fragment (bracing-excluded component of
    /// what survives the removal) that would end up without a tip or without a base.
    /// </summary>
    private void AddOrphanedFragments(HashSet<Guid> nodes, HashSet<Guid> segments)
    {
        var removedSegments = new HashSet<Guid>(segments);
        foreach (var id in nodes)
            foreach (var attached in Supports.SegmentsAt(id))
                removedSegments.Add(attached.Id);

        var visited = new HashSet<Guid>();
        foreach (var start in Supports.Nodes)
        {
            if (nodes.Contains(start.Id) || !visited.Add(start.Id)) continue;
            var fragment = new List<SupportNode> { start };
            var queue = new Queue<Guid>();
            queue.Enqueue(start.Id);
            while (queue.Count > 0)
            {
                foreach (var segment in Supports.SegmentsAt(queue.Dequeue()))
                {
                    if (removedSegments.Contains(segment.Id) ||
                        segment.Type == SupportSegmentType.Bracing) continue;
                    // Walk to whichever end we have not seen; ends on removed nodes are cut.
                    foreach (var endId in new[] { segment.NodeA, segment.NodeB })
                    {
                        if (nodes.Contains(endId) || !visited.Add(endId)) continue;
                        fragment.Add(Supports.GetNode(endId));
                        queue.Enqueue(endId);
                    }
                }
            }
            if (fragment.Any(n => n.Type == SupportNodeType.Tip) &&
                fragment.Any(n => n.Type == SupportNodeType.Base)) continue;
            foreach (var orphan in fragment) nodes.Add(orphan.Id);
        }
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

    /// <summary>Re-seats an input transform according to the configured placement mode.</summary>
    public Transform ApplyPlacement(SceneObject obj, Transform requested)
    {
        if (PlacementMode == PlacementMode.Off || obj.Mesh.Positions.Length == 0) return requested;
        var targetZ = PlacementMode == PlacementMode.RaiseAbovePlate
            ? MathF.Max(0, float.IsFinite(PlacementHeightMm) ? PlacementHeightMm : 0)
            : 0;
        var matrix = requested.ToMatrix();
        var minZ = float.PositiveInfinity;
        foreach (var point in obj.Mesh.Positions)
            minZ = MathF.Min(minZ, Vector3.Transform(point, matrix).Z);
        return requested with
        {
            Translation = requested.Translation with
                { Z = requested.Translation.Z + targetZ - minZ },
        };
    }

    /// <summary>Commits a requested transform and its automatic placement as one undo step.</summary>
    public void CommitTransform(SceneObject obj, Transform before, Transform requested,
        string name = "Transform") => CommitTransforms(new[] { (obj, before, requested) }, name);

    /// <summary>Multi-object transform commit used by both keyboard and gizmo modal edits.</summary>
    public void CommitTransforms(IEnumerable<(SceneObject Object, Transform Before, Transform Requested)> items,
        string name = "Transform")
    {
        var commands = new List<IDocumentCommand>();
        foreach (var (obj, before, requested) in items)
        {
            var after = ApplyPlacement(obj, requested);
            obj.Transform = after;
            if (after != before) commands.Add(new SetTransformCommand(obj, before, after, name));
        }
        if (commands.Count > 0) Execute(new CompositeCommand(name, commands));
        else NotifyTransientChange();
    }

    /// <summary>
    /// Adds a simple manual support under a picked surface point: a tip at the contact, a tapered
    /// neck dropping vertically to a junction, and a pillar straight down to a base on the plate.
    /// Contacts too close to the plate get a single tip-to-base pillar. One undo step.
    /// </summary>
    private BvhCollisionScene? _meshObstacleCache;
    private string? _meshObstacleSignature;

    /// <summary>
    /// The collision scene over every scene object's world-space triangles, rebuilt only when an
    /// object is added, removed or transformed. The first query after a change pays the BVH
    /// build; repeated support placements between changes reuse it.
    /// </summary>
    private BvhCollisionScene MeshObstacles()
    {
        var signature = string.Join(";", Scene.Objects.Select(o => $"{o.Id}:{o.Transform.ToMatrix().GetHashCode()}"));
        if (_meshObstacleCache is null || signature != _meshObstacleSignature)
        {
            var scene = new BvhCollisionScene();
            foreach (var obj in Scene.Objects)
                scene.AddSceneObject(obj);
            _meshObstacleCache = scene;
            _meshObstacleSignature = signature;
        }
        return _meshObstacleCache;
    }

    /// <summary>
    /// Adds a manual support at a picked surface point. By default the support is routed by the
    /// top-down router against every object and existing support, descending within lean limits
    /// and detouring around obstacles (DESIGN.md §8.6); returns false when no clear path to the
    /// plate exists, adding nothing. With <paramref name="routeAroundModel"/> false (the override
    /// gesture) the old straight vertical tree is placed blindly. One undo step either way.
    /// </summary>
    public bool AddManualSupport(SceneObject obj, Vector3 contact, Vector3 surfaceNormal,
        bool routeAroundModel = true)
    {
        if (routeAroundModel) return TryAddRoutedSupport(obj, contact, surfaceNormal);
        AddStraightSupport(obj, contact, surfaceNormal);
        return true;
    }

    private bool TryAddRoutedSupport(SceneObject obj, Vector3 contact, Vector3 surfaceNormal)
    {
        var obstacles = new CompositeCollisionScene(MeshObstacles(), SupportObstacles());
        var router = new TopDownSupportRouter(obstacles, GrowthRuleSet.Default);
        var tip = new RoutingTip(contact, -surfaceNormal, 0.4f, obj.Id);
        // The seed also drives the router's deterministic ids; vary it per placement or two
        // supports in one document would collide on identical Guid sequences.
        var options = new TopDownRoutingOptions
        {
            Seed = HashCode.Combine(contact.X, contact.Y, contact.Z, Supports.NodeCount),
        };
        var result = router.Route(new[] { tip }, options);
        if (result.UnroutedTips.Count > 0) return false;

        // A straight descent emits a junction per step; collapse to the minimal shape.
        SupportGraphSimplifier.CollapseCollinearJunctions(result.Graph);
        Execute(new AddSupportElementsCommand(Supports,
            result.Graph.Nodes.ToList(), result.Graph.Segments.ToList()));
        return true;
    }

    private LinearCollisionScene SupportObstacles()
    {
        var scene = new LinearCollisionScene();
        scene.AddSupportGraph(Supports);
        return scene;
    }

    /// <summary>
    /// Generates supports over every face of one object. All models and existing supports are
    /// collision obstacles, and the generated elements are committed as one undoable command.
    /// </summary>
    public SupportGenerationSummary GenerateSupports(SceneObject obj, int seed = 0)
    {
        var matrix = obj.Transform.ToMatrix();
        var worldMesh = new Mesh(obj.Mesh.Positions.Select(p => Vector3.Transform(p, matrix)).ToArray(),
            (int[])obj.Mesh.Indices.Clone());
        var region = Enumerable.Range(0, worldMesh.TriangleCount).ToHashSet();
        var origin = new SupportOrigin(obj.Id, 1);
        var obstacles = new CompositeCollisionScene(MeshObstacles(), SupportObstacles());
        var generated = SupportGenerator.Generate(worldMesh, region, TipPlacementParameters.Default,
            new GridRoutingOptions { Seed = seed, Origin = origin }, GrowthRuleSet.Default,
            obstacles, Supports, seed: seed);

        // Routing ids are deterministic from the seed. Fresh graph ids allow repeated generation
        // with seed zero while preserving deterministic placement and routing geometry.
        var idMap = generated.Routing.Graph.Nodes.ToDictionary(node => node.Id, _ => Guid.NewGuid());
        var nodes = generated.Routing.Graph.Nodes.Select(node => new SupportNode
        {
            Id = idMap[node.Id], Type = node.Type, Position = node.Position, Origin = origin,
            Pinned = node.Pinned, Hidden = node.Hidden, Disabled = node.Disabled,
            SurfaceNormal = node.SurfaceNormal, TipDiameter = node.TipDiameter,
            PenetrationDepth = node.PenetrationDepth, ContactObjectId = node.ContactObjectId,
        }).ToList();
        var segments = generated.Routing.Graph.Segments.Select(segment => new SupportSegment
        {
            Id = Guid.NewGuid(), Type = segment.Type, NodeA = idMap[segment.NodeA],
            NodeB = idMap[segment.NodeB], Diameter = segment.Diameter, Origin = origin,
            Pinned = segment.Pinned, Hidden = segment.Hidden, Disabled = segment.Disabled,
        }).ToList();
        if (nodes.Count > 0)
            Execute(new AddSupportElementsCommand(Supports, nodes, segments, "Generate supports"));

        return new SupportGenerationSummary(generated.Candidates.Count,
            nodes.Count(node => node.Type == SupportNodeType.Tip), generated.Routing.UnroutedTips.Count);
    }

    private void AddStraightSupport(SceneObject obj, Vector3 contact, Vector3 surfaceNormal)
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

    /// <summary>Hides every unselected support element when support elements are selected.</summary>
    public void HideUnselectedSupportElements()
    {
        if (_supportSelection.Count == 0) return;
        var entries = new List<SetSupportHiddenCommand.Entry>();
        foreach (var node in Supports.Nodes.Where(node => !_supportSelection.Contains(node.Id) && !node.Hidden))
            entries.Add(new SetSupportHiddenCommand.Entry(value => node.Hidden = value, node.Hidden, true));
        foreach (var segment in Supports.Segments.Where(segment => !_supportSelection.Contains(segment.Id) && !segment.Hidden))
            entries.Add(new SetSupportHiddenCommand.Entry(value => segment.Hidden = value, segment.Hidden, true));
        if (entries.Count > 0)
            Execute(new SetSupportHiddenCommand(Supports, entries, "Hide unselected supports"));
    }

    /// <summary>Returns every hidden object to normal. One undo step.</summary>
    public void UnhideAll()
    {
        var commands = Scene.Objects
            .Where(o => o.RenderState == RenderState.Hidden)
            .Select(o => (IDocumentCommand)new SetRenderStateCommand(o, RenderState.Normal, $"Unhide {o.Name}"))
            .ToList();
        var supportEntries = new List<SetSupportHiddenCommand.Entry>();
        foreach (var node in Supports.Nodes.Where(node => node.Hidden))
            supportEntries.Add(new SetSupportHiddenCommand.Entry(value => node.Hidden = value, true, false));
        foreach (var segment in Supports.Segments.Where(segment => segment.Hidden))
            supportEntries.Add(new SetSupportHiddenCommand.Entry(value => segment.Hidden = value, true, false));
        if (supportEntries.Count > 0)
            commands.Add(new SetSupportHiddenCommand(Supports, supportEntries, "Unhide supports"));
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
