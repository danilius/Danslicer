namespace Danslicer.App.ViewModels;

/// <summary>A bounded, synchronous preview owned by one numeric gesture. Commit restores
/// the immutable start before invoking the existing durable command exactly once.</summary>
public sealed class NumericPreview(Action<double> preview, Action restore, Action<double> commit)
{
    private bool _ended;
    public void Update(double value) { if (!_ended) preview(value); }
    public void Cancel()
    {
        if (_ended) return;
        _ended = true;
        restore();
    }
    public void Commit(double value)
    {
        if (_ended) return;
        _ended = true;
        restore();
        commit(value);
    }
}
