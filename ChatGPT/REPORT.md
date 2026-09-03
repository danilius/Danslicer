# ChatGPT brief 4 report — App lane, 2026-09-03

## Brief 6 screen-test follow-up

- Shift+H now preserves both endpoint nodes of every selected support segment, so the selected
  segment remains drawable and pickable. Selecting a node alone keeps only that node visible,
  following Blender-style vertex-selection semantics rather than expanding the selection.
- Added a Document-level regression test for selected-segment visibility.
- T-routing refusal text is now applied after the key handler's generic status refresh, so it
  remains visible until the next input/status event instead of being overwritten in the same frame.

## Brief 5 routing diagnosis

- Routing results now retain a reason for every refused tip: `ContactBlocked`, `NoClearStep`,
  `NoLanding`, or `BelowPlate`. The route CLI prints counts for each reason in text and JSON, and
  manual placement maps contact blockage to a specific App message.
- A steep-face fixture reproduces the tooth symptom as `ContactBlocked`: the vertical neck remains
  inside the sloped contact triangle after the old fixed terminal allowance. A rejected steep model
  landing is separately classified as `NoLanding`. This confirms neck clearance is the first and
  dominant failure in the sharper repro, so the fix must change the neck's contact departure rather
  than merely increase detour sampling.
- The neck now departs a down-facing steep contact along its outward surface normal before the
  top-down steps begin. If that interpolated normal is blocked by rough local facets, a fixed
  twelve-direction outward/downward fan searches for a deterministic escape. Collision checking
  still covers the portion beyond the contact allowance. If rough facets obstruct the full
  configured neck, the same search retries only the contact-sized length and lets ordinary
  collision-checked routing escape from there;
  a second-wall regression proves the departure cannot tunnel through unrelated geometry. The
  overhang-plus-wall pocket fixture now routes successfully, while a sealed step with model landing
  disabled remains an explicit `NoClearStep` refusal.
- Model landing is enabled for manual and top-down CLI routing. A landing below the configured preferred angle is accepted
  with a deterministic pad enlargement proportional to the angle shortfall instead of being
  rejected outright; the steep-landing fixture verifies the larger pad.
- The header now has one `Auto Drop` toggle and an always-editable lowest-point offset. Enabled
  with offset zero maps to the existing Drop mode, enabled with a positive offset maps to Raise,
  and disabled maps to Off. Legacy Drop settings migrate to enabled/zero; legacy Raise and Off
  retain their saved height. Ctrl+D and transform undo behavior are unchanged.
- Canonical Drogon CLI verification used the 12 elevated island contacts from a deterministic
  coarse tip pass, including the previously refusing tooth contact at approximately
  `(0.777, -53.467, 12.353)`. Result: 12/12 routed, zero `ContactBlocked`, `NoClearStep`,
  `NoLanding`, or `BelowPlate` refusals, and the collision audit passed.

## Status

Implemented all four brief-4 items after the required clean fast-forward merge of `main`
(`cac8625` -> `0160535`):

Brief-4 commit: `f8623cc` — Complete support generation and placement UX.

- Reinforce ring samples are re-projected onto triangle geometry through `ICollisionScene.Raycast`.
  The two-sided, radius-bounded projection updates each contact normal and drops samples which do
  not reach the model. A faceted sphere-cap regression proves the contacts follow curvature.
- Transform commits now share one exact-vertex placement path. `AutoDrop` is the default,
  `RaiseAbovePlate` targets the configured height, and `Off` preserves input Z. Modal keyboard
  transforms, gizmo transforms, and numeric property edits all commit the requested transform plus
  re-seat as one undo step. The header controls and height are persisted in `UserConfig.Placement`.
  `Ctrl+D` remains the independent manual drop command.
- Object > Generate Supports, the Object-panel button, and `Ctrl+G` generate over every face of the
  selected object with default tip parameters, grid routing, seed zero, and collision geometry from
  all scene objects plus existing supports. The pass is one undoable `Generate supports` command,
  uses a non-manual origin keyed to the object, and reports generated/unrouted counts in the status
  bar.
