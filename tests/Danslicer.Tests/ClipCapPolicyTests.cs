using Danslicer.Core.Config;

namespace Danslicer.Tests;

public sealed class ClipCapPolicyTests
{
    [Theory]
    [InlineData(ClipCapStyle.Sliced, RenderPathMode.Deferred, ClipCapStyle.Sliced)]
    [InlineData(ClipCapStyle.Sliced, RenderPathMode.Classic, ClipCapStyle.Sliced)]
    [InlineData(ClipCapStyle.Painted, RenderPathMode.Deferred, ClipCapStyle.Painted)]
    // Painted is a deferred-only screen-space technique with no Classic equivalent, so Classic
    // falls back to exact geometry rather than leaving the model uncapped.
    [InlineData(ClipCapStyle.Painted, RenderPathMode.Classic, ClipCapStyle.Sliced)]
    public void EffectiveStyleFallsBackOnlyForPaintedOnClassic(
        ClipCapStyle style, RenderPathMode renderPath, ClipCapStyle expected) =>
        Assert.Equal(expected, ClipCapPolicy.EffectiveStyle(style, renderPath));

    [Theory]
    // capInterior off: neither technique ever runs, whatever style or path.
    [InlineData(false, ClipCapStyle.Sliced, RenderPathMode.Deferred, true, false, false)]
    [InlineData(false, ClipCapStyle.Painted, RenderPathMode.Deferred, true, false, false)]
    // Not actually clipping: neither technique runs.
    [InlineData(true, ClipCapStyle.Sliced, RenderPathMode.Deferred, false, false, false)]
    [InlineData(true, ClipCapStyle.Painted, RenderPathMode.Deferred, false, false, false)]
    // Sliced always builds exact geometry, on either render path.
    [InlineData(true, ClipCapStyle.Sliced, RenderPathMode.Deferred, true, true, false)]
    [InlineData(true, ClipCapStyle.Sliced, RenderPathMode.Classic, true, true, false)]
    // Painted on Deferred paints in screen space and never builds exact geometry.
    [InlineData(true, ClipCapStyle.Painted, RenderPathMode.Deferred, true, false, true)]
    // Painted on Classic has no screen-space path, so it falls back to exact geometry.
    [InlineData(true, ClipCapStyle.Painted, RenderPathMode.Classic, true, true, false)]
    public void DecidesExactlyOnePathPerCombination(bool capInterior, ClipCapStyle style,
        RenderPathMode renderPath, bool isClipping, bool expectExact, bool expectPainted)
    {
        Assert.Equal(expectExact,
            ClipCapPolicy.ShouldBuildExactCaps(capInterior, style, renderPath, isClipping));
        Assert.Equal(expectPainted,
            ClipCapPolicy.ShouldPaintCaps(capInterior, style, renderPath, isClipping));
        // Never both: exact geometry and the screen-space technique are mutually exclusive so the
        // Painted style genuinely skips ClipCapBuilder rather than running it anyway.
        Assert.False(
            ClipCapPolicy.ShouldBuildExactCaps(capInterior, style, renderPath, isClipping) &&
            ClipCapPolicy.ShouldPaintCaps(capInterior, style, renderPath, isClipping));
    }
}
