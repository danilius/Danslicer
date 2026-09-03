# Danslicer handover

Written 2026-09-03 for a fresh conversation, updated overnight 2026-09-03. Read this, then
`docs/DESIGN.md` for the full design.

## Overnight session 2026-09-03 (while the user slept)

The user approved commit/merge/push and full autonomy mid-session. Everything below is **merged to
`main` and pushed** to the private repo `https://github.com/danilius/Danslicer` (created with the
user's gh credentials; all feature branches pushed too, old milestone branches deleted locally).

1. **Slicing stack-overflow fix** — the crash on large files: `stackalloc` inside the per-triangle
   loop in `MeshSlicer.CollectSegments` blew the 1 MB worker stack (0xC00000FD). Hoisted. Both
   ~1M-triangle test STLs slice (53 MB knocker dragon 18.6 s, 47 MB Drogon 5.1 s).
2. **OBJ import** — reader (v/f, v/vt/vn, negative indices, fan triangulation), `MeshFile`
   dispatch, picker/CLI/argument import; `ImportStl` renamed `ImportMesh`. Verified with the
   roof gripper.
3. **Lay flat on face** — F over a face (or Object menu, then click): picked triangle grows into a
   ≤3° cluster, model rotates face-down and rests exactly on the plate, one undo step. Verified on
   screen (Viper shell 180°, gripper fin compound rotation).
4. **Build-volume check** — slicing now refuses geometry past the plate in X/Y (it was silently
   cropped), like the existing Z checks; CLI reports it cleanly with exit 2.
5. **SpaceMouse (milestone 3 core)** — `ISixAxisInput` + `TdxSpaceMouse` late-bound 3DxWare COM
   backend, 66 Hz UI-thread polling, twist/tilt→orbit, slide→pan, push→zoom, roll locked; status
   bar shows "SpaceMouse". Connects to the real driver. **Motion signs/sensitivity await the
   user's hands**; constants in `ViewportControl` (SpaceMouseOrbitPixels etc.). Buttons and HID
   fallback not done.
6. **Overhang tint (milestone 4 start)** — Object > Overhang Tint colours faces past 45° from
   vertical, yellow at threshold to red on flat undersides, via world-space normals in the mesh
   shader. Verified on a table-shaped test mesh. Also fixed a latent bug: `MenuItem.IsChecked`
   binds one-way by default, so ALL menu checkboxes (gizmos, snapping) never wrote to the view
   model; now `Mode=TwoWay`.
7. **Hide/unhide** — H hides selection (deselects, undoable), Alt+H unhides all; menu items.
8. **Support graph foundation (milestone 5 start)** — `Supports/SupportGraph.cs`: typed nodes and
   segments with origin tags, pinned/hidden/disabled, referential integrity, component queries
   (bracing excluded = one support), unpinned-elements-of-region for regeneration; and
   `SupportSliceGeometry.cs`: analytic capsule cross-sections per layer (ellipse body + cap
   circles, 64-gon in Clipper units), `SectionsAt(graph, z)` ready to union into the slicer.
   Not yet wired into `Slicer.Slice` or any UI.

Also: rotate and scale gizmo drags verified on screen (the outstanding debt from the last session);
gizmo rotate drag committed "Rotate", scale drag committed "Scale", undo clean.

Verification notes for this machine: after the Alt-key foreground trick, press Escape before
typing — the menu bar is armed and F opens File. PowerShell tool calls don't share state; redefine
Add-Type classes with fresh names per call.

9. **Support slicing** — `Slicer.Slice` takes an optional graph; capsule sections union into every
   layer, print height extends to the tallest cap, `Document.Supports` exists and the app passes it.
10. **Manual supports** — press T over the model: a vertical tip-neck-pillar-base tree drops from
   the picked surface point to the plate, one undo step, drawn as depth-tested coloured lines
   (necks yellow, pillars blue, bracing green, tips orange crosses). Hidden elements skip drawing
   but still slice; disabled ones fade and don't slice. Verified on screen.

11. **Support selection, deletion, tip move** — click selects a support element (screen-space pick,
   depth-arbitrated against the model; Shift toggles; selected draws white); Delete removes it
   undoably (a node takes its segments); Esc clears support selection before object selection.
   G with one selected tip drags it along its contact object's surface, re-dropping the simple
   vertical tree live; LMB/Enter commits "Move tip", RMB/Esc cancels. All verified on screen.

Next candidates: whole-support selection (pick the tree, not the element); Shift+H hide unselected
on support elements; SpaceMouse buttons via COM connection points; SpaceMouse sign/sensitivity
tuning with the user; support render meshes (capsules, not lines); region painting and generation
(milestone 6); the test print (mirror-X question) is still the one thing only the user can do.
The 78-test suite is green on `main`.

## What this is

Danslicer is a resin (MSLA) slicer for power users, targeting the Anycubic Photon Mono X first.
C# on .NET 10, Avalonia 12 UI, Silk.NET OpenGL viewport, Clipper2 for polygons. The design
document is the source of truth for architecture, the support system and the milestone order.

## Repository state

- `main` holds milestones one and two, merged and clean.
- Branch `gizmos-and-layer-view` holds the gizmo and main-area layer view work, complete and verified, awaiting the merge decision (see below).
- Older branches `milestone-1-skeleton`, `numpad-emulation`, `milestone-2-first-print` are merged and can be deleted.

Solution `Danslicer.slnx` with projects:

| Project | Purpose |
| --- | --- |
| `src/Danslicer.Core` | Mesh, STL reader, scene, transforms, document and undo stack, expression parser, slicing, `.pwmx` writer and reader. No UI or GPU dependencies. |
| `src/Danslicer.Render` | OpenGL renderer, camera, shaders, line overlay batch. |
| `src/Danslicer.App` | Avalonia application: viewport control, modal transforms, view models, window. |
| `src/Danslicer.Cli` | `info`, `slice`, `inspect` commands over Core. |
| `tests/Danslicer.Tests` | xUnit, 50 tests, all passing at the last commit on `main`. |

Build and test:

```bash
dotnet build -c Debug
dotnet test --no-build
dotnet run --project src/Danslicer.App -- path/to/model.stl
dotnet run --project src/Danslicer.Cli -- slice model.stl -o out.pwmx
```

## Done so far

**Milestone one.** STL import (binary and ASCII), scene objects with transforms, document with
command stack and undo, studio-lit flat-shaded viewport with build plate and grid, turntable camera
with perspective and orthographic modes, click selection, Blender-style G/R/S modal transforms with
X/Y/Z axis and Shift-plane constraints and typed numeric values, expression fields with units in the
properties panel, numpad emulation on the digit row.

**Milestone two.** Mesh slicing to oriented contours, Clipper2 union and XY compensation, scanline
rasteriser with coverage anti-aliasing quantised to 16 grey levels, pw0Img run-length codec, parallel
slicing with layers kept RLE-encoded, Photon Workshop format version 516 writer plus a validation
reader, print settings panel, Slice (Ctrl+R), Export (Ctrl+E), layer preview.

**Not yet done from milestone two:** the actual test print. The writer mirrors layer images in X
because the published Mono X profiles do, and only a print of an asymmetric part confirms it. An
L-shaped test part was given to the user. If the print is mirrored, flip `MirrorX` in
`PrinterDefinition.PhotonMonoX`.

## Branch `gizmos-and-layer-view` (complete, awaiting the user's merge decision)

User requests from 2026-09-03 that drove this branch:

1. The main window area is used for positioning, support generation, slicing and layer preview, not
   the side panel. The centre now has a header strip and two modes: the 3D model view and the 2D layer
   view. `Tab` toggles, slicing switches to layers, any geometry change drops the stale slice and returns
   to the model view. The right panel keeps only object and print settings.
2. Visual gizmos for move, rotate and scale with two modes, free and snapping.
3. The pivot for move, rotate and scale is the centre of the selection's bounding box.

Implementation:

- `src/Danslicer.App/Editing/ModalTransform.cs`: pivot is the selection bounding-box centre; `Snap`
  flag with `MoveStep` 1 mm, `RotateStepDegrees` 5, `ScaleStep` 0.1; `Begin` takes an optional axis and
  plane so a gizmo handle can start it directly.
- `src/Danslicer.App/Editing/Gizmo.cs`: geometry, hit testing and overlay lines for move arrows and plane
  squares, rotation rings, scale cubes and a uniform-scale circle. Constant 90 px on screen. Hovered
  handle highlights yellow; a drag hands off to the modal transform and confirms on release.
- `src/Danslicer.App/Controls/ViewportControl.cs`: gizmo toggles and snap as styled properties bound to
  the view model, hover hit testing, drag handling, Ctrl inverts snapping while held, Shift+Tab toggles
  it, Tab raises `ToggleViewRequested`.
- `src/Danslicer.App/Controls/LayerPreviewControl.cs`: 2D layer view with zoom about the cursor, pan,
  Ctrl+wheel and Page Up/Down to step layers, Home to fit, Tab to toggle view.
- `MainViewModel`: `ViewMode`, gizmo and snap toggles, full-resolution `Gray8` layer bitmap.
- `MainWindow.axaml`: header strip with gizmo toggles, snap toggle and view switch; layer slider under
  the layer view; Object menu gains gizmo and snapping toggles.

Verified on screen with synthetic input: X-arrow drag moved the box with the live readout and axis
guide and committed one undo step; snapping toggled from the header; slicing switched to the layer
view and Tab switched back; a move after slicing invalidated the slice and disabled the Layers button.
Rotate and scale gizmo drags were not exercised by synthetic input; the user offered to test on screen.

Next after this branch: milestone three, SpaceMouse Pro via the 3DxWare COM interface.

## How to verify UI changes on this machine

Synthetic input from PowerShell works but has traps, recorded in memory as well:

- Force the window to the foreground with the Alt-key trick (`keybd_event 0x12` down and up) before
  `SetForegroundWindow`, and only send keys after `GetForegroundWindow()` equals the window handle.
  Otherwise keystrokes land in whatever window is in front, which has been the Claude Code app.
- Window position varies per launch, so derive click points from `GetWindowRect`. Launch with
  `-WindowStyle Maximized` and `ShowWindow(h, 3)` for predictable coordinates.
- Click inside the viewport before typing so it has keyboard focus.
- Set `DANSLICER_TRACE=1` and run `dotnet Danslicer.App.dll` with stderr redirected to log pointer and
  key events from the viewport.
- Kill the app before rebuilding; the exe and dll are locked while it runs.
- Capture the screen with `System.Drawing.Graphics.CopyFromScreen`, then view the PNG.
- Test meshes are generated by small Python scripts into the scratchpad, not stored in the repo. A torus
  plus box scene is the usual test.

## Decisions and lessons

- Avalonia `OpenGlControlBase` is invisible to hit testing until `Render` fills its bounds with a
  transparent brush. Without that the viewport gets no pointer events.
- `MenuItem.HotKey` only registers once its sub-menu has been opened. All shortcuts are explicit
  `Window.KeyBindings`, with import and export bindings added in code-behind.
- Quaternion composition uses `Quaternion.Concatenate(first, second)` throughout, verified by a test.
  Euler order is X then Y then Z.
- Contour orientation comes from face normals: walking along cross(+Z, normal) keeps the solid on the
  left, giving counter-clockwise outers and clockwise holes.
- The rasteriser writes 16 grey levels so the layer table's lit-pixel count matches the encoded image.
- Avalonia 12 is used, not 11 as first planned, because that is what the templates produce.
- UVtools is AGPL. It was read as a format specification only; nothing was copied or linked.

## User preferences

- Blender conventions everywhere. "Blender, not Word." No simplified mode, no wizards.
- Feature branch for every piece of work, named for the work. Merging is always the user's call:
  propose it when a branch is verified, never merge unprompted.
- Keyboard has no numpad: every numpad binding must exist on the digit row too.
- SpaceMouse Pro with 3DxWare installed. SpaceMouse support is milestone three, next after this branch.
- The user is willing to test on screen when asked and will do the real print later.
