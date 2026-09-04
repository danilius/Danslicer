using Avalonia.Input;
using Danslicer.Core.Config;

namespace Danslicer.App.Configuration;

/// <summary>A stable window-level action and its shipping gesture.</summary>
public sealed record WindowKeymapAction(string Id, string DisplayName, string DefaultGesture);

/// <summary>
/// The single source of truth for application-level shortcuts. Viewport-modal keys such as
/// G/R/S/T/H/F deliberately remain owned by <c>ViewportControl</c> and are not listed here.
/// </summary>
public static class WindowKeymap
{
    public const string Undo = "edit.undo";
    public const string Redo = "edit.redo";
    public const string RedoAlternate = "edit.redo-alternate";
    public const string Delete = "edit.delete";
    public const string DuplicateObjects = "object.duplicate";
    public const string MirrorX = "object.mirror-x";
    public const string MirrorY = "object.mirror-y";
    public const string MirrorZ = "object.mirror-z";
    public const string DropToPlate = "object.drop-to-plate";
    public const string GenerateSupports = "support.generate";
    public const string SelectAll = "edit.select-all";
    public const string HideUnselectedSupports = "support.hide-unselected";
    public const string Slice = "print.slice";
    public const string SaveProject = "file.save-project";
    public const string SaveProjectAs = "file.save-project-as";
    public const string OpenProject = "file.open-project";
    public const string ImportMesh = "file.import-mesh";
    public const string ExportPrint = "file.export-print";
    public const string Preferences = "edit.preferences";

    public static IReadOnlyList<WindowKeymapAction> Actions { get; } =
    [
        new(Undo, "Undo", "Ctrl+Z"),
        new(Redo, "Redo", "Ctrl+Shift+Z"),
        new(RedoAlternate, "Redo (alternate)", "Ctrl+Y"),
        new(Delete, "Delete", "Delete"),
        new(DuplicateObjects, "Duplicate Objects", "Shift+D"),
        new(MirrorX, "Mirror Objects on X", "Ctrl+Shift+1"),
        new(MirrorY, "Mirror Objects on Y", "Ctrl+Shift+2"),
        new(MirrorZ, "Mirror Objects on Z", "Ctrl+Shift+3"),
        new(DropToPlate, "Drop to Plate", "Ctrl+D"),
        new(GenerateSupports, "Generate Supports", "Ctrl+G"),
        new(SelectAll, "Select All", "Ctrl+A"),
        new(HideUnselectedSupports, "Hide Unselected Supports", "Shift+H"),
        new(Slice, "Slice", "Ctrl+R"),
        new(SaveProject, "Save Project", "Ctrl+S"),
        new(SaveProjectAs, "Save Project As", "Ctrl+Shift+S"),
        new(OpenProject, "Open Project", "Ctrl+O"),
        new(ImportMesh, "Import Mesh", "Ctrl+I"),
        new(ExportPrint, "Export Print File", "Ctrl+E"),
        new(Preferences, "Preferences", "Ctrl+OemComma"),
    ];

    public static WindowKeymapAction GetAction(string actionId) =>
        Actions.First(action => action.Id == actionId);

    public static KeyGesture GetGesture(UserConfig config, string actionId)
    {
        var action = GetAction(actionId);
        if (config.KeymapOverrides.TryGetValue(actionId, out var value) &&
            TryParse(value, out var overridden))
            return overridden;
        return Parse(action.DefaultGesture);
    }

    public static string GetGestureText(UserConfig config, string actionId) =>
        Format(GetGesture(config, actionId));

    public static KeyGesture Parse(string value) => KeyGesture.Parse(value);

    public static bool TryParse(string? value, out KeyGesture gesture)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                gesture = null!;
                return false;
            }
            gesture = Parse(value);
            return true;
        }
        catch (FormatException)
        {
            gesture = null!;
            return false;
        }
    }

    /// <summary>Invariant text is stable in JSON and accepted by <see cref="Parse"/>.</summary>
    public static string Format(KeyGesture gesture) => gesture.ToString("g", null);

    /// <summary>
    /// Applies an override unless another action already owns the effective gesture. Returning to
    /// the shipping gesture removes the override instead of persisting redundant state.
    /// </summary>
    public static bool TrySetGesture(UserConfig config, string actionId, KeyGesture gesture,
        out WindowKeymapAction? conflict)
    {
        var action = GetAction(actionId);
        foreach (var other in Actions)
        {
            if (other.Id == actionId) continue;
            if (SameGesture(GetGesture(config, other.Id), gesture))
            {
                conflict = other;
                return false;
            }
        }

        if (SameGesture(Parse(action.DefaultGesture), gesture))
            config.KeymapOverrides.Remove(actionId);
        else
            config.KeymapOverrides[actionId] = Format(gesture);
        conflict = null;
        return true;
    }

    public static void Reset(UserConfig config, string actionId) =>
        config.KeymapOverrides.Remove(GetAction(actionId).Id);

    public static void ResetAll(UserConfig config) => config.KeymapOverrides.Clear();

    private static bool SameGesture(KeyGesture left, KeyGesture right) =>
        left.Key == right.Key && left.KeyModifiers == right.KeyModifiers;
}
