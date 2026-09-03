using CommunityToolkit.Mvvm.Input;
using Danslicer.App.Configuration;
using Danslicer.Core;
using Danslicer.Core.Config;
using Danslicer.Core.Geometry;
using Danslicer.Core.Scene;
using Danslicer.Core.Supports;
using Danslicer.Core.Supports.Generation;
using Danslicer.Core.Supports.Routing;

namespace Danslicer.App.ViewModels;

/// <summary>A transactionally isolated support-preset edit session and its private preview scene.</summary>
public sealed class SupportPresetEditorViewModel : ViewModelBase, IDisposable
{
    private readonly string _presetName;
    private readonly Func<SceneObject?> _currentSelection;
    private readonly SupportConfig _editingSettings;
    private CancellationTokenSource? _regenerationCancellation;
    private Document _previewDocument = new();
    private int _selectedSampleIndex;
    private int _selectedDisplayModeIndex;
    private string _statusText = "Waiting to generate preview…";
    private string _statisticsText = "No preview statistics yet.";
    private double _generationProgress;
    private bool _isGenerating;
    private SupportDisplayConfig _previewSupportDisplay = new();

    public SupportPresetEditorViewModel(string presetName, Func<SceneObject?> currentSelection)
    {
        _presetName = presetName;
        _currentSelection = currentSelection;
        var preset = AppConfig.Current.FindSupportPreset(presetName)
            ?? throw new ArgumentException($"Support preset '{presetName}' does not exist.", nameof(presetName));
        _editingSettings = preset.Settings with { };
        Settings = new ConfigViewModel(_editingSettings, ScheduleRegeneration);
        SaveCommand = new RelayCommand(Save);
        CancelCommand = new RelayCommand(() => CloseRequested?.Invoke(false));
        _selectedSampleIndex = Math.Max(0,
            SupportPreviewShapes.Names.ToList().IndexOf(
                AppConfig.Current.Viewport.SupportPresetPreviewSample));
        ScheduleRegeneration();
    }

    public string Title => $"Edit support preset — {_presetName}";
    public ConfigViewModel Settings { get; }
    public IReadOnlyList<string> SampleNames => SupportPreviewShapes.Names;
    public IReadOnlyList<string> DisplayModes { get; } = ["Full", "Transparent"];
    public IRelayCommand SaveCommand { get; }
    public IRelayCommand CancelCommand { get; }

    public Document PreviewDocument
    {
        get => _previewDocument;
        private set
        {
            _previewDocument = value;
            OnPropertyChanged();
            PreviewDocumentChanged?.Invoke();
        }
    }

    public SupportDisplayConfig PreviewSupportDisplay
    {
        get => _previewSupportDisplay;
        private set
        {
            _previewSupportDisplay = value;
            OnPropertyChanged();
        }
    }

    public int SelectedSampleIndex
    {
        get => _selectedSampleIndex;
        set
        {
            if (value < 0 || value >= SampleNames.Count || value == _selectedSampleIndex) return;
            _selectedSampleIndex = value;
            OnPropertyChanged();
            AppConfig.Current.Viewport.SupportPresetPreviewSample = SampleNames[value];
            AppConfig.Save();
            ScheduleRegeneration();
        }
    }

