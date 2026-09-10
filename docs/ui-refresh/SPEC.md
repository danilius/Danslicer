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
