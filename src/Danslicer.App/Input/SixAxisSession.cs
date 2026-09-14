namespace Danslicer.App.Input;

/// <summary>One driver connection for attached viewports; handoffs wait for a neutral cap.</summary>
internal sealed class SixAxisSession(Func<ISixAxisInput> createDevice, TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;
    private long? _lastConnectionAttempt;
    private static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(2);
    private readonly HashSet<object> _viewports = [];
    private ISixAxisInput? _device;
    private object? _owner;
    private bool _waitingForNeutral;

    public void Attach(object viewport) => _viewports.Add(viewport);

    public ISixAxisInput? Acquire(object viewport)
    {
        if (!_viewports.Contains(viewport)) throw new InvalidOperationException("Viewport is not attached");
        if (!ReferenceEquals(_owner, viewport))
        {
            _owner = viewport;
            _waitingForNeutral = true;
            _device?.DrainButtonPresses();
        }
        if (_device?.IsConnected != true)
        {
            var now = _clock.GetTimestamp();
            if (_lastConnectionAttempt is { } last && _clock.GetElapsedTime(last, now) < RetryInterval)
                return null;
            _lastConnectionAttempt = now;
            _device?.Dispose();
            _device = createDevice();
            if (!_device.TryConnect()) { _device.Dispose(); _device = null; }
            _waitingForNeutral = true;
        }
        return _device;
    }

    public SixAxisMotion Poll(object viewport, float deadzone)
    {
        if (!ReferenceEquals(viewport, _owner) || _device is null) return default;
        var motion = _device.Poll();
        static bool Neutral(System.Numerics.Vector3 v, float zone) =>
            Math.Abs(v.X) <= zone && Math.Abs(v.Y) <= zone && Math.Abs(v.Z) <= zone;
        if (_waitingForNeutral)
        {
            if (Neutral(motion.Translation, deadzone) && Neutral(motion.Rotation, deadzone))
                _waitingForNeutral = false;
            return default;
        }
        return motion;
    }

    public void Release(object viewport)
    {
        if (!ReferenceEquals(viewport, _owner)) return;
        _owner = null;
        _waitingForNeutral = true;
    }

    public void Detach(object viewport)
    {
        Release(viewport);
        _viewports.Remove(viewport);
        if (_viewports.Count != 0) return;
        _device?.Dispose();
        _device = null;
        _lastConnectionAttempt = null;
    }
}
