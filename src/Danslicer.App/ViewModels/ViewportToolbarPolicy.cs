using Danslicer.Core;

namespace Danslicer.App.ViewModels;

public enum ViewportTool
{
    Objects,
    Supports,
    IslandSupport,
    IslandDetection,
    Visibility,
    Rafts,
    UvtoolsCheck,
}

public enum ViewportPopupCloseTrigger
{
    HeaderButton,
    Escape,
    OutsidePointer,
    ContentAction,
    AnotherPopup,
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
        ViewportTool.IslandSupport,
        ViewportTool.IslandDetection,
        ViewportTool.Visibility,
        ViewportTool.Rafts,
    ];

    private static readonly IReadOnlyList<ViewportTool> SlicingTools =
        [ViewportTool.Objects, ViewportTool.UvtoolsCheck];

    public static IReadOnlyList<ViewportTool> ToolsFor(WorkspaceMode mode) => mode switch
    {
        WorkspaceMode.Support => SupportTools,
        WorkspaceMode.Slicing => SlicingTools,
        _ => ObjectTools,
    };

    public static bool IsAvailable(ViewportTool tool, WorkspaceMode mode) =>
        ToolsFor(mode).Contains(tool);

    /// <summary>
    /// Layout selects the object to arrange; Support selects the object to support. Slicing has
    /// no object-level operations, so its list stays read-only.
    /// </summary>
    public static bool CanSelectObjects(WorkspaceMode mode) =>
        mode is WorkspaceMode.Layout or WorkspaceMode.Support;

    public static bool ShouldClosePopup(ViewportPopupCloseTrigger trigger) => trigger is
        ViewportPopupCloseTrigger.HeaderButton or ViewportPopupCloseTrigger.Escape
        or ViewportPopupCloseTrigger.AnotherPopup;
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

/// <summary>
/// The viewport pop-outs are mutually exclusive. They are light-dismiss-free panels floating
/// over the same viewport, so a second one opened from the toolbar would sit on top of the
/// first; opening one closes whichever was open, and clicking the open one's own icon still
/// just closes it.
/// </summary>
public sealed class ViewportPopupGroup(params ViewportPopupState[] states)
{
    private readonly IReadOnlyList<ViewportPopupState> _states = states;

    public IReadOnlyList<ViewportPopupState> States => _states;

    public void Toggle(ViewportPopupState state)
    {
        var opening = !state.IsOpen;
        CloseAll();
        if (opening) state.Toggle();
    }

    public void CloseAll()
    {
        foreach (var state in _states) state.Close(ViewportPopupCloseTrigger.AnotherPopup);
    }
}