- With support elements selected, `Shift+H` / Object > Hide Unselected Supports hides every other
  support node and segment as one undoable command. `Alt+H` now restores hidden support elements as
  well as hidden objects.

`Document.cs` was edited, as explicitly permitted by the brief, for: the common placement/transform
commit API, whole-object generation using the existing mesh-obstacle cache, and support hide/unhide
commands. `SupportGenerator.cs` was not edited. No locked file was changed.

## Verification

The app was not launched, screen-captured, or sent input, per the brief.

```text
dotnet build -c Debug
Build succeeded. 0 Warning(s), 0 Error(s).

dotnet test --no-build -c Debug
Passed: 197, Failed: 0, Skipped: 0.
```

New coverage includes exact post-rotation/translation landing behavior and undo, Raise/Off modes,
placement config round-trip and tolerant null-section loading, generated-origin/one-step undo,
support hide/unhide undo, sphere-cap Reinforce projection, and rejection of unprojectable ring tips.

## User screen-test scripts

### 1. Reinforce re-projection

There is not yet a profile UI that enables Reinforce, so this item has no honest click path in the
current App. Its runnable verification is
`RoutingGridTests.ReinforceReprojectsRingTipsOntoSphereCap`; it asserts all three ring contacts lie
on the curved cap. Please exercise Reinforce visually when its profile controls land: on a rounded
underside, every larger ring tip should touch the surface and no tip should float or bury.

### 2. Automatic placement

1. Import a model with a non-flat/angled silhouette and leave header **Placement > Drop** selected.
2. Press `G`, `Z`, type `20`, then Enter. The input is accepted, then the model must re-seat with its
   lowest point exactly at the plate; no transform axis is suppressed during the modal.
3. Rotate it with `R`, `X`, type `25`, Enter, then use a numeric Position or Scale field. After each
   commit, its newly oriented lowest point must be at Z=0. Press Ctrl+Z once after each operation:
   both the transform and automatic re-seat must undo together.
4. Select **Raise**, enter `5` in the adjacent edit box, and commit another move/rotation/numeric
   edit. The lowest point must finish 5 mm above the plate. One Ctrl+Z must undo the whole action.
5. Select **Off**, move the model upward, and confirm that it stays there. Press `Ctrl+D` and confirm
   the manual one-shot still drops it to the plate.
6. Close and reopen the app: the selected mode and Raise distance should persist.

### 3. Generate Supports

1. Put a model above the plate (for example select **Raise**, enter `5`, then commit a transform) and
   keep that object selected.
2. Invoke Object > **Generate Supports**, then repeat using the **Generate Supports** button in the
   Object panel or `Ctrl+G` on a fresh/undone pass.
3. Supports should appear across the object's required underside regions. The status bar must state
   the number of tips added and the number unrouted (or say that no tips were needed).
4. Press Ctrl+Z once. The entire generated pass must disappear in that single undo step; Ctrl+Shift+Z
   must restore it.

### 4. Hide unselected supports

1. With several support elements visible, click one support element so it draws selected/white.
2. Press `Shift+H` (or Object > **Hide Unselected Supports**). Every unselected support element must
   disappear while the selected element remains; slicing semantics are unchanged because this is
   visibility only.
3. Press Ctrl+Z once to restore them. Repeat the hide and press `Alt+H`; all hidden support elements
   should reappear.

# ChatGPT routing-stage report

## Status

Implemented and committed on `grid-routing-prototype`:

- Phase 3 complete after the required `git merge main` fast-forward to `a130d17`.
- Routing debt fixed: promoted trunk collision radii stay synchronized, and both routers share
  deterministic-ID and safe-normal helpers.
- Attach-to-existing is implemented for grid and top-down routing. `AttachToExisting` plus an
  existing graph makes eligible pillar/trunk endpoints merge targets; successful routes append
  their new nodes and joining segment to that graph without changing existing element properties.
- `ReinforceGrowthRule` selects the object-lowest tip, region-lowest tip, or every critical tip,
  adds the configured number of larger contacts on a deterministic XY ring, and feeds the expanded
  list through either active routing strategy.
