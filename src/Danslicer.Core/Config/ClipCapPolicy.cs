namespace Danslicer.Core.Config;

/// <summary>
/// Decides which clip-cap rendering technique applies for a given combination of settings. Kept as
/// a pure function, independent of the viewport and renderer, so the decision is directly testable
/// without a GL context.
/// </summary>
public static class ClipCapPolicy
{
    /// <summary>
    /// The style actually used once render-path support is accounted for. <see cref="ClipCapStyle.Painted"/>
    /// is a deferred-only screen-space technique; on the Classic path it falls back to
    /// <see cref="ClipCapStyle.Sliced"/> so switching render paths never uncaps a clipped model.
    /// </summary>
    public static ClipCapStyle EffectiveStyle(ClipCapStyle style, RenderPathMode renderPath) =>
        style == ClipCapStyle.Painted && renderPath != RenderPathMode.Deferred
            ? ClipCapStyle.Sliced
            : style;

    /// <summary>
    /// True when the CPU-exact <c>ClipCapBuilder</c> path should run: <see cref="ClipCapStyle.Sliced"/>
    /// directly, or <see cref="ClipCapStyle.Painted"/> falling back to it on the Classic path.
    /// </summary>
    public static bool ShouldBuildExactCaps(bool capInterior, ClipCapStyle style,
        RenderPathMode renderPath, bool isClipping) =>
        capInterior && isClipping && EffectiveStyle(style, renderPath) == ClipCapStyle.Sliced;

    /// <summary>
    /// True when the deferred renderer's screen-space cap technique should run. Only ever true on
    /// the Deferred path; Painted on Classic resolves to <see cref="ShouldBuildExactCaps"/> instead.
    /// </summary>
    public static bool ShouldPaintCaps(bool capInterior, ClipCapStyle style,
        RenderPathMode renderPath, bool isClipping) =>
        capInterior && isClipping && EffectiveStyle(style, renderPath) == ClipCapStyle.Painted;
}
