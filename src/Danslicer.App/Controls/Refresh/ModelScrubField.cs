using System.ComponentModel;
using Avalonia;
using Avalonia.Automation;
using Avalonia.LogicalTree;
using Danslicer.App.ViewModels;

namespace Danslicer.App.Controls.Refresh;

/// <summary>One commit callback into the existing NumericField/model/undo path; no Value binding.</summary>
public sealed class ModelScrubField : ScrubField
{
    private bool _attached;
    public static readonly StyledProperty<NumericField?> FieldProperty = AvaloniaProperty.Register<ModelScrubField, NumericField?>(nameof(Field));
    public NumericField? Field { get => GetValue(FieldProperty); set => SetValue(FieldProperty, value); }
    public ModelScrubField()
    {
        Minimum = -double.MaxValue; Maximum = double.MaxValue;
        BeginPreview = () => Field?.BeginPreview?.Invoke();
        EditCommitted += (_, e) =>
        {
            if (!CommittedPreview) Field?.CommitValue(e.NewValue);
            Sync(); // Reflect model validation/clamping even when no model change occurred.
        };
    }
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != FieldProperty) return;
        Cancel();
        if (change.OldValue is NumericField old) old.PropertyChanged -= Changed;
        if (_attached && Field is { } field) field.PropertyChanged += Changed;
        Sync();
    }
    protected override void OnAttachedToLogicalTree(LogicalTreeAttachmentEventArgs e)
    {
        base.OnAttachedToLogicalTree(e);
        _attached = true;
        if (Field is { } field) { field.PropertyChanged -= Changed; field.PropertyChanged += Changed; }
        Sync();
    }
    protected override void OnDetachedFromLogicalTree(LogicalTreeAttachmentEventArgs e)
    {
        if (Field is { } field) field.PropertyChanged -= Changed;
        _attached = false;
        base.OnDetachedFromLogicalTree(e);
    }
    private void Changed(object? sender, PropertyChangedEventArgs e) => Sync();
    private void Sync()
    {
        if (Field is not { } field) return;
        UnitKind = field.Kind; Format = field.Format; Unit = field.Suffix;
        Step = field.Kind == Danslicer.Core.Utilities.UnitKind.Angle ? 1 : 0.1;
        AutomationProperties.SetName(this, field.Label);
        Value = field.Value;
    }
}
