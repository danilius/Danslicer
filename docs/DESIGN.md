# Danslicer Design Document

Status: draft 1, 2026-09-02

## 1. Purpose

Danslicer is a desktop slicer for resin (MSLA) printers aimed at advanced users. Its distinguishing feature is a support system in which automatic generation and manual editing are the same tool: generation is scoped to user-defined regions, driven by explicit rule sets, deterministic, incremental, and every generated element is fully editable afterwards. The second distinguishing feature is a viewport whose shading is designed for reading geometry, borrowing from Blender's solid mode.

### In scope

- Import STL, OBJ and 3MF meshes.
- Place objects on the build plate: move, rotate, scale, rotate about an arbitrary plane or face, lay a face flat.
- Region-scoped, rule-driven support generation with a fully editable support graph.
- Viewport with studio lighting, MatCap, screen-space cavity, outlines, overhang tinting, per-object render states (normal, ghosted, highlighted, hidden).
- SpaceMouse (six-axis) navigation.
- Slicing to anti-aliased layer bitmaps and export to `.pwmx` for the Anycubic Photon Mono X.
- A project file that round-trips everything above.

### Out of scope

- Mesh repair, mesh editing, hollowing, drainage holes, boolean operations.
- Printers other than the Photon Mono X in the first release. The printer definition and file writer are behind interfaces so others can follow.
- Rendering of the sliced layer stack beyond a simple layer preview.

## 2. Target printer

| Property | Photon Mono X |
| --- | --- |
| LCD resolution | 3840 x 2400 |
| Build area | 192 x 120 mm |
| Pixel pitch | 50 µm |
| Z travel | 245 mm |
| File format | `.pwmx` (Anycubic Photon Workshop family) |

Layer bitmaps are 8-bit greyscale, run-length encoded per the `.pwmx` specification. UVtools documents the format and is the reference for validating output. UVtools.Core is AGPL and is used only as a specification and validation tool, never linked.

## 3. Technology stack

| Concern | Choice | Notes |
| --- | --- | --- |
| Language and runtime | C# on .NET 10 LTS | |
| UI | Avalonia 12 | Cross-platform. Windows first, Linux and macOS should work with no design changes. |
| MVVM | CommunityToolkit.Mvvm | |
| 3D rendering | Silk.NET OpenGL on Avalonia `OpenGlControlBase` | Own renderer. Shaders written for GL 3.3 core and GL ES 3.0, since Avalonia on Windows runs on ANGLE by default. See section 6. |
| 2D polygon operations | Clipper2Lib | Offsetting, booleans, contour cleanup. |
| Rasterisation | Own scanline rasteriser, SkiaSharp as fallback | SkiaSharp already ships with Avalonia. |
| Maths | System.Numerics | Single precision in the renderer, double precision in geometry and slicing. |
| SpaceMouse | 3DxWare COM interface on Windows, raw HID via HidSharp elsewhere | Behind an `ISixAxisInput` interface. |
| Tests | xUnit | Core is UI-free and fully testable. |

Rejected: C++ with Qt (double the effort for this scale), Rust (weak desktop UI story), WPF (Windows only, no long-term benefit), HelixToolkit and game engines (wrong shape for the viewport requirements).

## 4. Architecture

### 4.1 Projects

```
Danslicer.Core       Meshes, scene, transforms, support graph, generation, slicing, file writers. No UI or GPU dependencies.
Danslicer.Render     OpenGL renderer, shaders, G-buffer pipeline, picking. Depends on Core, not on Avalonia.
Danslicer.App        Avalonia application, view models, input handling, SpaceMouse.
Danslicer.Cli        Headless command-line front end over Core for scripting and regression tests.
Danslicer.Tests      Unit and golden-file tests.
```

Core must stay free of UI and GPU dependencies so that slicing and support generation can be run and tested headless.

### 4.2 Document, commands, undo

A single `Document` holds the scene, the support graph, regions, profiles and print settings. Every mutation goes through a command object with `Execute` and `Undo`. The UI, the CLI and any future scripting all issue the same commands, so there is one code path to test and one undo stack. Commands that mutate the support graph record graph diffs rather than whole-graph snapshots.

