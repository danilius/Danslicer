using System.Numerics;

namespace Danslicer.App.Input;

/// <summary>Short time-based smoothing, with immediate neutral/reversal handling on each axis.</summary>
internal sealed class SixAxisMotionFilter
{
    private SixAxisMotion _previous;

    public SixAxisMotion Apply(SixAxisMotion motion, float deadzone, float elapsedMs)
    {
        var alpha = 1f - MathF.Exp(-Math.Clamp(elapsedMs, 0, 45) / 25f);
        float Axis(float target, float previous)
        {
            if (!float.IsFinite(target) || MathF.Abs(target) <= deadzone) return 0;
            // Subtract the threshold so crossing it doesn't introduce a discontinuity.
            target = MathF.CopySign(MathF.Abs(target) - deadzone, target);
            if (MathF.Sign(target) != MathF.Sign(previous)) previous = 0;
            return previous + (target - previous) * alpha;
        }
        Vector3 Filter(Vector3 target, Vector3 previous) => new(
            Axis(target.X, previous.X), Axis(target.Y, previous.Y), Axis(target.Z, previous.Z));
        return _previous = new(Filter(motion.Translation, _previous.Translation),
            Filter(motion.Rotation, _previous.Rotation));
    }

    public void Reset() => _previous = default;
}
