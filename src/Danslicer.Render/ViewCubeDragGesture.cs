namespace Danslicer.Render;

/// <summary>
/// The press-move-release state machine behind view-cube drag-orbit: it decides when a press on the
/// cube has become a drag, and converts pointer movement into the pixel deltas
/// <see cref="Camera.Orbit"/> takes. Pure — no camera, no Avalonia, no GL — so the part that is
/// easy to get subtly wrong is the part that is unit tested. The control that owns it only has to
/// feed it positions and honour <see cref="IsClick"/> on release.
///
/// The gesture deliberately does NOT touch the camera itself. Scaling the pixel deltas and handing
/// them to the one existing <see cref="Camera.Orbit"/> keeps a cube drag on the same rotation rule
/// and the same pitch clamp as a viewport drag, rather than forking a second orbit.
/// </summary>
public sealed class ViewCubeDragGesture
{
    /// <summary>
    /// How far the pointer must travel from the press point before the gesture stops being a click
    /// and starts orbiting. A shade larger than the marquee's 3px on purpose: the cube's rotation
    /// rate is about three times a viewport drag's, so an imprecise click that slipped a few pixels
    /// would spin the view several degrees AND lose the snap the user was actually asking for.
    /// </summary>
    public const float ClickThresholdPixels = 4f;

    private readonly float _gain;
    private readonly float _pressX;
    private readonly float _pressY;
    private float _lastX;
    private float _lastY;

    private ViewCubeDragGesture(int region, float x, float y, int sizePixels)
    {
        Region = region;
        _pressX = _lastX = x;
        _pressY = _lastY = y;
        _gain = ViewCube.DragDegreesPerPixel(sizePixels) * MathF.PI / 180f / Camera.OrbitRadiansPerPixel;
    }

    /// <summary>Starts a gesture from a press at (<paramref name="x"/>, <paramref name="y"/>) in
    /// pointer units, over cube region <paramref name="region"/>.</summary>
    public static ViewCubeDragGesture Begin(int region, float x, float y,
        int sizePixels = ViewCube.DefaultSizePixels) => new(region, x, y, sizePixels);

    /// <summary>The region under the press point — the view a click (not a drag) snaps to.</summary>
    public int Region { get; }

    /// <summary>True once the pointer has moved past <see cref="ClickThresholdPixels"/>.</summary>
    public bool IsDragging { get; private set; }

    /// <summary>True while a release would still count as a click and snap to <see cref="Region"/>.</summary>
    public bool IsClick => !IsDragging;

    /// <summary>
    /// Feeds a pointer position. Returns the deltas to pass straight to <see cref="Camera.Orbit"/>,
    /// or null while the gesture is still within the click threshold. The move that crosses the
    /// threshold reports its whole travel from the press point, so the cube picks up exactly where
    /// the pointer has got to instead of dropping the first few pixels on the floor.
    /// </summary>
    public (float DxPixels, float DyPixels)? Move(float x, float y)
    {
        if (!IsDragging)
        {
            var fromPressX = x - _pressX;
            var fromPressY = y - _pressY;
            if (MathF.Sqrt(fromPressX * fromPressX + fromPressY * fromPressY) < ClickThresholdPixels)
                return null;
            IsDragging = true;
            _lastX = x;
            _lastY = y;
            return (fromPressX * _gain, fromPressY * _gain);
        }

        var dx = x - _lastX;
        var dy = y - _lastY;
        _lastX = x;
        _lastY = y;
        return (dx * _gain, dy * _gain);
    }
}
