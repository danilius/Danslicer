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
using System.Runtime.CompilerServices;

namespace Danslicer.Core;

public sealed record SupportGenerationSummary(int CandidateCount, int GeneratedTipCount,
    int UnroutedTipCount)
{
    /// <summary>Honest routing refusal buckets for diagnostics and live preview feedback.</summary>
    public IReadOnlyDictionary<RoutingFailureReason, int> RefusalReasons { get; init; } =
        new Dictionary<RoutingFailureReason, int>();
}
public readonly record struct SupportPositionSnapshot(Vector3 Position, Vector3 SurfaceNormal);

public sealed record SceneMeshSnapshot(Mesh Mesh, Matrix4x4 Transform);
public sealed record SupportGenerationRequest(Guid ObjectId, SceneMeshSnapshot Target,
    IReadOnlyList<SceneMeshSnapshot> SceneMeshes, SupportGraph ExistingSupports, int Seed,
    SupportConfig Settings, SupportGenerationScope Scope = SupportGenerationScope.Full)
{
    /// <summary>The object's painted region, captured with the rest of the mutable state so a
    /// background generation is not affected by painting that happens while it runs. Empty means
    /// every face, which is what an unpainted object has always done.</summary>
    public ObjectSupportRegions Regions { get; init; } = ObjectSupportRegions.Empty;
}

public sealed record IslandDetectionRequest(SceneMeshSnapshot Target,
    SupportGraph ExistingSupports, float LayerHeightMm, SupportConfig Settings);

