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
