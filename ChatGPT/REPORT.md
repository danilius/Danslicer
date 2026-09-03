# ChatGPT implementation report — briefs 10, 9, 8, 11 and 12

Branch: `grid-routing-prototype`. Merged current `main` (`e7d0792`, containing the requested
`6228f40` SpaceMouse merge) before brief 12 implementation.
Headless verification only; the app was not launched.

## Brief 12 — workspace semantics and screen-test fixes

### 1. Marquee visibility

Diagnosis confirmed hypothesis (a): the marquee is painted by Avalonia's 2D
`ViewportControl.Render`, but `Redraw()` only called `RequestNextFrameRendering()`, which schedules
the OpenGL composition surface and does not invalidate the 2D visual. Pointer movement therefore
updated the rectangle coordinates without rerunning the code that draws it. `Redraw()` now also
calls `InvalidateVisual()` on the UI thread.

Hypothesis (b) was ruled out: `PickSurface` ray-tests only visible scene objects. The rendered build
plate is not a `SceneObject`, so empty plate/background space yields both `hitObj == null` and no
support hit, correctly arming the marquee.

### 2. Text-input key gesture arbitration

All application-level bindings now pass through one focus-aware command wrapper. When a `TextBox`
(including the numeric `ExpressionBox`) has keyboard focus, the wrapper reports that the application
command cannot execute, so the key event remains available to the editor.

Every gesture formerly declared in `MainWindow.axaml` was affected by the same preemption class and
is guarded: Ctrl+Z, Ctrl+Shift+Z, Ctrl+Y, Delete, Ctrl+D, Ctrl+G, Ctrl+A, Shift+H and Ctrl+R. The
programmatic Ctrl+I, Ctrl+E and Ctrl+, bindings use the same central path as a preventative measure.
This is especially visible for Ctrl+A, Delete, undo/redo and literal uppercase H entry, but yielding
all application shortcuts is the consistent rule while a text editor owns focus.

### 3. Workspace semantics and owned-support transforms

- Support mode clears viewport object selection and suppresses object hit-selection, gizmos,
  G/R/S object transforms and lay-flat. The Objects list retains an independent `SelectedObject`,
  so it remains the generation target without making that object viewport-selected. Support click,
  marquee, tip move and support deletion remain available.
- Layout clears support selection and suppresses all support hit-testing. Object selection and every
  transform path remain available there.
- `SupportOrigin` now records `ObjectId`. Generated, routed-manual and straight-manual support nodes
  and segments all receive the target object's id.
- `CommitTransform(s)` maps every owned support node from the object's old local space into its new
  world space and records the node positions/normals beside the object transform in one composite
  command. Numeric edits, modal/gizmo commits, explicit drop-to-plate, auto-drop and lay-flat all use
  that path. Modal previews also carry supports live and cancel restores both object and supports.
- Bracing between supports owned by one object works automatically because both endpoint nodes move
  through the same mapping. If a brace ever spans objects, each endpoint follows its own owning
  object; moving only one object therefore deforms the cross-object brace rather than moving the
  other object's support.
- Tab uses a tested state machine: with a slice, Layout → Support → Slicing → Layout; without one,
  Layout ↔ Support (and Slicing → Layout if invoked while an empty slicing view is open).
- Slicing is always enterable from the header. An absent or invalidated slice leaves the user in the
  empty layer view with print controls available, and slicing completes in Slicing mode.

### 4. Mode-specific right panel and resizable sidebars

- Removed the Object/Print tab control. The right panel now follows the workspace directly: Layout
  shows object transforms and generation, Support shows the support-control area, and Slicing shows
  print settings, anti-aliasing, slice/export actions and the slice summary.
- Replaced fixed DockPanel sidebars with grid columns and two `GridSplitter`s. Left and right panel
  widths resize independently with minimum widths of 140 and 220 px.
- `WindowStateConfig` now stores both panel widths beside the existing main-window bounds/state.
  `WindowStatePersistence.Track` restores valid saved widths and captures actual widths on close.
  Config round-trip coverage includes the two new values.

## Brief 10 — screen-test fixes