public enum ObjectMirrorAxis
{
    X,
    Y,
    Z,
}

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
    public ResinPreset ResinPreset { get; set; } = ResinPreset.Default;
    public ResinSettings ResinSettings { get; set; } = ResinSettings.Default;
    public PlacementMode PlacementMode { get; set; } = PlacementMode.AutoDrop;
    public float PlacementHeightMm { get; set; }
    public SupportConfig SupportSettings { get; set; } = new();

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

    /// <summary>
    /// Replaces the persisted contents while retaining this document instance and its UI event
    /// subscriptions. Selection and undo history are intentionally fresh after an open.
    /// User-level support-generation preferences are not project state and remain unchanged.
    /// </summary>
    public void ReplaceWith(Document source)
    {
        ArgumentNullException.ThrowIfNull(source);
        _selection.Clear();
        _supportSelection.Clear();
        SelectionChanged?.Invoke();
        SupportSelectionChanged?.Invoke();

        PrintSettings = source.PrintSettings;
        Printer = source.Printer;
        ResinPreset = source.ResinPreset;
        ResinSettings = source.ResinSettings;
        Scene.ReplaceWith(source.Scene.Objects.Select(obj => new SceneObject(obj.Name, obj.Mesh, obj.Id)
        {
            Transform = obj.Transform,
            RenderState = obj.RenderState,
            Regions = obj.Regions,
        }));
        Supports.ReplaceWith(source.Supports.Nodes.Select(node => node.Clone()),
            source.Supports.Segments.Select(segment => segment.Clone()));
        _meshObstacleCache = null;
        _meshObstacleSignature = null;
        History.Clear();
    }

    /// <summary>Raise Changed for transient edits (e.g. live drag) that bypass the command stack.</summary>
    public void NotifyTransientChange() => Changed?.Invoke();

    /// <summary>Applies a named resin snapshot without touching per-print raster choices.</summary>
    public void ApplyResinPreset(ResinPreset preset)
    {
        ArgumentNullException.ThrowIfNull(preset);
        ResinPreset = preset.Normalize();
        ResinSettings = ResinPreset.Settings with { };
        NotifyTransientChange();
    }

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

    /// <summary>
    /// The model support work acts on: the single selected object, or null when the selection
    /// is empty or holds more than one. Support mode keeps exactly one object selected, so this
    /// is that object; in Layout it is simply "the one selected model", which is the same thing
    /// the user would mean. See <see cref="SupportTargetPolicy"/> for what the target governs.
    /// </summary>
    public SceneObject? SupportTarget => _selection.Count == 1 ? _selection.First() : null;

    public bool IsSupportSelected(Guid id) => _supportSelection.Contains(id);

    public void SelectSupportElement(Guid id, bool additive = false)
    {
        var selectable = IsSupportElementVisible(id);
        if (additive)
        {
            if (!_supportSelection.Remove(id) && selectable) _supportSelection.Add(id);
        }
        else
        {
            _supportSelection.Clear();
            if (selectable) _supportSelection.Add(id);
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
        foreach (var id in nodes)
            if (IsSupportElementVisible(id)) _supportSelection.Add(id);
        foreach (var id in segments)
            if (IsSupportElementVisible(id)) _supportSelection.Add(id);
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

        // A tip attached to a shared trunk owns a short branch twig. Removing only that tip must
        // not leave a junction-and-branch dead end on the surviving tree. Peel non-terminal leaf
        // junctions until the remaining topology is load-bearing again; bases and tips are the
        // intentional terminals.
        while (true)
        {
            var deadEnds = Supports.Nodes.Where(node =>
            {
                if (nodes.Contains(node.Id) || node.Type != SupportNodeType.Junction) return false;
                var liveIncident = Supports.SegmentsAt(node.Id).Count(segment =>
                    segment.Type != SupportSegmentType.Bracing &&
                    !removedSegments.Contains(segment.Id) &&
                    !nodes.Contains(segment.NodeA) && !nodes.Contains(segment.NodeB));
                return liveIncident <= 1;
            }).ToList();
            if (deadEnds.Count == 0) break;
            foreach (var deadEnd in deadEnds)
            {
                nodes.Add(deadEnd.Id);
                foreach (var attached in Supports.SegmentsAt(deadEnd.Id))
                    removedSegments.Add(attached.Id);
            }
        }

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
        var name = commands.Count == 1 ? commands[0].Name : $"Delete {commands.Count} objects";

        // Supports are object-owned, so a deleted model must take its supports with it — otherwise
        // they are left standing in mid-air, owned by an object that no longer exists. Both go into
        // one composite so a single undo brings the model and its supports back together.
        var orphaned = new HashSet<Guid>();
        foreach (var obj in _selection) orphaned.UnionWith(AssociatedSupportNodeIds(obj));
        if (orphaned.Count > 0) commands.Add(DiscardSupportsCommand(orphaned, name));

        Execute(new CompositeCommand(name, commands));
    }

    /// <summary>
    /// Replaces an object's painted support region as one undo step (DESIGN 8.3 stage 1). The
    /// selection tools of stages 2 and 3 are expected to compute a whole new face set and hand
    /// it here, so a brush stroke or a grow is one undo, not one per face.
    /// </summary>
    public void SetSupportRegions(SceneObject obj, ObjectSupportRegions regions,
        string name = "Edit support region")
    {
        ArgumentNullException.ThrowIfNull(obj);
        ArgumentNullException.ThrowIfNull(regions);
        if (regions.Equals(obj.Regions)) return;
        Execute(new SetSupportRegionsCommand(obj, regions, name));
    }

    /// <summary>Clears an object's region, returning it to "every face" — today's behaviour.</summary>
    public void ClearSupportRegions(SceneObject obj) =>
        SetSupportRegions(obj, ObjectSupportRegions.Empty, "Clear support region");

    /// <summary>
    /// The support elements of <paramref name="original"/>, copied onto <paramref name="copy"/>
    /// and shifted by the offset between them, as one command to fold into the duplicate's undo
    /// step. Null when the original has no supports.
    ///
    /// <para>Only segments with BOTH ends owned by the original are copied. A support that shares
    /// a trunk with another model is half-owned by a model that was not duplicated, and there is
    /// no honest place to put the other half — so that fragment is left behind rather than
    /// invented.</para>
    /// </summary>
    private IDocumentCommand? CopySupportsForDuplicate(SceneObject original, SceneObject copy)
    {
        var offset = copy.Transform.Translation - original.Transform.Translation;
        var owned = Supports.Nodes.Where(node => node.Origin.ObjectId == original.Id).ToList();
        if (owned.Count == 0) return null;

        var newIds = owned.ToDictionary(node => node.Id, _ => Guid.NewGuid());
        var nodes = owned.Select(node =>
        {
            var clone = node.Clone(newIds[node.Id], node.Origin with { ObjectId = copy.Id });
            clone.Position = node.Position + offset;
            if (clone.ContactObjectId == original.Id) clone.ContactObjectId = copy.Id;
            return clone;
        }).ToList();

        var segments = Supports.Segments
            .Where(segment => newIds.ContainsKey(segment.NodeA) && newIds.ContainsKey(segment.NodeB))
            .Select(segment => segment.Clone(Guid.NewGuid(), newIds[segment.NodeA],
                newIds[segment.NodeB], segment.Origin with { ObjectId = copy.Id }))
            .ToList();

        return new ApplySupportGraphEditCommand(Supports,
            new SupportGraphEdit(nodes, segments, []), $"Duplicate {original.Name}");
    }

    /// <summary>Ids of every support node owned by <paramref name="obj"/>. Segments are not listed:
    /// <see cref="RemoveSupportElementsCommand"/> pulls in each node's attached segments itself.</summary>
    private IEnumerable<Guid> AssociatedSupportNodeIds(SceneObject obj) => Supports.Nodes
        .Where(node => node.Origin.ObjectId == obj.Id)
        .Select(node => node.Id);

    /// <summary>
    /// Removal command for a set of owned support nodes, extended with any fragment the removal
    /// would strand — the same orphan sweep a manual support deletion does, so discarding one
    /// object's supports cannot leave a tipless twig hanging off a trunk shared with another
    /// object. Clears the ids from the support selection first: selection is transient and must
    /// not keep pointing at elements that are about to leave the graph.
    /// </summary>
    private IDocumentCommand DiscardSupportsCommand(HashSet<Guid> nodeIds, string name)
    {
        var segments = new HashSet<Guid>();

        // AddOrphanedFragments sweeps the WHOLE graph and takes any fragment that lacks a tip or
        // a base. That is right for a manual support deletion, but here it would also collect a
        // different object's half-built supports, which this removal has nothing to do with. So
        // run the sweep and then keep only the extras that are actually connected to what we are
        // removing — the shared-trunk case the sweep exists for.
        var swept = new HashSet<Guid>(nodeIds);
        AddOrphanedFragments(swept, segments);
        var extras = swept.Except(nodeIds).ToHashSet();
        if (extras.Count > 0)
        {
            var connected = ConnectedToAny(nodeIds);
            nodeIds.UnionWith(extras.Where(connected.Contains));
        }

        if (_supportSelection.RemoveWhere(id => nodeIds.Contains(id) || segments.Contains(id)) > 0)
            SupportSelectionChanged?.Invoke();
        return new RemoveSupportElementsCommand(Supports, nodeIds, segments, name);
    }

    /// <summary>Every node reachable from <paramref name="seeds"/> across segments, seeds excluded.
    /// Used to tell "this fragment was stranded by the removal" from "this fragment was already
    /// standing on its own somewhere else in the graph".</summary>
    private HashSet<Guid> ConnectedToAny(IReadOnlySet<Guid> seeds)
    {
        var reached = new HashSet<Guid>();
        var queue = new Queue<Guid>(seeds);
        var visited = new HashSet<Guid>(seeds);
        while (queue.Count > 0)
            foreach (var segment in Supports.SegmentsAt(queue.Dequeue()))
                foreach (var endId in new[] { segment.NodeA, segment.NodeB })
                {
                    if (!visited.Add(endId)) continue;
                    reached.Add(endId);
                    queue.Enqueue(endId);
                }
        return reached;
    }

    /// <summary>
    /// Copies the selected objects as mesh-sharing instances, offset together in XY and selected
    /// as the new selection. Object selection is transient; the scene additions are one undo step.
    /// </summary>
    public IReadOnlyList<SceneObject> DuplicateSelection()
    {
        var originals = Scene.Objects.Where(_selection.Contains).ToList();
        if (originals.Count == 0) return [];

        var copies = new List<SceneObject>(originals.Count);
        var commands = new List<IDocumentCommand>(originals.Count);
        var usedNames = Scene.Objects.Select(obj => obj.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var offset = new Vector3(5f, 5f, 0f);
        foreach (var original in originals)
        {
            var copy = new SceneObject(NextCopyName(original.Name, usedNames), original.Mesh)
            {
                RenderState = original.RenderState,
                // The painted region indexes faces of the mesh, which the copy shares, so it
                // stays meaningful. Regions are immutable, so the instance can be shared.
                Regions = original.Regions,
            };
            var requested = original.Transform with
            {
                Translation = original.Transform.Translation + offset,
            };
            copy.Transform = ApplyPlacement(copy.Mesh, requested);
            copies.Add(copy);
            commands.Add(new AddObjectCommand(Scene, copy));
            // Duplicating a supported model duplicates its supports: the copy is the same model
            // in the same orientation, just moved, so its supports are valid by the same argument
            // that lets a translation keep them (see SupportTransformRule).
            var supportCopy = CopySupportsForDuplicate(original, copy);
            if (supportCopy is not null) commands.Add(supportCopy);
        }

        var name = copies.Count == 1 ? $"Duplicate {originals[0].Name}" : $"Duplicate {copies.Count} objects";
        Execute(new CompositeCommand(name, commands));
        _selection.Clear();
        foreach (var copy in copies) _selection.Add(copy);
        SelectionChanged?.Invoke();
        return copies;
    }

    /// <summary>
    /// Mirrors the selected objects around the selection's world-space bounding-box centre.
    /// Reflection is baked into a new mesh with reversed winding, leaving each object's editable
    /// transform intact except for the normal automatic placement adjustment.
    /// </summary>
    public void MirrorSelection(ObjectMirrorAxis axis)
    {
        var objects = Scene.Objects.Where(_selection.Contains).ToList();
        if (objects.Count == 0) return;

        var bounds = objects.Aggregate(Aabb.Empty, (current, obj) => current.Union(obj.WorldBounds));
        if (bounds.IsEmpty) return;

        var centre = bounds.Center;
        var scale = axis switch
        {
            ObjectMirrorAxis.X => new Vector3(-1f, 1f, 1f),
            ObjectMirrorAxis.Y => new Vector3(1f, -1f, 1f),
            _ => new Vector3(1f, 1f, -1f),
        };
        var worldReflection = Matrix4x4.CreateTranslation(-centre) *
                              Matrix4x4.CreateScale(scale) *
                              Matrix4x4.CreateTranslation(centre);
        var supportBefore = CaptureAssociatedSupportPositions(objects);
        var commands = new List<IDocumentCommand>(objects.Count + 1);
        var supportEntries = new List<SetSupportPositionsCommand.Entry>();
        var name = objects.Count == 1 ? $"Mirror {axis}" : $"Mirror {objects.Count} objects {axis}";

        foreach (var obj in objects)
        {
            var beforeTransform = obj.Transform;
            var beforeMatrix = beforeTransform.ToMatrix();
            Mesh mirroredMesh;
            Transform requested;
            if (Matrix4x4.Invert(beforeMatrix, out var inverseBefore))
            {
                // Bake world reflection into local geometry: p * (M H M^-1) * M = p * M H.
                mirroredMesh = obj.Mesh.Reflected(beforeMatrix * worldReflection * inverseBefore);
                requested = beforeTransform;
            }
            else
            {
                // A zero scale is not invertible. Bake directly to world space and retain the same
                // visible result under an identity transform.
                mirroredMesh = obj.Mesh.Reflected(beforeMatrix * worldReflection);
                requested = Transform.Identity;
            }

            var afterTransform = ApplyPlacement(mirroredMesh, requested);
            var placementDelta = Matrix4x4.CreateTranslation(
                afterTransform.Translation - requested.Translation);
            AppendAssociatedSupportMatrix(obj, worldReflection * placementDelta,
                supportBefore, supportEntries);
            commands.Add(new SetMeshTransformCommand(obj, obj.Mesh, beforeTransform,
                mirroredMesh, afterTransform, name));
        }

        if (supportEntries.Count > 0)
            commands.Add(new SetSupportPositionsCommand(Supports, supportEntries, name));
        Execute(new CompositeCommand(name, commands));
    }

    private static string NextCopyName(string source, HashSet<string> used)
    {
        var root = $"{source} copy";
        var candidate = root;
        for (var suffix = 2; !used.Add(candidate); suffix++) candidate = $"{root} {suffix}";
        return candidate;
    }

    /// <summary>Moves each selected object so its lowest point sits on the plate.</summary>
    public void DropSelectionToPlate()
    {
        var transforms = new List<(SceneObject Object, Transform Before, Transform Requested)>();
        foreach (var o in _selection)
        {
            var minZ = o.WorldBounds.Min.Z;
            if (MathF.Abs(minZ) < 1e-6f) continue;
            var before = o.Transform;
            var after = before with { Translation = before.Translation with { Z = before.Translation.Z - minZ } };
            transforms.Add((o, before, after));
        }
        if (transforms.Count > 0) CommitTransforms(transforms, "Drop to plate", applyPlacement: false);
    }

    /// <summary>Re-seats an input transform according to the configured placement mode.</summary>
    public Transform ApplyPlacement(SceneObject obj, Transform requested) =>
        ApplyPlacement(obj.Mesh, requested);

    private Transform ApplyPlacement(Mesh mesh, Transform requested)
    {
        if (PlacementMode == PlacementMode.Off || mesh.Positions.Length == 0) return requested;
        var targetZ = PlacementMode == PlacementMode.RaiseAbovePlate
            ? MathF.Max(0, float.IsFinite(PlacementHeightMm) ? PlacementHeightMm : 0)
            : 0;
        var matrix = requested.ToMatrix();
        var minZ = float.PositiveInfinity;
        foreach (var point in mesh.Positions)
            minZ = MathF.Min(minZ, Vector3.Transform(point, matrix).Z);
        return requested with
        {
            Translation = requested.Translation with
                { Z = requested.Translation.Z + targetZ - minZ },
        };
    }

    /// <summary>Commits a requested transform and its automatic placement as one undo step.</summary>
    public void CommitTransform(SceneObject obj, Transform before, Transform requested,
        string name = "Transform", bool applyPlacement = true) =>
        CommitTransforms(new[] { (obj, before, requested) }, name, applyPlacement: applyPlacement);

    /// <summary>Multi-object transform commit used by both keyboard and gizmo modal edits.</summary>
    public void CommitTransforms(IEnumerable<(SceneObject Object, Transform Before, Transform Requested)> items,
        string name = "Transform", IReadOnlyDictionary<Guid, SupportPositionSnapshot>? supportBefore = null,
        bool applyPlacement = true)
    {
        var itemList = items.ToList();
        supportBefore ??= CaptureAssociatedSupportPositions(itemList.Select(item => item.Object));
        var commands = new List<IDocumentCommand>();
        var supportEntries = new List<SetSupportPositionsCommand.Entry>();
        var discardedSupportNodes = new HashSet<Guid>();
        foreach (var (obj, before, requested) in itemList)
        {
            var after = applyPlacement ? ApplyPlacement(obj, requested) : requested;
            obj.Transform = after;
            if (after == before) continue;
            commands.Add(new SetTransformCommand(obj, before, after, name));
            // See SupportTransformRule: a transform keeps this object's supports only if it maps
            // every contact exactly. Only objects that actually moved are considered, so a
            // multi-object selection never discards supports on an object that stayed put.
            if (SupportTransformRule.MapsContactsExactly(before, after))
                AppendAssociatedSupportTransform(obj, before, after, supportBefore, supportEntries);
            else
                discardedSupportNodes.UnionWith(AssociatedSupportNodeIds(obj));
        }
        if (supportEntries.Count > 0)
            commands.Add(new SetSupportPositionsCommand(Supports, supportEntries, name));
        if (discardedSupportNodes.Count > 0)
            commands.Add(DiscardSupportsCommand(discardedSupportNodes, name));
        if (commands.Count > 0) Execute(new CompositeCommand(name, commands));
        else NotifyTransientChange();
    }

    public IReadOnlyDictionary<Guid, SupportPositionSnapshot> CaptureAssociatedSupportPositions(
        IEnumerable<SceneObject> objects)
    {
        var objectIds = objects.Select(obj => obj.Id).ToHashSet();
        return Supports.Nodes
            .Where(node => node.Origin.ObjectId is { } id && objectIds.Contains(id))
            .ToDictionary(node => node.Id,
                node => new SupportPositionSnapshot(node.Position, node.SurfaceNormal));
    }

    /// <summary>Moves owned supports during a live modal preview from the immutable start state.</summary>
    public void ApplyAssociatedSupportTransformsTransient(
        IEnumerable<(SceneObject Object, Transform Before)> items,
        IReadOnlyDictionary<Guid, SupportPositionSnapshot> supportBefore)
    {
        var entries = new List<SetSupportPositionsCommand.Entry>();
        foreach (var (obj, before) in items)
            AppendAssociatedSupportTransform(obj, before, obj.Transform, supportBefore, entries);
        if (entries.Count > 0) Supports.NotifyChanged();
    }

    public void RestoreSupportPositions(IReadOnlyDictionary<Guid, SupportPositionSnapshot> snapshots)
    {
        foreach (var (id, snapshot) in snapshots)
        {
            if (!Supports.TryGetNode(id, out var node)) continue;
            node.Position = snapshot.Position;
            node.SurfaceNormal = snapshot.SurfaceNormal;
        }
        if (snapshots.Count > 0) Supports.NotifyChanged();
    }

    private void AppendAssociatedSupportTransform(SceneObject obj, Transform before, Transform after,
        IReadOnlyDictionary<Guid, SupportPositionSnapshot> supportBefore,
        List<SetSupportPositionsCommand.Entry> entries)
    {
        if (!Matrix4x4.Invert(before.ToMatrix(), out var oldWorldToLocal)) return;
        var worldDelta = oldWorldToLocal * after.ToMatrix();
        AppendAssociatedSupportMatrix(obj, worldDelta, supportBefore, entries);
    }

    private void AppendAssociatedSupportMatrix(SceneObject obj, Matrix4x4 worldDelta,
        IReadOnlyDictionary<Guid, SupportPositionSnapshot> supportBefore,
        List<SetSupportPositionsCommand.Entry> entries)
    {
        var hasNormalTransform = Matrix4x4.Invert(worldDelta, out var inverseDelta);
        var normalTransform = hasNormalTransform ? Matrix4x4.Transpose(inverseDelta) : Matrix4x4.Identity;
        foreach (var node in Supports.Nodes)
        {
            if (node.Origin.ObjectId != obj.Id ||
                !supportBefore.TryGetValue(node.Id, out var snapshot)) continue;
            var position = Vector3.Transform(snapshot.Position, worldDelta);
            // A zero target scale makes the normal transform undefined, but node positions still
            // have a well-defined result and must continue to follow the object.
            var normal = hasNormalTransform
                ? Vector3.TransformNormal(snapshot.SurfaceNormal, normalTransform)
                : snapshot.SurfaceNormal;
            if (normal.LengthSquared() > 1e-12f) normal = Vector3.Normalize(normal);
            node.Position = position;
            node.SurfaceNormal = normal;
            entries.Add(new SetSupportPositionsCommand.Entry(node, snapshot.Position,
                snapshot.SurfaceNormal, position, normal));
        }
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
        var signature = string.Join(";", Scene.Objects.Select(o =>
            $"{o.Id}:{RuntimeHelpers.GetHashCode(o.Mesh)}:{o.Transform.ToMatrix().GetHashCode()}"));
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

    /// <summary>Raycasts the cached world-space mesh BVH, optionally restricting object ids.</summary>
    public ObstacleRayHit? RaycastMeshes(Vector3 origin, Vector3 direction, float maxDistance,
        IReadOnlySet<Guid>? includedObjectIds = null) =>
        MeshObstacles().Raycast(origin, direction, maxDistance,
            includedObjectIds is null ? null : tag => tag is Guid id && includedObjectIds.Contains(id));

    /// <summary>
    /// Adds a manual support at a picked surface point, routed by the tree router into the spec
    /// anatomy (cone tip, optional branch, vertical trunk, disc base) against every object and,
    /// unless independent-manual mode is enabled, existing supports; returns false when no clear
    /// path to the plate exists, adding nothing.
    /// One undo step. (The old Shift+T blind straight drop was removed 2026-09-03 at the user's
    /// request — it had no practical use.)
    /// </summary>
    public bool AddManualSupport(SceneObject obj, Vector3 contact, Vector3 surfaceNormal)
        => AddManualSupport(obj, contact, surfaceNormal, out _);

    public bool AddManualSupport(SceneObject obj, Vector3 contact, Vector3 surfaceNormal,
        out RoutingFailureReason? failureReason)
    {
        failureReason = null;
        // A click on a model that is not the support target is refused before any routing work:
        // this is not a routing failure, so it carries no routing reason. The caller turns it
        // into the status line from SupportTargetPolicy.
        if (!SupportTargetPolicy.CanSupport(SupportTarget, obj)) return false;
        var settings = SupportSettings with { };
        var independent = settings.IndependentManualSupports;
        ICollisionScene obstacles = independent
            ? MeshObstacles()
            : new CompositeCollisionScene(MeshObstacles(), SupportObstacles());
        var rules = GrowthRuleSet.FromConfig(settings);
        var router = new TreeSupportRouter(obstacles, rules);
        var tip = new RoutingTip(contact, -surfaceNormal, settings.TipDiameter, obj.Id,
            TipShape: SupportTipShape.Cone, ConeLength: settings.ConeLength,
            BallDiameter: settings.BallDiameter, PenetrationDepth: settings.PenetrationDepth,
            TipNormalLeadIn: settings.TipNormalLeadInMm);
        // The seed also drives the router's deterministic ids; vary it per placement or two
        // supports in one document would collide on identical Guid sequences.
        var options = new TreeRoutingOptions
        {
            TrunkDiameter = settings.TrunkDiameter,
            BranchDiameter = settings.BranchDiameter,
            MaxMemberAngleDegrees = settings.MemberAngleDegrees,
            TipMemberLength = settings.TipMemberLength,
            MaxBranchLength = settings.MaxBranchLength,
            PreferExistingTrunks = !independent && settings.PreferExistingTrunks,
            ExistingTrunkBranchRange = settings.ExistingTrunkBranchRange,
            IgnoreExistingSupports = independent,
            MinMemberSeparationMm = independent ? 0 : settings.MinMemberSeparationMm,
            MiniSupportDiameter = settings.MiniSupportDiameter,
            MiniSupportTipDiameter = settings.MiniSupportTipDiameter,
            MiniSupportConeLength = settings.MiniSupportConeLength,
            MiniSupportMaxLength = settings.MiniSupportMaxLength,
            MiniSupportMaxAngleDegrees = settings.MiniSupportMaxAngleDegrees,
            MiniSupportMaxFanPerBranchEnd = settings.MiniSupportMaxFanPerBranchEnd,
            FineFeatureMinisFallBackToRegular = settings.FineFeatureMinisFallBackToRegular,
            RefusedTipsFallBackToMini = settings.RefusedTipsFallBackToMini,
            UseBaseGrid = settings.UseBaseGrid,
            BaseGridPitch = settings.BaseGridPitch,
            BaseShape = settings.BaseShape,
            BaseDiameter = settings.BaseDiameter,
            BaseHeight = settings.BaseHeight,
            BaseConeHeight = settings.BaseConeHeight,
            Seed = HashCode.Combine(contact.X, contact.Y, contact.Z, Supports.NodeCount),
            Origin = SupportOrigin.ManualFor(obj.Id),
        };
        var result = router.Route(new[] { tip }, options, Supports);
        if (result.UnroutedTips.Count > 0)
        {
            failureReason = result.Failures.Single().Reason;
            return false;
        }

        failureReason = null;
        Execute(new ApplySupportGraphEditCommand(Supports, result.Edit));
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
        var prepared = ComputeSupportGeneration(CaptureSupportGeneration(obj, seed));
        var batch = new SupportGenerationBatch(Supports, History, prepared, int.MaxValue);
        batch.CommitNextBatch();
        batch.Complete();
        return prepared.Summary;
    }

    public void SelectAllSupportElements()
    {
        _supportSelection.Clear();
        foreach (var node in Supports.Nodes)
            if (!node.Hidden) _supportSelection.Add(node.Id);
        foreach (var segment in Supports.Segments)
            if (!segment.Hidden && !Supports.GetNode(segment.NodeA).Hidden &&
                !Supports.GetNode(segment.NodeB).Hidden) _supportSelection.Add(segment.Id);
        SupportSelectionChanged?.Invoke();
    }

    public void SelectSupportElements(IEnumerable<Guid> ids, bool additive = false)
    {
        if (!additive) _supportSelection.Clear();
        foreach (var id in ids)
            if (IsSupportElementVisible(id)) _supportSelection.Add(id);
        SupportSelectionChanged?.Invoke();
    }

    private bool IsSupportElementVisible(Guid id)
    {
        if (Supports.TryGetNode(id, out var node)) return !node.Hidden;
        if (!Supports.TryGetSegment(id, out var segment) || segment.Hidden) return false;
        return !Supports.GetNode(segment.NodeA).Hidden && !Supports.GetNode(segment.NodeB).Hidden;
    }

    /// <summary>Captures the mutable document state needed by background generation.</summary>
    public SupportGenerationRequest CaptureSupportGeneration(SceneObject obj, int seed = 0,
        SupportGenerationScope scope = SupportGenerationScope.Full)
    {
        var scene = Scene.Objects.Select(o => new SceneMeshSnapshot(o.Mesh, o.Transform.ToMatrix())).ToList();
        return new SupportGenerationRequest(obj.Id,
            new SceneMeshSnapshot(obj.Mesh, obj.Transform.ToMatrix()), scene, CloneGraph(Supports), seed,
            SupportSettings with { }, scope)
        {
            Regions = obj.Regions,
        };
    }

    public IslandDetectionRequest CaptureIslandDetection(SceneObject obj) => new(
        new SceneMeshSnapshot(obj.Mesh, obj.Transform.ToMatrix()), CloneGraph(Supports),
        PrintSettings.LayerHeight, SupportSettings with { });

    public static IReadOnlyList<DetectedIsland> ComputeIslandDetection(
        IslandDetectionRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = IslandDetection.FindUnsupported(TransformMesh(request.Target),
            request.ExistingSupports, request.LayerHeightMm,
            request.Settings.MinIslandAreaMm2, plateZ: 0,
            request.Settings.OverhangAngleDegrees);
        cancellationToken.ThrowIfCancellationRequested();
        return result;
    }

    /// <summary>Runs generation using only a captured snapshot; safe to call off the UI thread.</summary>
    public static PreparedSupportGeneration ComputeSupportGeneration(SupportGenerationRequest request,
        CancellationToken cancellationToken = default,
        IProgress<SupportGenerationProgress>? progress = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var worldMesh = TransformMesh(request.Target);
        // No painted region is the same set this line has always produced — every face — so an
        // unpainted object generates bit-identically to before regions existed. Keep-clean is
        // subtracted here AND passed to the tip placer, which also enforces its distance rule.
        var region = request.Regions.EffectiveFaces(worldMesh.TriangleCount);
        var origin = new SupportOrigin(request.ObjectId, 1, request.ObjectId);
        var meshes = new BvhCollisionScene();
        foreach (var snapshot in request.SceneMeshes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            meshes.AddMesh(snapshot.Mesh, snapshot.Transform);
        }
        var supportObstacles = new LinearCollisionScene();
        supportObstacles.AddSupportGraph(request.ExistingSupports);
        var obstacles = new CompositeCollisionScene(meshes, supportObstacles);
        var rules = GrowthRuleSet.FromConfig(request.Settings);
        // Spec-shaped generation: cone tips on trunk/branch trees with disc bases. The capsule
        // grid/top-down paths remain available through the CLI for comparison.
        var generated = SupportGenerator.GenerateTree(worldMesh, region,
            TipPlacementParameters.Default with
            {
                TipDiameterMm = request.Settings.TipDiameter,
                TipShape = SupportTipShape.Cone,
                ConeLengthMm = request.Settings.ConeLength,
                BallDiameterMm = request.Settings.BallDiameter,
                PenetrationDepthMm = request.Settings.PenetrationDepth,
                TipNormalLeadInMm = request.Settings.TipNormalLeadInMm,
                SpacingMm = request.Settings.Spacing,
                MinSpacingMm = request.Settings.Spacing,
                IslandSpacingMm = request.Settings.IslandSpacingMm,
                OverhangAngleDegrees = request.Settings.OverhangAngleDegrees,
                MinIslandAreaMm2 = request.Settings.MinIslandAreaMm2,
                MaxContactFaceAngleDegrees = request.Settings.MaxContactFaceAngleDegrees,
                RequireContactSeesPlate = request.Settings.RequireContactSeesPlate,
                EnableMiniSupports = true,
                MiniIslandMaxAreaMm2 = request.Settings.MiniIslandMaxAreaMm2,
                MiniSupportTipDiameterMm = request.Settings.MiniSupportTipDiameter,
                MiniSupportConeLengthMm = request.Settings.MiniSupportConeLength,
                MiniSupportClusterDistanceMm = request.Settings.MiniSupportClusterDistance,
                FineFeatureMaxAreaMm2 = request.Settings.FineFeatureMaxAreaMm2,
            },
            new TreeRoutingOptions
            {
                TrunkDiameter = request.Settings.TrunkDiameter,
                BranchDiameter = request.Settings.BranchDiameter,
                MaxMemberAngleDegrees = request.Settings.MemberAngleDegrees,
                TipMemberLength = request.Settings.TipMemberLength,
                MaxBranchLength = request.Settings.MaxBranchLength,
                PreferExistingTrunks = request.Settings.PreferExistingTrunks,
                ExistingTrunkBranchRange = request.Settings.ExistingTrunkBranchRange,
                MinMemberSeparationMm = request.Settings.MinMemberSeparationMm,
                MiniSupportDiameter = request.Settings.MiniSupportDiameter,
                MiniSupportTipDiameter = request.Settings.MiniSupportTipDiameter,
                MiniSupportConeLength = request.Settings.MiniSupportConeLength,
                MiniSupportMaxLength = request.Settings.MiniSupportMaxLength,
                MiniSupportMaxAngleDegrees = request.Settings.MiniSupportMaxAngleDegrees,
                MiniSupportMaxFanPerBranchEnd = request.Settings.MiniSupportMaxFanPerBranchEnd,
                FineFeatureMinisFallBackToRegular =
                    request.Settings.FineFeatureMinisFallBackToRegular,
                RefusedTipsFallBackToMini = request.Settings.RefusedTipsFallBackToMini,
                UseBaseGrid = request.Settings.UseBaseGrid,
                BaseGridPitch = request.Settings.BaseGridPitch,
                BaseShape = request.Settings.BaseShape,
                BaseDiameter = request.Settings.BaseDiameter,
                BaseHeight = request.Settings.BaseHeight,
                BaseConeHeight = request.Settings.BaseConeHeight,
                Seed = request.Seed,
                Origin = origin,
            }, rules,
            obstacles, request.ExistingSupports,
            keepCleanFaces: request.Regions.KeepCleanFaces.Count > 0
                ? request.Regions.KeepCleanFaces
                : null,
            seed: request.Seed, progress: progress,
            scope: request.Scope);
        cancellationToken.ThrowIfCancellationRequested();

        // Routing ids are deterministic from the seed. Fresh graph ids allow repeated generation
        // with seed zero while preserving deterministic placement and routing geometry.
        var idMap = generated.Routing.Graph.Nodes.ToDictionary(node => node.Id, _ => Guid.NewGuid());
        var nodes = generated.Routing.Graph.Nodes
            .Select(node => node.Clone(idMap[node.Id], origin)).ToList();
        var segments = generated.Routing.Graph.Segments
            .Select(segment => segment.Clone(Guid.NewGuid(), idMap[segment.NodeA],
                idMap[segment.NodeB], origin)).ToList();
        var summary = new SupportGenerationSummary(generated.Candidates.Count,
            nodes.Count(node => node.Type == SupportNodeType.Tip), generated.Routing.UnroutedTips.Count)
        {
            RefusalReasons = generated.Routing.Failures
                .GroupBy(failure => failure.Reason)
                .ToDictionary(group => group.Key, group => group.Count()),
        };
        return new PreparedSupportGeneration(nodes, segments, summary,
            request.Scope == SupportGenerationScope.IslandsOnly
                ? "Generate island supports" : "Generate supports");
    }

    private static Mesh TransformMesh(SceneMeshSnapshot snapshot) => new(
        snapshot.Mesh.Positions.Select(p => Vector3.Transform(p, snapshot.Transform)).ToArray(),
        (int[])snapshot.Mesh.Indices.Clone());

    private static SupportGraph CloneGraph(SupportGraph source)
    {
        var clone = new SupportGraph();
        foreach (var node in source.Nodes)
            clone.AddNode(node.Clone());
        foreach (var segment in source.Segments)
            clone.AddSegment(segment.Clone());
        return clone;
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

    /// <summary>
    /// Hides the selected SUPPORTS and deselects them: each selected element expands to its
    /// complete non-bracing connected component, because a support is one user-visible thing
    /// (user screen test 2026-09-03: hiding a lone trunk left its tip and base floating).
    /// A selected brace hides itself only. One undo step.
    /// </summary>
    public void HideSelectedSupportElements()
    {
        if (_supportSelection.Count == 0) return;
        var nodes = new HashSet<Guid>();
        var segments = new HashSet<Guid>();
        foreach (var id in _supportSelection)
        {
            if (Supports.TryGetNode(id, out var node))
            {
                AddComponent(node.Id);
            }
            else if (Supports.TryGetSegment(id, out var segment))
            {
                segments.Add(segment.Id);
                if (segment.Type != SupportSegmentType.Bracing)
                {
                    AddComponent(segment.NodeA);
                    AddComponent(segment.NodeB);
                }
            }
        }

        void AddComponent(Guid seed)
        {
            var component = Supports.Component(seed, includeBracing: false);
            nodes.UnionWith(component.Nodes);
            segments.UnionWith(component.Segments);
        }

        var entries = new List<SetSupportHiddenCommand.Entry>();
        foreach (var id in nodes)
        {
            var node = Supports.GetNode(id);
            if (!node.Hidden)
                entries.Add(new SetSupportHiddenCommand.Entry(value => node.Hidden = value, false, true));
        }
        foreach (var id in segments)
        {
            var segment = Supports.GetSegment(id);
            if (!segment.Hidden)
                entries.Add(new SetSupportHiddenCommand.Entry(value => segment.Hidden = value, false, true));
        }
        ClearSupportSelection();
        if (entries.Count > 0)
            Execute(new SetSupportHiddenCommand(Supports, entries, "Hide supports"));
    }

    /// <summary>
    /// Hides every unselected support tree when support elements are selected. Selecting any node
    /// or segment retains its complete non-bracing connected component, because a support is one
    /// user-visible thing even when only one of its elements is selected.
    /// </summary>
    public void HideUnselectedSupportElements()
    {
        if (_supportSelection.Count == 0) return;
        var visibleNodes = new HashSet<Guid>();
        var visibleSegments = new HashSet<Guid>();
        foreach (var selectedId in _supportSelection)
        {
            if (Supports.TryGetNode(selectedId, out var node))
            {
                AddComponent(node.Id);
            }
            else if (Supports.TryGetSegment(selectedId, out var segment))
            {
                AddComponent(segment.NodeA);
                AddComponent(segment.NodeB);
                visibleSegments.Add(segment.Id);
            }
        }

        void AddComponent(Guid seed)
        {
            var component = Supports.Component(seed, includeBracing: false);
            visibleNodes.UnionWith(component.Nodes);
            visibleSegments.UnionWith(component.Segments);
        }

        var entries = new List<SetSupportHiddenCommand.Entry>();
        foreach (var node in Supports.Nodes.Where(node => !visibleNodes.Contains(node.Id) && !node.Hidden))
            entries.Add(new SetSupportHiddenCommand.Entry(value => node.Hidden = value, node.Hidden, true));
        foreach (var segment in Supports.Segments.Where(segment => !visibleSegments.Contains(segment.Id) && !segment.Hidden))
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
        CommitTransform(obj, before, after, "Lay flat on face", applyPlacement: false);
    }
}
