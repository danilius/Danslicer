# Task 09 — main integration and cleanup

Task09 runs at C:/Users/plane/.codex/worktrees/9947/Danslicer-chatgpt on codex/ui-refresh-09, from completed Task08 a5d2bd2f4e197b66544ae806b3f8b8822a0a54b4. User explicitly authorizes integration and verified obsolete local/remote cleanup. Earlier no-main-merge instructions are superseded for Task09; main publication is not requested and will not be performed. Task10 remains separate.

## Audit and plan

Pre-integration main at F:/Git Repos/Danslicer: 7d95cebfe7f289d1e3230fb789a4526d1f3b518d, also live origin/main and origin default HEAD. Main is an ancestor of Task08, with no intervening commits or tracked modifications. Untracked .claude/, ref/, test project files/ and CODEX-UI-COORDINATION.md are user-owned, noncolliding and must survive unchanged. Ignored user directories and shortcut remain untouched. No publication instruction found in current handoffs; queued task explicitly distinguishes remote cleanup from publishing main.

Read START/SPEC/TASKS/RESUME, Task06/07/08 and both native-writer reports. Task08 includes Task06 estimates, all Task07 native writers and dedicated preset editors. Audit found only the coordinator active in this repository's task listing. Completed task histories and committed handoffs support prior completion; idle status alone is not deletion evidence. Process audit found Claude desktop and reusable MSBuild nodes, no Danslicer app or repository-specific active writer command; main's source is stable. Recheck immediately before integration/removal.

Full old refs, worktree paths, heads, status and ignored-directory inventories are in evidence/task-09. Recovery uses the recorded commits (for example git worktree add -b <recovery-branch> <new-path> <old-head>); all deleted branches must remain ancestors of surviving main.

Proposed clean worktree removals: 3321 (Task03), 3713 (Task02), da71 (Task04b), e262 (Task04), ed62 (Task05), f60d (Task01), all under C:/Users/plane/.codex/worktrees/<id>/Danslicer-chatgpt. They contain only tracked ancestor content plus disposable bin/obj outputs. Remove via git worktree remove without force, then git branch -d their exact branches. Also delete fully merged unattached codex/ui-refresh-07.

Retain 271f, a19b and e7d1 with their branches: unique raw test/capture/export evidence (61/5650/131 artifact files respectively) is useful beyond committed selected evidence. a19b also has three queued docs: byte hashes differ only because of line endings; normalized text exactly matches committed artifacts/ui-refresh-handoff copies. Preserve them along with this evidence worktree. Retain F:/Git Repos/.danslicer-review5 at 083915c593eb597166261fc673f771678b146f78 because it has commits outside Task08 ancestry. Retain active coordinator F:/Git Repos/Danslicer-chatgpt and grid-routing-prototype, and this active Task09 checkout.

Remote cleanup candidates: all live origin heads except main (default/protected) and grid-routing-prototype (coordinator-associated, retained conservatively). Every candidate is already an ancestor of live origin/main, so removing them will not depend on publishing this integration. Exact old heads are in origin-before.txt. Recheck remote URL, default branch, exact head and ancestry immediately before each named deletion. Leave localmain aliases/tracking refs alone: these are references to the local repository, not GitHub branch cleanup targets.

Next: Release build and full regression in isolated Task09; commit audit and integrate by fast-forward into main preserving user files; build/capture actual main; execute checked cleanup; commit final recovery record into surviving main. No Task10 implementation or task creation here.

Checkpoint: isolated Release solution build passed (five existing warnings); full suite 1061 passed, zero failed/skipped. Logs preserved in evidence/task-09. Source unchanged from Task08. Next: authorized main fast-forward, actual-main validation, cleanup.
