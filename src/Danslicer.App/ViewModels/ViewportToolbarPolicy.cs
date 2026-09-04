using Danslicer.Core;

namespace Danslicer.App.ViewModels;

public enum ViewportTool
{
    Objects,
    Supports,
    Visibility,
    Rafts,
}

public enum ViewportPopupCloseTrigger
{
    HeaderButton,
    Escape,
    OutsidePointer,
    ContentAction,
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

    public static bool IsAvailable(ViewportTool tool, WorkspaceMode mode) =>
        ToolsFor(mode).Contains(tool);

    public static bool CanSelectObjects(WorkspaceMode mode) => mode == WorkspaceMode.Layout;

    public static bool ShouldClosePopup(ViewportPopupCloseTrigger trigger) => trigger is
        ViewportPopupCloseTrigger.HeaderButton or ViewportPopupCloseTrigger.Escape;
}

/// <summary>
/// Keeps a toolbar pop-out's session open state separate from its mode-dependent visibility.
/// </summary>
public sealed class ViewportPopupState(ViewportTool tool)
{
    public ViewportTool Tool { get; } = tool;
    public bool IsOpen { get; private set; }

    public void Toggle() => IsOpen = !IsOpen;

    public void Close(ViewportPopupCloseTrigger trigger)
    {
        if (ViewportToolbarPolicy.ShouldClosePopup(trigger)) IsOpen = false;
    }

    public bool IsVisible(WorkspaceMode mode) =>
        IsOpen && ViewportToolbarPolicy.IsAvailable(Tool, mode);
}
