# Task 04b — live numeric scene previews

Worktree: C:/Users/plane/.codex/worktrees/da71/Danslicer-chatgpt
Branch: codex/ui-refresh-04b
Verified clean base / last-good commit: 0df54ff6014dce8f143def47c3fe4831101cd450.

## Plan — 2026-09-10
Implement explicit per-control preview transactions: synchronous scene preview, release-only durable commit, cancellation restoration, no document undo for preferences. Preserve pointer mapping, Shift, precision, expression validation and existing appearance. Audit filled sliders, relative scrubs, Preferences legacy sliders, isolation and layer navigation. Keep generation/slicing/export explicit. Reuse transform transient support-position facilities and commit from immutable original state. Protect provisional configuration from unrelated saves. Validate native before-release state, commit/undo/cancel/write boundaries and actual GL feedback. No task 05, main merge or other checkout changes.

Initial inspection: branch creation required approved shared Git metadata access; succeeded. Source and user checkout unchanged. Read START/SPEC/TASKS/RESUME/CLAUDE-NOTE and task 01–04 history. Filled fields currently preview only labels; Preferences legacy sliders use immediate saving setters. Config Saved can apply support geometry, so preview must bypass this event. Document already exposes transient support transform/restore and final transform commit facilities.

Tests: baseline/status inspection only; no implementation checks yet. Next: implement transaction lifecycle and explicit host policies, then native and regression verification.
