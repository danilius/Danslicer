# Latest handoff — user-review refinements complete

Tested source e27b0fcfdc3828469999ebe786b99e3f48120a13 in ed62/codex/ui-refresh-05. Full 1011 tests, native UI/editor/input-connection checks and actual GL width comparison pass. See latest acceptance report and RESUME. Historical checkpoints below remain for recovery; no later task or main merge.

# Current checkpoint — Task 05 complete, review candidate

Worktree: C:/Users/plane/.codex/worktrees/ed62/Danslicer-chatgpt
Branch: codex/ui-refresh-05
Last-good tested source: 0e42f4d0ff69744c7fc95f26ee11a9373e8ef94c
Core fix: b34807aac8864843823d5f3b9e6f729479d2074e
Final evidence/documentation descendant is the handoff at branch HEAD (exact hash in final response).

All authorized engineering checks complete: solution build, 1011 full tests, native integrated workflow, gallery, 112 actual GL framebuffers. No unresolved exercised failure. Three production defects fixed; see task-05-acceptance.md for specification checklist, findings, results, costs, compatibility/reconciliation and explicit remaining manual limits. Main rechecked at 7d95cebfe7f289d1e3230fb789a4526d1f3b518d, fully ancestral; no merge. Preserve 06/07 queue.

Retained evidence under task-05-tests, task-05-ui-final, task-05-controls and task-05-gl. Failed-run text consolidated into initial-native-findings.txt before removing only this worktree's verified task-05 attempt directories. No other worktree/files touched. Latest RESUME.md is rewritten unambiguously; pre-task-05 RESUME retained in RESUME-history-through-04b.md. Next: user reviews MAIN app with absolute commands in RESUME; no main merge or later task dispatch.

---
Historical iteration notes follow. Their pending-work language is superseded above.
# Task 05 — regression and integration candidate

## Current checkpoint — started 2026-09-10
Worktree: C:/Users/plane/.codex/worktrees/ed62/Danslicer-chatgpt
Branch: codex/ui-refresh-05
Exact clean starting/last-good commit: c829020492020c333f1c0937b7275732927a6a4f.
Read START, SPEC, TASKS, RESUME, CLAUDE-NOTE, all task 01–04b notes and slider-policy-audit. Latest SPEC refinements govern.

Read-only committed main audit: main and Claude checkout HEAD are 7d95cebfe7f289d1e3230fb789a4526d1f3b518d (SpaceMouse merge). `git merge-base main HEAD` is that same commit; `git log HEAD..main` is empty. Main is fully ancestral: no newer committed work to reconcile, so no merge. No uncommitted source read/copied and no other checkout modified. Git branch metadata required approved escalation; branch creation succeeded.

Plan: full Release solution build and test suite; inspect integrated production contracts; extend native fixture workflow through supports/raft/project/slicing/export; rerun native main/control harnesses and both real GL paths; inspect retained UI/framebuffer evidence. Fix only actionable defects, keeping rendering/core corrections separate from UI fixes. Final acceptance matrix, compatibility and limitations report, clean commits and absolute MAIN commands. Tasks 06/07 remain queued; no merge/push/later task.

Actual checks so far: clean base and ancestry passed. Initial restricted restore reports NU1301 socket denial; retry with approved network access after it exits. No build or tests yet claimed passing.
Next: restore/build, run broad regression, add complete fixture workflow and examine UI evidence.

## Iteration 1 — broad regression and native workflow
Worktree/branch unchanged; last-good checkpoint 05a8004. Release solution build passed after approved restore (initial sandbox NU1301); baseline full suite: 1010 passed, 0 failed/skipped (TRX in evidence/task-05-tests). Existing warnings: SurfaceContour CA2014, two viewport CS8602 and two raft-test xUnit2031.
Added opt-in native full-print workflow to the existing isolated MainWindow capture. First compile corrected an Avalonia/Core PlacementMode name collision; diagnostic compile corrected a nonexistent preview-property reference. First native run failed the raft 220ms preview assertion; unchanged production passed that assertion on diagnostic rerun, suggesting dispatcher timing. Diagnostic rerun reached new generation/raft/undo checks, then failed project roundtrip assertion; investigating exact differing state before classifying. No passing workflow claimed yet.
Source review found malformed project-menu ellipsis; fixed label only. Core slicing/support/project algorithms remain byte-identical to main; only Core viewport config differs across the entire UI branch. Next: diagnose roundtrip, stabilize bounded async harness assertions, GL/native evidence and final acceptance review.

## Iteration 2 — confirmed project-open defect and core correction
Last-good plan remains 05a8004; current tested correction is ready for its separate core commit. Native diagnostics proved file-loaded raft parameters were discarded only by Document.ReplaceWith; SourcePath was also omitted in that same clone. Preserve both persisted fields when replacing the live document. No file-schema or slicing/support/raft algorithm change. Extended existing raft/source-path roundtrip tests through the actual live-document/VM open path: 26 targeted project/raft/reload tests passed after rebuild.
The raft assertion passed unchanged on both diagnostic reruns. Harness now waits for the observable coalesced change for at most two seconds instead of assuming dispatcher execution within 220ms; production interval remains 100ms. Full-print fixture now resets support settings so previous slider sweeps do not inflate its raft. Next: rerun native workflow, then GL/gallery and final broad suite.

## Iteration 3 — embedded-printer binding correction
Last-good core commit b34807aac8864843823d5f3b9e6f729479d2074e; same worktree/branch. Native post-fix run now preserves raft/transform/supports, but loses the custom printer: actual printer becomes default Mono X while layer height survives. Synchronizing injected fixture bindings did not resolve it. Root cause: RefreshPrinterOptions publishes new ComboBox items and receives transient SelectedIndex=0 writeback, overwriting Document.Printer. Guard selection setter only during synchronous item/selection publication; deliberate subsequent selections still apply. Added regression simulating binding writeback. 36 targeted project/printer/raft/reload tests pass. Native full run pending; no printer-format or slicing changes. Next: confirm workflow, gallery/GL and full final suite.

