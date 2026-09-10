using System.ComponentModel;
using Avalonia;
using Avalonia.Automation;
using Danslicer.App.ViewModels;

namespace Danslicer.App.Controls.Refresh;

/// <summary>One commit callback into the existing NumericField/model/undo path; no Value binding.</summary>
public sealed class ModelScrubField : ScrubField
{
    public static readonly StyledProperty<NumericField?> FieldProperty = AvaloniaProperty.Register<ModelScrubField, NumericField?>(nameof(Field));
    public NumericField? Field { get => GetValue(FieldProperty); set => SetValue(FieldProperty, value); }
    public ModelScrubField()
    {
        Minimum = -double.MaxValue; Maximum = double.MaxValue;
        EditCommitted += (_, e) =>
        {
            Field?.CommitValue(e.NewValue);
            Sync(); // Reflect model validation/clamping even when no model change occurred.
        };
    }
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != FieldProperty) return;
        if (change.OldValue is NumericField old) old.PropertyChanged -= Changed;
        if (Field is { } field) field.PropertyChanged += Changed;
        Sync();
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
