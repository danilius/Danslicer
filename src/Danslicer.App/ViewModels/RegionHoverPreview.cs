using Danslicer.Core.Scene;

namespace Danslicer.App.ViewModels;

/// <summary>
/// The support-region patch under the cursor: which object, and which of its faces a click would
/// paint. Carried as one value so the viewport cannot end up drawing one object's faces over
/// another object's geometry while a hover is being replaced.
/// </summary>
/// <remarks>
/// This is a preview, not state: it is recomputed from the cursor and the patch angle, never
/// stored in the document, and it disappears the moment painting is disarmed.
/// </remarks>
public sealed record RegionHoverPreview(SceneObject Object, IReadOnlySet<int> Faces);
