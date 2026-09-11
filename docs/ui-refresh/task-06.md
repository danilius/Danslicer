# Task 06 — resin volume and estimated print time

Implemented and validated on `codex/ui-refresh-06`.
Worktree: `C:/Users/plane/.codex/worktrees/271f/Danslicer-chatgpt`.
Clean starting ref: `dc4664cd7321030cfe04b866815427b6b4f8a531` (task 05).
Last-good tested source: `f988a327e45ec344e4ada145ae3a9b54109edf1c`.
Final handoff is its documentation/evidence descendant at branch HEAD; resolve HEAD and require clean status. Exact final hash is in the task response.

## Delivered behavior

Slicing has a compact persistent estimate strip beneath layer navigation and above the unchanged status bar. It shows two-decimal mL, readable estimated hours/minutes (hours do not wrap at 24), and completed/unsliced/changed/pending/cancelled/failed state. Hover the strip/Assumptions label for calculation assumptions. Placement was corrected after screenshot review found the first top-strip design overlapped the floating toolbar. Final 1200x800 and 640x480 labeled-toolbar captures are clear.

No pre-slice geometric guess is presented. Existing conservative document-change invalidation is preserved: edits to geometry, support/raft data, printer, print or resin settings clear the estimates and require explicit Slice (or the existing explicit Export action, which slices when needed). This includes resin-only edits; it avoids mixing a new recipe estimate with an old export snapshot. No changes to generation, save, undo or numeric preview policies. Cancelled/failed retries clear old estimates. Edits during slicing cancel the worker and a revision/settings guard prevents stale completion from publishing. Workers snapshot transforms, visibility, raft parameters and cloned support nodes/segments, retaining ownership IDs. Mesh references are reused as immutable geometry.

## Material audit and calculation

`MeshSlicer.Finish` performs a nonzero-winding polygon union of model sections, printable analytic support sections and raft sections, then applies XY compensation. Supports owned by hidden objects and disabled support members are excluded according to existing print semantics; viewport-hidden support members still print. Rafts replace base geometry according to the existing raft policy. Overlapping printable polygons count once, holes subtract area.

The old `AreaMm2` measured the union beyond the LCD even though permissive rasterization clipped it. Area metadata now uses that same union clipped to the physical plate rectangle (existing 0.001mm polygon coordinate precision). Bitmap generation is unchanged. Z cropping/layer counts use the existing slicer behavior. For constant layer height h in mm:

`VolumeMl = sum(clipped union area for each layer in mm²) * h / 1000`.

This is theoretical layer-integrated resin, including printed model/support/raft material, not tank fill, purge/waste, shrinkage or a measured cured volume. Layers sample at mid-height and each counts its full configured thickness. AA uses 16 grey coverage levels; nonzero `LitPixels` would overcount fractional edge pixels, and grey intensity is not a measured cure-volume fraction. Therefore neither lit-pixel counts nor brightness sums substitute for the geometric union. AA on/off gives the same theoretical volume. Pixel quantization and resin cure physics can produce physical differences. Corrected area/volume metadata also flows to existing preview/export totals; no file schema/version or bitmap algorithm changed.

## Time audit and calculation

The actual recipe and writer support bottom and normal exposures, bottom/normal lift height and speed, a common retract speed, and a light-off delay. Writer EXTRA has two stage slots but second lift distance is zero; there is no configured transition layer/exposure schedule, acceleration, separate rest stages or printer startup/finish timing. No unsupported schedule was invented.

For N actual output layers, B=min(N, configured bottom layers), and speeds in mm/min:

`bottom cycle = bottom exposure + delay + 60*bottom lift height/bottom lift speed + 60*bottom lift height/retract speed`

`normal cycle = exposure + delay + 60*lift height/lift speed + 60*lift height/retract speed`

`seconds = B*bottom cycle + (N-B)*normal cycle`.

Calculation is O(1), includes motion and configured wait, and accepts valid sub-1mm/min speeds instead of the old hidden 1mm/min clamp. Invalid/nonfinite recipes return unavailable timing. Valid recipes are normalized by existing project/UI/slicer entry paths. UI rounds up to minutes and labels the result approximate. Actual firmware may interpret light-off delay differently, overlap it with motion, accelerate/decelerate, home, finish or add overhead. The additional-wait convention is explicitly disclosed in the tooltip; this is not firmware-certified duration. Existing export time metadata uses the same estimate.

## Actual validation

