# Preferences

Open **Edit → Preferences** (`Ctrl+,`). Use search to locate settings. Settings generally apply immediately and persist; preset editors with Save/Cancel have their own commit workflow.

## Appearance and workspace

Choose a theme in Preferences. The theme catalog includes Classic, Slate, Techy, Carbide, and Blender-inspired themes. The floating toolbar can show labels; resizing, popout positions, and section arrangements are workspace preferences.

## Viewport settings

| Setting | Effect |
| --- | --- |
| Render path | Deferred enables screen-space effects; Classic is the fallback. A failed deferred pipeline can fall back for the session. |
| Shading | Studio lighting or Clay, Metal, and Pearl MatCaps (material/light appearances). |
| Model shadows | Off, Working, or Presentation. Working is subtler; Presentation uses stronger soft shadows. |
| Shadow strength | Darkness of shadows; Working and Presentation retain separate values. |
| Shadow softness (mm) | Softness of shadow edges, separately for each enabled mode. |
| Contact depth (AO) | Local proximity shading on opaque geometry in Deferred. |
| AO strength / radius | How much proximity shading to apply and its sampling distance in mm. |
| Plate reflections / strength | Reflections on the plate and their intensity. |
| Plate shadows | Model shadows on the build plate. |
| Plate perimeter below | Border visibility when looking from below; 0 hides it. Plate surface, grid, and reflections disappear below. |
| Cavity shading | Brighten ridges and darken creases in Deferred. |
| Cavity ridge / valley strength | Independent amounts for raised and recessed details. |
| Cavity radius (px) | Screen-space distance over which those details are sampled. |
| Outlines / strength / border width | Surface boundaries in Deferred; width is in pixels. |
| FXAA | Smooth viewport edges in the final Deferred image. Does not affect printed layers. |
| Wireframe | Overlay mesh triangle edges on either render path. |
| Cap interior / style | Close horizontal viewport cuts. Sliced computes exact cap geometry; Painted uses Deferred screen-space drawing and falls back to Sliced on Classic. |
| Overhang angle | Threshold for the display tint, measured from vertical. Independent of support generation's threshold. |
| Overhang colours A / B | Checker colours, entered as hexadecimal RGB such as `#FACC26`. |
| Overhang checker size | Size of checker squares on the model, in mm. |
| View cube / size | Show and size the orientation cube in pixels. Click for aligned views; drag to orbit. |
| Support gizmo size / line width | Size and stroke width of support edit handles, in screen pixels. |

Some switches are in **View settings** or the **View** menu, while detailed tuning is in Preferences. All of these are display settings, not geometry or exposure settings.

## Keymap

Click the shortcut capture button for an application action and enter a gesture. Conflicts are rejected. **Reset** restores one default; **Reset all** restores the application keymap. Viewport modal keys such as G/R/S/T/H/F and axis constraints remain fixed. See [Keyboard shortcuts](Keyboard-shortcuts.md).

## External tools

Set **UVtools executable** to the installed program, or use **Browse…**. Slicing's **UV check** opens an exported file in that program. Invalid paths or no current exported file leave the action unavailable.

## SpaceMouse (Windows)

| Setting | Meaning |
| --- | --- |
| Orbit sensitivity | Multiplier on cap tilt/twist rotation speed. |
| Pan sensitivity | Multiplier on lateral motion. |
| Zoom sensitivity | Multiplier on push/pull zoom. |
| Deadzone | Fraction of full deflection ignored to prevent resting drift. |
| Invert orbit yaw / pitch | Reverse twist or tilt separately. |
| Invert pan X / Y | Reverse horizontal or vertical translation separately. |
| Invert zoom | Reverse push/pull. |

Sensitivity 1.0 is the untuned default. Changes apply while moving the cap. The Raw Input backend is Windows-only; other platforms omit its connection/status indicator. The repository's `docs/SPACEMOUSE.md` has device diagnostics.

## Saved preferences

User configuration is in the operating system's application-data directory under `Danslicer/config.json`; Windows normally uses `%APPDATA%\Danslicer\config.json`. Workspace UI state is stored alongside it in `workspace-ui.json`. Project files separately carry their scene and print data. Back up configuration before manually editing it, and close the app first.