- Both routing options accept `KeepCleanObstacleTags`. Pillar/trunk/branch collision checks use
  `DistanceFromModel` against everything, then a second filtered query using
  `DistanceFromKeepCleanFaces` against matching tagged geometry.
- Top-down routing now consumes `LandGrowthRule`: a downward triangle ray query finds an upward
  model surface within the next growth step, enforces landing angle and keep-clean exclusion, and
  emits a model-contact `Base`. The final member uses `LandingPadDiameter`, whose capsule end cap
  represents the pad without changing the locked graph schema.

Phase-3 commits:

- `4f19b43` — promoted collision radii and shared routing helpers
- `af8c771` — attach-to-existing for grid and top-down strategies
- `990c62f` — deterministic Reinforce rule
- `01a1580` — tagged keep-clean clearance
- `cac8625` — top-down model landing and surface ray queries

- `8e29429` — collision scene and exact capsule queries
- `0de9b9a` — ordered growth-rule framework and deterministic grid router
- `723faf4` — headless `route` CLI harness
- `5600527` — snap-tolerance behavior and complete lean reporting
- `6e71737` — merge-rule fix, pillar-relative taper, outward graph normals
- `e54c742` — deterministic top-down routing and CLI strategy selection
- `e98e2bb` — BVH collision scene and randomized reference comparison
- `0922b46` — rerunnable Brace stage
- `59ae664` — include earlier necks in same-pass top-down collision avoidance

No locked/shared foundation file was edited. `Program.cs` received only command registration and
one usage line; the command implementation is in `RouteCommand.cs`.

## Implemented

### Collision structure

`LinearCollisionScene` implements `ICollisionScene` and stores world-space triangles plus support
capsules. Queries provide:

- exact capsule-versus-triangle collision (AABB broad-phase, segment/triangle distance narrow-phase)
- exact capsule-versus-capsule collision
- nearest point/distance/tag for both triangles and support capsules
- helpers to add a transformed `Mesh`, `SceneObject`, or every enabled segment of an existing
  `SupportGraph`

The simple linear implementation remains the exact reference behind the interface. The production
CLI uses the phase-2 BVH implementation without changing either routing strategy.

### Growth rules

`GrowthRuleSet` evaluates enabled `IGrowthRule` instances in list order and stops after rejection.
The data-style rule classes and public parameters are:

- `LeanGrowthRule`
- `BranchGrowthRule`
- `MergeGrowthRule`
- `TaperGrowthRule`
- `ClearanceGrowthRule`
- `LandGrowthRule`

The grid router actively consumes lean, branch, taper, and model-clearance behavior. Branch count
limits can cause a later tip to select a neighbouring base. Merge parameters set shared-trunk
diameter where their constraints permit. Land is available to future model-landing strategies;
the current grid implementation deliberately lands only on `PlateZ`.

### Grid bottom-up routing

`GridSupportRouter` consumes plain `RoutingTip` records and emits a new `SupportGraph`:

- square and staggered-row hexagonal lattices
- spacing, XY offset, rotation, snap tolerance, plate Z, candidate search rings, diameters and seed
- nearest viable lattice-base selection with obstacle fallback
- vertical or slightly leaning pillars, shared trunks for tips assigned to one base, tapered necks
- branch-distance/count rejection and lean limits
- endpoint-aware collision checks so the final neck may intentionally contact its target surface
- deterministic node/segment IDs from an explicitly seeded RNG
- unrouted-tip list, base positions and maximum observed lean in the result

Snap tolerance means a tip close to a lattice column gets a junction directly beneath the tip;
the base remains on the lattice and the pillar absorbs the small offset.

### CLI

Usage:

```text
danslicer route <mesh.stl|mesh.obj> --tips <tips.json> [--strategy grid|topdown]
  [--step-height 2] [--spacing 5]
  [--lattice square|hex] [--offset-x 0] [--offset-y 0]
  [--rotation 0] [--snap 0.25] [--seed 1] [--json]
```

