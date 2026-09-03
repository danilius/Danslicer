# Danslicer handover

Written 2026-09-03 for a fresh conversation and updated through the day; the FIRST section
(night) supersedes all older state notes below. Read this, then `docs/DESIGN.md` for the
full design, then `docs/SUPPORT-GEOMETRY-SPEC.md` for the user's dictated support spec.

## State as of 2026-09-03 night — READ THIS SECTION FIRST

**main is at `6228f40`, 242 green tests, pushed** (with branches `grid-routing-prototype`
and `spacemouse-buttons`). Merged tonight with the user's approval: ChatGPT's whole
brief 10/9/8/11 queue (Shift+H whole-tree hide; model landing disabled by default —
honest refusals only, Drogon top-down 343/1113; `PenetrationDepth` as the single
embedding-depth name, default 0, CLI-exposed; the seated benchmark matrix in BENCHMARKS.md
that Grok never wrote — seated gripper top-down 5.9 s / 52 of 445 where unseated was killed
at 39 min; non-blocking batched generation with progress bar + cancel-rollback + one undo
step; Layout/Support/Slicing workspaces, mode-dependent Ctrl+A, marquee), and Claude's own
`spacemouse-buttons` branch (Claude implements again now — own worktree
`F:\Git Repos\Danslicer-claude`, never files in ChatGPT's queue).

**SpaceMouse buttons are DONE and accepted** — user: "leave the SpaceMouse for now, it has
all the functionality I require." `_IKeyboardEvents` COM sink in `TdxSpaceMouse` (GUID/
DISPIDs verified against the installed typelib, Advise probed live), drained in the
viewport poll. Fit=frame-all, T/R/F=views (codes 3/5/6 confirmed on hardware), Rotation=
device-only rotation lock in the status bar, device Esc=keyboard-Esc chain. Codes 31 and
32 were observed from unbound buttons on the user's unit and are recorded for any future
binding pass; Menu/1-4 stay unbound by choice.

**User screen-tested the merged queue.** Everything passes EXCEPT: the marquee draws
nothing (bug), and Ctrl+A inside a text box selected all supports instead of text (window
KeyBinding preemption). Both, plus a workspace-semantics redesign the user dictated, are
in **`ChatGPT/INSTRUCTIONS-12.md`** (the current brief): Support mode = objects neither
selectable nor movable; Layout mode = supports unselectable and rigidly carried by object
transforms (even if it ruins them); Tab cycles Layout→Support→Slicing (Slicing in the
cycle only when sliced, but the Slicing workspace itself is ALWAYS available); the right
panel loses its Object|Print tabs and becomes mode-specific (print controls live in
Slicing); left/right panels resizable; a select-through toggle for the marquee in the
Support panel; H hides selected supports in Support mode; hidden supports never
selectable. **Per-printer and per-resin settings editors are decided but explicitly
LATER** — do not brief yet.

**Island search rumination (user, tonight): already implemented** — the user's
slice-and-diff idea is exactly `MeshSlicer.NewbornIslands` + `IslandFinder` (polygon
difference with slope-aware inflation), feeding TipPlacer/PrintChecker/SupportAreaDetector,
configurable via `--min-island` / `--overhang` / `--layer`. Claude proposed tweaks, USER
DECISION PENDING, do not brief until they choose: multiple tips per large island
(`IslandAreaPerTipMm2`), weakly-supported detection (`MinSupportedFraction`), island
tracking through layers, `IslandTipAt` centroid|lowest|both, island painting in the
Support workspace.

