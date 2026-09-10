# Task 03 — settings integration and persistence

## Start — 2026-09-10
Worktree: C:/Users/plane/.codex/worktrees/3321/Danslicer-chatgpt
Branch: codex/ui-refresh-03
Last good commit/base: a0fed8b933f3358e875e4650bbe8915d5a57c574 (clean exact task-02 handoff).
User confirmed task 02 is done; this supersedes its historical pending-review language.
Plan: separate compatible workspace preferences file; isolated capture configuration; production rectangular numeric controls through existing model setters; preserve undo, units, commands and the approved Support isolation exception. Validate corrupt/stale preference recovery, numeric transactions, native main workflows and screenshots. No renderer/core geometry edits, other checkout changes, main merge or later task implementation.
Initial checks: exact HEAD and clean status verified, dedicated branch created (Git metadata requires escalation because shared .git lives outside this worktree). No implementation checks yet.
Next: implement persistence and numeric integration, build and test.

## Iteration 1 — production binding and durable workspace state
Worktree: C:/Users/plane/.codex/worktrees/3321/Danslicer-chatgpt; branch codex/ui-refresh-03.
Last good committed checkpoint: e18b37a (plan).
Changed: AppConfig/App startup isolate native capture config; WorkspacePreferences sidecar and MainWindow.Preferences restore/persist toolbar, widths, recent projects, section order/expansion; ToolShells reports completed resize and cancels capture loss; ModelScrubField/NumericField retain one existing model callback; MainWindow templates and support/raft bounded numeric controls use centered in-field units; new persistence tests/native settings checks.
Checks: initial Release solution build passed (existing warnings); latest build plus 171 focused tests passed, 0 failed/skipped. Restricted restore required approved NuGet access. First updated native main capture failed in the unchanged task-02 isolation delayed-dismiss assertion before reaching new settings checks; investigate deterministic dispatcher timing. No visual pass claimed yet. Support config setters retain existing immediate-save semantics and no document undo; transform edits use existing document undo. Visibility has no numeric parameter, so existing modes/switches remain.
Next: resolve native capture timing, exercise settings/persistence interactions, inspect screenshots and commit verified refinement. No renderer/core geometry edits.