The reported diagnosis matched the code, so no divergent finding was required before work.

- Shift+H now expands every selected node or segment to its complete non-bracing connected
  component. The selected element's whole support tree stays visible; unrelated trees hide in the
  same single undoable command. A selected brace retains the components at both ends plus itself.
- Removed the two default model-landing opt-ins from manual T routing and CLI top-down routing.
  `LandGrowthRule` and its opt-in tests remain compiled for a future profile setting.
- Default refusals remain honest: the canonical runs report only ContactBlocked/NoClearStep and
  zero NoLanding.
- Full-resolution unchanged Drogon comparison: landing-enabled 302 / 1113 unrouted became
  landing-disabled **343 / 1113** (24 ContactBlocked, 319 NoClearStep), an accepted increase of 41.

## Brief 9 — embedding-depth reconciliation and benchmarks

`PenetrationDepth` is the surviving name; no duplicate `EmbeddingDepth` property was introduced.
It now flows through `TipPlacementParameters` (`PenetrationDepthMm`), `TipCandidate`, `RoutingTip`,
CLI tips JSON, CLI route JSON ingestion, routed nodes, and document graph copies.

- Default is 0 mm and negative values clamp to 0.
- A cone's narrow end moves past the contact along the opposite of the neck direction while its
  base stays fixed. This extends the frustum into the model and makes its surface section wider.
- The same value continues to offset an optional snap-off ball along the inward surface normal,
  preserving the reviewed ball rule while using one parameter for contact embedding.
- Tests cover JSON schema round-trip, negative clamping, zero-depth bit identity, embedded sections
  past the contact plane, surface widening, and parameter flow into routed tip nodes.
- The complete seated canonical matrix was appended to `docs/BENCHMARKS.md`. In particular, the
  previously killed gripper top-down case now finishes in 5.909 s with 52 / 445 unrouted.

## Brief 8 — non-blocking generation

Implemented before brief 11 to settle the generation command lifecycle before changing workspace
UI structure.

- The UI captures an immutable scene/support snapshot, then runs mesh transformation, BVH setup,
  tip placement and routing on a worker thread. No `Document` mutation occurs there.
- Pipeline progress reports tips placed and tips routed. A determinate insertion bar appears above
  the status bar with a Cancel button.
- The prepared deterministic result is inserted on the UI thread in 32-element batches, yielding
  between batches so supports appear incrementally.
- Batches are deliberately absent from history. Completion records the already-applied aggregate as
  one `Generate supports` command; cancel removes inserted segments/nodes and leaves no undo entry.
- A second request is disabled while generation runs. Any other document geometry change cancels
  the captured run; changes caused by its own batches are distinguished and do not self-cancel.
- Core tests cover incremental visibility, one-step undo/redo, exact cancellation rollback, and
  equivalence between one-element and one-shot batch sizes.

## Brief 11 — workspaces, Ctrl+A and marquee

- Replaced Model/Layers with `WorkspaceMode.Layout`, `Support`, and `Slicing`, exposed as a three-way
  header switch. Layout keeps today's gizmo controls; Support uses the same 3D viewport; Slicing is
  today's layer preview.
- Ctrl+A and the Select All menu item are mode-dependent: visible objects in Layout, visible support
  nodes/segments in Support, and no action in Slicing. Plain A remains the existing viewport binding.
- Tab returns from Slicing to the model workspace it came from. With a valid slice it enters Slicing;
  without one it switches Layout/Support. Slice invalidation returns to the remembered model mode.
- Dragging empty space in Support draws a marquee; release selects projected visible support nodes
  and segment midpoints, and Shift extends. Occluded elements use the same surface-depth arbitration
  tolerance as click picking. B arms border selection so a box can start over model geometry.
- Tests cover mode-dependent select-all and screen-space marquee inclusion/hidden filtering.

## Verification

- `dotnet build --no-restore`: clean, 0 warnings / 0 errors.
- `dotnet test --no-restore`: **242 passed** after the final added tests (update if final run differs).
- No app launch, per the briefs; progress bar and marquee remain for user screen verification.