**Start a persistent Monitor** in the new session polling ChatGPT's mailbox
(`F:\Git Repos\Danslicer-chatgpt\ChatGPT\` REPORT/QUESTIONS mtimes) and the
`grid-routing-prototype` head each minute; the old session's monitor dies with it.
Review protocol unchanged: detached scratch worktree, build + full tests, merge-test
against main, merge only with the user's per-merge approval, then push. ChatGPT's last
queue landed as three commits after the report — it was asked (REVIEW note) to commit
granularly this time.

**User-only outstanding:** screen-test brief 12 when it lands, the Reinforce visuals
(needs profile UI), the physical mirror-X test print, and re-running `/auto-mode-setup`
(broken earlier by a transient classifier outage).

## State as of 2026-09-03 late (SUPERSEDED by the section above)

**Grok is out of tokens and retired.** ChatGPT is the only implementing agent; it owns
both former lanes and the file-lock split is dissolved. **Claude's manager-only restriction
is relaxed** (the 5-hour usage window reset): Claude may implement again alongside managing
ChatGPT — pick work that cannot collide with ChatGPT's queue (different files/areas, own
branch, never ChatGPT's worktree), and keep reviewing/merging ChatGPT's commits as before
(detached scratch worktree: build + full tests, merge-test against main, then merge only
with the user's per-merge approval, then push).

**main is at `cab0d64` + this handover commit, 233 green tests, pushed.** Merged today,
in order: ChatGPT's brief 5+6 queue (Shift+H endpoint fix, visible+specific T-refusal
messages, per-tip routing refusal reasons ContactBlocked/NoClearStep/NoLanding/BelowPlate
with counts+coordinates in the route CLI, steep-contact neck departure along the outward
normal + deterministic 12-direction escape fan, padded steep model landings, single
Auto Drop toggle + offset box with config migration), then Grok's salvaged brief 5
(`--seat` CLI flag on all commands; cone/ball tip contact schema — `SupportTipShape`
capsule|cone, ConeLength, TipDiameter, BallDiameter, PenetrationDepth — frustum/sphere
slice sections, sphere collision primitives, defaults bit-identical; committed by Claude
after Grok died with it finished-but-uncommitted; one merge conflict in RouteCommand.cs's
JSON writer was resolved keeping Grok's dictionary form with ChatGPT's refusal fields).

**Benchmark trace (Drogon full-res, top-down, same 1113 tips):** unrouted 586 (baseline)
→ 434 (normal departure) → 302 (escape fan); ContactBlocked 364 → 44; NoClearStep 232 is
now dominant. The user added a 10× decimated fast-iteration model:
`test files\drogon collapse.stl` (`drogon-lo` in BENCHMARKS.md) — dev runs only, official
numbers stay on the full-res canonical pair.

**User decisions today (all recorded in `docs/SUPPORT-GEOMETRY-SPEC.md`):** support
anatomy base/trunk/branch/tip(+brace) is canonical vocabulary; tips get end diameter +
embedding depth; everything configurable, 45° defaults; recipes = complete geometry
bundles, user-editable; batched non-blocking generation with a progress bar above the
status bar, ONE undo step, cancel ROLLS BACK; **supports never land on the model for now**
(landing stays compiled but opt-in only — the padded-landing behaviour merged today put
blobs on Drogon's toes and is being disabled in brief 10).

**ChatGPT's queue (briefs in `F:\Git Repos\Danslicer-chatgpt\ChatGPT\`):**
1. `INSTRUCTIONS-10.md` — screen-test bugs: Shift+H must keep the whole connected support
   (`SupportGraph.Component`, bracing excluded), and disable model landing per the user
   decision above (expect Drogon unrouted to rise vs 302; accepted).
2. `INSTRUCTIONS-9.md` — Grok-lane handover: reconcile the user's embedding depth with
   Grok's `PenetrationDepth` (one concept, one name; cone continues past the contact),
   plus write the seated benchmark section Grok CLAIMED but never wrote (its REPORT lies
   about this — audit finding; run `--seat` matrix on both full-res models).
3. `INSTRUCTIONS-8.md` (non-blocking generation) and `INSTRUCTIONS-11.md` (Layout/Support/
   Slicing window modes, mode-dependent Ctrl+A = objects/supports/nothing, marquee box
   selection for supports) — ChatGPT picks the order, both touch MainWindow.

ChatGPT protocol notes: it once committed `ChatGPT/REPORT.md` into its branch (mailbox
files must stay untracked; strip at merge if it happens again — it was told to
`git rm --cached` it). Reviews go newest-at-top in `ChatGPT/REVIEW.md`; answers to
questions live there. Grok's worktree (`F:\Git Repos\Danslicer-grok`) is kept for
reference only (its REPORT/QUESTIONS/REVIEW hold the tip-geometry context) — nothing
runs there any more.

**Start a persistent Monitor** in the new session polling ChatGPT's mailbox
(REPORT/QUESTIONS mtimes) and `grid-routing-prototype`'s head each minute. The old
session's monitor is stopped.

**User-only outstanding:** screen-test after brief 10 lands (Shift+H whole-tree, no more
toe blobs, teeth T behaviour), the Reinforce visuals (needs profile UI), and the physical
mirror-X test print. A transient Anthropic classifier outage today also broke the user's
`/auto-mode-setup` runs — suggest re-running it when convenient.

## Multi-agent phase, state as of 2026-09-03 evening (SUPERSEDED by the section above)

Three AI agents work this repo in parallel; **Claude is manager/integrator ONLY** (user
directive after hitting a usage limit: no implementation, only briefs, reviews, merges, and
asking the user to test). ChatGPT and Grok run as local CLIs with file access.

**Mechanics.** Each agent has its own git worktree and branch: ChatGPT in
`F:\Git Repos\Danslicer-chatgpt` on `grid-routing-prototype`, Grok in
`F:\Git Repos\Danslicer-grok` on `region-generation`. Mailbox folders (`ChatGPT/`, `Grok/` in
each worktree, git-excluded via `.git/info/exclude`) hold numbered briefs
(`INSTRUCTIONS[-N].md`), the agent's `REPORT.md`/`QUESTIONS.md`, and Claude's `REVIEW.md`
(newest review at top; answers to their questions live there too). Agents commit to their
branch only, never launch the app or touch the screen (the user tests), never push. Claude
reviews every commit in a detached scratch worktree (build + full tests in isolation), then
— with the user's approval per merge, which they have granted promptly each time — merges to
`main` and pushes. `main` is at 212 green tests with both agents' brief 4 merged. Start a
persistent Monitor polling both mailboxes and branch heads (stat REPORT/QUESTIONS mtimes +
`rev-parse HEAD` each minute); a fresh session must restart it.

**Merged and working (headless-verified unless noted):** support capsule rendering
(user-verified on screen), config window with colour pickers/numeric boxes/window persistence
(user-verified), calibrated SpaceMouse (user's feel = multiplier 1.0, all axes inverted in
config), plate fade from below, configurable overhang checker (two solid colours + cell size),
support delete residue pruning, full generation stack (tip placement with derived-mesh-data
cache and BVH, grid + top-down routing, growth rules incl. attach-to-existing / keep-clean /
Reinforce with ring re-projection / Land, bracing stage, BVH + linear + composite collision
scenes), print checks (suction cups, proximity, islands, bounds), `SupportGenerator` pipeline
with grid wiring, T routes manual supports around the model (Shift+T = blind override),
auto-placement after transform commits (user-verified: Drop/Raise/Off header controls, one
undo step), Generate Supports (Ctrl+G, user-verified working), Shift+H hide-unselected
supports (buggy, see below), `MeshSlicer.LayerPolygons`/`NewbornIslands`, support-area
auto-detection (`SupportAreaDetector`), CLI: `tips` / `route` / `checks` / `areas`, and
`docs/BENCHMARKS.md` — the brief-4 baseline on the two canonical models.

**In flight (briefs written, agents working):**

- **ChatGPT** — order: brief 6 item 1 first (bug: Shift+H hides selected supports too; root
  cause diagnosed in the brief — `SupportRenderMesh.Build` skips segments whose endpoint
  nodes are hidden, and hide-unselected hides a selected segment's own endpoints), then
  brief 5 (user-reported T-routing bug: refusal message is clobbered by `UpdateStatus()` at
  the end of `OnKeyDown`, so refusals are silent; router needs per-tip refusal reasons
  plumbed to App + CLI, a concave-pocket repro fixture, then a deterministic fix — wider
  blocked-step search / padded steep landings / neck-clearance rethink), then brief 6 item 2
  (replace the three placement buttons with one "Auto Drop" toggle + offset edit box).
- **Grok** — brief 5: `--seat` CLI flag (drop lowest point to Z=0, centre XY) and a seated
  benchmark section in BENCHMARKS.md; then support tip contact geometry per the user's
  2026-09-03 spec: cone tips (base = support diameter, configurable contact diameter),
  optional snap-off ball at the contact, additive schema on `SupportNode` +
  `SupportSliceGeometry` (those two files are unlocked additively for this), defaults
  bit-identical to today. Rendering the new shapes is a future App-lane brief.

**Benchmark findings that drive current briefs:** 41% (grid) / 53% (top-down) of auto tips
fail to route on Drogon — the user's T-refusal bug at scale; top-down on the unseated
gripper (raw CAD coords ~3 km up) was killed at 39 min — hence `--seat`.

**User decisions 2026-09-03 evening:** generation-quality tuning (densities, what looks
right) is deferred until supports have real printable geometry and profiles; tip cone/ball
geometry is the prerequisite and is briefed now. Placement UI becomes a single Auto Drop
toggle + offset box.

**User has NOT yet screen-tested:** the T-routing refusal message and Shift+T override
(blocked on ChatGPT brief 5), Reinforce visuals (no profile UI yet). The physical test print
(mirror-X) remains user-only.

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

**Auto-drop to plate (user request 2026-09-03, not yet built).** After any object transform
commit (move, rotate or scale; modal, gizmo or numeric), the object should re-seat on the plate
using the lowest point of the mesh in its current orientation. Three modes:

1. *Auto-drop* (the default): lowest point lands exactly on Z = 0 after every transform.
2. *Raise above plate*: like auto-drop but the lowest point lands at a user-entered height
   (edit box for the distance).
3. *Off*: the object stays wherever it is put (today's behaviour).

**Canonical support benchmarks (user decision 2026-09-03).** `test files/Drogon_flat_surface.stl`
(organic extreme, ~47 MB) and `test files/roof gripper T2.obj` (CAD extreme) are the definitive
test models for support work. Every significant support feature should be exercised against both
via the CLI (`tips`, `route`, `checks`) and the numbers recorded; they are deliberately NOT in
git (large binaries), they live in the repo directory on the user's machine.

**Support "recipes" (user rumination 2026-09-03, later stage).** Largely DESIGN.md's existing
profiles (§8.1: named parameter sets, "CAD clean" vs "organic dense") plus regions (§8.3), with
three genuinely new elements worth designing when profiles land:

1. *Auto-detection painting*: a tool that reads the model, finds areas needing support, and
   paints each detected area a distinct random colour; the user then selects an area and applies
   a recipe to it. (Overhang/island analysis from tip placement already computes the raw data.)
2. *Modifier-style attachment*: a recipe attached to an auto-detected area the way Blender
   modifiers attach — non-destructive, re-evaluated, per-area — rather than a one-shot apply.
3. *Import/export*: recipes as shareable files so users can exchange them.

Different recipes within one model type (several CAD recipes for different problems) should be
normal, not an edge case.

Design notes (user decision 2026-09-03): the drop applies after ANY transform commit,
including a G/Z move — the move happens exactly per the user's input, then the object snaps
back to the correct level; rotation likewise commits first, then drops. Never suppress an
axis. Ctrl+D (Drop to Plate) remains as the manual one-shot. The drop must be part of the
same undo step as the transform that triggered it.

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
