using Danslicer.Core.Scene;

namespace Danslicer.Core.Supports;

/// <summary>
/// A model and its supports appear and disappear together. Hiding a model takes its supports out
/// of the viewport and out of the print; showing it brings them back, unchanged.
///
/// <para>This is deliberately NOT the per-element <see cref="SupportNode.Hidden"/> flag. That flag
/// is the user's own hiding of individual support elements in Support mode, it is persisted, and
/// it survives being shown again. Ownership visibility is derived state: it is recomputed from
/// which models are hidden right now, so it can never be left stale, and hiding a model can never
/// silently discard the user's element-level choices underneath it.</para>
///
/// <para>The print rule is the strict one and the reason this lives in Core rather than in the
/// viewport: a hidden model is not printed, so neither are its supports. Anything else would put
/// resin on the plate holding up something that is not there.</para>
/// </summary>
public static class SupportOwnerVisibility
{
    /// <summary>The ids of the hidden models, as the two predicates below want them.</summary>
    public static IReadOnlySet<Guid> HiddenObjectIds(IEnumerable<SceneObject> objects)
    {
        ArgumentNullException.ThrowIfNull(objects);
        var hidden = new HashSet<Guid>();
        foreach (var obj in objects)
            if (obj.RenderState == RenderState.Hidden) hidden.Add(obj.Id);
        return hidden;
    }

    /// <summary>
    /// True when the element's owning model is hidden. An element owned by nothing — a manual
    /// support placed before object ownership existed, say — belongs to no model and is never
    /// hidden by this rule.
    /// </summary>
    public static bool IsOwnedByHidden(SupportGraph graph, Guid elementId, IReadOnlySet<Guid> hiddenObjectIds)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(hiddenObjectIds);
        if (hiddenObjectIds.Count == 0) return false;
        return graph.OwningObjectId(elementId) is { } owner && hiddenObjectIds.Contains(owner);
    }

    /// <summary>Convenience for the render and slice loops, which hold the element already.</summary>
    public static bool IsOwnedByHidden(SupportNode node, IReadOnlySet<Guid> hiddenObjectIds) =>
        node.Origin.ObjectId is { } owner && hiddenObjectIds.Contains(owner);
}
