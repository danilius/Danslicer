# Task 05 acceptance and integration report

Review candidate: task-05 source commit `0e42f4d0ff69744c7fc95f26ee11a9373e8ef94c`, plus its committed evidence/documentation descendant (final hash in handoff). Worktree `C:/Users/plane/.codex/worktrees/ed62/Danslicer-chatgpt`, branch `codex/ui-refresh-05`. Task 05 engineering review complete; user acceptance pending. Tasks 06 and 07 remain queued.

## Findings and fixes

| Finding | Correction | Verification |
| --- | --- | --- |
| Reopening a rafted project discarded the raft and imported source path, despite correct file deserialization. | `Document.ReplaceWith` now copies `Raft` and `SourcePath`. Separate core commit `b34807aac8864843823d5f3b9e6f729479d2074e`. | Extended raft/source roundtrip tests through live document/VM open; native generated-support/raft project reload. |
| Refreshing printer dropdown items could write a transient index 0 back and replace an embedded project printer with the default machine. | Ignore selection writeback during synchronous options/selection publication, preserving later deliberate selection. | New binding-writeback regression and native custom-printer save/open/slice/export. |
| Project menu displayed malformed ellipsis text. | Corrected “Open another project…” label. | Native menu assertion. |
| Native raft check sometimes assumed background dispatcher execution within 220ms. | Harness awaits actual change with a two-second bound; production timer stays 100ms. | Subsequent native runs, applied/pending preview cancellation and undo checks pass. |

The complete fixture initially assumed a lit terminal tetrahedron layer. Its last cross-section can be smaller than a pixel; the assertion now witnesses interior raft/support/model heights and compares every exported bitmap against its sliced source. This was a fixture assumption, not a slicing correction. Initial failures and diagnostics are retained in `evidence/task-05-tests/initial-native-findings.txt`.

## Specification acceptance checklist

“Passed” below means the indicated source, automated, routed native or framebuffer check passed. It does not imply physical input or hardware certification.

| Approved requirement | Result / evidence |
| --- | --- |
| Charcoal palette, restrained amber, vector icons, compact 12-DIP typography and 24/26/28-DIP controls | Passed source/native visual review; `task-05-ui-final/workspace-layout.png`, `settings-support.png`, gallery density renders. No runtime raster icons introduced. |
| Floating inset toolbar, both label modes, tooltips, mode scoping, complete settings access | Passed native all 12 popouts, toolbar modes and command/keymap full-suite tests; `workspace-ok.txt`. Support rail is the approved exception. |
| Anchored popouts, scrolling, bounds, resizing, explicit close/toggle/Escape and focus return | Passed native main/gallery. In-window host has no outside-click light dismissal; physical orbit-through-popout not certified. 640x480 bounds checked. |
| Whole-expander live swaps at displayed gripper centres; reversal and cancellation; 250ms cubic displaced easing and 150ms release settle | Passed gallery and native workspace checks; `task-05-controls/capture-ok.txt`, `preview-drag.png`. |
| Persist width/order only on completed gesture, expansion and toolbar mode | Passed native main new-window restoration, unchanged file during reorder, resize cancellation; `settings-ok.txt`. |
| Compatible workspace-ui.json, corrupt/missing defaults, stale IDs, newer-schema read-only, unknown fields, ten recents | Passed full WorkspacePreferences tests and native recents/restoration. Offline recent paths retain failed-open status. Workspace writes leave unrelated user config intact. |
| Preserve status content/commands/behavior | Status Border subtree identical to main after newline normalization; `source-invariants.txt`. Full keymap/mode/history tests; native generation, slicing, export and missing-project status exercised. No status-bar source changes. |
| Auto drop left, truly centred Layout/Support/Slicing, right Project, subtle accessible machine/resin | Native centre assertions at normal/narrow width and Auto drop binding pass; narrow uses second machine/resin row. Printer refresh defect corrected. |
| Filled rectangular bounded fields, centred value/unit, actual pointer-track mapping, Shift fine control | Passed numeric tests and native `slider-tracking-ok.txt`; unbounded model scrubs remain relative. |
| Expressions/units, two-decimal edits, range rejection, integer constraints, locks where defined | Passed parser/numeric tests, native fields and gallery lock checks. No new production locks invented. |
| Live scene previews, one undo where applicable, release-only save, no-op suppression | Passed `live-numeric-ok.txt`, numeric tests and seven real GL before/cancel/commit comparisons. Support/preference setters retain existing commit semantics; no new preference document undo. |
| Cancel on Escape/capture loss/selection/mode/detach/close; prevent unrelated-save leakage | Native lifecycle/config-save checks pass; project Save cancels active scrub in source before serialization. Capture harness uses synthetic routed events, with explicit thumb capture for slice navigation. |
| Existing selected raft coalescing, accurate final computation; no Add/generation during drags | Passed native selected-raft preview/undo/redo/pending and applied cancellation. Production 100ms unchanged; measured dense rebuild limit below. |
| Future print/support settings do not regenerate existing results during dragging | Explicit policies retained from `slider-policy-audit.md`; native policy tests/full suite pass. Generate, Slice and Export executed explicitly by full fixture workflow. |
| Support-only borderless isolation; cube +12 DIP; centred Reset/Cap; saved Cap off; layer/mm hover editors and 300ms grace | Native assertions, bounds and hover/edit/cancel checks pass; `workspace-isolation-narrow.png`; config-off roundtrip pass. |
| Shallow plate, subtle reflections, complete below surface/grid/reflection removal and selection through | Both real GL paths pass below pick and below reflection identity; `Deferred-below.png`, `Classic-below.png`, above/reflection frames. Geometry stays below Z=0. |
| Independent AO/cavity configuration | Deferred pixel-difference checks and live preview/cancel comparisons pass. Classic has no AO/cavity; transparent geometry does not contribute AO. |
| Working/Presentation/Off shadows, independent strength/softness, camera light, transparent exclusion | Both paths' framebuffer comparisons, self-shadow, clipped/transparent fixtures and native saved-mode independence pass. |
| Clipping/caps/cube/dense supports | Both GL paths, 15 real supports and 600-support/422400-triangle scene pass; cap-leak assertion passes. Classic exact caps and Deferred painted caps inspected. |
| Outward raft scraper lip and core compatibility | All raft mesh/slicing tests pass; geometry and support/slicing algorithms unchanged. Only live-document copy corrected. |
| Actual import/project/transform/support/raft/slice/export workflow | Native imported STL, transform/generation/raft undo-redo, generated 12 nodes/8 segments, project reload, custom 480x300 printer, 110 layers, navigation and export pass. Every exported bitmap equals its sliced source. `workflow-ok.txt`, `workflow-sliced.png`, retained project/export fixture. |
| 100/150/200% and narrow/accessibility review | Render-density images generated; representative normal, 150/200 and narrow images inspected; keyboard/routed pointer checks pass. Physical DPI transition, physical mouse and screen reader remain unperformed. |

