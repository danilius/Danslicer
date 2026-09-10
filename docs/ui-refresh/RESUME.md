# Task 04b native lifecycle checks resolved
Same da71 worktree / codex/ui-refresh-04b. Final main native run passes including layer navigation; see task-04b.md and evidence/task-04b-final-ui. Build passed. Next: gallery, final regression record, evidence cleanup and clean handoff commit. Prior good checkpoint d3624ff; this iteration is ready for its source checkpoint.

# Task 04b iteration 2
Same da71 worktree / codex/ui-refresh-04b; prior good d3624ff. GL numeric preview evidence and 225 regressions pass. Audit at slider-policy-audit.md. Expanded native suite found layer-navigation capture-loss issue; fix builds and fresh native rerun is in progress. Next: verify fresh final native outputs, control gallery, final build/checks and clean commit. Task 05 remains queued.

# Task 04b iteration 1
Same da71 worktree / codex/ui-refresh-04b. Prior checkpoint 5300cb7. Transaction implementation builds; 51 focused tests and updated native MainWindow checks pass. Read task-04b.md for changed files/evidence. Next: real GL preview evidence and raft profile, native shading/lifecycle coverage, complete audit/regression. Task 05 remains queued.

# Task 04b active — latest checkpoint
Worktree: C:/Users/plane/.codex/worktrees/da71/Danslicer-chatgpt; branch codex/ui-refresh-04b.
Exact clean base / last-good: 0df54ff6014dce8f143def47c3fe4831101cd450. Read task-04b.md and latest SPEC contract before continuing. Plan recorded; no implementation/tests yet. Next: explicit preview transactions and host audit. Task 05 remains queued; no main merge.

# Final requested change: slider tracking and decimal precision
Filled sliders now follow pointer position across their actual range/width. Step no longer controls mouse sensitivity. Numeric field displays and commits use at most two decimals; integer fields stay integers. Existing preview/commit/undo/cancel contracts retained. See task-04.md and evidence/task-04-slider-final for final validation. Same branch/worktree; no merge. User indicated this completes the task.

# Latest follow-up: Preferences visual controls
Preferences > Viewport now includes all shadow presets/tuning and independent AO, cavity, plate shadow, reflection and cube switches. Display-only settings save separately from support geometry settings and synchronize View controls. Shadow renderer unchanged from 2f4404980b204aee541b58837c64064d297e799c. See task-04.md for follow-up validation. Same worktree/branch, no merge.

# Optional shadow system — latest authorized work
Same e262 worktree, branch codex/ui-refresh-04. Prior good code: 2f3e48e1c18298989812bd2f37221b83fc20fc72; implementation plan: aa95a8b. Resolve current committed HEAD for the shadow handoff. User approved implementing subtle default shadows plus stronger optional Presentation.

View settings now offers Shadows: Off / Working / Presentation. Working defaults to strength 0.22, softness 0.6 mm; Presentation defaults to 0.50 and 1.2 mm. Each mode retains independent saved fields. AO is separate. Both Classic and Deferred render opaque model/support cast and self-shadows from one camera-relative directional light. Plate rendering retains its existing separate controls.

One 2048-square depth map and 48 fixed disk taps; receiver-plane depth correction at actual sampled texel centres prevents flat-face acne. No temporal accumulation. Strength capped at 0.7 to retain illumination. Off/zero strength skips the map pass. Transparent/ghost/overlay geometry is excluded. Geometry is clipped before casting; Deferred painted caps are screen-space faces and are not additional shadow casters. Synthetic box/support fixtures validate the effect; no actual bust asset or alternate GPU was tested. This is an approximation of soft studio shadows, not ray-traced global illumination. Map resolution bounds very large-scene detail; final harness timing is in the evidence log.

Release solution build, 115 focused tests, native shadow controls/persistence checks and real GL checks are documented in task-04.md. Historical handoffs below describe earlier revisions and are superseded by this section where they say model shadows are not implemented. MAIN launch command below remains correct. No task 05 or main merge.

