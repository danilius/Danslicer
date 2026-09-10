# Task 01 checkpoints

## Plan — 2026-09-10
Worktree: C:/Users/plane/.codex/worktrees/f60d/Danslicer-chatgpt
Branch: codex/ui-refresh
Base: 7d95cebfe7f289d1e3230fb789a4526d1f3b518d (committed main)

Import approved package and references first. Audit found existing theme dictionaries, vector IconSet, ExpressionBox commit/revert behavior and NumericField using the core ExpressionParser. Build isolated reusable controls without changing those existing integrations: charcoal resources and outline icons; toolbar modes; persistent-in-session popout shell; grip-only reorderable sections; numeric editing with threshold scrubbing, fine adjustment, locks and one commit per gesture. Add a --ui-preview startup path before normal view model creation. Test editing/state contracts and build, capture native preview if supported. Later integration/persistence/rendering tasks remain queued.
