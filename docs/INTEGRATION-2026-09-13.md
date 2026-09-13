# Danslicer consolidation — 13 September 2026

User destination: `F:/Git Repos/Danslicer`, branch `main`.
Pre-integration main: `4b7e70b304fc598cf821ec2d572193903bccff96`.
Original prototype head: `24c4fa7712244968d049d103e2b46b776efb091a`.
Uncommitted island work preserved as `c6300f0` on `grid-routing-prototype`.
Integration branch: `codex/consolidate-danslicer`.

## Resolution

The user explicitly chose to leave out Forge and mini supports. Preserve main's current UI, theme catalog, routing, generation policy, printer writers, dedicated preset editors and toolbar edge interaction. The old prototype commits remain in the merged history, but their superseded changes are excluded from the final tree.

Integrate disconnected-component detection, hole-aware island footprints and markers, nearest contour continuation, support-edit marker updates, benchmark source and regression tests. Preserve main's contour recovery and reconsideration of starts below the minimum area. Cache islands before support filtering so removing a support that existed at detection time restores its marker. Opening the detection popout no longer automatically starts detection.

Preserve the three untracked queued-task notes from the native-printer-writers worktree verbatim under `docs/ui-refresh/`. These are historical notes; their queued status does not describe current completion.

## Worktree and branch recovery map

No branches, worktrees, remote refs or existing user data are deleted by this integration.

| Branch / detached head | Original head | Worktree |
|---|---|---|
| main | 4b7e70b | F:/Git Repos/Danslicer |
| grid-routing-prototype | 24c4fa7, then c6300f0 | F:/Git Repos/Danslicer-chatgpt; now integration branch |
| codex/ui-refresh-06 | 93844c4 | C:/Users/plane/.codex/worktrees/271f/Danslicer-chatgpt |
| codex/ui-refresh-09 | a611489 | C:/Users/plane/.codex/worktrees/9947/Danslicer-chatgpt |
| codex/native-printer-writers | ede24be | C:/Users/plane/.codex/worktrees/a19b/Danslicer-chatgpt |
| codex/ui-refresh-10 | b6c03d5 | C:/Users/plane/.codex/worktrees/b4fb/Danslicer-chatgpt |
| codex/dedicated-preset-editors | a5d2bd2 | C:/Users/plane/.codex/worktrees/e7d1/Danslicer-chatgpt |
| detached review merge | 083915c | F:/Git Repos/.danslicer-review5 |

The five UI feature branch tips were already ancestors of pre-integration main. The detached review merge only combines `24c4fa7` with `2375cbb`, both included by this integration; its obsolete prototype resolution should not overwrite the current result.

Main's existing untracked `.claude/`, `CODEX-UI-COORDINATION.md`, `ref/` and `test project files/` are retained in place. Ignored files and build outputs have not been approved for cleanup.

Remote cleanup is deferred. `origin` points to `https://github.com/danilius/Danslicer.git`; `localmain` points back to the local destination repository. Existing remote-tracking refs may be stale and must be audited before any deletion. No push is requested or performed.

## Validation

Release regression suite: 1,071 passed, zero failed or skipped on the resolved integration (before excluding Forge). Repeat the suite from the destination after the final merge. Existing warnings concern SurfaceContour stack allocation, ViewportControl nullable references and RaftBuilder test assertions. No interactive viewport or physical printing validation is claimed.