# Latest task-04 refinement — ready for review
Same e262 worktree and codex/ui-refresh-04 branch. Starting/last-good prior handoff: acbff390c73835cfde7235e8c7159047888f4281. Current HEAD is the committed refinement; resolve exact ref before continuing. Auto drop restored to left of top strip, workspace tabs centered, AO strength/radius directly in View settings, compact dark cube/XYZ arrows implemented. Build, 127 focused tests, native main checks and 64 GL views passed. Details and evidence in task-04.md and evidence/task-04-refinement-{ui,gl}. MAIN launch command below is unchanged.

User chose gentle working shadows by default plus stronger optional presentation mode during discussion. That is the agreed direction for subsequent shadow work, not implemented cast/self-shadows in this revision. No later task or merge. Physical-input/alternate-GPU limits below remain. The historical task-04 handoff follows.

# Task 04 — completed implementation, ready for user review

Date: 2026-09-10
Worktree: C:/Users/plane/.codex/worktrees/e262/Danslicer-chatgpt
Branch: codex/ui-refresh-04
Exact user-accepted task-03 base: 8a0fac4f52515f18f1560a54b938dbb2b8f17a60.
Last-good implementation/evidence: cafca37cbc74f9f0bf21094271f2b2bb07961f1f.
Final handoff is the following documentation-only descendant; resolve branch HEAD and check clean status. No main merge or later task created. Main, previous worktrees and routing prototype were not modified; shared Git metadata only was used for this worktree's commits.

## Delivered
Viewport-only 2 mm chamfered build plate, exact printable footprint/top offset retained. Restrained quarter-resolution blurred planar reflections on both paths. Smooth angle/eye-height fade removes surface, reflection and grid below; optional perimeter remains. Legacy PlateOpacityFromBelow JSON value is preserved but now controls perimeter only (Preferences label updated). Deferred ID picking passes through below; Classic CPU picking traverses document objects only and has no plate candidate.

Deferred contact AO now provides independent local depth/proximity shading; existing cavity ridge/valley effect retained. View menu/popout toggles, Preferences filled numeric fields for AO strength/radius and reflection strength. Existing settings saving conventions. AO/cavity operate on opaque geometry in Deferred; Classic fallback retains studio shading without these effects. Both paths have plate/reflections. Plate material remains satin Studio in model MatCap modes and fades its deferred effects before crossing to translucent rendering.

Fixed faded-plate ordering before transparent supports. Required real-support GL checks also exposed a painted-cap stencil artifact; the narrow renderer fix excludes solids not crossing the named cap plane and culls reverse cap faces. Framebuffer regression guards phantom upper-cap marks. No raft, slicing or support-generation algorithm/output changes. Existing outward-only top scraper lip confirmed by mesh/slicing tests, so no geometry correction commit.

## Verification and evidence
Release solution build passed. Broad targeted run: 280 passed, zero failures/skips. Final cap/plate/raft/render-config run after refinements: 50 passed, zero failures/skips. Existing SurfaceContour CA2014, viewport CS8602 and raft-test xUnit2031 warnings only; final incremental build may not re-emit all.

Actual GL host framebuffer reads on ANGLE OpenGL ES 3.0 / NVIDIA RTX4090 at 1000x760: 58 views passed without GL errors or deferred fallback. Above, below, grazing/0/3/6/9/12-degree transitions, contact, independent AO/cavity/reflection toggles, orthographic, MatCap, selection, transparent supports, production cone/disc/trunk support meshes, upper/lower painted and exact sliced caps, 600-support fixture. Below reflection toggles byte-identical; deferred GPU pick reaches underside model. Cap on/off and no-phantom-cap pixel assertions passed. Inspected representative images, including both sides of caps and new view-settings popout.

