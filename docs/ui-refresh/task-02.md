# Task 02 — workspace integration

## Start — 2026-09-10
Task 01 accepted by user at exact handoff 0fadc0e2412498513e34db6337aa3bba1ed9dc8c.
Worktree: C:/Users/plane/.codex/worktrees/3713/Danslicer-chatgpt
Branch: codex/ui-refresh-02 (created from clean detached accepted HEAD).
Last good commit: 0fadc0e2412498513e34db6337aa3bba1ed9dc8c.
Plan: retain real XAML settings and command bindings, host them in viewport-constrained reusable shells; adopt existing command buttons into floating toolbar; move Slicing settings and support inspection off permanent right edge; slim navigation and real project history. Preserve status bar and keymap. Add native main-window validation/captures. No numeric model/persistence overhaul, rendering, other checkout edits or main merge.
Audit: existing popup mode policies and keymaps are reusable. Existing recent-project history is absent; implement session history now, durable persistence belongs to 03. Task-01 physical input/DPI/screen-reader limitations remain to assess.
Tests: initial git/worktree/base inspection only. Next: implement shell adaptation and main layout.

## Iteration 1 — native main window integration
Worktree/branch unchanged: C:/Users/plane/.codex/worktrees/3713/Danslicer-chatgpt; codex/ui-refresh-02.
Last good committed checkpoint: 5d1d143 (plan). Source iteration ready for commit.
Changed: MainWindow.axaml and code-behind; MainWindow.Workspace.cs, MainWindow.WorkspaceCapture.cs; ToolShells.cs and WorkspacePopout.cs. Existing commands/buttons and every settings subtree retained. Right panel replaced by Print tool; support layer isolation moved to a tool. Session recent projects added; no config schema changes. Real window capture skips window-state writes.
Actual checks: Release build passed after fixing a bindable PlacementTarget; existing viewport nullable warnings plus one capture-only nullable warning to remove. First restricted restore failed on NuGet socket access; approved restored build succeeded. Native main-window 13-popout/mode/focus/Escape/reorder/resize/session project smoke checks passed; images generated. Visual inspection found theme still fills workspace tabs (must override template), new unnamed tool title incorrectly says Tool (fix), and production support sections still use tall legacy expanders (adapt section shells only). Offscreen captures omit native GL surface, physical mouse/DPI/screen-reader unverified. Next: correct visual findings, exercise model workflows and relevant tests, then final evidence.