## Iteration 4 — native print and final broad suite passed
Same ed62 worktree/codex/ui-refresh-05; last-good committed core b34807aac8864843823d5f3b9e6f729479d2074e. Native fixture after printer guard reached successful slice/export. A new assertion incorrectly required the tetrahedron's sub-pixel terminal section to contain a lit pixel; corrected fixture expectation to known raft/support/model interior heights and strengthened verification to compare every decoded bitmap with the sliced source. No production slicing change.
Final native MainWindow harness: evidence/task-05-ui-final/workspace-ok.txt, live-numeric-ok.txt and workflow-ok.txt; no error. 12 nodes/8 segments, undo/redo, raft and source-path reload, embedded 480x300 printer, 110 layers, explicit export, every decoded image equal, unchanged export reuses slice. Project dropdown ellipsis assertion passes. Final full suite 1011 passed, zero failed/skipped; final solution build passed (existing warnings). Real GL/gallery evidence next; do not mark complete yet.

## Iteration 5 — actual rendering and gallery passed
Same worktree/branch; last-good core b34807aac8864843823d5f3b9e6f729479d2074e. Native GL exited 0, 112 fresh PNG framebuffers in task-05-gl, result.txt and no error.txt. ANGLE GLES3, NVIDIA RTX4090/D3D11, 1000x760. Both paths, below model picking, independent reflections/shadows, Deferred AO/cavity, exact/painted caps and cap-leak check, cube and 600-support/422400-triangle fixture passed. Seven live numeric comparisons: changes before commit, zero saves, cancellation byte-identical, one-save final commit byte-identical. Reviewed contact/below/reflections/caps/cube/shadows/dense frames. No renderer changes needed.
Timings: Deferred dense median 0.31ms/p95 0.44/max 6.65; Classic dense median 15.59/p95 16.07/max 16.11. Classic often clustered around 16ms even with effects disabled; these glFinish-synchronized host measurements do not establish a portable GPU cost or causal UI regression. Raft Plate 600 feet median 51.70/max 64.02ms; Web median 17.32/max 27.47. Coalescing remains 100ms with synchronous final calculation; frame blocking limitation remains.
Gallery exited 0 with capture-ok.txt: locks, expressions, numeric pointer/keyboard transactions, gripper-centre swaps/reversal/250ms easing/cancellation/150ms settle and resize. 100/150/200-density and narrow renders generated. Reviewed main Layout, Support fields, isolation narrow, View settings and actual sliced layer preview; offscreen main images omit GL and are not renderer proof. Next: acceptance/reconciliation report, evidence cleanup and clean committed handoff.

## User review follow-up — in progress
User requested configurable object-outline width, practical slider maxima (base diameter 25mm), consistent draggable Support editor headers and recovery of main SpaceMouse after opening/closing the preset editor. Clean base e8d769c3c79011de9c8d79b27653227a9fc3f725, same ed62 worktree/codex/ui-refresh-05. Audit: deferred outline is fixed one pixel; shared SupportSettingsView keeps legacy expanders outside MainWindow; each ViewportControl opens its own COM device on attach with no activation ownership. Plan: persisted live outline width, practical UI ranges without rewriting old saved data, reusable editor adaptation/persistence, active-window connection handoff, tests/native/GL checks. 06/07 remain queued; no merge.

### Follow-up iteration 1
Added 1–5px Deferred border width with release-only persistence/live preview in View and Preferences. Practical support/raft maxima inventoried in slider-range-refinement.txt; base 25mm, raft thickness 5mm, tip3mm, penetration 3mm; old stored config is not normalized to new UI bounds. Corrected stem lean angle units. Preset editor and Preferences now use the existing draggable refresh headers; editor section order/expansion persists in workspace sidecar with completed-order events. SpaceMouse now has one viewport owner: another viewport's activation releases the previous COM connection before acquiring it; reactivating main reconnects; plain Preferences keep camera tuning available.
Release solution build and full 1011 tests passed. Native full harness passed including new border settings and preset editor; actual COM driver connected in editor and main after close, old viewport disconnected. Physical movement remains untested. Reviewed support-editor.png. Final additional changes synchronize island-field ranges and preserve editor-owned preference keys when main saves; rebuild passed, final UI rerun pending. GL width before/during/cancel/commit run in progress. No main merge/06/07.

### Follow-up completed — final verification
Tested source e27b0fcfdc3828469999ebe786b99e3f48120a13; renderer/config change 8588831. Full 1011 tests pass again after final source changes; Release solution build passes. Native final harness passes all earlier workflows plus pointer and keyboard editor reordering, no order writes during drag, release persistence, preservation across main-window save, practical base 25mm bound, and real COM driver transfer/reconnect (editor=True, main after close=True, competing main connection released). Physical motion untested. Final evidence task-05-refinements-final, with build/full-test logs and editor screenshot.
Actual GL completed 116 frames and all prior assertions plus eighth live setting OutlineWidthPixels: 56416 changed channels at 4px versus 1px, zero pre-release saves, cancelled identical original, committed identical preview with one save. Reviewed both width frames. Retained four new width images plus complete result.txt in task-05-refinements-gl; redundant unchanged renderer fixtures removed after validating 116 outputs. Prior task05 GL evidence remains. Earlier successful UI attempt removed only within verified current worktree evidence root. No main merge or task 06/07.