Tips JSON is an array like:

```json
[
  {
    "surfacePoint": [1, 1, 10],
    "inwardSurfaceNormal": [0, 0, 1],
    "tipDiameter": 0.4
  }
]
```

Text and JSON summaries include node and segment counts by type, unrouted count, every base
position, maximum lean, and a collision audit against the input mesh. Exit code 2 indicates an
unrouted tip, collision failure, or invalid input.

### Review fixes and phase 2

- Merge height is interpreted as the required **vertical clearance below the lowest tip served by
  the merge**. `GrowthContext.LowestTipZ - mergePoint.Z` is compared with
  `MinHeightAboveTipsToMerge`. Grid shared trunks now evaluate the actual lowest junction as the
  merge point, and regression tests assert the configured trunk diameter.
- Taper is pillar-relative. Routing starts neck evaluation with pillar diameter, then applies
  `TipToPillarDiameterRatio`; `RoutingTip.TipDiameter` remains the contact diameter stored on the tip
  node. This avoids making a neck thinner than its own contact because the ratio was applied to the
  contact diameter. The current graph stores one diameter per segment, so a continuously varying
  neck profile remains a later geometry concern.
- Both routers negate `RoutingTip.InwardSurfaceNormal` when populating the graph, whose convention is
  outward contact normals.
- `TopDownSupportRouter` descends from each tip in bounded `StepHeight` increments. It tries vertical
  travel first, then deterministic radial detours within the active Lean limit. Earlier routes are
  collision obstacles and merge targets. Rule-approved merges connect to a lower node and promote
  the shared downstream path to configured trunk diameter.
- `BvhCollisionScene` lazily rebuilds a median-split AABB hierarchy after additions and uses the same
  exact narrow-phase primitives as the linear reference. The CLI now uses the BVH.
- `BraceGrowthRule` contains the §8.5 parameters. `SupportBraceStage` ranks rule-valid neighboring
  node pairs by preferred-angle error and length and adds at most one cross-member per tree pair.
  Existing endpoint pairs are detected, making an unchanged rerun idempotent.

## Verification

Phase 3 final verification:

```text
dotnet build -c Debug
Build succeeded. 0 Warning(s), 0 Error(s).

dotnet test --no-build -c Debug
Passed: 167, Failed: 0, Skipped: 0.
```

The existing procedural CLI fixture remained clean with both strategies. Grid reported 2 tips,
6 nodes, 4 segments, 2 bases, 35-degree maximum lean, and `Collision-free: yes`; top-down reported
2 tips, 12 nodes, 10 segments, 2 bases, 0-degree maximum lean, and `Collision-free: yes`.

Final required commands:

```text
dotnet build -c Debug
Build succeeded. 0 Warning(s), 0 Error(s).

dotnet test --no-build -c Debug
Passed: 105, Failed: 0, Skipped: 0.
```

New test classes:

- `RoutingCollisionTests` — face hit/miss, edge proximity, support capsules, nearest obstacle
- `RoutingGrowthRuleTests` — ordered evaluation, enable/disable, lean, branch rejection, taper
- `RoutingGridTests` — graph taxonomy, shared trunk, blocked-base fallback, determinism, branch
  rejection/count, snap tolerance, merge diameter/height semantics, pillar-relative taper
- `RoutingTopDownTests` — plate descent, merging/trunk promotion, obstacle detours, determinism
- `RoutingCollisionTests.BvhAgreesWithLinearSceneOnFixedSeedRandomizedQueries` — 500 capsule and
  nearest-point queries over 120 triangles and 40 capsules, fixed seed
- `RoutingBraceTests` — rule gating, cross-member creation and idempotent rerun

CLI invocation against procedural scratch fixtures:

```text
dotnet run --no-build --project src\Danslicer.Cli -- route \
  ChatGPT\route-fixture.stl --tips ChatGPT\route-tips.json \
  --spacing 5 --lattice hex --rotation 10 --seed 123
```

Output:

