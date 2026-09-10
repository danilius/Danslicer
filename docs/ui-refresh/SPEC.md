# Approved UI specification
## Authoritative correction
The FINAL user correction is references/slider-authoritative.png: the LEFT green-circled filled rectangular numeric slider is CORRECT. The RIGHT red-circled thin track with circular thumb and outside number is INCORRECT. This supersedes earlier contradictory text. Center the numeric value and adjacent unit together inside the field. Numeric text fields support direct drag scrubbing and click-to-type expressions; use a drag threshold, fine adjustment and undo semantics. Locks are optional per field, only when their operation has a clear meaning; a locked field cannot scrub.
## Appearance and layout
Professional advanced-user desktop Avalonia UI; charcoal neutral surfaces, restrained amber accent, clean outline icons from the original Charcoal icon study. Rebuild icons as vector geometry, never ship generated raster icons.
Floating left toolbar inset from all viewport edges; icon-only with tooltips or icons plus labels. Remember chosen mode. No permanent right panels.
Compact tool-anchored popouts, comfortable hit areas, consistent property alignment. Stay open during viewport orbit; explicit close, toolbar toggle and Escape dismissal. Fit within viewport; long content scrolls.
Darker expander headers than body, disclosure arrow left, reorder gripper right; remember order and expansion state. Keep keyboard accessibility and avoid accidental drags.
Layout / Support / Slicing tabs use slim underline selection like Slate reference. Project dropdown upper right offers recent projects and opening another. Subtle selected machine and resin shown in top strip.
Preserve ALL existing status-bar content, behavior and commands. Preserve existing keymaps, tool commands, expression support, settings, undo and mode scoping.
## Rendering and geometry
Shallow modelled build plate with subtle rough reflections; transparency from below removes obscuring surface and reflections, optionally retaining faint perimeter. Selection through plate from below must work. Treat rendering as separate task/commits.
Raft bevel must cant OUTWARDS, superseding images. Verify orientation against actual raft geometry/spec before implementing; mockup diagrams are not geometry authority.
AO/cavity are desired subtle geometric readability effects; inspect existing renderer support first, avoid duplicate implementation or silently changing core algorithms.
## References are conceptual
Mockup settings are illustrative: bind real existing parameters and commands. Raft controls must not imply functionality absent from the application. Preserve core slicing/support algorithms and project compatibility.
Review at actual desktop scale, 100/150/200 percent DPI, narrow windows, light/dark states where supported, keyboard operation, unlocked/locked fields and long labels.

## Approved compact refinements (task 01 acceptance)
12-DIP typography, 26-DIP section headers, 28-DIP popout headers, 24-DIP numeric fields. Resizable popout right edge (keyboard Left/Right; Escape cancels resize). Six round 2-DIP grip dots on a 4-DIP grid with a 26-DIP hit target. Drag the entire expanded section and settle over 150 ms; Alt+Up/Down reorders. Preserve these accepted controls during integration. Production numeric bindings/undo and durable toolbar/section state belong to task 03.

## Latest approved isolation refinement
In Support mode, layer isolation is always visible on the right, with no toolbar button. This is an explicit exception to removing permanent right settings panels. Replace fixed top/bottom edit boxes with a compact popout left of the hovered slider handle, displaying editable layer number and height in mm. Follow the handle; keep open across handle/card hover, drag and keyboard editing, with a short dismissal delay to cross the gap. Cap interior is a two-state checkbox, default on; honor the user's saved on/off selection.

Latest screenshot correction: the Support isolation slider sits below the view cube with clear spacing, has no enclosing background panel or border, centers Reset below the slider, and labels its two-state checkbox "Cap". Handle-following editors remain unchanged.

## Settings and persistence contract (task 03)
Workspace UI preferences live in workspace-ui.json beside the existing user config. Persist toolbar labels, completed popout widths, section order/expansion and ten recent project paths. Missing/corrupt data uses defaults; stale/duplicate section IDs are filtered and new sections appended. Newer schema versions are read-only; unknown JSON members round-trip. Workspace saves do not rewrite unrelated printer/resin/keymap configuration. Existing offline recent paths remain available and use the normal failed-open status handling.
Production bounded support/raft fields use existing limits and setters with centered in-field units; integer fields normalize on commit. Transform/print/region/placement scrub fields use the existing NumericField validation and model callback, once per committed edit. Preserve existing undo where present: transforms have one document undo per gesture; support configuration retains its existing immediate-save behavior without document undo. Visibility retains its actual modes and switches; do not invent numeric opacity or new raft parameters. No field locks without explicit semantics.
Main capture and unit-test VMs use isolated temporary configuration. The Support rail keeps its 300ms gap grace, shared ViewCube.Rect plus 12-DIP clearance, centered Reset and saved two-state Cap choice.