Long operations (generation, slicing) run on background threads, report progress, are cancellable, and commit their result as a single command on completion.

### 4.3 Derived data

Mesh acceleration structures (BVH), planar-patch clustering, per-vertex curvature and the support graph's render and slice meshes are all derived data, computed lazily and invalidated by the commands that affect them. Nothing derived is stored in the project file.

## 5. Scene and object manipulation

### 5.1 Objects

An object is a mesh reference plus a transform (translation, rotation as a quaternion, non-uniform scale). Multiple objects may share one mesh. Objects carry a render state used by the viewport and editing modes.

### 5.2 Mesh analysis at import

- Build a triangle BVH for ray casting and collision queries.
- Cluster triangles into planar patches: flood fill across edges whose dihedral angle is below a threshold (default 1 degree) and whose normals agree. CAD exports produce clean patches. Organic meshes produce many small ones, which is harmless.
- Detect sharp edges (dihedral angle above a threshold, default 30 degrees) for outline rendering and for edge-preferring tip placement.
- Compute per-vertex curvature for world-space cavity and for tip placement scoring.

### 5.3 Transform tools

Blender-style modal transforms with gizmos and numeric entry. The pivot for moving, rotating and scaling is always the centre of the selection's world bounding box.

- **Gizmos.** Move arrows with plane squares, rotation rings and scale cubes, individually toggleable in the viewport header and drawn at a constant screen size at the pivot. Dragging a handle runs the same modal transform as the keyboard, so typed values, axis keys and the status readout work mid-drag.
- **Snapping.** Two modes, free and snapping, toggled in the header or with `Shift+Tab`. Holding `Ctrl` inverts the mode for the duration of a drag, as in Blender. Steps: 1 mm, 5 degrees, 0.1 scale.


- `G` move, `R` rotate, `S` scale. Axis constraint by `X`, `Y`, `Z`, plane constraint by `Shift` plus axis. Type a number to enter a value directly. Numeric fields accept units and expressions.
- Rotate about an arbitrary axis: pick a face, the axis is its normal through the pick point. Pick an edge, the axis is the edge.
- Lay flat: pick a face, the object rotates so that face's normal points down and the object drops to the plate. Works on the planar patch containing the picked triangle so the alignment is exact on CAD meshes.
- Rotate on a plane: pick a face to define the plane, then rotate within it. Useful for orienting a part while keeping a chosen face level.
- Drop to plate, centre on plate, arrange.
- Snapping: to grid, to plate, object-to-object bounding box.

Objects extending below Z zero are drawn in a warning colour and slicing refuses until resolved.

## 6. Viewport rendering

### 6.1 Goals

Shape must be readable at a glance, on both organic and CAD models, with thin supports distinguishable from the model behind them. Every effect is independently toggleable from a shading popover.

### 6.2 Pipeline

A small deferred pipeline:

1. **Geometry pass.** All opaque scene geometry to a G-buffer: depth, view-space normal, object and element ID, base colour and flags. One draw per object, supports drawn as instanced capsules and cylinders from the support graph.
2. **Lighting and composite pass.** Full-screen. Studio lighting (three or four camera-space directional lights) or MatCap lookup from the normal buffer. Multiplied by cavity, overhang tint applied, outlines composited.
3. **Ghosted pass.** Forward-rendered, alpha blended, depth tested against the opaque depth but not writing it. Used for de-emphasised objects and supports. These do not receive outlines or cavity, which is the desired look for de-emphasised geometry.
4. **Overlay pass.** Gizmos, grid, build volume, selection outlines, region paint, layer plane, all depth tested as appropriate.
5. **Anti-aliasing.** FXAA on the final image. SMAA later if FXAA is not good enough. Multisampling is avoided because it fights the G-buffer approach.

### 6.3 Effects

