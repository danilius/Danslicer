using System.Collections.ObjectModel;
using System.Numerics;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Danslicer.Core;
using Danslicer.Core.Commands;
using Danslicer.Core.IO;
using Danslicer.Core.Scene;
using Danslicer.Core.Slicing;
using Danslicer.Core.Utilities;

namespace Danslicer.App.ViewModels;

public enum ViewMode { Model, Layers }

public partial class MainViewModel : ViewModelBase
{
    private bool _syncingSelection;
    private CancellationTokenSource? _sliceCancellation;

    public Document Document { get; } = new();

    public ObservableCollection<SceneObject> Objects { get; } = new();

    public PrintSettingsViewModel PrintSettings { get; }

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

    // ----- Viewport tools -----

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsModelView), nameof(IsLayersView))]
    public partial ViewMode ViewMode { get; set; } = ViewMode.Model;

    public bool IsModelView
    {
        get => ViewMode == ViewMode.Model;
        set { if (value) ViewMode = ViewMode.Model; }
    }

    public bool IsLayersView
    {
        get => ViewMode == ViewMode.Layers;
        set { if (value) ViewMode = ViewMode.Layers; }
    }

    [ObservableProperty]
    public partial bool ShowMoveGizmo { get; set; } = true;

    [ObservableProperty]
    public partial bool ShowRotateGizmo { get; set; }

    [ObservableProperty]
    public partial bool ShowScaleGizmo { get; set; }

    [ObservableProperty]
    public partial bool SnapEnabled { get; set; }

    // ----- Slicing -----

    [ObservableProperty]
    public partial bool IsSlicing { get; set; }

    [ObservableProperty]
    public partial double SliceProgress { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSlice))]
    public partial SliceResult? LastSlice { get; set; }

    public bool HasSlice => LastSlice is not null;

    [ObservableProperty]
    public partial string SliceSummary { get; set; } = "Not sliced yet.";

    [ObservableProperty]
    public partial int PreviewLayer { get; set; }

    [ObservableProperty]
    public partial int PreviewLayerMax { get; set; }

    [ObservableProperty]
    public partial string PreviewLayerText { get; set; } = "";

    [ObservableProperty]
    public partial WriteableBitmap? PreviewImage { get; set; }

    public NumericField[] Position { get; }
    public NumericField[] Rotation { get; }
    public NumericField[] Scale { get; }

    public MainViewModel()
    {
        PrintSettings = new PrintSettingsViewModel(Document);
        Position = MakeAxisFields(UnitKind.Length, "0.###", (t, axis, v) => t with { Translation = SetAxis(t.Translation, axis, (float)v) });
        Rotation = MakeAxisFields(UnitKind.Angle, "0.##", (t, axis, v) => t with { EulerDegrees = SetAxis(t.EulerDegrees, axis, (float)v) });
        Scale = MakeAxisFields(UnitKind.Scalar, "0.####", (t, axis, v) => t with { Scale = SetAxis(t.Scale, axis, (float)v) });

        Document.Scene.ObjectAdded += o => Objects.Add(o);
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
        SliceCommand.NotifyCanExecuteChanged();

        // Geometry changed: the slice no longer matches the scene.
        if (LastSlice is not null && !IsSlicing) InvalidateSlice();
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

    public void ImportMesh(string path)
    {
        var mesh = MeshFile.Read(path);
        var obj = new SceneObject(System.IO.Path.GetFileNameWithoutExtension(path), mesh);
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

    [RelayCommand]
    private void ToggleView() => ViewMode = ViewMode == ViewMode.Model ? ViewMode.Layers : ViewMode.Model;

    [RelayCommand]
    private void ToggleSnap() => SnapEnabled = !SnapEnabled;

    public void StepLayer(int delta)
    {
        if (LastSlice is null) return;
        PreviewLayer = Math.Clamp(PreviewLayer + delta, 0, PreviewLayerMax);
    }

    private bool HasSelection() => Document.Selection.Count > 0;

    // ----- Slicing -----

    private bool CanSlice() => !IsSlicing && Document.Scene.Objects.Any(o => o.RenderState != RenderState.Hidden);

    /// <summary>Slices the scene in the background. Returns the result, or null on failure or cancel.</summary>
    [RelayCommand(CanExecute = nameof(CanSlice))]
    private async Task<SliceResult?> Slice()
    {
        if (IsSlicing) return null;
        IsSlicing = true;
        SliceProgress = 0;
        SliceCommand.NotifyCanExecuteChanged();
        _sliceCancellation = new CancellationTokenSource();
        var token = _sliceCancellation.Token;
        var objects = Document.Scene.Objects.ToList();
        var printer = Document.Printer;
        var settings = Document.PrintSettings;
        var progress = new Progress<double>(p =>
        {
            SliceProgress = p;
            ViewportStatus = $"Slicing… {p * 100:0}%";
        });

        try
        {
            var result = await Task.Run(() => Slicer.Slice(objects, printer, settings, progress, token), token);
            LastSlice = result;
            SliceSummary =
                $"{result.LayerCount} layers × {settings.LayerHeight:0.###} mm = {result.PrintHeight:0.##} mm\n" +
                $"{result.VolumeMl:0.##} ml resin\n" +
                $"≈ {TimeSpan.FromSeconds(result.EstimatedSeconds):h\\:mm\\:ss}\n" +
                $"footprint X {result.MinX:0.#}…{result.MaxX:0.#}  Y {result.MinY:0.#}…{result.MaxY:0.#} mm";
            PreviewLayerMax = Math.Max(0, result.LayerCount - 1);
            PreviewLayer = Math.Min(PreviewLayer, PreviewLayerMax);
            UpdatePreview();
            ViewMode = ViewMode.Layers;
            ViewportStatus = $"Sliced {result.LayerCount} layers.  Tab returns to the model view · Page Up/Down or Ctrl+wheel steps layers · wheel zooms · drag pans";
            return result;
        }
        catch (OperationCanceledException)
        {
            ViewportStatus = "Slicing cancelled.";
            return null;
        }
        catch (Exception ex)
        {
            ViewportStatus = $"Slicing failed: {ex.Message}";
            SliceSummary = ex.Message;
            return null;
        }
        finally
        {
            IsSlicing = false;
            _sliceCancellation = null;
            SliceCommand.NotifyCanExecuteChanged();
        }
    }

    public void CancelSlice() => _sliceCancellation?.Cancel();

    /// <summary>Slices if needed, then writes the printer file.</summary>
    public async Task<bool> ExportAsync(string path)
    {
        var result = LastSlice;
        if (result is null || result.Settings != Document.PrintSettings)
            result = await Slice();
        if (result is null) return false;

        try
        {
            await Task.Run(() => PhotonWorkshopWriter.Write(result, path));
            ViewportStatus = $"Exported {System.IO.Path.GetFileName(path)}: {result.LayerCount} layers, {result.VolumeMl:0.##} ml.";
            return true;
        }
        catch (Exception ex)
        {
            ViewportStatus = $"Export failed: {ex.Message}";
            return false;
        }
    }

    /// <summary>Drops a stale slice and returns to the model view.</summary>
    public void InvalidateSlice()
    {
        LastSlice = null;
        PreviewImage = null;
        PreviewLayerText = "";
        SliceSummary = "Scene changed since the last slice.";
        if (ViewMode == ViewMode.Layers) ViewMode = ViewMode.Model;
    }

    partial void OnPreviewLayerChanged(int value) => UpdatePreview();

    private void UpdatePreview()
    {
        var result = LastSlice;
        if (result is null || result.LayerCount == 0)
        {
            PreviewImage = null;
            PreviewLayerText = "";
            return;
        }

        var index = Math.Clamp(PreviewLayer, 0, result.LayerCount - 1);
        var layer = result.Layers[index];
        var w = result.Printer.ResolutionX;
        var h = result.Printer.ResolutionY;

        var bitmap = PreviewImage;
        if (bitmap is null || bitmap.PixelSize.Width != w || bitmap.PixelSize.Height != h)
            bitmap = new WriteableBitmap(new PixelSize(w, h), new Avalonia.Vector(96, 96), PixelFormats.Gray8, AlphaFormat.Opaque);

        using (var fb = bitmap.Lock())
        {
            unsafe
            {
                var dst = (byte*)fb.Address;
                if (fb.RowBytes == w)
                {
                    layer.Decode(w, h, new Span<byte>(dst, w * h));
                }
                else
                {
                    var pixels = new byte[w * h];
                    layer.Decode(w, h, pixels);
                    for (int y = 0; y < h; y++)
                        pixels.AsSpan(y * w, w).CopyTo(new Span<byte>(dst + y * fb.RowBytes, w));
                }
            }
        }

        // Re-assign so bindings see a change even when the same bitmap instance was reused.
        PreviewImage = null;
        PreviewImage = bitmap;
        PreviewLayerText = $"Layer {index + 1} / {result.LayerCount}   Z {layer.Z:0.###} mm   {layer.AreaMm2:0.#} mm²   {result.Settings.ExposureForLayer(index):0.##} s";
    }
}
