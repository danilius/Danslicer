# Danslicer handover

Written 2026-09-03 for a fresh conversation, updated overnight 2026-09-03. Read this, then
`docs/DESIGN.md` for the full design.

## Overnight session 2026-09-03 (while the user slept)

The user tested the gizmo branch, approved commit/merge/push; `gizmos-and-layer-view` is merged to
`main`. **Push is blocked**: no git remote exists and the permission classifier refused `gh repo create`,
so the user must create the remote (a private repo was intended) and push.

Three new branches, each built, tested and verified, all merging cleanly (proven on
`overnight-integration`, which is `main` + all three, 58 tests passing — the user can merge that
or the branches individually; merges are the user's call):

1. `fix-slicing-stack-overflow` — the crash the user hit slicing a large file: `stackalloc` inside
   the per-triangle loop in `MeshSlicer.CollectSegments` blew the 1 MB worker stack (0xC00000FD).
   Hoisted out of the loop. Both ~1M-triangle test STLs now slice (53 MB knocker dragon 18.6 s,
   47 MB Drogon 5.1 s).
2. `obj-import` — Wavefront OBJ reader (v/f lines, v/vt/vn and negative indices, fan triangulation),
   `MeshFile` extension dispatch, file picker/CLI/argument import accept both formats,
   `ImportStl` renamed `ImportMesh`. Verified with `test files/roof gripper T2.obj`.
3. `lay-flat-on-face` — press F over a face (or Object > Lay Flat on Face, then click): the picked
   triangle grows into a connected cluster within 3° of its normal, the model rotates so that face
   points down and rests exactly on the plate. One undo step. Verified on screen: Viper shell
   flipped 180°; roof gripper fin face gave a compound rotation with the face planted.

A `test files/` folder (gitignored) holds the user's large STLs and the roof gripper OBJ.

Known issue spotted, not fixed: slicing does not warn when the model exceeds the build volume
(knocker dragon footprint is 158×263 mm against the Mono X's 192×120 mm plate yet slices happily).

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