- **Studio lighting.** Fixed key, fill and rim lights in camera space so every surface always has a lit and a shaded side regardless of view direction. Adjustable presets.
- **MatCap.** Sphere texture lookup by view-space normal. Ships with a clay and a metal MatCap. Users can add their own.
- **Screen cavity.** Screen-space ridge and valley detection from depth and normal buffers. Separate ridge and valley strengths, as in Blender. Radius in pixels, adjustable.
- **World cavity.** Per-vertex curvature baked at import, used where screen cavity is too noisy.
- **Outlines.** Edge detection on depth discontinuities and object or element ID changes. Separate colour for selection outlines. Critical for making supports legible.
- **Overhang tint.** Faces whose normal is more than a threshold angle below horizontal tinted red, graded by angle. Threshold matches the support profile's overhang angle.
- **Shadow.** Single directional shadow map onto the plate. Lowest priority, only if the plate still feels floaty.
- **Layer plane.** Clip plane at a chosen height with capped and coloured cut faces, to inspect a layer in context.

### 6.4 Render states

Every object and every support element carries one of: normal, ghosted, highlighted, hidden. Editing modes set these. For example, editing supports in one region ghosts everything else. Selection is a separate flag drawn as an outline.

### 6.5 Picking

Read the ID buffer under the cursor. No CPU ray casting for picking. Box and lasso selection read a rectangle of IDs. Depth under the cursor gives the 3D point for orbit-about-cursor and for placing manual supports. Surface normal at the pick comes from the normal buffer, or from the mesh for exact values.

## 7. Input

### 7.1 Mouse and keyboard

Blender-style conventions: middle mouse orbit, `Shift` pan, wheel zoom, orbit about the point under the cursor, numpad views, `Home` frame all, `.` frame selection. Everything has a keyboard shortcut and the shortcut is shown in the menu. Keymap is a data file and editable.

- **Numpad emulation.** Every numpad binding also exists on the main digit row, so `1`, `3`, `7` and `5` switch views on keyboards without a numpad. Digits only mean numeric entry while a modal tool is active. On by default; the keymap file can rebind.
- **Projection.** Perspective and orthographic, toggled with numpad `5` or `5`. Axis-aligned numpad views switch to orthographic automatically when auto-perspective is on, as in Blender. Orthographic is the mode for checking alignment and support spacing; perspective for reading shape.
- **View cube.** A small interactive cube in the viewport corner, drawn in the overlay pass. Faces, edges and corners are clickable and snap the camera to that view with a short animated transition. Dragging it orbits. It doubles as the orientation indicator and shows the projection mode.

### 7.2 SpaceMouse

Primary development device is a SpaceMouse Pro with the 3DxWare driver installed, so the COM backend is built and tested first. The Pro has fifteen buttons (Menu, Fit, T, R, F, Rotation lock, 1 to 4, Esc, Alt, Shift, Ctrl) that map naturally onto framing, the numpad views and the modal tool modifiers.

`ISixAxisInput` delivers six axes plus buttons at device rate. Two backends:

- **3DxWare COM** (`TDxInput`) on Windows when the driver is installed. Gives access to the driver's own sensitivity and axis mapping.
- **Raw HID** via HidSharp for Linux and macOS, or Windows without the driver. Known report layouts for the common devices.

Navigation modes: object mode (device moves the scene) and camera mode (device moves the camera), with per-axis inversion and sensitivity curves. Buttons bind to the same command layer as keyboard shortcuts. Roll about the view axis can be locked, as it is rarely wanted in a slicer.

## 8. Support system

### 8.1 Concepts

- **Support graph.** The single source of truth. Nodes with position and type, segments joining nodes with type and diameter. Every element carries an origin tag: the region and generation pass that made it, or manual. Every element has a pinned flag and a hidden flag. Render meshes and slice geometry are derived from the graph on demand.
- **Region.** A set of mesh faces on one object plus a profile reference, plus a role. Roles are `support` (generate here) and `keep-clean` (no contacts may land here, and pillars must stay a set distance away). Global generation is one region covering the whole object.
- **Profile.** A named, saveable parameter set: tip parameters, placement strategy and its parameters, routing strategy and its parameters, an ordered list of growth rules, bracing rules, constraints. Profiles ship with sensible defaults such as "organic dense", "CAD clean bottom face", "grid forest".
- **Generation pass.** One run of one region. Passes are recorded on the elements they create so that a region can be regenerated without touching anything else.

### 8.2 Element taxonomy

