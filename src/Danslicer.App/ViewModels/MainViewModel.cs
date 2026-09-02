using System.Collections.ObjectModel;
using System.Numerics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Danslicer.Core;
using Danslicer.Core.Commands;
using Danslicer.Core.IO;
using Danslicer.Core.Scene;
using Danslicer.Core.Utilities;

namespace Danslicer.App.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private bool _syncingSelection;

    public Document Document { get; } = new();

    public ObservableCollection<SceneObject> Objects { get; } = new();

    [ObservableProperty]
    public partial SceneObject? SelectedObject { get; set; }

    [ObservableProperty]
    public partial string Title { get; set; } = "Danslicer";

    [ObservableProperty]
    public partial string ViewportStatus { get; set; } = "";

    [ObservableProperty]
    public partial string HistoryStatus { get; set; } = "";

    [ObservableProperty]
    public partial string DimensionsText { get; set; } = "";

    [ObservableProperty]
    public partial string TriangleText { get; set; } = "";

    public NumericField[] Position { get; }
    public NumericField[] Rotation { get; }
    public NumericField[] Scale { get; }

    public MainViewModel()
    {
        Position = MakeAxisFields(UnitKind.Length, "0.###", (t, axis, v) => t with { Translation = SetAxis(t.Translation, axis, (float)v) });
        Rotation = MakeAxisFields(UnitKind.Angle, "0.##", (t, axis, v) => t with { EulerDegrees = SetAxis(t.EulerDegrees, axis, (float)v) });
        Scale = MakeAxisFields(UnitKind.Scalar, "0.####", (t, axis, v) => t with { Scale = SetAxis(t.Scale, axis, (float)v) });

        Document.Scene.ObjectAdded += o => { Objects.Add(o); Title = "Danslicer"; };
        Document.Scene.ObjectRemoved += o => Objects.Remove(o);
        Document.SelectionChanged += OnDocumentSelectionChanged;
        Document.Changed += OnDocumentChanged;
        OnDocumentChanged();
    }

    private NumericField[] MakeAxisFields(UnitKind kind, string format, Func<Transform, int, double, Transform> edit)
    {
        var labels = new[] { "X", "Y", "Z" };
        var fields = new NumericField[3];
        for (int axis = 0; axis < 3; axis++)
        {
            var a = axis;
            fields[axis] = new NumericField(labels[axis], kind, format, value =>
            {
                var obj = SelectedObject;
                if (obj is null) return;
                var before = obj.Transform;
                var after = edit(before, a, value);
                if (after == before) return;
                Document.Execute(new SetTransformCommand(obj, before, after, "Edit transform"));
            });
        }
        return fields;
    }

    private static Vector3 SetAxis(Vector3 v, int axis, float value) => axis switch
    {
        0 => v with { X = value },
        1 => v with { Y = value },
        _ => v with { Z = value },
    };

    private void OnDocumentSelectionChanged()
    {
        if (_syncingSelection) return;
        _syncingSelection = true;
        try
        {
            SelectedObject = Document.Selection.FirstOrDefault();
        }
        finally
        {
            _syncingSelection = false;
        }
        RefreshFields();
        DeleteCommand.NotifyCanExecuteChanged();
        DropToPlateCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedObjectChanged(SceneObject? value)
    {
        if (_syncingSelection) return;
        _syncingSelection = true;
        try
        {
            if (value is null) Document.ClearSelection();
            else Document.Select(value);
        }
        finally
        {
            _syncingSelection = false;
        }
        RefreshFields();
    }

    private void OnDocumentChanged()
    {
        RefreshFields();
        var undo = Document.History.UndoName;
        var redo = Document.History.RedoName;
        HistoryStatus = (undo is null ? "" : $"Undo: {undo}") + (redo is null ? "" : $"   Redo: {redo}");
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
    }

    private void RefreshFields()
    {
        var obj = SelectedObject;
        if (obj is null)
        {
            DimensionsText = "";
            TriangleText = "";
            foreach (var f in Position.Concat(Rotation).Concat(Scale)) f.SetValue(0);
            return;
        }

        var t = obj.Transform;
        var euler = t.EulerDegrees;
        for (int i = 0; i < 3; i++)
        {
            Position[i].SetValue(Component(t.Translation, i));
            Rotation[i].SetValue(Component(euler, i));
            Scale[i].SetValue(Component(t.Scale, i));
        }
        var size = obj.WorldBounds.Size;
        DimensionsText = $"{size.X:0.##} × {size.Y:0.##} × {size.Z:0.##} mm";
        TriangleText = $"{obj.Mesh.TriangleCount:N0} triangles";
    }

    private static float Component(Vector3 v, int i) => i switch { 0 => v.X, 1 => v.Y, _ => v.Z };

    public void ImportStl(string path)
    {
        var mesh = StlReader.Read(path);
        var obj = new SceneObject(System.IO.Path.GetFileNameWithoutExtension(path), mesh);
        // Place on the plate, centred in XY.
        var b = mesh.Bounds;
        obj.Transform = Transform.Identity with { Translation = new Vector3(-b.Center.X, -b.Center.Y, -b.Min.Z) };
        Document.AddObject(obj);
    }

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void Undo() => Document.Undo();
    private bool CanUndo() => Document.History.CanUndo;

    [RelayCommand(CanExecute = nameof(CanRedo))]
    private void Redo() => Document.Redo();
    private bool CanRedo() => Document.History.CanRedo;

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Delete() => Document.DeleteSelection();

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void DropToPlate() => Document.DropSelectionToPlate();

    [RelayCommand]
    private void SelectAll() => Document.SelectAll();

    private bool HasSelection() => Document.Selection.Count > 0;
}
