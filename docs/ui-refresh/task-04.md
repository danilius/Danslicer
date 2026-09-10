# Task 04 — plate and depth readability

Worktree: C:/Users/plane/.codex/worktrees/e262/Danslicer-chatgpt
Branch: codex/ui-refresh-04
Exact clean task-03 base / last-good commit: 8a0fac4f52515f18f1560a54b938dbb2b8f17a60.

## Plan and committed overlap audit — 2026-09-10
Verified detached HEAD at the requested base and clean status; created the dedicated branch. Read START, SPEC, TASKS, RESUME, CLAUDE-NOTE and prior task notes. Shared repository history only was inspected; no source from another checkout was read or copied. Main remains 7d95ceb. Renderer/raft commits already in the base include plate angle fade 1767634, shadows bc6eb18, and outward-only scraper lip ebfb13e. Tasks 01–03 did not change renderer/core geometry.

Existing plate is a flat quad at -0.05 mm; fading uses direction and eye height, defaults to 0.3 residual opacity. Shadows switch off as soon as fading starts. Deferred has four normal-neighbour cavity samples, outlines and FXAA, but no proximity AO. Classic has studio lighting, no cavity/AO. Raft mesh and slicing explicitly widen the outer top edge and keep holes vertical; validate existing tests before considering a correction.

Next: add an opt-in native GL framebuffer evidence harness isolated before config loading; capture exact baseline renderer above/below/contact. Implement shallow plate, restrained configurable rough reflections and complete below removal; add proximity AO to deferred while retaining cavity and honest Classic limits. Validate actual framebuffer output, picking, clipping, transparency, effects toggles and cost. Run geometry/renderer/UI regression tests and Release build. Preserve all task-03 contracts; no task 05 or main merge.

No implementation or visual checks passed yet.

## Baseline framebuffer checkpoint
Native GL harness renders deterministic scene/aux contacts through the production renderer, reads host framebuffer RGBA, verifies deferred did not fall back and GPU selection reaches the model from below. Initial pick target was obscured by a fixture pillar; moved to an unobstructed underside point. Both paths completed all seven views, GL error checks passed. ANGLE ES 3.0 on NVIDIA RTX 4090; 1000x760. Baseline timing 0.12–0.57 ms/frame with glFinish (tiny synthetic scene, not a production benchmark). Actual rendered images are evidence/task-04-before; above image reviewed. Build passed after qualifying PixelFormat; existing viewport warnings only. Initial sandbox restore was denied network sockets, approved restore succeeded. No production renderer edits yet. Last good plan commit bda9772; next implement shared plate/reflections and deferred AO.

## Renderer implementation checkpoint
Last good committed baseline: 2446a42. Same worktree/branch. Added viewport-only 2 mm closed chamfered plate (top still -0.05 mm), smooth angle/eye-height surface fade, smooth pre-fade shadow attenuation, grid fade and optional below perimeter. Legacy PlateOpacityFromBelow JSON value now controls perimeter only; saved value remains intact and the preferences label states its new role. No raft/slicing/support-generation source changed.

Shared quarter-resolution RGBA8/depth24 mirrored scene target plus nine weighted blur taps provides restrained reflections on both render paths. Default 0.12, maximum 0.3; omitted entirely below/off. Opaque visible model/aux geometry only, respecting isolation; no ghost/transparent/reflection picking. Plate has broad satin specular response. Fixed faded-plate ordering before transparent aux in both paths and enabled convex-skin culling during plate blending.

Deferred AO adds 16 bounded view-space depth samples (default 2 mm radius, 0.35 strength) to the existing composite; independent of the existing four normal-sample cavity. No new full-resolution attachment or pass for AO. Normal alpha identifies plate material for restrained specular. View/menu toggles and preferences strengths/radius use existing saving conventions; new preference fields use filled numeric controls. Classic retains its existing studio lighting without AO/cavity; this is explicit, not an AO implementation claim.

Actual checks: Release app builds passed; 280 targeted tests passed, 0 failed/skipped, including raft mesh/slicing outward-lip tests, new closed outward slab/continuous fade/config tests, renderer/clipping/support mesh and task-03 UI contracts. Native main smoke completed with workspace-ok.txt. Native GL after capture passed on both paths without errors/fallback; below GPU picking passed. Inspected contact, below, reflection-on and Classic above images. Quantitative comparisons detect independent deferred AO/cavity and shared reflections; below reflection toggles are byte-identical. First implementation had shadows overwriting the reflection material and translucent-support ordering inherited from baseline; both corrected before latest after capture. No raft mismatch confirmed, so no geometry correction or geometry commit.

Next: expand real support/cap and native view-toggle evidence, final visual/regression review, solution build and clean handoff. Tiny-scene GL timing is not a dense production benchmark. Prior physical DPI/mouse/screen-reader limits remain.