| Kind | Type | Meaning |
| --- | --- | --- |
| Node | Tip | Contact with the model. Has contact point, surface normal, tip diameter, penetration depth. |
| Node | Junction | Branch or merge point. |
| Node | Base | Contact with the plate, or with the model when landing on the model is allowed. |
| Segment | Neck | Tip to first junction. Thin, tapered. |
| Segment | Pillar | Ordinary vertical or leaning member. |
| Segment | Trunk | Merged pillar of larger diameter. |
| Segment | Bracing | Cross-member between two pillars or trunks. Never part of a "whole support" selection. |

A **support** in the user's sense is a connected component of the graph with bracing segments removed, that is, one tree from its base to its tips.

### 8.3 Region selection tools

- Click a planar patch. Grow across edges below a dihedral angle, with the angle adjustable live.
- Select all faces facing down more than N degrees, optionally limited to the current selection.
- Paint and erase with a brush of adjustable radius, for organic models.
- Invert, grow, shrink, select connected.
- Regions are shown as a translucent colour overlay in the viewport. Keep-clean regions use a distinct colour.

### 8.4 Generation pipeline

Generation for a region runs in three stages. Each stage is a strategy chosen by the profile, so new algorithms slot in without changing the model.

**Stage 1, tip placement.** Produces candidate tips on the region's faces. Strategies:

- *Overhang sampling.* Sample faces steeper than the overhang angle at the profile density, using Poisson-disk spacing so tips are evenly spread. Score candidates by overhang severity, curvature (prefer ridges and points), island area and distance to existing tips.
- *Islands and minima.* Slice the object at layer resolution, find islands (regions of a layer with nothing below them) and local minima, and place tips there first. These are the points that must be supported regardless of density. Minimum island area to support is a parameter.
- *Edge and corner preference.* On CAD meshes, prefer tips on sharp edges and corners, where marks are least visible and where the tip can be smaller. Strength is a parameter, from 0 (ignore) to forced (interior of faces never receives a tip).
- *Grid projection.* Project the base grid upward and place tips where grid lines hit the region, so tips line up with bases directly.

Candidates that violate a keep-clean region or fall within the minimum spacing of an existing tip (from any pass or manual) are dropped.

**Stage 2, routing.** Connects tips to something load bearing. Strategies:

- *Top-down.* From each tip, descend along the profile's lean limit toward the plate, avoiding the model and existing supports via the collision structure, merging with nearby pillars when the merge rule allows. Classic tree supports.
- *Grid bottom-up.* Bases sit on a lattice (square or hexagonal, with spacing, offset, rotation and snap tolerance). Pillars grow upward from lattice points and branch to reach tips, evaluating the growth rules at each step. Produces regular forests that are easy to read and remove.
- *Attach to existing.* Not a standalone strategy but a flag on the others: tips may route onto an existing pillar or trunk from an earlier pass or manual placement when within reach, instead of growing a new pillar.

**Stage 3, bracing.** Adds bracing segments according to the bracing rules. Runs after routing and can be rerun on its own.

### 8.5 Growth rules

An ordered list evaluated as pillars are routed. Each rule has parameters and can be enabled or disabled. Initial set:

| Rule | Parameters | Effect |
| --- | --- | --- |
| Lean | max angle, max angle near tip, near-tip distance | Limits deviation from vertical. |
| Branch | trigger distance to tip projection, max branches per trunk, diameter step per level | Spawns branches toward tips. |
| Merge | trigger distance between pillars, resulting trunk diameter, min height above tips to merge | Joins converging pillars into a trunk. |
| Brace | min height, min slenderness, preferred angle, max length, neighbour distance | Adds cross-members between neighbours. |
| Land | allow landing on model, min landing angle, landing pad diameter | Permits bases on the model instead of the plate. |
| Reinforce | seed selector (lowest point of object, lowest point of region, tips marked critical), count, ring radius, ring diameter multiplier | Adds N extra tips in a ring around a seed point and routes them, to strengthen the first contact the print makes. |
| Taper | neck length, tip-to-pillar diameter ratio | Shapes the neck. |
| Clearance | distance from model, distance from keep-clean faces | Pushes pillars away from surfaces. |

Rules are data. Adding a rule means adding a class implementing `IGrowthRule` and a settings panel.

### 8.6 Constraints and existing supports

