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
