# Task 01 checkpoints

## Plan — 2026-09-10
Worktree: C:/Users/plane/.codex/worktrees/f60d/Danslicer-chatgpt
Branch: codex/ui-refresh
Base: 7d95cebfe7f289d1e3230fb789a4526d1f3b518d (committed main)

Import approved package and references first. Audit found existing theme dictionaries, vector IconSet, ExpressionBox commit/revert behavior and NumericField using the core ExpressionParser. Build isolated reusable controls without changing those existing integrations: charcoal resources and outline icons; toolbar modes; persistent-in-session popout shell; grip-only reorderable sections; numeric editing with threshold scrubbing, fine adjustment, locks and one commit per gesture. Add a --ui-preview startup path before normal view model creation. Test editing/state contracts and build, capture native preview if supported. Later integration/persistence/rendering tasks remain queued.

## Iteration 1
Implemented opt-in control classes, native preview and numeric transaction tests. First solution build restored successfully but found ambiguous Path type in vector factory; qualify Avalonia shape. Capture uses obsolete bitmap overload: update before final validation. Existing unrelated SurfaceContour/ViewportControl warnings observed. No main layout or renderer changes.

## Iteration 2
Solution build passes using `-p:UsedAvaloniaProducts=` (Avalonia telemetry attempted writing an external AppData log under the sandbox; this flag skips that telemetry target only). Fixed initial Path ambiguity and obsolete bitmap save overload. 77 focused numeric/parser/theme/toolbar/keymap tests passed. Native preview opened and captured; first visual review found blue Fluent selection, corrected with reusable opt-in charcoal resources. Routed native keyboard smoke checks pass for commit, lock, expression, invalid input, Escape cancellation, expander order and popout dismissal. Reviewed 100% and narrow captures; 150/200 density captures generated. Fixed pointer capture release on cancellation after code review. Final build/tests/captures to follow.

## Final verification and integration contract
- `dotnet build Danslicer.slnx --no-restore --nologo -p:UsedAvaloniaProducts=`: passed, no new warnings. Existing ViewportControl nullable and RaftBuilderTests analyzer warnings remain; initial clean build also reported existing SurfaceContour CA2014.
- `dotnet test tests/Danslicer.Tests/Danslicer.Tests.csproj --no-restore -p:UsedAvaloniaProducts= --filter 'FullyQualifiedName~UiRefreshNumericTests|FullyQualifiedName~Expression|FullyQualifiedName~Theme|FullyQualifiedName~ViewportToolbar|FullyQualifiedName~WindowKeymap' --nologo`: 77 passed, 0 failed, 0 skipped. Includes 7 new numeric transaction/parser cases. Full geometry/slicing suite not run for this isolated UI addition.
- `dotnet run --no-build --project src/Danslicer.App -- --ui-preview --capture-directory docs/ui-refresh/evidence`: passed. Native routed pointer drag commit/cancel/capture-release and keyboard lock/expression/invalid-input/step/expander-reorder/popout checks passed; evidence/capture-ok.txt records success. Capture failure writes capture-error.txt and sets a failing exit code.
- Rendered and visually inspected preview-100.png, preview-150.png, preview-200.png and preview-narrow.png. Rectangular amber fill with centered value/unit, dark headers, outline icons, lock/disabled states and long labels are present. Narrow view scrolls the popout. Density captures are offscreen renders of a live native window, not physical monitor-DPI testing. Physical pointer/hit testing, screen-reader review, and monitor transitions remain manual checks for integration. The existing app is dark-only; no light theme support claimed.
- `git diff --check`: passed for tracked changes before staging.

### What shipped
`Controls/Refresh` contains NumericEditSession, ScrubField, FilledNumericSlider, RefreshPalette/RefreshIcons, FloatingToolbar, ToolPopout and ReorderableExpander. Only App startup is touched outside the new gallery/control files; --ui-preview branches before AppConfig/theme/main VM creation. Normal startup, ExpressionBox, NumericField, existing styles/settings/keymaps/renderer/status bar are unchanged. Reference rasters are documentation only; runtime icons are vector paths.

### Host responsibilities for tasks 02/03
Apply `RefreshPalette.CreateResources()` to the containing refresh surface's Resources. Set field Value/Minimum/Maximum/Step/Unit/UnitKind/Format from actual parameter metadata. Value is committed only on release/Enter/focus-loss/keyboard step; drag preview remains private. Wire ONE existing model/undo path using either Value binding or EditCommitted (OldValue/NewValue); do not apply the same edit twice. Escape and capture loss abandon preview. Invalid expressions retain focus on Enter; focus loss cancels invalid input. CanLock is optional; IsLocked prevents all edits. Define each lock's meaning before enabling it.

FloatingToolbar accepts explicit tool IDs/actions; host owns command enablement/mode scoping and selected state via Select. ShowLabels is bindable. ToolPopout is an in-window overlay, so orbit/outside clicks never light-dismiss it. Host must place it in a viewport-constrained grid, set available MaxHeight, route global Escape after editor handling, and restore focus to the invoking tool. Preview demonstrates placement, closing and toolbar toggling. ReorderableExpander exposes SectionId, IsExpanded, ExpansionChanged and MoveRequested (+/- one position); only grip drags beyond 6 px or Alt+Up/Down request movement. Host persists order, expansion and toolbar mode in task 03. Preview remembers them only for its lifetime; it intentionally does not write production settings.

### Next task
Task 02 only, in a separate coordinator-created chat/worktree from this task's final handoff commit. Read START.md, SPEC.md, TASKS.md, RESUME.md and this note. Integrate workspace layout/navigation with real commands while preserving every setting, status-bar item and keymap. Do not start settings persistence (03) or rendering (04). No main merge, no changes to Claude's checkout. Resolve the final handoff commit with `git rev-parse HEAD` on codex/ui-refresh; RESUME records the implementation commit and final docs commit can be its descendant.