Every pass runs against a collision structure containing every object in the scene, not only the one being supported, plus every existing support element regardless of origin. Supports for one object therefore never pass through or land on another object, and pillars from neighbouring objects avoid each other. Existing elements are obstacles always, and attachment candidates when the profile allows. Keep-clean regions are hard constraints for tips and soft constraints (clearance rule) for pillars. Manual placement respects the same constraints, with an override modifier key.

### 8.7 Determinism and incremental regeneration

Generation is a pure function of (mesh, region, profile, existing graph, seed). Regenerating a region:

1. Deletes that region's elements from earlier passes that are not pinned.
2. Reruns the three stages against the remaining graph.
3. Commits as one undoable command.

Pinned elements and manual elements are never touched. Editing a generated element pins it automatically. Changing a profile parameter regenerates only regions using that profile, and only when the user asks, never live, so the user stays in control on large models.

### 8.8 Editing operations

All operate on the current selection through the command layer:

- Add tip at pick point on the model, routed by the active profile, or as a bare tip awaiting manual routing.
- Move tip along the surface (constrained to the mesh) with the pillar re-routed.
- Move junction or base freely, with connected segments following.
- Insert junction on a segment, delete junction (segments join).
- Connect two nodes with a pillar or bracing, disconnect.
- Change diameter on selected segments, change tip parameters on selected tips.
- Delete, pin, unpin.
- Convert generated to manual (pins and clears origin).
- Re-route selected tips with a chosen profile.
- **Area painting.** Paint a region with the brush and generate into it immediately with the active profile, as one command. The fastest way to say "supports here".
- **Support lines.** Select an existing support, then click a second point on the model. Tips are placed at the profile's spacing along the surface path between the two and routed with the active profile. Also works between two existing supports, and from a support along a picked sharp edge.

### 8.9 Selection and visibility

- Selection filter mask in the viewport header: tips, junctions, bases, necks, pillars, trunks, bracing. Only enabled types are pickable.
- Whole-support mode selects the connected tree from a picked element, excluding bracing.
- Select all of type, select by region, select by pass, select connected, invert.
- `H` hide selected, `Shift+H` hide unselected, `Alt+H` unhide all. Hidden is a per-element flag, so hiding all bracing, or everything except one tree, is one action.
- Hidden elements are still sliced. A separate `disabled` flag excludes an element from slicing without deleting it.

## 8.10 Print checks

Analyses that run on demand or after generation and report into the outliner and the viewport as coloured overlays. None of them modify geometry.

- **Suction cups.** Closed or nearly closed cavities that open downward trap resin and pull on the FEP during lift. Detected from the layer stack: a region of a layer that is enclosed by material and whose enclosed volume grows upward without an opening to the outside. Reported with the enclosed volume and the layer range, and highlighted on the model. The fix, a drain hole or reorientation, is left to the user since mesh editing is out of scope.
- **Proximity.** Supports too close to each other, supports too close to the model surface they do not touch, and objects too close to each other, each with its own threshold. Reported as pairs with the distance and highlighted in the viewport.
- **Islands.** Layers containing material with nothing beneath it. Already computed during slicing, surfaced here as a check.
- **Below plate and outside volume.** Objects or supports outside the build volume.

## 9. Slicing and export

### 9.1 Slicing

1. Gather all objects and the support graph's slice geometry (analytic capsules and cylinders, not tessellated, so supports slice exactly).
2. For each layer height, intersect every mesh triangle with the plane (BVH-accelerated), chain segments into closed contours, and orient them by winding.
3. Clean and union contours per object with Clipper2. Apply XY compensation (offset) if set.
4. Rasterise to a 3840 x 2400 8-bit buffer with a scanline fill and coverage anti-aliasing, with configurable AA levels including off.
5. Detect islands per layer for warnings and for the island tip strategy.

Layers slice in parallel. Memory is bounded by streaming layers to the writer rather than holding the whole stack.

### 9.2 Print settings

Layer height, bottom layer count, bottom and normal exposure, light-off delay, lift distance and speed (two-stage where the format supports it), retract speed, AA level, XY compensation. Presets per resin.

### 9.3 `.pwmx` writer