```text
Tips:           2 (0 unrouted)
Nodes:          6
Segments:       4
  Neck         2
  Pillar       2
  Trunk        0
  Bracing      0
Bases:          2
  -4.924, -0.868, 0
  0, 0, 0
Max lean:       35 degrees
Collision-free: yes
```

The same fixture was also run with `--json`; it reported the same counts, zero unrouted tips, and
`"collisionFree": true`.

Final CLI verification ran both strategies against the fixture. Grid retained the documented 6
nodes / 4 segments / 2 bases result. Top-down reported 12 nodes, 10 segments, 2 bases, zero unrouted
tips, 0-degree maximum lean for this unobstructed fixture, and `Collision-free: yes`.

## Design decisions

- Degenerate routing normals fall back to inward `+Z`. The graph negates inward normals, so this
  produces a downward outward contact normal, matching the common underside-support case and the
  grid router's established behavior.
- The graph model only permits segments to end at nodes, so attach-to-existing targets enabled
  endpoints incident to pillar/trunk segments rather than splitting an existing segment. This is
  the strict interpretation of “never modify” existing pinned/manual members: old nodes and
  segments retain type, diameter, origin, and flags; only the new joining segment references one.
- Reinforcement uses an XY ring at the seed contact's Z and inherits its normal/object ID. Routing
  inputs can explicitly mark object-lowest, region-lowest, and critical seeds; when a lowest marker
  is absent, the router deterministically uses the lowest supplied tip. `SupportGenerator` marks
  its region-lowest candidate and marks it object-lowest only when it lies at the mesh's minimum Z.
- Keep-clean enforcement is deliberately split at the stage boundary in DESIGN §8.6: `TipPlacer`
  remains responsible for the hard rejection of tip contacts on/near keep-clean faces; routing
  applies the larger soft clearance only to load-bearing members. Necks use ordinary model
  clearance because their terminal contact is intentional and was already accepted by placement.
- Landing angle is measured between the candidate surface and the downward approach: horizontal
  upward-facing surfaces are 90 degrees, vertical faces are 0, and the configured minimum is the
  acceptance threshold. Ray queries consider mesh triangles, not support capsules; supports remain
  collision obstacles and attach targets rather than landing surfaces.

- Collision geometry is copied to world space once. Query callers do not need to understand object
  transforms, and the future BVH can index immutable primitives.
- Clearance inflates collision-query radius rather than modifying geometry.
- Terminal neck collision is checked only up to a radius-aware distance before the surface because
  the contact itself must intersect the model.
- Candidate and group ordering uses coordinates and input indices; seeded IDs remove the remaining
  nondeterminism from `Guid.NewGuid()` defaults in the existing graph element types.
- Routing returns partial success plus `UnroutedTips`; it does not silently emit invalid supports.
- Disabled existing supports are excluded from collision geometry because they are excluded from
  slicing; hidden elements remain obstacles because they still print.

## Known limitations / next work

- The linear scene remains O(triangles + capsules) as a reference. Production CLI routing uses the
  BVH; performance profiling and SAH construction are future optimization work.
- Grid routing remains a readable prototype rather than an optimizer: obstacle avoidance tries
  neighbouring lattice bases but does not create multi-bend detours. Top-down routing can create
  multi-step detours but uses deterministic local search rather than global path optimization.
- Shared-base trunks are implemented. General merging of independently growing neighbouring
  pillars is not yet implemented.
- Clearance currently treats all model triangles uniformly; keep-clean faces need tagged obstacle
  subsets so `DistanceFromKeepCleanFaces` can be applied separately.
- `LandGrowthRule` is data/evaluation only. Model landing needs surface ray queries and is not used
  by this plate-based router.
- Grid supports generated earlier in the same call are not fed back as obstacles. Top-down routes do
  avoid and merge with earlier routes. Existing supports are respected when callers add them to
  either collision-scene implementation, but attach-to-existing graph emission is not implemented.
- Brace endpoints are selected from existing graph nodes; the stage does not yet split a long pillar
  to place a brace at an arbitrary ideal height. Reinforce remains unimplemented.
