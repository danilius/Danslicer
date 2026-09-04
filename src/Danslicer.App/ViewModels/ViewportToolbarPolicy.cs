using Danslicer.Core;

namespace Danslicer.App.ViewModels;

public enum ViewportTool
{
    Objects,
    Supports,
    Visibility,
    Rafts,
}

/// <summary>Keeps the floating viewport tools deterministic and independently testable.</summary>
public static class ViewportToolbarPolicy
{
    private static readonly IReadOnlyList<ViewportTool> ObjectTools =
        [ViewportTool.Objects];

    private static readonly IReadOnlyList<ViewportTool> SupportTools =
    [
        ViewportTool.Objects,
        ViewportTool.Supports,
        ViewportTool.Visibility,
        ViewportTool.Rafts,
    ];

    public static IReadOnlyList<ViewportTool> ToolsFor(WorkspaceMode mode) => mode switch
    {
        WorkspaceMode.Support => SupportTools,
        _ => ObjectTools,
    };

    public static bool CanSelectObjects(WorkspaceMode mode) => mode == WorkspaceMode.Layout;
}
