# Task10 — toolbar edge reveals labels (complete, review branch)

Worktree: C:/Users/plane/.codex/worktrees/b4fb/Danslicer-chatgpt.
Branch: codex/ui-refresh-10.
Clean base and unchanged local main: a611489edfa2df429a158a4c96c15debcf7f0b12.
Tested implementation / last good source: 309b6c4656421d2712ba80196aa4778b1de1a838.
Final handoff is a documentation/evidence descendant of that source; exact final commit is reported in the completion response (or git rev-parse codex/ui-refresh-10).

Main at F:/Git Repos/Danslicer and coordinator/Claude/routing checkouts were not edited. No merge, push, cleanup reversal or new task. Task09's merge authority does not apply to Task10. Next action: user/coordinator review this committed branch; merge only with separate authorization.

## Delivered behavior

Removed the production Toolbar labels button. A dedicated 12-DIP full-height right edge has a horizontal resize cursor, accessible name “Resize toolbar labels”, tooltip instructions, focus cue and Left/Right keyboard operation. Its own grid column does not overlap tool buttons; existing tool commands, selected backgrounds, tooltips and mode visibility remain intact. Existing popout resize controls and viewport interaction code are unchanged.

Drag previews continuously between 58-DIP icon width and 184-DIP labelled width, clipping/fading text as space changes. Expand at 60% (133.6 DIP), collapse at 40% (108.4 DIP): reversal through the intervening band retains state. Release consumes its final pointer position and settles immediately to the usable endpoint; there is no timed settling animation. Existing size-change placement keeps popouts anchored; the toolbar is constrained by viewport width and height.

ShowLabels changes are transient during capture. Only LabelsCommitted updates the committed WorkspacePreferences snapshot, reusing the existing ShowToolbarLabels JSON member/schema. A completed changed drag saves once; no-op/reversed drag and repeated same-direction key do not save. Escape, capture loss, detach, workspace mode change, window close and deactivation cancel to the starting label state. Unrelated workspace saves cannot persist preview state. Left/Right during capture are consumed without committing the gesture.

No numeric/model/undo, print estimate, writer, editor or SpaceMouse implementation changed. App startup has an opt-in workspace-capture preference probe: it copies only the supplied test workspace JSON into a new temporary configuration directory and verifies restoration in a fresh MAIN process. Normal startup is unchanged.

## Actual validation

- Release solution rebuild with --no-incremental: passed, zero errors, the same five existing warnings (SurfaceContour CA2014, two ViewportControl CS8602, two RaftBuilderTests xUnit2031).
- Full suite: 1061 passed, zero failed/skipped.
- Committed source native MAIN harness: exit 0, all 16 top-level success markers plus two fresh-process restart markers, no error files. The original 14 regression markers remain green; Task10 adds toolbar-resize and toolbar-close markers.
- New routed native pointer checks cover both thresholds and boundary stability, reversal/no-op, release without prior move, capture ownership/release, Escape/capture loss, commit event counts and saved file bytes, unrelated save isolation, Left/Right/no-op, cursor/name/focusable edge and nonoverlap/tooltips. The obsolete toggle is explicitly asserted absent.
- Open Transform popout stays anchored through preview/release at 1200x800 and 640x480. Mode change and detach cancel; closing the actual MAIN window during preview restores state and saved preference. Fresh MAIN processes restore both saved icon-only and labelled JSON snapshots. Existing checks exercise popout keyboard resizing, all 12 popouts, modes and narrow bounds.
- Existing native regression exercises live numeric edits/undo/cancel, generated supports/rafts, project reload, 110-layer slicing, Photon Workshop bitmap export checks, GOO/other native formats, estimates, dedicated editors, Preferences and shared SpaceMouse lifecycle. The independent Task07 115-profile hardware-format audit was preserved, not rerun.

Logs, all marker text and five screenshots are committed at evidence/task-10. Full raw final output remains at artifacts/task10-verified-native in this worktree; earlier iteration output remains in artifacts/task10-native and artifacts/task10-final-native. Visually inspected labelled, icon-only, drag/focus cue, narrow anchored popout and Support labels; text/buttons fit without the old toggle.

Screenshots use native Avalonia RenderTargetBitmap and omit native GL composition (black viewport). Routed pointer events verify production handlers, not physical mouse movement or OS cursor appearance. No screen-reader, actual monitor/DPI transition, new GL rendering or physical printer certification is claimed. Existing density captures are rendering-scale evidence only.

## Absolute MAIN build/run

```powershell
dotnet build "C:/Users/plane/.codex/worktrees/b4fb/Danslicer-chatgpt/Danslicer.slnx" -c Release -p:UsedAvaloniaProducts= --nologo
& "C:/Users/plane/.codex/worktrees/b4fb/Danslicer-chatgpt/src/Danslicer.App/bin/Release/net10.0/Danslicer.App.exe"
```

That executable is the full MAIN app in the Task10 worktree, not --ui-preview. Normal launch uses normal configuration. For isolated repeatable validation:

```powershell
dotnet test "C:/Users/plane/.codex/worktrees/b4fb/Danslicer-chatgpt/Danslicer.slnx" -c Release --no-build --nologo
& "C:/Users/plane/.codex/worktrees/b4fb/Danslicer-chatgpt/src/Danslicer.App/bin/Release/net10.0/Danslicer.App.exe" --workspace-capture "C:/Users/plane/.codex/worktrees/b4fb/Danslicer-chatgpt/artifacts/task10-review-native"
```

## Checkpoints

Verified clean detached a611489 base before editing and created codex/ui-refresh-10. First implementation passed Release/1061 tests and 15 native markers. Added focus cue, fresh-process preference probes and actual-close cancellation. Committed source at 309b6c4, then completed clean Release rebuild, full suite and final native run (exit 0, 18 total markers). Git reference writes and NuGet/native execution needed sandbox escalation; approved execution succeeded, no unresolved approval block. Only this review branch was committed; main remains a611489.
