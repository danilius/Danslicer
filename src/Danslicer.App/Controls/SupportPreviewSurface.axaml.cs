using Avalonia;
using Avalonia.Controls;
using Danslicer.Core;
using Danslicer.Core.Config;

namespace Danslicer.App.Controls;

/// <summary>
/// The single seam between the preset editor and its rendering strategy. If the dual-context
/// probe fails on a target driver, the main-viewport modal fallback is confined to this host.
/// </summary>
internal interface ISupportPreviewSurface
{
    void FrameAll();
}

public partial class SupportPreviewSurface : UserControl, ISupportPreviewSurface
{
    public static readonly StyledProperty<Document?> DocumentProperty =
        AvaloniaProperty.Register<SupportPreviewSurface, Document?>(nameof(Document));

    public static readonly StyledProperty<SupportDisplayConfig> SupportDisplayProperty =
        AvaloniaProperty.Register<SupportPreviewSurface, SupportDisplayConfig>(nameof(SupportDisplay),
            new SupportDisplayConfig());

    public SupportPreviewSurface() => InitializeComponent();

    public Document? Document
    {
        get => GetValue(DocumentProperty);
        set => SetValue(DocumentProperty, value);
    }

    public SupportDisplayConfig SupportDisplay
    {
        get => GetValue(SupportDisplayProperty);
        set => SetValue(SupportDisplayProperty, value);
    }

    public void FrameAll() => Viewport.FrameAll();
}
