# Current checkpoint — task 02 with approved isolation refinement

Date: 2026-09-10. Task 02 implementation ready for user review.
Worktree: C:/Users/plane/.codex/worktrees/3713/Danslicer-chatgpt
Branch: codex/ui-refresh-02
Task-01 accepted starting commit: 0fadc0e2412498513e34db6337aa3bba1ed9dc8c.
Last good implementation/evidence commit: 070a5a8d1d4cf37f4ce813644903f87f01934ffe.
Final handoff: documentation-only descendant at HEAD of codex/ui-refresh-02; exact hash in final response. Verify `git rev-parse HEAD` and clean `git status --short`.

## Current behavior
Floating inset toolbar retains real buttons, commands/enablement/mode policies; icon-only/tooltips and icons-plus-labels. 12 inline resizable, constrained settings popouts. Slim underline Layout/Support/Slicing tabs; responsive selected machine/resin strip; project dropdown with actual session recents and open-another action. Compact draggable sections in Transform, Support and Print. Status bar preserved exactly; no renderer/core geometry changes or main merge.

Latest user instruction supersedes earlier task-02 notes: layer isolation always appears on the RIGHT in SUPPORT mode only, without a toolbar button. Resized tool popouts reserve its space. Fixed top/bottom edit boxes replaced by a handle-following card on the left with editable layer number and mm height. The 300ms gap-crossing delay, dragging, card hover and typing retain it; blur/exit dismisses only the card. Keyboard Left/Right chooses bottom/top, Up/Down steps a print layer (Shift fine), Enter edits. Escape reverts text or cancels active dragging; Escape on idle slider hides the card. Reset and a two-state Cap interior checkbox remain below. Cap interior defaults on already; explicit saved off choice is preserved.

Main sources: Controls/Refresh/{ToolShells,RefreshPalette,WorkspacePopout}.cs; Controls/LayerRangeSlider.cs; ViewModels/LayerRangeClipViewModel.cs; Views/MainWindow.axaml and its Workspace, Isolation and native-capture partials. Existing NumericField/ExpressionBox model/editor and global keymap source unchanged. New paired layer/mm tests: IsolationHeightEditorTests.cs. Details and iteration history: task-02.md.

## Launch MAIN application
```powershell
& "C:\Users\plane\.codex\worktrees\3713\Danslicer-chatgpt\src\Danslicer.App\bin\Release\net10.0\Danslicer.App.exe"
```
Build from this worktree: `dotnet build Danslicer.slnx -c Release -p:UsedAvaloniaProducts=`.
Native smoke/capture: same executable with `--workspace-capture docs/ui-refresh/evidence/task-02`. Wait for exit; failures emit workspace-error.txt and nonzero exit. Capture skips window geometry persistence.

## Actual verification
Release app/solution build passed. Latest refinement: 87 focused tests passed, 0 failed/skipped, covering layer range, paired layer/mm fields, cap policy, toolbar, keymaps, workspace and command scoping. Earlier task-02 suite had 121 passing tests and task-01 controls native smoke passed. Latest main native smoke passed for STL import, transform expression/undo, duplicate/undo, 12 popouts, isolation handle hover/drag/release/cancel, editor gap/focus/typing/revert, keyboard access, narrow 640x480 bounds and project round trip/error handling. Status-bar XAML re-audited byte-identical; diff whitespace check passed.
Evidence: evidence/task-02/workspace-isolation.png and workspace-isolation-narrow.png plus refreshed support/layout/print captures, workspace-ok.txt and binding-audit.txt. Reviewed isolation images at normal and narrow sizes. Existing compiler/analyzer warnings only.
Limits: offscreen native-window captures omit the GL composition surface (black viewport); 100/150/200 density renders are not physical monitor DPI checks. No physical mouse, screen-reader, monitor transition or light-theme verification. Full geometry suite/native export dialogs/UVtools process/full support-generation job not run. No renderer changes.

## Next action / task 03
User reviews this task directly. Do not create later tasks or implement later scope here. After approval, task 03 starts from exact final HEAD in a separate worktree and reads START, SPEC, TASKS, task-01 and task-02.
Task 03 owns durable toolbar/section/popout-width/recent-project persistence and general production filled-slider/scrub-field/undo integration. Current toolbar/section/recents state is session-only. Isolation layer/mm fields are authorized refinement using the existing clip model, not a general numeric-control migration. Preserve the right-side Support rail exception and two-state cap choice. Task 04 alone owns plate/renderer/AO/cavity work.

Latest authorized screenshot refinement in progress: cube clearance, no isolation background/border, centered Reset, checkbox label Cap. Same worktree/branch; last good 504c385cf4197408e81b8013283d6afa95bcbdf1. See task-02.md.