Latest approved expander behavior: swap immediately during dragging when the dragged section crosses a neighbouring section midpoint, keeping the complete expanded content under the pointer. Allow swapping back on reversal. Escape/capture loss restores initial order; persist only completed drop or keyboard reorder. Preserve 150ms release settling and round grippers. This supersedes historical drop-only reordering notes.

Authoritative gripper correction: a swap triggers just after the dragged gripper centre passes the displayed centre of another gripper. Full expander centres are not swap targets. The displaced, ungripped section animates from its current visible position to its new slot over 250ms with smooth deceleration (cubic ease-out). Preserve drag anchoring, cancellation/restoration and save-on-release behavior.

## Task 04 consolidated rendering refinements
AO is explicitly in scope: independent subtle local proximity/contact depth shading plus existing cavity ridge/valley readability. Keep effects configurable using view-settings conventions and document active paths and cost. Deferred implements local screen-space AO/cavity on opaque geometry; Classic fallback retains studio lighting and shared plate/reflections. Transparent geometry does not supply AO. Do not equate finding the cavity checkbox with implementing AO.

Surface/reflections/grid disappear completely below regardless of a legacy residual-opacity setting; that retained JSON value now controls the optional faint perimeter. Plate geometry is viewport-only, beneath Z=0. Below picking must reach models/supports. Real GL framebuffer evidence, above/below/contact and independent toggles, is required; offscreen Avalonia screenshots with black GL regions cannot validate rendering. Tests/previews isolate configuration.

Raft orientation authority is the actual geometry/spec: outer top lip wider than bottom, holes vertical, matching the existing committed scraper-lip implementation. No task-04 geometry mismatch was found or geometry correction made. Task 05 must retain core slicing/support output compatibility.

## Latest user refinement — top strip and orientation widget
Auto drop belongs at the left of the same strip as the workspace tabs. Layout/Support/Slicing is centered in that strip, with equal outer space; keep machine/resin and Project accessible at narrow sizes. Auto drop retains existing Layout placement semantics. AO strength and radius must be readily configurable from View settings with the approved numeric field interaction.

Orientation style now follows the user's compact dark cube with fine outlines and red X, green Y and blue Z arrows/letters, superseding earlier large face-word styling. Preserve snapping, orbit and isolation clearance.

Authorized model/support cast and self-shadows: Working is the subtle default, Presentation is stronger, Off bypasses the pass. Both modes remember independent strength and softness settings in View settings; AO remains independent. The common directional light follows the camera. Shadow strength is bounded to retain readable illumination; transparent/ghost geometry is excluded. This is viewport shading only, with no slicing/support geometry changes.

## Task 04b — latest approved contract (supersedes commit-only preview history)
Scene-facing numeric drags show live visible/model preview. Release creates one undo entry where document undo already applies, and persists once; Escape/capture cancellation restores original values and scene. Preferences retain existing non-document undo policy. Preview uses explicit per-control policies; saving setters and expensive generation must not run per movement. Transforms, isolation, visibility and shading preview immediately; expensive rebuilds must be bounded/coalesced with accurate final computation. Support generation, slicing, export and file writes stay explicit actions. Future-operation settings preview values without modifying existing generated results. Guard cancellation, detach/close/selection/mode changes and stale async work. Preserve actual track mapping, Shift fine adjustment, two-decimal/integer edits, units/expressions/range rejection, locks, keyboard and no-op handling. Audit every numeric host and verify pre-release scene state, no saves/undo during drag, final undo/redo, restoration and actual GL feedback.

Task 04b selected-raft policy: settings are immediate; existing selected raft geometry is coalesced at 100 ms after native profiling, with original-state restoration and accurate final undoable commit. No raft is added by dragging. Exact CPU clip caps use an open-surface preview while dragging and accurately recompute on release. See slider-policy-audit.md for the complete host inventory and future-operation exceptions.

## Latest user-review refinements — task 05
Object borders have a persisted live-preview width setting, 1–5 framebuffer pixels, in View settings and Preferences. This configures the existing Deferred outline effect; Classic does not implement that effect. Off remains the Outlines checkbox.
Support/raft numeric ranges use practical interaction maxima, including base diameter 25mm. See slider-range-refinement.txt for the inventory. Existing saved values are not rewritten just by opening settings; new edits obey the displayed range. Angle fields must retain angle units.
The Support preset editor uses the same compact dark draggable expander headers as the workspace, with gripper-centre swapping and commit-only order persistence. Preferences Support sections use the same headers. Only one viewport owns the SpaceMouse connection at a time; activating the preset preview transfers ownership, and returning to main reacquires it. Preferences without a viewport preserve live main-camera sensitivity tuning.