- evidence/task-04-before: unchanged baseline renderer (captured at harness commit 2446a42).
- evidence/task-04-after: final actual GL PNGs and result.txt with timing/toggle metrics; painted-cap-before-fix.png is explicitly pre-fix evidence.
- evidence/task-04-ui: native main workflows/settings/view-toggle round trips and screenshots. These offscreen UI screenshots omit GL composition; use the GL directories for renderer claims.
- evidence/task-04-controls: native reusable-control checks, including gripper thresholds, 250ms ease, reversal/cancel and persistence boundary.

Native main/control checks passed. Status-bar subtree identical to task-03 after newline normalization; no ToolShells, workspace persistence, isolation, support or slicing source diff. User preferences were isolated in tests; GL harness bypasses AppConfig entirely.

## Launch MAIN application
```powershell
& "C:\Users\plane\.codex\worktrees\e262\Danslicer-chatgpt\src\Danslicer.App\bin\Release\net10.0\Danslicer.App.exe"
```
Build from this worktree: `dotnet build Danslicer.slnx -c Release --no-restore -p:UsedAvaloniaProducts= --nologo`.
GL evidence: same absolute executable with `--renderer-capture docs/ui-refresh/evidence/task-04-after`.
Main checks: same executable with `--workspace-capture docs/ui-refresh/evidence/task-04-ui`.
Controls: same executable with `--ui-preview --capture-directory docs/ui-refresh/evidence/task-04-controls`.
Check result.txt/workspace-ok.txt/capture-ok.txt and absence of corresponding error files; Windows GUI launch can return before process completion. Commands use relative evidence destinations from this worktree. Full test filter is in task-04.md.

## Cost and limitations
AO: 16 depth samples per lit opaque deferred pixel in existing composite, bounded to 96 pixels and a world-mm radius; no extra full-resolution attachment/pass. Cavity: existing four depth/normal neighbours. Reflections: one extra opaque geometry pass at quarter width/height, RGBA8 + depth24 (about 0.38 MB at 1000x760), nine weighted texture taps on top plate; no pass below/off. Does not reflect ghosts/transparent supports. No temporal accumulation, global illumination, off-screen AO, transparent-surface AO or physically based reflection claim. Screen-space AO is view-dependent and may miss subpixel contacts; Classic has no AO/cavity. Reflection allocation failure disables it for that session with a log.

Final 30-frame median timings after 12 warmups, CPU submission plus glFinish: small contact Deferred 0.28 ms vs 0.19 with AO/cavity/reflections off; Classic 0.13 vs 0.09. 600 supports / 422400 aux triangles: Deferred 0.22 vs 0.16; Classic 0.22 vs 0.15. Short earlier samples had outliers up to 15 ms; these are fixture measurements, not end-to-end or portable hardware promises. See result.txt for p95/max. Baseline/after effects-off screenshots are not an exact shared toggle configuration; use within-after toggle comparisons.

No physical mouse/orbit, monitor/DPI transitions, screen readers, alternate GPU/native desktop GL3.3 driver, forced failure injection, full geometry suite, full production support generation/slicing job, dialogs or UVtools verification. Real GL ES compilation/execution and both explicitly selected renderer paths were verified. UI density captures are not physical monitor DPI tests.

## Next action / task 05 review instructions
User reviews this MAIN build. Do not create/implement task 05 here. After acceptance, task 05 starts in a separate worktree from this exact final HEAD. Read START/SPEC/TASKS/task-01..04 and this RESUME; verify base/status and audit committed Claude overlap before any reconciliation. Preserve task-03 numeric/expression/units/undo, filled sliders, status/keymaps/modes, gripper-centred swaps with 250ms displaced ease and OrderCommitted-only saving, and the Support-only borderless right rail with cube Rect +12 DIP, centered Reset/Cap, saved off choice and 300ms hover grace. Review normal models/dense supports at real desktop scale, both projections, isolation/caps/selection, effects toggles, alternate GPU and physical DPI; run integration regressions. Keep rendering and any future confirmed geometry changes separate. No main merge without user instruction.