Writes format version 516 (file mark, HEADER, PREVIEW, grey table, LAYERDEF, EXTRA, MACHINE, then pw0Img run-length layer images). Layer images are mirrored in X per the published Mono X profiles; the first print of an asymmetric test part confirms or flips this. Validation: every exported file in the test suite is opened with UVtools and compared to golden images. The writer sits behind `IPrinterFileWriter` so further formats are additive.

## 10. Project file

A zip container with a JSON manifest and referenced binary meshes:

```
project.json      objects, transforms, regions, profiles, support graph, print settings, view state
meshes/*.bin      imported meshes in a compact binary form, one per unique mesh
```

The support graph is stored fully. Derived data is never stored. Version field and forward-compatible unknown-field handling from day one.

## 11. User interface

Blender, not Word. Dense, keyboard-first, no wizards, no confirmation dialogs for undoable actions, no simplified mode. Modal tools with live numeric readout in the status bar, everything reachable by shortcut and by the command palette, panels that show data rather than explain it. Layout:

- **Main area** centre, used for everything spatial: positioning, support editing, slicing results and the layer preview. A header strip holds the gizmo and snapping toggles and the mode switch. Modes so far are the 3D model view and the 2D layer view (`Tab` toggles; slicing switches to layers, any geometry change drops the stale slice and returns to the model). Later modes add the shading popover, selection filter mask and transform orientation here.
- **Outliner** left: objects, regions per object, support passes per region, with visibility and selectability toggles.
- **Properties** right, tabbed: object transform, active region and profile, selected support elements, print settings.
- **Status bar**: current mode, live hints for the modal tool, generation and slicing progress, cancel.
- **Command palette** (`F3`) exposing every command by name.

Every numeric field accepts units and expressions and can be dragged. Every panel is dockable. Nothing hides behind a "simple mode".

## 12. Milestones

Each milestone ends in something usable.

1. **Skeleton.** Solution layout, Document and command stack, STL import, Avalonia window with an OpenGL viewport showing a mesh with studio lighting, orbit, pan, zoom, perspective and orthographic projection, numpad views. Object move, rotate, scale with modal tools and numeric entry.
2. **First print.** Slicer, `.pwmx` writer, print settings panel, layer preview. A test model exported and printed on the Mono X, validated with UVtools. No supports yet.
3. **SpaceMouse.** COM backend for the SpaceMouse Pro, navigation modes, button binding, HID backend as a stretch goal. Pulled forward because navigation is used from day one and nothing else depends on it.
4. **Readable viewport.** G-buffer pipeline, MatCap, screen cavity, outlines, overhang tint, FXAA, ID-buffer picking, render states, view cube. Lay flat and rotate about face.
5. **Support graph and manual supports.** Graph model, derived render and slice meshes, manual add, move, connect, delete, undo, selection filter, hide and unhide. Print a manually supported model.
6. **Regions and generation.** Region selection tools, profiles, overhang and island tip placement, top-down routing, keep-clean, determinism and incremental regeneration.
7. **Rules and grids.** Growth rule framework, grid bottom-up routing, bracing, reinforce rule, attach to existing.
8. **Polish.** Project file, presets, keymap editing, CLI, cross-platform builds.

Milestones 4 and 5 can be developed in parallel since they touch different projects.

## 13. Risks and open questions

- **Viewport on Avalonia OpenGL.** `OpenGlControlBase` has had context and resize quirks across Avalonia versions. Mitigation: isolate the renderer behind an interface from the start, so moving to a different surface (or Vulkan) does not touch the App project.
- **Grid bottom-up routing.** The least proven algorithm here. Prototype it early in the CLI against a few CAD and organic models before building UI for it.
- **Rasterisation quality.** Anti-aliasing and XY compensation interact with the printer's pixel grid in ways only real prints reveal. Budget for print tests in milestone 2.
- **`.pwmx` variants.** The Photon Workshop family has several versions. Confirm which version the Mono X firmware in use expects and validate with UVtools on that exact version.
- **Open: profile inheritance.** Should profiles derive from one another (a "CAD clean" base with variants) or stay flat? Flat for now; revisit when the count grows.
- **Open: region overlap.** When two support regions overlap, which profile wins? Proposal: regions are ordered, last wins, and the UI shows overlap.