    public int SelectedDisplayModeIndex
    {
        get => _selectedDisplayModeIndex;
        set
        {
            if (value is < 0 or > 1 || value == _selectedDisplayModeIndex) return;
            _selectedDisplayModeIndex = value;
            OnPropertyChanged();
            PreviewSupportDisplay = PreviewSupportDisplay with
            {
                Mode = value == 1 ? SupportDisplayMode.Transparent : SupportDisplayMode.Full,
            };
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set { _statusText = value; OnPropertyChanged(); }
    }

    public string StatisticsText
    {
        get => _statisticsText;
        private set { _statisticsText = value; OnPropertyChanged(); }
    }

    public double GenerationProgress
    {
        get => _generationProgress;
        private set { _generationProgress = value; OnPropertyChanged(); }
    }

    public bool IsGenerating
    {
        get => _isGenerating;
        private set { _isGenerating = value; OnPropertyChanged(); }
    }

    public event Action? Saved;
    public event Action<bool>? CloseRequested;
    public event Action? PreviewDocumentChanged;

    private void Save()
    {
        var config = AppConfig.Current;
        var preset = config.FindSupportPreset(_presetName);
        if (preset is null)
        {
            StatusText = "This preset was deleted while the editor was open.";
            return;
        }
        preset.Settings = _editingSettings with { };
        config.Supports = _editingSettings with { };
        config.ActiveSupportPresetName = preset.Name;
        AppConfig.Save();
        Saved?.Invoke();
        CloseRequested?.Invoke(true);
    }

    private void ScheduleRegeneration()
    {
        _regenerationCancellation?.Cancel();
        var cancellation = new CancellationTokenSource();
        _regenerationCancellation = cancellation;
        _ = RegenerateAfterDelayAsync(cancellation);
    }

    private async Task RegenerateAfterDelayAsync(CancellationTokenSource cancellation)
    {
        var token = cancellation.Token;
        SupportGenerationBatch? batch = null;
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(300), token);
            var sampleName = SampleNames[SelectedSampleIndex];
            var previewObject = CreatePreviewObject(sampleName);
            if (previewObject is null)
            {
                StatusText = "Select an object in the main document to preview Current selection.";
                StatisticsText = "No preview statistics yet.";
                return;
            }

            var document = new Document { SupportSettings = _editingSettings with { } };
            document.AddObject(previewObject);
            PreviewDocument = document;
            IsGenerating = true;
            GenerationProgress = 0;
            StatusText = $"Generating {sampleName} preview…";
            var progress = new Progress<SupportGenerationProgress>(update =>
            {
                if (token.IsCancellationRequested) return;
                GenerationProgress = update.Fraction * 0.8;
                StatusText = $"{update.Stage}: {update.Completed} / {update.Total}";
            });
            var request = document.CaptureSupportGeneration(previewObject, seed: 0);
            var prepared = await Task.Run(
                () => Document.ComputeSupportGeneration(request, token, progress), token);
            token.ThrowIfCancellationRequested();
            batch = new SupportGenerationBatch(document.Supports, document.History, prepared, 64);
            while (!batch.IsFinished)
            {
                token.ThrowIfCancellationRequested();
                batch.CommitNextBatch();
                GenerationProgress = 0.8 + batch.Progress * 0.2;
                await Task.Yield();
            }
            batch.Complete();
            var measurements = SupportGraphStatistics.Calculate(document.Supports);
            StatisticsText = FormatStatistics(prepared.Summary, measurements);
            StatusText = "Preview ready.";
            GenerationProgress = 1;
        }
        catch (OperationCanceledException)
        {
            batch?.Cancel();
        }
        catch (Exception ex)
        {
            batch?.Cancel();
            StatusText = $"Preview failed: {ex.Message}";
        }
        finally
        {
            if (ReferenceEquals(_regenerationCancellation, cancellation))
            {
                IsGenerating = false;
                _regenerationCancellation = null;
            }
            cancellation.Dispose();
        }
    }

    private SceneObject? CreatePreviewObject(string sampleName)
    {
        if (sampleName != "Current selection")
            return new SceneObject(sampleName, SupportPreviewShapes.Create(sampleName));
        var selected = _currentSelection();
        if (selected is null) return null;
        var mesh = new Mesh(selected.Mesh.Positions.ToArray(), selected.Mesh.Indices.ToArray());
        return new SceneObject(selected.Name, mesh)
        {
            Transform = selected.Transform,
            RenderState = selected.RenderState,
        };
    }

    private static string FormatStatistics(SupportGenerationSummary summary,
        SupportGraphStatistics measurements)
    {
        var refusals = summary.RefusalReasons.Count == 0
            ? "none"
            : string.Join(", ", summary.RefusalReasons.OrderBy(pair => pair.Key)
                .Select(pair => $"{FriendlyReason(pair.Key)} {pair.Value}"));
        return $"Candidates {summary.CandidateCount}  •  Routed {summary.GeneratedTipCount}  •  " +
               $"Refused {summary.UnroutedTipCount} ({refusals})  •  Minis {measurements.MiniCount}  •  " +
               $"Bases {measurements.BaseCount}  •  Max lean {measurements.MaxLeanDegrees:0.0}°  •  " +
               $"Estimated volume {measurements.EstimatedVolumeMm3:0.0} mm³";
    }

    private static string FriendlyReason(RoutingFailureReason reason) => reason switch
    {
        RoutingFailureReason.NoReachableGridPoint => "no grid point",
        RoutingFailureReason.NoBranchEndInRange => "no branch in range",
        RoutingFailureReason.NoClearStep => "no clear step",
        RoutingFailureReason.ContactBlocked => "contact blocked",
        RoutingFailureReason.NoLanding => "no landing",
        RoutingFailureReason.BelowPlate => "below plate",
        _ => reason.ToString(),
    };

    public void Dispose()
    {
        _regenerationCancellation?.Cancel();
        _regenerationCancellation = null;
    }
}
