# Current checkpoint — task 02 implemented, awaiting user review

Date: 2026-09-10
Task: 02 — workspace layout and navigation.
Worktree: C:/Users/plane/.codex/worktrees/3713/Danslicer-chatgpt
Branch: codex/ui-refresh-02
Task-01 accepted starting commit: 0fadc0e2412498513e34db6337aa3bba1ed9dc8c.
Last good tested implementation/evidence commit: 173977652b2e5b94c7303194b4c9ea3e0b262337.
Final handoff ref: HEAD of codex/ui-refresh-02, the documentation-only descendant of that implementation. Resolve with `git rev-parse HEAD`; the task's final response gives the exact handoff hash. Expected clean status after the final documentation commit.

## Completed
Floating inset toolbar adopts original production buttons, preserving command bindings, enablement and mode rules. Icon-only/tooltips and icons-plus-text modes; selected popout tool highlighted. All 13 settings popouts are inline, resizable, tool-anchored and viewport-constrained. Print settings and layer isolation are tools; no permanent right settings panel. Slim underline workspace tabs, responsive machine/resin strip, project dropdown with real session recent-project open and Open another project. Compact draggable section shells in Transform, Support and Print. Existing numeric binding/undo implementation, keymaps and status bar remain intact. No renderer/core changes, other checkout edits or main merge.

Changed source: Controls/Refresh/{ToolShells,RefreshPalette,WorkspacePopout}.cs and Views/MainWindow.axaml, MainWindow.axaml.cs, MainWindow.Workspace.cs, MainWindow.WorkspaceCapture.cs. Documentation/evidence under docs/ui-refresh.

## Main application launch
```powershell
& "C:\Users\plane\.codex\worktrees\3713\Danslicer-chatgpt\src\Danslicer.App\bin\Release\net10.0\Danslicer.App.exe"
```
No preview flag. Build from this worktree with `dotnet build Danslicer.slnx -c Release -p:UsedAvaloniaProducts=`.

## Actual verification
Release app and solution builds passed. 121 focused existing tests passed, 0 failed/skipped. Native main-window checks passed: STL import, selected transform expression/undo, duplicate/undo, all popouts/modes, focus/Escape, section reorder, resizing/narrow bounds, project save/open and missing-file status. Prior task-01 native pointer/keyboard controls suite re-run successfully. Status-bar XAML byte-identical and original setting/command bindings retained. `git diff --check` passed. See task-02.md for exact commands and evidence paths.

Limits: render-density captures (100/150/200) are not physical monitor DPI checks. Offscreen captures omit the OpenGL composition surface, so viewport backgrounds appear black. No physical mouse, screen-reader, monitor transition or light-theme verification. Full geometry suite, native export/file dialogs, actual UVtools launch and full support-generation/slicing jobs not run; relevant existing command tests passed. Existing SurfaceContour/ViewportControl/RaftBuilderTests warnings remain.

## Next action / task 03 handoff
User reviews task 02 directly. Do not create the next task or start later work here. After approval, task 03 should start from the exact final committed HEAD in a separate worktree. Read START, SPEC, TASKS, task-01 and task-02.
Task 03 owns durable toolbar mode, popout width/section order/expansion and recent-project persistence, plus the approved filled numeric slider/scrub controls wired to real parameter metadata and exactly one existing undo path. Current session state is preserved across popout toggles, but intentionally not across restarts. Existing ExpressionBox/NumericUpDown production editors remain until task 03. Review narrow content, focus and DPI during that integration. Existing layer navigation sliders are unchanged; they are not replacement numeric setting controls. Task 04 alone owns renderer/plate/AO/cavity work.

## Latest user refinement — in progress
Authorized Support-only persistent right isolation rail, no toolbar button; handle-following editable layer/mm hover card; two-state Cap interior checkbox, default on. Supersedes earlier isolation-popout text above. Worktree/branch unchanged; last good commit 1fb53e442bbcc888a73c14ffb3beec42a38d2365. See task-02.md. Next: implement and validate.

Isolation iteration: implementation and first native check pass; 87 focused tests pass. Last good committed ref remains 1fb53e4 until refinement commit. Final drag-release/checkbox polish and verification underway; see task-02.md.
