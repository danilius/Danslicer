# Task 10 — Drag toolbar edge to reveal or hide labels

Status: queued by user after task 09.

## User request
Remove the toolbar labels toggle button. Hovering over the toolbar's right edge changes the pointer to a horizontal resize cursor. Dragging right reveals toolbar labels; dragging left hides them.

## Implementation instructions
Start a separate task from the surviving integrated main after task 09, inspecting the current branch/worktree and instructions. Replace the existing label-toggle button with an adequately sized right-edge interaction area on the floating toolbar. The edge must not interfere with selecting tools, tooltips, popout resizing or viewport navigation. Use a horizontal resize cursor only over that edge and while dragging it.

Use clear expansion/collapse thresholds with hysteresis so labels do not flicker near the boundary. Keep icon-only and icons-plus-text states aligned with existing toolbar design; smoothly reveal/conceal labels during resize where practical, then settle at a usable width. Preserve tool selection, mode-specific tools, popout anchoring and viewport bounds. Clamp width safely at narrow window sizes. Escape or pointer capture loss cancels and restores the original state; persist the final label-mode preference only on completed drag, reusing existing settings compatibility.

Retain an accessible keyboard equivalent through the focusable resize edge (for example Left/Right to hide/show with an accessible name), without reintroducing the removed toggle button. Preserve icon-only tooltips. No unrelated layout redesign.

## Validation and handoff
Verify rightward expansion, leftward collapse, threshold stability, cursor/hit area, cancellation, keyboard operation, persistence across restart, narrow layouts and open-popout positioning. Use isolated config in tests. Update task-10.md and RESUME with exact branch/worktree/base, last good commit, changes, actual results and next action. Provide native UI evidence and absolute launch command; record physical input/DPI verification limits. Task 09 may have removed prior worktrees: do not rely on old paths and do not undo its cleanup. Task 09's merge authorization applies to task 09; deliver task 10 as a reviewable change unless separately authorized to merge.
