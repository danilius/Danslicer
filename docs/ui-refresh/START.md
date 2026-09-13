# UI refresh handoff
User authorized isolated implementation, a Claude coordination note, separate chats per task, and durable recovery notes.
Read SPEC.md, TASKS.md, RESUME.md and CLAUDE-NOTE.md before editing.
Copy this package and references into docs/ui-refresh in the new UI worktree. Commit the specification and references before implementation. Never modify F:/Git Repos/Danslicer or the grid-routing-prototype checkout from an implementation task.
Source baseline: main at 7d95ceb (verify full commit and record). Do not absorb Claude's uncommitted changes.
Each task is a separate chat, executed sequentially. Task 01 starts now; later tasks are queued in TASKS.md. Coordinator creates the next chat from the completed predecessor commit. No concurrent writers in the UI worktree.
Reference images are in this package's references directory. Written requirements override mockup inaccuracies.
