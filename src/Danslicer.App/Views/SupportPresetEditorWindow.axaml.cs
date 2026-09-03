using Avalonia.Controls;
using Danslicer.App.Configuration;
using Danslicer.App.ViewModels;

namespace Danslicer.App.Views;

public partial class SupportPresetEditorWindow : Window
{
    private readonly SupportPresetEditorViewModel _viewModel;

    public SupportPresetEditorWindow() : this(new SupportPresetEditorViewModel(
        AppConfig.Current.ActiveSupportPresetName, () => null))
    {
    }

    public SupportPresetEditorWindow(SupportPresetEditorViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        viewModel.PreviewDocumentChanged += FramePreview;
        viewModel.CloseRequested += OnCloseRequested;
        Opened += OnOpened;
        Closed += OnClosed;
        Configuration.WindowStatePersistence.Track(this, "support-preset-editor");
    }

    private void OnOpened(object? sender, EventArgs e) => PreviewSurface.FrameAll();

    private void FramePreview() => PreviewSurface.FrameAll();

    private void OnCloseRequested(bool saved) => Close();

    private void OnClosed(object? sender, EventArgs e)
    {
        _viewModel.PreviewDocumentChanged -= FramePreview;
        _viewModel.CloseRequested -= OnCloseRequested;
        _viewModel.Dispose();
    }
}
