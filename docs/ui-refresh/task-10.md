# Task10 — toolbar resize edge

Worktree: C:/Users/plane/.codex/worktrees/b4fb/Danslicer-chatgpt. Branch: codex/ui-refresh-10. Clean detached base verified at a611489edfa2df429a158a4c96c15debcf7f0b12 (integrated main). No main/source/coordinator edits, merge or push authorized.

Plan: replace the toolbar toggle with a dedicated focusable right edge; implement continuous width preview, hysteresis and commit-only compatible label preference; cancel on Escape, capture loss, detach, close and mode change. Extend native MAIN harness to verify pointer/keyboard, persistence, unrelated saves and bounds, run Release/full regression and capture evidence. Existing numeric, editor, writer and SpaceMouse regressions remain required. Last good ref is the base above; implementation/validation pending.

Implementation checkpoint: dedicated 12-DIP edge, 58/184-DIP settled widths, continuous width/label-opacity preview and 60/40% hysteresis. Existing ShowToolbarLabels JSON stays unchanged; explicit LabelsCommitted updates the committed preferences snapshot. Escape/capture loss/detach/mode/closing/deactivation cancel. Focus marker and Left/Right added. Removed production toolbar button; harness now explicitly asserts its absence and checks nonoverlap/cursor/tooltips.

First Release build and 1061 tests passed; first native run passed all 15 markers including toolbar resize. Visually inspected labelled and 640x480 anchored-popout evidence. Added fresh-process preference probes (both modes), closing-during-drag check and visible edge focus; final build passed, final full/native rerun pending. Last good base remains a611489; next action run final validation and inspect evidence, then commit review handoff. No main changes.