- Release solution build passed (NuGet restore initially blocked by sandbox network; approved restore succeeded). Existing SurfaceContour CA2014, two ViewportControl CS8602 and two RaftBuilderTests xUnit2031 warnings remain; incremental builds show only recompiled warnings.
- Targeted slicing/support/raft/estimate suite: 23 passed. Final complete suite: **1020 passed, 0 failed, 0 skipped**, retained `evidence/task-06/final.trx`. All VM tests use the module's temporary isolated AppConfig.
- Deterministic 10mm cube = 1mL; duplicate cube remains 1mL; half-overlap union = 1.5mL; half-cropped cube = 0.5mL and 50mm² per layer. Both AA modes pass. Duplicate bitmaps are equal. Existing raft integration now checks volume increase and summed sliced area. Existing support integration checks viewport-hidden versus disabled members and their material contribution.
- Timing fixture: bottom cycle 28.5s, normal 7s; one-layer job 28.5s, five layers 78s. Zero bottom layers, zero layers, 0.2mm/min, invalid speeds/NaN/overflow and >24-hour formatting pass. Empty/invalid metadata, cancellation, failure, relevant setting invalidation and changed-during-slice tests pass.
- Native MAIN full workspace harness completed all ten success logs, with no workspace-error.txt. It exercised existing numeric preview/cancel/save/lifecycle, toolbar/preferences and SpaceMouse driver-session assertions plus STL import, transform undo/redo, explicit generation (12 nodes/8 segments), raft undo/redo, project reload, 110-layer slicing, layer navigation, export and decoding every layer. Every exported bitmap equals the sliced source. Screenshot-bound estimate values, labeled-toolbar clearance and stale state passed. GUI wrapper did not report a usable numeric exit code; completion is evidenced by the final success logs and process completion.
- Native fixture material = **0.24170238mL**, time = **1368.3333333333333s**, UI **0.24mL / approximately 23min**. Calculation uses 5 bottom +105 normal layers with the default recipe. SHA256 of workflow export: `9C29366C532CB3B1C7D6A6ADA7A94B64E0F0FCA0BF21D3434157AF45FBC08D17`.
- Inspected final full, narrow/labels and stale images; 150/200% render-density images retained. These are Avalonia MAIN captures; slice preview is a real decoded 2D layer. They do not certify physical monitor DPI transitions or GL rendering. Renderer source is untouched, so no redundant GL run.
- Status-bar Border subtree equals task-05 baseline after newline normalization (`source-invariants.txt`). Project schema, numeric controls and SpaceMouse implementation are unchanged. Existing full-suite compatibility checks pass.

Intermediate issues fixed before final verification: inaccessible internal support replacement API (use public clone/add), capture Vector namespace ambiguity, and first-strip toolbar overlap. No unresolved test failure remains.

## Build, launch and reproduce

Actual MAIN app, normal user configuration:

```powershell
dotnet build "C:\Users\plane\.codex\worktrees\271f\Danslicer-chatgpt\Danslicer.slnx" -c Release -p:UsedAvaloniaProducts= --nologo
& "C:\Users\plane\.codex\worktrees\271f\Danslicer-chatgpt\src\Danslicer.App\bin\Release\net10.0\Danslicer.App.exe"
```

Tests and MAIN capture use isolated temporary config; capture output is inside this worktree:

```powershell
dotnet test "C:\Users\plane\.codex\worktrees\271f\Danslicer-chatgpt\tests\Danslicer.Tests" -c Release --no-build --no-restore --nologo
Start-Process -FilePath "C:\Users\plane\.codex\worktrees\271f\Danslicer-chatgpt\src\Danslicer.App\bin\Release\net10.0\Danslicer.App.exe" -ArgumentList '--workspace-capture','C:\Users\plane\.codex\worktrees\271f\Danslicer-chatgpt\artifacts\task06-recheck' -WindowStyle Hidden -Wait
```

## Limits and exact next action

User reviews the Slicing strip in the MAIN app, including hovering assumptions and changing recipe/geometry then explicitly slicing again. Physical printing, firmware timing, physical input/DPI transitions and **task-05 physical SpaceMouse neutral-cap retest remain unverified**. Native driver/synthetic checks are not hardware motion certification. No production-sized job benchmark or accuracy certification.

Task 07 is an independent assessment from the SAME `dc4664c` baseline, explicitly authorized in parallel. It is not this branch's prerequisite or descendant. Later coordination must deliberately import its report and reconcile overlapping TASKS/RESUME documentation. No printer support implementation here, no new tasks, no main merge or push, no writes to Claude/previous/routing checkouts. Next integration action requires the user's instruction; preserve the exact tested branch as the handoff.
