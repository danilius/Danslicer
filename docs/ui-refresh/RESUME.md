# Task 03 completed — recovery and handoff

Date: 2026-09-10
Worktree: C:/Users/plane/.codex/worktrees/3321/Danslicer-chatgpt
Branch: codex/ui-refresh-03
Exact accepted task-02 base: a0fed8b933f3358e875e4650bbe8915d5a57c574. User said task 02 is done; all earlier pending-review wording is historical.
Last good implementation/evidence commit: dfc760f (full hash available through git rev-parse dfc760f).
Final handoff is the following committed descendant, which also preserves thread-safe lazy AppConfig initialization; exact hash in the final response. Verify HEAD and clean status before continuing.

## Shipped
Durable workspace-ui.json sidecar beside existing user config: toolbar label mode, completed preferred popout widths, expander order/expansion and ten recent project paths. Atomic replacement, corrupt/missing fallback, stale/duplicate identifiers filtered, new sections appended, unknown properties preserved, newer schema read-only. Unrelated existing user config is not rewritten by workspace preference saves. Missing/offline recent paths remain in history and fail through existing status handling when opened.
Production support/available raft bounded controls retain limits, increments, validation and existing saving bindings; centered numeric value and unit inside rectangular fill. Transform, print, region and placement use ModelScrubField through the existing NumericField/model callback once per commit, with no Value binding double-apply. Transform drag has one document undo; support configuration retains its pre-existing immediate-save behavior without document undo. Visibility retains real switches/modes; no numeric opacity or raft parameters invented; no field locks without semantics.
Compact typography, whole-expander drag/settling, round grippers, constrained resizable popouts and Support-only right isolation rail preserved. Resize cancellation restores preferred width even before pending layout completes and does not persist a transient width. Isolation keeps shared ViewCube.Rect plus 12 DIP, no enclosing panel, centered Reset, saved two-state Cap and the 300ms handle/card grace.

## Verification
Release solution/app builds passed; 181 focused tests passed, 0 failures/skips. Existing SurfaceContour CA2014, ViewportControl CS8602 and raft-test xUnit2031 warnings only. Native main capture and reusable control preview each exited 0. Final main capture re-run after thread-safe lazy configuration change also passed.
Native checks cover import, transform pointer previews/single commit/undo/cancel, expressions/units, invalid and range rejection, blur/no-op/integer/keyboard semantics, support setter called once, raft config and saved cap-off round trips, actual new-window preference restoration, recent projects, resize cancellation, twelve popouts, isolation interaction and narrow layout. Unit tests and main capture now use fresh temporary config; previews branch before config loading.
Reviewed transform/support/raft/print/narrow/isolation images in evidence/task-03. Control smoke evidence is in evidence/task-03-controls. Status-bar subtree unchanged after newline normalization; no Core/Render/ViewportControl diff. Whitespace check passed.
Limits: screenshots are offscreen native-window renders and omit GL composition (black viewport). 100/150/200 render densities are not physical monitor DPI or monitor-transition checks. Physical mouse/hit-testing, screen readers and light theme not verified (app has dark themes). Full geometry suite, native picker/export dialogs, UVtools execution and full support-generation/slicing job not run. Early isolation timing failure was resolved by a bounded dispatcher wait in the test only; production 300ms timing unchanged.

## Launch MAIN application
```powershell
& "C:\Users\plane\.codex\worktrees\3321\Danslicer-chatgpt\src\Danslicer.App\bin\Release\net10.0\Danslicer.App.exe"
```
Build from this worktree: `dotnet build Danslicer.slnx -c Release --no-restore -p:UsedAvaloniaProducts= --nologo`.
Main native check: same absolute executable with `--workspace-capture docs/ui-refresh/evidence/task-03`; wait for process exit and inspect workspace-ok.txt/settings-ok.txt (failure writes workspace-error.txt and exits nonzero).
Control check: same executable with `--ui-preview --capture-directory docs/ui-refresh/evidence/task-03-controls`.
Focused test filter and iteration history are in task-03.md.

## Exact next action / task 04
User follows up here. No further task was created and no main merge performed. For task 04, start a separate isolated worktree from this exact final handoff HEAD, read START/SPEC/TASKS/RESUME and task-01/02/03. Audit committed renderer overlap before editing; do not touch F:/Git Repos/Danslicer, prior worktrees or routing prototype, or absorb uncommitted changes. Task 04 owns modelled plate, subtle reflections, transparency and selection through from below, AO/cavity audit, and only a confirmed outward bevel correction. Preserve task-03 numeric/persistence contracts, the Support rail, all status content, modes and keymaps. Obtain actual above/below GL evidence; these offscreen captures cannot verify renderer work.

## Latest user refinement — live expander swapping
Approved after the task-03 handoff at 97eedfc404614494e0fea4cab382b6b4ff2c3459. Same worktree C:/Users/plane/.codex/worktrees/3321/Danslicer-chatgpt and branch codex/ui-refresh-03; current HEAD is the new handoff. Expanders swap immediately when the dragged section crosses a neighbour midpoint, retaining pointer anchoring through layout changes. Reverse dragging swaps back; Escape/capture loss restores starting order. Only release (or a keyboard reorder) persists order; existing 150ms settling remains. New OrderCommitted event is the persistence boundary, MoveRequested also carries transient swaps. Native preview and full main smoke passed these cases, including on-disk preference checks. Release build passed; drag screenshot reviewed. Earlier physical DPI/input/GL limits remain. Exact commit in final response; next action is user review, with task 04 still owning rendering only.

## Latest correction — gripper-centred swap targets
Same worktree C:/Users/plane/.codex/worktrees/3321/Danslicer-chatgpt and codex/ui-refresh-03 branch; prior good HEAD 996e65e90590ddd38749e16e2c223cc2598b7be6. Current HEAD is the new handoff. Swapping is triggered by the dragged gripper crossing the displayed centre of another gripper, not a section midpoint. Displaced ungripped sections animate into place with a 250ms cubic ease-out, preserving visible positions on retarget. Existing pointer anchoring, reversal, cancellation and drop-only persistence remain. Release app build and native preview/main checks passed; before/after gripper threshold and intermediate/end animation checks passed. Mid-animation screenshot reviewed in evidence/task-03-gripper-swap. Exact commit in response. Next action remains user review; task 04 rendering boundaries and physical DPI/input/GL verification limits unchanged.
