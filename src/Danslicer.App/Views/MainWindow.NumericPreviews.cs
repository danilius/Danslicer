using Danslicer.App.Controls.Refresh;
using Danslicer.App.ViewModels;
using Danslicer.App.Configuration;
using Danslicer.Core.Config;

namespace Danslicer.App.Views;

public partial class MainWindow
{
    private void InitializeNumericPreviews()
    {
        var config = new ConfigViewModel();
        void Redraw() { SyncRenderPathMenu(); Viewport.RequestRedraw(); }
        config.ViewportPreviewed += Redraw;
        config.ViewportSaved += Redraw;
        PopAoStrength.BeginPreview = () => config.BeginNumericPreview(nameof(config.AmbientOcclusionStrength));
        PopAoRadius.BeginPreview = () => config.BeginNumericPreview(nameof(config.AmbientOcclusionRadiusMm));
        PopShadowStrength.BeginPreview = () => config.BeginNumericPreview(AppConfig.Current.Viewport.ModelShadows == ModelShadowMode.Presentation
            ? nameof(config.PresentationShadowStrength) : nameof(config.WorkingShadowStrength));
        PopShadowSoftness.BeginPreview = () => config.BeginNumericPreview(AppConfig.Current.Viewport.ModelShadows == ModelShadowMode.Presentation
            ? nameof(config.PresentationShadowSoftnessMm) : nameof(config.WorkingShadowSoftnessMm));
        Closing += (_, _) => { ScrubField.CancelActive(); IsolationSlider.CancelDrag(); SlicePreviewSlider.CancelDrag(); };
        Deactivated += (_, _) => { ScrubField.CancelActive(); IsolationSlider.CancelDrag(); SlicePreviewSlider.CancelDrag(); };
    }
}
