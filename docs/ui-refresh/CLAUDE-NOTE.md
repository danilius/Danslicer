# Coordination: isolated Danslicer UI refresh
The user authorized Codex to implement a UI refresh separately from your ongoing work.
Base: committed main 7d95ceb; UI gets a separate worktree and branch. Your checkout and uncommitted source files are not to be modified by Codex implementation tasks.
Scope: Avalonia themes/vector icons/reusable controls, floating left toolbar/popouts, workspace tabs/recent projects, settings presentation/persistence; later a separate plate-rendering task.
Core slicing/support algorithms and existing status-bar functionality must remain intact. Raft geometry changes require confirming an actual bug; mockup values are illustrative.
Potential overlap: MainWindow, settings/view models, numeric input controls and renderer. Please note any interface changes in your handoff; continue your current work. No changes are merged into main automatically.
Final slider requirement: filled rectangular field with centered numeric value and unit; NOT thin track/round-thumb slider. Raft bevel should cant outwards.
Coordination package: F:/Git Repos/Danslicer-chatgpt/artifacts/ui-refresh-handoff
Implementation will keep docs/ui-refresh/RESUME.md and per-task notes committed. Each phase runs in a separate sequential chat/worktree based on the prior completed commit.
