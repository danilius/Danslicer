using Danslicer.Core.Utilities;

namespace Danslicer.App.Controls.Refresh;

/// <summary>One edit transaction. Preview values never invoke the application's undo/apply callback.</summary>
public sealed class NumericEditSession
{
    public const double DragThreshold = 4;
    public double Original { get; }
    public double Preview { get; private set; }
    public bool IsDragging { get; private set; }
    private double _lastX;
    private readonly double _originX;
    public NumericEditSession(double value, double originX) => (Original, Preview, _originX, _lastX) = (value, value, originX, originX);

    public double Move(double x, double step, bool fine, double minimum, double maximum)
    {
        if (!IsDragging)
        {
            if (Math.Abs(x - _originX) < DragThreshold) return Preview;
            IsDragging = true;
        }
        Preview = Math.Clamp(Preview + (x - _lastX) * step * (fine ? 0.1 : 1), minimum, maximum);
        _lastX = x;
        return Preview;
    }

    public static bool TryExpression(string text, UnitKind kind, double minimum, double maximum, out double value)
    {
        return ExpressionParser.TryEvaluate(text, kind, out value) && double.IsFinite(value)
            && value >= minimum && value <= maximum;
    }
}

public sealed class NumericCommittedEventArgs(double oldValue, double newValue) : EventArgs
{
    public double OldValue { get; } = oldValue;
    public double NewValue { get; } = newValue;
}