## Actual regression and rendering results

- Final Release solution build passed. Clean build had five existing warnings: SurfaceContour CA2014, two ViewportControl CS8602, two RaftBuilderTests xUnit2031. Incremental final build reports four; unchanged Core warning may not repeat.
- Final entire test suite: **1011 passed, 0 failed, 0 skipped**. Retained `task-05-tests/task05-full.trx` and `full-suite.log`. Baseline before fixes: 1010 passed. Targeted project/printer/raft/reload: 36 passed.
- Final MainWindow process exited 0 with all eight success text files in `task-05-ui-final`, no workspace-error.txt. Isolated temporary AppConfig; test outputs inside this worktree. GUI offscreen images omit GL.
- Gallery process exited 0; capture-ok.txt; numeric/resize/reorder keyboard and routed pointer checks plus 100/150/200-density/narrow images.
- Actual GL process exited 0: **112 PNGs**, no error.txt, no fallback, all GL error checks/picking/pixel comparisons passed. ANGLE OpenGL ES 3.0 on NVIDIA RTX 4090 / D3D11 driver 32.0.15.9186, 1000x760. `task-05-gl/result.txt` contains every timing and comparison.
- Seven GL live numeric settings visibly differ before release with zero saves; cancellation returns identical original bytes, final commit identical preview bytes with one save. These are real framebuffer reads, not Avalonia screenshots.

Representative actual costs (milliseconds, CPU submission plus glFinish; 30 samples after 12 warmups):

| Fixture | Median | p95 | Max |
| --- | ---: | ---: | ---: |
| Deferred dense | 0.31 | 0.44 | 6.65 |
| Deferred dense effects off | 0.16 | 0.18 | 0.18 |
| Classic dense | 15.59 | 16.07 | 16.11 |
| Classic dense effects off | 15.67 | 16.11 | 16.43 |
| Deferred Working shadows | 0.47 | 15.73 | 15.83 |
| Deferred Presentation shadows | 0.53 | 16.06 | 16.58 |

Classic frequently clustered around 16ms even with effects off. These synchronized host measurements include scheduling/driver effects; they are not a portable GPU benchmark or proof of an effects slowdown. Raft outline+mesh, seven warm samples: Plate 600 feet median 51.70/max 64.02ms; Web median 17.32/max 27.47ms. A large synchronous rebuild can still block a frame despite 100ms coalescing. Accurate final raft calculation remains synchronous; exact CPU caps use open-surface dragging and final recomputation.

## Compatibility and committed-main reconciliation

Clean starting ref was `c829020492020c333f1c0937b7275732927a6a4f` (task-04b plus queue update). Read-only shared-main and Claude-HEAD audit, rechecked after validation: stable `7d95cebfe7f289d1e3230fb789a4526d1f3b518d`. `git merge-base HEAD main` equals main and `git log HEAD..main` is empty. No newer main commits exist to reconcile, so no merge was performed. No uncommitted Claude source was read/copied; no other worktree, routing checkout, main, push or later task was changed. Only this branch's Git metadata uses the shared repository.

Relative to main, Core changes are viewport UserConfig additions and the two-field live Document copy fix. Project version/schema, STL/OBJ loading, support/raft generation and slicing/export algorithms remain unchanged. Existing legacy project/config and new workspace sidecar tests pass. Latest policies in SPEC and slider-policy-audit override historical commit-only descriptions and inherited short success-log wording. No task-06 volume/time UI or task-07 printer assessment work included.

## Remaining limits / review instructions

No unresolved actionable failure remains in the exercised checks. User should still review physical mouse feel, real 100/150/200% monitor transitions, touch/pen, screen readers and alternate GPU/driver behavior. Native file pickers, overwrite/unsaved-change dialogs, UVtools launch/integration, SpaceMouse hardware and physical printing were not exercised. No reference bust or full-size production print job certified. Only dark styling is supported; no light-theme claim. Classic AO/cavity limitation and synchronous raft/cap costs remain explicit.

Review the MAIN executable using RESUME.md's absolute command. Task 06 must start in its own worktree from this candidate's final documentation/evidence commit after user instruction; preserve queued task 07. Do not merge into main without user instruction.
