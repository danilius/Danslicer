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
    /// <summary>Layout: the object's transform fields, formerly the right-hand panel (user, 2026-09-08).</summary>
    Transform,
    /// <summary>Support: a button for every guided-placement key (user rule 2026-09-08).</summary>
    Guided,
    /// <summary>Support: Generate Supports as a one-click toolbar button (split out 2026-09-09).</summary>
    Generate,
    /// <summary>Support: parenting, bracing and edit mode — what shapes supports already placed.</summary>
    Structure,
    /// <summary>Support: region painting, which decides where generation may place contacts.</summary>
    Region,
    /// <summary>Layout: import a mesh as a new object (user, 2026-09-09).</summary>
    AddObject,
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
        [ViewportTool.Objects, ViewportTool.AddObject, ViewportTool.Transform];

    // The Supports pop-out was overloaded (user, 2026-09-08): it now holds the settings only,
    // and its functions are toolbar buttons of their own, in working order top to bottom.
    private static readonly IReadOnlyList<ViewportTool> SupportTools =
    [
        ViewportTool.Objects,
        ViewportTool.Supports,
        ViewportTool.Generate,
        ViewportTool.Guided,
        ViewportTool.Structure,
        ViewportTool.Region,
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
