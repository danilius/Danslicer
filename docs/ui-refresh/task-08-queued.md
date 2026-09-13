# Task 08 — Dedicated preset editors

Status: queued by user; do not start until task 07 is complete. Also preserve completed task-06 implementation when choosing the starting commit. Tasks 06 and 07 were dispatched in parallel; combine their relevant committed handoffs deliberately rather than starting blindly from the report-only branch.

## User request
Remove Support presets and Printer presets from the Settings dialog. Keep the existing Support preset editor and provide a separate Printer preset editor.

## Implementation instructions
Create a separate implementation chat/worktree from the latest combined implementation baseline after task 07 completion. Read current SPEC, RESUME, prior task notes and task-07 printer assessment. Audit existing Printer editor/view model before adding another; reuse or extract it into a dedicated editor where possible. Remove preset-management sections from Settings, not the user's saved presets or unrelated settings. Preserve and provide clear access to the existing Support preset editor. Add an discoverable Printer preset editor entry, preferably from the existing selected-printer control/menu, consistent with Support editing and the approved UI.

Preserve creation, editing, duplication/save-as, rename/delete validation and printer selection capabilities already supported, saved identifiers, embedded project printer definitions, config compatibility and existing preset data. Handle unsaved edits consistently. Respect task-07 compatibility limitations; this task does not authorize new output formats or unsupported printer claims. Keep printer selection available in the workspace. Maintain existing status bar, compact controls, live preview/commit/cancel semantics and shared SpaceMouse ownership across editor windows.

## Acceptance and handoff
Verify Settings no longer contains Support/Printer preset management; both editors are reachable and operate on existing saved data. Test editing/save/reopen, selection and project round trips, validation and editor-close/input handoff where relevant, without rewriting actual user config in tests. Produce native UI evidence and document verification limits. Keep task-08.md and RESUME checkpoints with branch/worktree/last good commit, actual test results and exact next action. Commit coherent changes, provide absolute main-app launch command and final handoff ref. No main merge or modifications to Claude's checkout without user instruction.
