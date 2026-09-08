# Danslicer handover

## STATE 2026-09-09 evening — READ FIRST (supersedes everything below)

**Single-session work (Claude implementing directly, user screen-testing live).** Auto-parenting
was screen-tested by the user on 2026-09-09: checklist items 1–4 all passed. Branch **`bracing`**
(off main `9250fac`) carries the bracing work, 899 tests green, NOT merged and NOT screen-tested:
spec `db8f32e`, Core `0198d1f`, UI `9ee0bd4`, round-trip test `c676cf8`, chain rework to the
user's drawing `ec11f83` (pairs chain along the row, alternating direction, continuous zigzag,
braces never block braces, two selected supports brace regardless of distance), stems
rework (the user drew braces over the near-vertical branches parenting leaves above short
trunks: a branch continuing a trunk within "Max stem lean" 30° is braced as part of it, and
the rung that would overshoot the shorter stem is laid flatter to its top), then top-down
ladders with the model as the only obstacle (user, 2026-09-09: braces may run through
branches and other trunks), then the angle as a maximum lean from vertical with no
flattening anywhere (user screen test 2026-09-09: three flat bottom rungs circled; a rung
that cannot fit at the angle is left out), then bundles (user, 2026-09-09: a cluster of
trunks closer than "Cluster gap" 3 mm is one stem for bracing, nothing inside it, the row
ties to whichever member is nearest at each rung's height). The app runs from
`src\Danslicer.App\bin\Debug\net10.0\Danslicer.App.exe` after `dotnet build -c Debug`. Memory
files (`~/.claude/projects/F--Git-Repos-Danslicer/memory/`) carry the roadmap and standing
rules; read `MEMORY.md`. Every support rule is in `docs/SUPPORT-GEOMETRY-SPEC.md` — the new
section "Bracing (user-approved spec, 2026-09-09)" is the contract for this branch.

**Screen-test checklist for bracing** (on `test files\roof gripper T2 single and tilted
cube.danslicer`; the probe put 73 braces on the gripper's 29 supports, heads up to 60 mm, with
defaults — the saved gripper's trunks end at junctions at 1–65 mm with near-vertical branches
above them, which is why the first cut stopped half way up):
1. Support mode, nothing selected, K — status "Bracing: n braces added, m supports tied";
   braces draw in the bracing colour as a continuous zigzag up each pair of neighbouring
   trunks, consecutive pairs running opposite ways so a row reads as diamonds (the user's
   drawing of 2026-09-09), and no trunk is split (select a trunk: still one segment).
1b. Select exactly two supports, K — just those two are braced, even far apart or with
   another trunk between them.
2. K again — "no brace fits" and nothing changes. One undo removes every brace.
3. Select a few supports, K — only those get braces. Shift+K — their braces go, one undo.
4. "Select braces" button (Supports pop-out) or Object > Select Braces — only braces selected;
   Delete removes them cleanly (no stray balls left on the trunks).
5. Delete one braced support — its braces go, the neighbours' other braces stay.
6. J on braced supports — braces on rebuilt trunks vanish, then auto-bracing re-braces the
   result (Bracing expander, "Auto-bracing" on). Untick it: J leaves no braces.
7. Generate Supports — the status line ends "… n braces." and one undo removes all.
8. Bracing expander: Pattern Diagonal, angle, spacing, min support height, neighbour distance.
Fix what they find on `bracing`; merging is their call.

**How bracing works (`SupportBracing.cs`, spec section "Bracing"):**
- Operands: the supports containing the selection, else every support of the target (one
  base per support). Each support's vertical trunk segments sharing an axis form a *column*
  (bottom = base top, top = highest trunk node).
- A column ("stem") is a polyline from a base up the trunk and then whichever member
  continues most nearly vertically, while it leans ≤ `BracingMaxStemLeanDegrees` (30);
  brace ends are interpolated along it (`Column.At(z)`), cones never count.
- Columns within `BracingClusterGapMm` (3) of each other (transitively, by bottom XY) form
  a `Bundle`; a lone column is a bundle of one. Pairs, partners, chains and the existing
  brace check all work on bundles; each rung lands on the member of each bundle nearest
  the other bundle at its height (`Bundle.NearestAt`).
- Bundles with top ≥ `BracingMinSupportHeightMm` (20) are walked as chains: start at the
  column with the fewest neighbours in `BracingNeighbourDistanceMm` (10), then its nearest
  unvisited neighbour, and so on; consecutive chain members are a pair. A pair already
  tied by a brace is skipped (idempotent); a column at `BracingMaxPartners` (3) is skipped.
  With `chosen` (an explicit selection) and exactly two supports, distance, partners and
  other supports are ignored and the rise is flattened to fit.
- Ladder: foot at max(`BracingLowestHeightMm` or min branch height, bottoms + radius), head
  = foot + gap·tan(angle), next foot = previous head (`BracingSpacingMm` 0 = continuous,
  else that pitch), alternating sides (Zigzag) or not (Diagonal), until an end would pass
  top − radius. Even pairs of a chain start from their earlier trunk, odd pairs from the
  later one. `BracingAngleDegrees` is the lean from vertical and every rung is laid at
  exactly it. Ladders are laid top-down: the first rung as high as both stems allow, each
  next rung ending where the last started; a rung that would start under the floor is
  dropped, never flattened. Continuous braces share
  their brace-end node. Each brace is a capsule test against the model meshes only.
- **Brace ends are `SupportNodeType.BraceEnd` nodes on the trunk axis; the trunk is never
  split.** The carrier is found geometrically (`SupportBracing.CarrierOf`, 0.05 mm off the
  axis), so a split or replaced trunk still carries them. `SupportGraph.Supports()` skips
  them; `Document.AddOrphanedFragments` removes a brace end when its carrier or its brace
  goes (iterates to a fixed point: one end going takes the brace, which orphans the other);
  `SupportParenting.Components` adds the braces on a component's segments so a re-route
  takes them down. Braces slice and render as plain capsules (their own small ball sits
  inside the trunk).
- Commands: `Document.BraceSupports` ("Brace supports"), `UnbraceSupports` ("Unbrace
  supports"), `SelectBraces`; `AutoBraceAfter` folds a brace pass into the last undo step
  via `History.MergeLastTwo` after `ParentSupports`, `AutoParentAfterPlacement` and
  generation (`AutoBraceAfterGeneration`, called from `GenerateSupports` and the batched
  path in `MainViewModel`). Viewport: `Key.K` / `Shift+K`, `BraceSupports` /
  `UnbraceSupports` / `SelectBraces` public methods, buttons in the Supports pop-out and
  Object menu items. Settings: `SupportConfig.Bracing*` + `AutoBracing`, "Bracing"
  expander in `SupportSettingsView.axaml`, `ConfigViewModel.SupportBracing*`.
- The old unused `SupportBraceStage` / `BraceGrowthRule` / `GrowthOperation.Brace` are gone.

**Then (roadmap, user order):** rafts (write the spec section first, get it approved). Notes
for later (user, 2026-09-08, not ordered): manual support editing (click a support, Space
enters an edit mode; move base XY, trunk XY, tip across the surface); a manual placement MODE
instead of T with a ghosted support following the cursor; split the overloaded Supports
pop-out into toolbar functions. Standing rule: every key-bound function needs a toolbar
button (L/P/E/R/C/J/K have them via pop-outs; keep it that way for anything new). Bracing
"Later": braces to branches, manual bracing, braces following edited trunks, cross-object.

**Working rules that bit us (do not repeat):**
- Kill the running app before building (`taskkill /IM Danslicer.App.exe /F`); never drive
  the app on screen while the user is present — they test, you build.
- Gate commits on `grep -q "Failed:     0"` over the test output, not on grep's exit code.
  `dotnet test --no-build` after a failed build reports stale green: build first.
- Throwaway probes (`tests\Danslicer.Tests\Zz*Probe.cs`) against the roof gripper project
  find limits in minutes (`ProjectFile.Load(path).Document`, `Select(obj)`, run the
  command, `ITestOutputHelper`). Delete probes before committing.
- Long Python patch scripts fed through a bash heredoc get mangled by the tool: write the
  script to the scratchpad and run it by path.
- A later command in a composite must not be built before earlier ones execute
  (`DeferredCommand`); `RemoveSupportElementsCommand` looks its ids up at construction.
- Avalonia commits bindings per keystroke; `UpdateSourceTrigger=LostFocus` on expression
  fields. Crash stacks: `%AppData%\Danslicer\logs\`.
- Ask before branching; never commit to main directly; merging is the user's call, proposed
  actively at a sensible stopping point.

## STATE 2026-09-09 — READ FIRST (supersedes everything below)

**Single-session work (Claude implementing directly, user screen-testing live).** main is
the merge of `auto-parenting` (`68f3818`, pushed); 891 tests green; the app runs from
`src\Danslicer.Appin\Debug
et10.0\Danslicer.App.exe`. Memory files
(`~/.claude/projects/F--Git-Repos-Danslicer/memory/`) carry the roadmap and standing rules;
read `MEMORY.md`. Every support rule is in `docs/SUPPORT-GEOMETRY-SPEC.md` ("Guided tip
placement", "Parenting" incl. "Auto-parenting") — read them before touching
`Supports/Guided/*`, `SupportParenting.cs`, `HierarchicalParenting.cs` or
`TreeSupportRouter.cs`.

**Auto-parenting is merged but NOT yet screen-tested by the user.** Run this checklist
with them first, on `test files
oof gripper T2 single and tilted cube.danslicer`:
1. T two supports close together — the second should report "Support: placed → 1 trunk".
2. A guided line (L) over a supported edge — "Support line: n placed → m trunks".
3. Densify (D) — same suffix. One undo after each must remove placement AND parenting.
4. Untick "Auto-parenting" (Parenting expander) — supports stay single until J.
Fix what they find on a branch off main (ask before branching; merging is their call).

**How auto-parenting works (built 2026-09-08 night, design confirmed by the user):**
- `SupportConfig.AutoParenting` (default ON). `Document.AutoParentAfterPlacement` runs
  after `AddManualSupport`, `PlaceGuidedTips` (guided commit and densify): operands are the
  tips just placed plus the target's existing tips within the trunk search range
  (`ParentingTrunkRange`, 0 = `ExistingTrunkBranchRange`) of any new tip; the ordinary
  `SupportParenting.Plan` runs (hierarchical or router, same settings), then
  `UndoStack.MergeLastTwo(name)` folds placement + parenting into one step under the
  placement's name. `AutoParentingOutcome(Placed, Trunks, Refused)` feeds the status line
  (`ViewportControl.AutoParentSuffix`).
- `HierarchicalParenting.Build(..., existingTrunks)` joins the standing supports' vertical
  trunks (`HierarchicalParenting.ExistingTrunks(working, targetId)`) nearest first within
  the trunk range before `DropTrunk`: attach z = min(junction.Z − horiz/tan(angle), trunk
  top), ≥ max(min branch height, base top). Below the top the trunk is split
  (edit.RemovedSegments + two Trunk clones + a junction); at the top, or exactly on an
  earlier split's junction, the branch joins that node. Obstacle capsules carry the segment
  id (`LinearCollisionScene.AddSupportGraph`), excluded for the trunk being joined. A piece
  the build itself made is replaced in AddedSegments, never listed for removal (that
  crashed: "segment not in the graph"). Router mode shared trunks already.
- Probe on the saved roof gripper (36 tips in runs of 10, its own 8 mm / 45° settings,
  grid on): 26 bases for 36 tips; the low-edge tips stay single because their cones end
  under the 10 mm floor or no lattice point is within 8 mm — settings, not bugs. Under
  100 ms per placement. Raising branch length / angle / cone bend in the Parenting
  expander is what makes long runs collapse onto one trunk (60° → one tree yesterday).

**Then (roadmap, user order):** bracing (write the spec section first, get it approved),
then rafts (spec section first). **Notes for later** (user, 2026-09-08, not ordered): manual
support editing (click a support, Space enters an edit mode; move base XY, trunk XY, tip
across the surface); a manual placement MODE instead of T with a ghosted support following
the cursor; split the overloaded Supports pop-out into toolbar functions. Standing rule:
every key-bound function needs a toolbar button (L/P/E/R/C/J have them via pop-outs; keep
it that way for anything new).

**Working rules that bit us (do not repeat):**
- Kill the running app before building (`taskkill /IM Danslicer.App.exe /F`); never drive
  the app on screen while the user is present — they test, you build.
- Gate commits on `grep -q "Failed:     0"` over the test output, not on grep's exit code.
  `dotnet test --no-build` after a failed build reports stale green: build first.
- Throwaway probes (`tests\Danslicer.Tests\Zz*Probe.cs`) against the roof gripper project
  find routing limits in minutes; `HierarchicalParenting.Trace` prints why pairs refuse.
  Delete probes before committing.
- A later command in a composite must not be built before earlier ones execute
  (`DeferredCommand`); `RemoveSupportElementsCommand` looks its ids up at construction.
- Avalonia commits bindings per keystroke; `UpdateSourceTrigger=LostFocus` on expression
  fields. Crash stacks: `%AppData%\Danslicer\logs\`.
- Ask before branching; never commit to main directly; merging is the user's call, proposed
  actively at a sensible stopping point.

## STATE 2026-09-08 evening — READ FIRST (supersedes everything below)

**Single-session work (Claude implementing directly, user screen-testing live).** main is
`a81f28f`, pushed; 880 tests green; the app runs from
`src\Danslicer.App\bin\Debug\net10.0\Danslicer.App.exe`. Branch `auto-parenting` exists off
main with NO code yet — it is where the next task starts. Memory files (`~/.claude/projects/
F--Git-Repos-Danslicer/memory/`) carry the roadmap and standing rules; read `MEMORY.md`.

**Merged to main today, in order** (every merge pushed): guided placement (line L, polygon P,
edge E, ring R, contour C, densify D, thin Shift+D) with a "Guided" settings expander; a
Transform pop-out replacing the Layout right panel and a Guided pop-out with a button per
guided key; import seating (meshes centred at import, so position fields read the model's
place, not the STL's origin); expression fields commit on focus loss; parenting (J) in two
modes with a "Parenting" expander; crash log + app-wide exception handler; base-lattice
markers on the plate. Every rule is in `docs/SUPPORT-GEOMETRY-SPEC.md` sections "Guided tip
placement" and "Parenting" — read both before touching `Supports/Guided/*`,
`SupportParenting.cs`, `HierarchicalParenting.cs` or `TreeSupportRouter.cs`.

**Parenting as it stands (user-verified on screen 2026-09-08):**
- Explicit command (J / "Parent Supports" button in the Supports pop-out). Operands: the
  supports containing the selection, or all of the target's when nothing is selected. A tip
  the re-route refuses keeps its support. One undo step "Parent supports".
- **Hierarchical tree** (default): cones → junctions; cheapest pair merges, either under the
  midpoint or by the higher junction branching into the lower one's position (whichever
  stays highest); repeat; each surviving junction drops a trunk — lattice points only with
  the grid on, else straight down or a fan of clear columns. Existing trunks are NOT joined
  in this mode yet (see auto-parenting below).
- **Router mode** (hierarchical off): the tree router with `ShareTrunks` (sharing in free
  mode too, any join within range beats a fresh trunk). Lattice placement stays grid-only.
- Settings, 0 = the Members value: max branch length, max branch angle, trunk search range,
  min tips per trunk (second pass with double range), rounds (seeds; fewest trunks wins),
  max cone bend (the branch leaving a cone may bend this far from the cone axis; 90 lets a
  tip on a leaning wall join sideways), max branches per trunk (growth-rule cap, default 6).
- **Min branch height** (Members, default 10 mm): no branch attaches and no junction is made
  below it; applies to generation, manual and both parenting modes. On the roof gripper's
  lower edge (44 tips, 64 mm rise): 45° → four trees, 60° → one tree, plus the six lowest
  tips as singles (their cones would end under the floor).

**Guided placement rules to remember:** guided tools ignore existing supports by default
("Guided tools ignore existing", on) and route as if alone; untick for existing-aware
placement with "Guided tip clearance". The within-gesture duplicate radius is half the
spacing (a full-spacing radius dropped every tip past a bend). Densify/thin work on the
selection or the whole target.

**NEXT: auto-parenting (branch `auto-parenting`, user directive).** Design agreed with
myself, not yet with the user in detail — confirm before coding:
1. Setting `AutoParenting` (Parenting expander, default ON): after any placement — T,
   guided commit, densify — the new tips PLUS the tips of existing supports within the
   trunk search range of any new tip become parenting operands, and the parenting plan runs
   at once. Placement + parenting must be ONE undo step: add `UndoStack.MergeLastTwo(name)`
   (composite of the last two commands) rather than threading the plan into placement.
2. The hierarchical builder must learn to **join existing trunks**: for each surviving
   junction, before dropping a new trunk, try the working graph's Trunk segments nearest
   first — attach at z = min(junction.Z − horiz/tan(angle), trunk top) ≥ max(min branch
   height, base top); split the segment (SupportGraphEdit.RemovedSegments + two Trunk
   segments + junction) or join at the top node when the attach point is the top. Obstacle
   capsules from `LinearCollisionScene.AddSupportGraph` are tagged with the segment id
   (`CollisionScene.cs:67`), so exclude that trunk's own capsule when testing the branch.
3. Status line: "Support line: 12 placed → 3 trunks".

**Then:** bracing (spec section first), rafts (spec section first). **Notes for later** (user,
2026-09-08 evening; not ordered): manual support editing (click a support, Space enters an
edit mode; move base XY, trunk XY, tip across the surface); a manual placement MODE instead
of T, with a ghosted support following the cursor and nothing shown where placement is
impossible; the Supports pop-out is overloaded — split its controls into toolbar functions.
Standing rule: every key-bound function needs a toolbar button.

**Lessons from today (do not repeat):**
- A crash reached the user twice before the crash log existed; stacks are now in
  `%AppData%\Danslicer\logs\`. Windows event log (`Get-WinEvent`, provider ".NET Runtime")
  has the stack for anything older.
- A later command in a composite must not be built before the earlier ones execute
  (`DeferredCommand`); `RemoveSupportElementsCommand` looks its ids up at construction.
- Gate commits on the test result explicitly (`grep -q "Failed:     0"`), not on grep's
  exit code over the output — one commit went in with two failing tests.
- Throwaway probes (`tests\Danslicer.Tests\Zz*Probe.cs`) against the roof gripper project
  found every parenting limit in minutes; write one before theorising, delete before commit.
  Clear the target's supports first: laying a guided edge over saved supports with
  ignore-existing on puts two cones on each spot and they refuse each other.
- Avalonia commits bindings per keystroke by default; `UpdateSourceTrigger=LostFocus` on
  every expression field.
- Kill the running app before building (standing user rule); the user may be running it —
  it is fine to kill, not fine to drive it on screen while the user is present.

## STATE 2026-09-07 ~11:00 — READ FIRST (supersedes everything below)

**Single-session night (Claude implementing directly, user screen-testing live).** main is
`3b09e69`, pushed; 798 tests green; the app runs from
`src\Danslicer.App\bin\Debug\net10.0\Danslicer.App.exe`. Two branches merged tonight:
`align-branch-with-cone-tip` (`b9e6507`, 10 commits) and `remove-mini-supports` (`3b09e69`).
Every rule below is a user decision recorded in `docs/SUPPORT-GEOMETRY-SPEC.md`, section
"Cone orientation and joints (user decisions, 2026-09-07)" — read that section before touching
`TreeSupportRouter.cs` or `TipBodyGeometry.cs`.

**What the support anatomy is now (user's Blender drawings: cone, sphere, cylinder):**
- A cone tip is the whole tip member: one taper from the contact radius to the radius of the
  ball it grows from, base ring at that ball's centre. No neck, no normal lead-in bend; the
  "Cone length" row in the panel is the tip member length. Straight cones only.
- The cone points along the contact normal, clamped to 45° from vertical; when that direction
  is blocked or nothing can follow from it, the route is retried with the cone vertical.
- One cone per ball. The member leaving a cone's junction bends at most 45° from the cone's
  axis (no Z-kinks); the branch continuing the cone's axis is offered first. Cones keep their
  full base radius clear of each other (checked as a frustum, touching allowed; no model
  clearance margin between supports; the member-separation setting is the only gap rule).
- No stub branches: a junction within half a cone length of a trunk axis or grid drop line is
  snapped onto it (cone re-aimed ≤30°, trunk split or raised); a branch never starts within a
  branch radius of the line it descends to. A trunk carrying its own cone is never raised.
- Grid mode joins an existing trunk only when its branch is at most half a grid pitch longer
  than a fresh trunk's would need; when no lattice point is reachable at all, the base leaves
  the grid rather than refusing.
- Free mode (grid off): every tip is a whole support of its own, blind to every other support,
  existing or new, collisions allowed; other supports are not obstacles for it.
- Viewport: one member draws each joint's ball, the others tuck their caps inside it (no seam
  flicker).

**Mini supports are REMOVED** (user: "not worth dealing with right now"). Member type,
placement (mini islands, density clusters, fine-feature minis), the refused-tip downgrade,
router paths, settings, CLI flags (`--fine-feature-*`) and 23 tests are gone. Old project
files still carrying `"miniSupport"` segments load with those members dropped, plus the branch
end / trunk / base left holding nothing (`ProjectFile.DropMiniSupportRemnants`). Stale mini
properties in `%AppData%\Danslicer\config.json` are ignored. Do not resurrect from the
addenda below: the 2026-09-03 mini-support dictation is superseded.

**Lessons from tonight (do not repeat):**
- Three separate "fan of tips on one ball" reports were all mini supports: saved ones from an
  old file, then the refused-tip→mini fallback still on in the user config after its checkbox
  was removed, then the density-cluster pass forced on in `SupportGenerator.GenerateTree`.
  The tell: save + reopen made them vanish (the loader dropped minis). Probes that use the
  project's saved settings do not see what the viewport does with the user config.
- Test subject is now `test files\roof gripper T2 single and tilted cube.danslicer` (user:
  the drogon was steering the algorithm too much). Reference numbers, fresh generation, grid
  on: roof gripper 30 routed / 4 refused, cube 45 / 4, no stubs, no shared balls; grid off:
  34 / 0 and 49 / 0.
- Throwaway probe tests (`tests\Danslicer.Tests\Zz*Probe.cs`) were the fastest way to see
  routing on real files; always delete them before committing.
- `dotnet test --no-build` after a failed build reports stale green: check the build first.
- Kill the running app before building (`taskkill /IM Danslicer.App.exe /F`); the user may be
  running the exe themselves — ask before killing when they are present.

**Open / next:** the five branches over 6 mm left on the roof gripper in grid mode were not
chased (nearer lattice points lose to some check; instrument `TryRouteFromJunction` to see
which). Manual placement midway between generated contacts still fails where two cones cannot
both fit (1.25 mm from a neighbour), which is correct. Merging is the user's call per merge.

## SUPERVISOR HANDOVER 2026-09-04 ~afternoon — READ FIRST (supersedes everything below)

**You are the new supervisor session (non-Fable model; the user ran out of weekly
Fable budget mid-day).** Your job: (1) spawn ONE worker session (the Claude
implementation lane — predecessor was `danslicer-75`, now stopped; it worked in
`F:\Git Repos\Danslicer-claude`, branch `viewport-quality`, all merged) and manage it;
(2) keep supervising ChatGPT's autonomous queue; (3) ALL user-facing messages go
through YOUR chat only — workers hand you content to relay; inter-session traffic
minimal (see memory `danslicer-session-comms`).

**State at handover:** main was `dac3d7a` + the W3 merge (view cube + View pop-out,
`2b2ba66` on viewport-quality — user approved; the outgoing worker was executing the
merge+push as this was written: VERIFY `origin/main` contains `2b2ba66` before
anything else; if not, that merge is the first thing to complete). ~513 green tests.
Deferred rendering is the default path. ChatGPT's scheduled queue RUNS UNATTENDED
(Codex task polls `Danslicer-chatgpt\ChatGPT\inbox\` every ~5 min; protocol in
`ChatGPT\PROTOCOL.md`): job **023e (clusters on island contacts) is IN FLIGHT** —
commits f953532/08ef893 reviewed green (508/508); review it on completion, propose
its merge to the user. Then: 023f fine-feature minis, 023g pop-out mode visibility,
024 island workflow (user's stated FOCUS: drogon-lo pushed to well-supported), 025
clip caps (worker owns a Painted-shader follow-up keyed to 025's result file), 026
duplicate/mirror, 027 out-of-plate, 028 UVtools, 029 multi-model hiding, 030
support-target selection, 031 stylesheet proposal. The WORKER's first task: its own
stylesheet proposal(s) (user directive: both lanes propose complete dark
Blender-leaning Avalonia themes, runtime-selectable, inline vector icons — see job
031's brief in the inbox for the agreed deliverable shape). The outgoing worker
PARKED its stylesheet WIP on branch `theme-claude` (`5be68c8`, in Danslicer-claude):
shared icon set plus an execution plan at `src/Danslicer.App/Themes/PLAN.md` — the
new worker continues from there. W3's screen checklist for the user's next testing
batch is in `Danslicer-claude\CLAUDE-LANE.md` (untracked). WORKSHEET.md `11ecaee`
carries the outgoing worker's final lane state. Also owed: a REVIEW.md go-note for 023d
(support-panel removal) once W3 is confirmed on main.

**Governance (hard rules):** merging to main is the USER's call, per-merge, proposed
in your chat — the overnight autonomy grant is DEAD; do not resurrect it from old
text. Single-executor per merge (you execute ChatGPT-queue merges; the worker
executes its own lane's). Verify EVERYTHING with process exit codes — a `--no-build`
test run after a silently failed build reports stale green (this bit us once).
Review flow: every commit verified detached in the scratch worktree
`F:\Git Repos\.danslicer-review5` (fetch from the agent worktree, build+test with
exit checks); reviews to `ChatGPT\REVIEW.md` newest-at-top; merge-test against main
before proposing. Re-arm on start: the queue monitor (folders/QUESTIONS-hash/branch
head each minute) and the hourly heartbeat with stale-claim detection — both died
with the old session.

**Running ChatGPT (exact operating manual, as run today):** ChatGPT is a Codex
scheduled task on the user's desktop app (their machine must stay on, app running —
if the queue goes silent past ~15 min with jobs eligible, tell the user; you cannot
restart it). Every ~5 min it starts a FRESH chat that: reads
`F:\Git Repos\Danslicer-chatgpt\ChatGPT\PROTOCOL.md` and the top of `REVIEW.md`,
claims the lowest-numbered ELIGIBLE `NNN[letter]-slug.md` in `inbox\` (eligible =
its `Requires:` jobs sit in `completed\`; letters order between numbers, e.g. 023b <
023c < 024), moves it to `working\`, implements on branch `grid-routing-prototype`
in `F:\Git Repos\Danslicer-chatgpt` (merges main first when REVIEW.md notes a new
head), commits granularly, never pushes, then writes `completed\NNN-slug.result.md`
and archives the job (failures go to `failed\` — none ever has). You SUPERVISE by:
(1) monitoring the mailbox (folder listings + QUESTIONS.md content hash + branch
head, each minute) and reviewing EVERY commit detached in the scratch worktree —
build+test with exit-code checks; (2) writing reviews/corrections/answers into
`REVIEW.md` newest-at-top (it reads them at each run start — red commits get a "fix
before completing" note there); (3) writing new briefs into `inbox\` — SELF-CONTAINED
(fresh chat each run: exact file paths, exact API signatures pasted in, reference
test files named for patterns, NEVER "derive it from the code" clauses — those caused
every failure), benchmarks via `danslicer bench` when routing changes, "full suite
green + result file" always; (4) on completion, merge-testing against main in the
scratch worktree and PROPOSING the merge to the user; after approval you merge/push
and note the new head in REVIEW.md; (5) triaging stale claims (`working\` untouched
>2h with no commits → move to `failed\`, requeue split). QUESTIONS.md is its
question channel (answer in REVIEW.md); its mtime churns every run — compare content,
not timestamps. The mailbox is git-excluded; never commit it.

**Read next:** `docs/WORKSHEET.md` (live board, user decisions D1-D7),
`ChatGPT\PROTOCOL.md`, memory files (`danslicer-multi-agent-workflow`,
`danslicer-session-comms`, `danslicer-ui-testing`,
`no-screen-testing-while-user-present`), then the addenda below for deeper history.
The user runs all screen tests; batch checklists for them. Canonical models incl.
`test files\drogon collapse.stl` (drogon-lo, the current tuning subject) and
`drogon_collapse with Lychee supports.stl` (shape reference) are on disk, not in git.

## MORNING SUMMARY 2026-09-04 (~03:30) — READ FIRST

**The entire overnight queue is DONE. main `dd93834`, 478 green tests, pushed, app
smoke-tested on screen.** All 18 ChatGPT jobs merged (refusal reasons, mini
classification+band fix, optional grid+A/B, mode-scoped UI, branch shaping, display
modes, manual attach, flush junctions, bench runner, slice integration, project
save/open `.danslicer`, support presets, preset editor with live 3D preview [GL PROBE
PASS on screen], keymap editor, tips perf −32.7%, printer definitions+editor, resin
presets+split, layer clipping, hover waterline). Claude lane merged milestone 4
deferred rendering flag-gated (default Classic; smoke test confirms the DEFERRED path
renders live on this machine — no fallback triggered). One incident: the
viewport/017 semantic merge initially shipped a non-compiling main (stale-binary
green tests masked it); caught in the smoke test, fixed and verified with exit-code
checks (`dd93834`). Qwen: 7 merged test files. ChatGPT queue EMPTY — refill from the
user's morning verdicts. USER DECISIONS pending: grid on/off + 20mm pitch (A/B in
BENCHMARKS.md), screen tests of everything above (checklists in the result files and
CLAUDE-LANE.md), deferred-path adoption, `.danslicer` extension, preset split tables,
island-search tweaks (still user-pending), the mirror-X test print.

Written 2026-09-03 for a fresh conversation and updated through the day; the FIRST section
supersedes all older state notes below. Read this, then `docs/DESIGN.md` for the full
design, then `docs/SUPPORT-GEOMETRY-SPEC.md` for the user's dictated support spec (updated
tonight: grid bases, no-shrink bases, mini-supports).

## Addendum 2026-09-03 ~night 2 — full overnight queue loaded (user: "keep as much
queued overnight as possible")

**main `49f41d1`→ (rolling), 381 green tests at last count.** Jobs 001–009 merged plus
five Qwen test tasks (~660 lines coverage; unattended pattern validated — hardened
briefs with embedded source and reference files). ChatGPT queue: 010 slice
integration (in flight), **011 project save/open** (user-approved tonight; DESIGN §10
zip container, CLI slice-a-project as end-to-end proof), 012 support presets (§8.1
minimal, recipes/regions explicitly excluded as user-pending), 013 keymap editing
(window-level gestures only), 014 tips performance (bit-identity gated, null result
allowed). Qwen batch 4 running: PhotonRle round-trip + ObjReader edge tests.
Deliberately NOT queued: viewport G-buffer pass (needs interactive screen checks),
anything touching the user's pending decisions (grid/pitch verdict, island tweaks,
recipes design).

**Claude implementation lane COMPLETE** (~02:30 2026-09-04, under the user's one-hour
budget): milestone 4 stages 1–5 implemented on `Danslicer-claude` branch
`viewport-quality` (5 commits, 460/460 green, independently verified per commit):
G-buffer, composite with studio/3 procedural MatCaps, cavity+outlines, ghosted/
overlay passes, FXAA — all in new Danslicer.Render files, ViewportControl touched by
2 lines, `RenderPath` Classic|Deferred default Classic with GL-failure latch-off
fallback. Stretch goals (ID picking, view cube) deliberately skipped. MERGE AFTER
ChatGPT's 017/018. Screen-test checklist in that worktree's untracked CLAUDE-LANE.md;
the deferred toggle needs the user's morning approval before any default flip. NOTE:
017/018's clip/waterline shaders apply to the CLASSIC path only — a known gap to
close if the user adopts Deferred. ALSO: **dual-GL-context PROBE PASS** confirmed on
screen (~02:15, user away) — preset editor's live preview renders alongside the main
viewport flawlessly; screenshot sent to the user.

**Qwen lane CLOSED for the night** (user shut down Unsloth ~01:00 2026-09-04 after
7 merged test files; the API at 127.0.0.1:8888 is DOWN — do not call it until the
user restarts Unsloth; driver scripts and the hardened-brief pattern live in
`Danslicer-qwen\Qwen\qwen_overnight.py`).

**Later that night the user EXTENDED the queue** (supersedes the older "do not brief
printer/resin editors" note): 012b preset editor with live 3D preview pane, sample
shapes incl. current selection, stats strip (user liked Claude's additions); 015
printer definitions + editor (hardcoded Mono X becomes a seeded, user-editable
collection); 016 resin presets + editor, with the PrintSettings
printer/resin/per-print field split proposed in the result file as an assumption.
Final order: 011 → 012 → 012b → 013 → 014 → 015 → 016.

## Addendum 2026-09-03 overnight — queue progress (kept current; RECOVERY: read this + the
`danslicer-multi-agent-workflow` memory, re-arm the queue monitor AND the hourly
heartbeat monitor, then resume the review/merge loop)

**main is at `95cb53a`, 324 green tests, pushed. Jobs 001–005 all merged tonight**, in
order: 001 refusal reasons (`e17e07c`), 002 mini classification — fallback-to-mini off
by default (`3985f34`), 003 optional base grid + A/B (`80f74fd`; spec amended
`3e8a86c` — grid now optional, default on; grid-off cuts Drogon refusals 1759→882),
002b island band hole (`c38a81c`), 004 mode-scoped UI + Support-panel settings
(`abd071c` — the user's mode-visibility directive is DONE), 005 branch shaping toward
the Lychee reference + 75° mini lean cap (`95cb53a`; Lychee mined: 174 bases median
1.44 mm spacing, trunks median 9.68 mm, branches median 5.36 mm ≤45°). **Job 006**
(support display modes) is IN FLIGHT — its first commit `830fbdb` is RED (its own
`ContactPointModeSelectsOnlyTipMarkers` fails, flagged in REVIEW.md as a completion
blocker). Claude merges autonomously overnight; user decisions pending in the morning:
grid on/off + pitch after screen test, mini/shaping defaults, Qwen lane start (user
deferred to "later this evening").

## Addendum 2026-09-03 ~21:30 — ChatGPT overnight queue LIVE

The overnight run is started for ChatGPT (Qwen joins later this evening, per the user).
ChatGPT now works as a Codex scheduled task: every ~5 min a FRESH chat claims the
lowest-numbered job from `Danslicer-chatgpt\ChatGPT\inbox\` (atomic move to `working\`,
results to `completed\`/`failed\`) per `ChatGPT\PROTOCOL.md`, which answers its setup
questions (inbox path, `NNN-slug.md` format with `Requires:`, 5-min interval,
completion = granular commits + clean build + full tests + result file). Queue loaded:
001 refusal reasons, 002 mini classification (brief 18), 003 grid optional (user
decision: pitch stays configurable AND grid gets an on/off toggle, both for screen
experimentation), 004 mode-scoped UI (brief 17) + ALL support configs also shown in the
right panel in Support mode "for now", 005 branch shaping toward the Lychee reference.
Claude merges autonomously overnight (standing grant), triages failed/stale claims,
refills the inbox. Monitor watches the queue folders + QUESTIONS + branch head.

## Addendum 2026-09-03 ~21:00 — brief 16 merged, user screen-test feedback

**main is at merge `89eca94`, 305 green tests, pushed** — ChatGPT's whole brief 16
(grid bases 20 mm pitch, branch-first config, mini-supports + island feed + CLI +
benchmarks) merged with the user's approval and rebuilt. Benchmarks: bases 315→17 /
134→17, refusals 629→1690 / 84→352 under 20 mm pitch + 8 mm branch reach — the user
has been shown these numbers; grid-pitch policy decision PENDING.

**User feedback from Lychee comparison (screenshots, 2026-09-03 evening):** added
`test files/drogon_collapse with Lychee supports.stl` (low-poly drogon with Lychee
supports, reference target for support shape; not in git). Branch X-crossing
"weirdness" still present; Lychee's tree shape (no grid, near-vertical trunks,
Y-merges) is the target look. Some tips look like minis in regular-support places →
brief 18 (`INSTRUCTIONS-18.md`, classification control; root cause: refused regular
candidates silently retry as minis). Queue order for ChatGPT: refusal-reason
micro-commit → brief 18 → brief 17 (`INSTRUCTIONS-17.md`, mode-scoped controls per the
user's Layout/Slicing visibility principle) → brief 19 (branch shaping toward the
Lychee reference, NOT WRITTEN — blocked on the user's grid decision).

**Support display modes** (user request; BRIEFED overnight as queue job 006 once the
queue drained) — show just contact points; just lines (the old line rendering); just
tips; transparent/ghost supports with or without contact points; plus Claude's
additions: per-element-type visibility toggles and (if cheap) dimmed supports outside
Support mode.

## State as of 2026-09-03 ~20:00 — READ THIS SECTION FIRST

**main is at `350cad0`, 297 green tests, pushed.** Merged today after the night section
below was written, in order: ChatGPT's brief 12 queue (workspace semantics, marquee fix,
text-box gesture yielding, mode panels); Claude's `support-geometry`
(tip/branch/trunk/base vocabulary, base disc/cone geometry, cone tip RENDERING,
TreeSupportRouter = spec-shaped generation now behind Ctrl+G and manual T, benchmarks);
Claude's `support-fixes` (Shift+T REMOVED per user, island coverage for teeth
— min island 0.1 + island tips exempt from spacing, base disc fitting, short tip-member
fallback); Claude's `support-ux-fixes` (marquee drawn by a SelectionRectOverlay ABOVE the
GL surface — the GL compositor hides the viewport's own 2D layer; Blender-style
drag-anywhere box select with click deferred to release; H hides WHOLE supports; base
disc picking; selection-only render-mesh rebuilds fixing a multi-second freeze);
ChatGPT's briefs 13+14 (support config window — 15 fields + base shape dropdown,
persisted, snapshot-isolated; dense-teeth routing: 30°/15° branch fans + 0.5 mm
same-trunk sibling fusion); and ChatGPT's brief 15 (six screen-test fixes: island config
default regression, marquee visibility via cached BVH, parent-aware tip taper — the
"branches ignore diameter setting" report, base-transition regression tests, bases
RELOCATE instead of shrinking, projected-disc base picking).

**Claude is supervisory-only** (user directive): briefs, reviews in a detached scratch
worktree (build + full tests per commit), merges, pushes. Tonight the user granted
autonomous merge authority for the overnight run ("merge as you see fit").

**OVERNIGHT PLAN — waiting for the user's explicit "start".** Keep ChatGPT AND Qwen busy;
monitor, review, merge autonomously. ChatGPT is on brief 16 (`INSTRUCTIONS-16.md`: grid
bases with configurable pitch, branch-first preference, MINI-SUPPORTS for teeth/barbs —
all new user dictation, recorded in the spec). Queue further briefs from the outstanding
pool as it finishes; keep granularity and the REPORT/REVIEW protocol. The Qwen lane is
validated and documented in the auto-memory (`danslicer-multi-agent-workflow`): worktree
`F:\Git Repos\Danslicer-qwen` branch `qwen-trial`, driver scripts in its git-excluded
`Qwen/` folder, author-rich/repair-minimal pattern, compile/test gates; give it SMALL
factorable tasks (test authoring, pure Core functions, diagnosis) and review everything.
API details + key in the memory file, not here.

**Start a persistent Monitor** polling ChatGPT's mailbox
(`F:\Git Repos\Danslicer-chatgpt\ChatGPT\` REPORT/QUESTIONS mtimes) and the
`grid-routing-prototype` head each minute; the old session's monitor dies with it.

**User decisions tonight (all in SUPPORT-GEOMETRY-SPEC.md):** bases on a configurable
grid (pitch default 20 mm — square/plate-aligned/nearest-first are ASSUMPTIONS, user
offered a Blender mock-up); branch-first before new trunks; bases never shrink (relocate);
DiscCone tops match their member; mini-supports = fine rods fanning from branch ends with
a max length; everything configurable. Island-search tweaks (multiple tips per island,
weakly-supported detection, tracking, IslandTipAt, painting) remain USER-PENDING — do not
brief.

**User-only outstanding:** screen-test briefs 15/16 when convenient (the running app is
main `350cad0` with drogon-lo); set their saved Min island area from 0.5 to 0.1 in
Preferences (deliberately not auto-migrated); the Blender grid mock-up if they choose;
the Reinforce visuals (needs profile UI); the physical mirror-X test print; re-running
`/auto-mode-setup`.

## State as of 2026-09-03 night (SUPERSEDED by the section above)

**main is at `6228f40`, 242 green tests, pushed** (with branches `grid-routing-prototype`
and `spacemouse-buttons`). Merged tonight with the user's approval: ChatGPT's whole
brief 10/9/8/11 queue (Shift+H whole-tree hide; model landing disabled by default —
honest refusals only, Drogon top-down 343/1113; `PenetrationDepth` as the single
embedding-depth name, default 0, CLI-exposed; the seated benchmark matrix in BENCHMARKS.md
that Grok never wrote — seated gripper top-down 5.9 s / 52 of 445 where unseated was killed
at 39 min; non-blocking batched generation with progress bar + cancel-rollback + one undo
step; Layout/Support/Slicing workspaces, mode-dependent Ctrl+A, marquee), and Claude's own
`spacemouse-buttons` branch (Claude implements again now — own worktree
`F:\Git Repos\Danslicer-claude`, never files in ChatGPT's queue).

**SpaceMouse buttons are DONE and accepted** — user: "leave the SpaceMouse for now, it has
all the functionality I require." `_IKeyboardEvents` COM sink in `TdxSpaceMouse` (GUID/
DISPIDs verified against the installed typelib, Advise probed live), drained in the
viewport poll. Fit=frame-all, T/R/F=views (codes 3/5/6 confirmed on hardware), Rotation=
device-only rotation lock in the status bar, device Esc=keyboard-Esc chain. Codes 31 and
32 were observed from unbound buttons on the user's unit and are recorded for any future
binding pass; Menu/1-4 stay unbound by choice.

**User screen-tested the merged queue.** Everything passes EXCEPT: the marquee draws
nothing (bug), and Ctrl+A inside a text box selected all supports instead of text (window
KeyBinding preemption). Both, plus a workspace-semantics redesign the user dictated, are
in **`ChatGPT/INSTRUCTIONS-12.md`** (the current brief): Support mode = objects neither
selectable nor movable; Layout mode = supports unselectable and rigidly carried by object
transforms (even if it ruins them); Tab cycles Layout→Support→Slicing (Slicing in the
cycle only when sliced, but the Slicing workspace itself is ALWAYS available); the right
panel loses its Object|Print tabs and becomes mode-specific (print controls live in
Slicing); left/right panels resizable; a select-through toggle for the marquee in the
Support panel; H hides selected supports in Support mode; hidden supports never
selectable. **Per-printer and per-resin settings editors are decided but explicitly
LATER** — do not brief yet.

**Island search rumination (user, tonight): already implemented** — the user's
slice-and-diff idea is exactly `MeshSlicer.NewbornIslands` + `IslandFinder` (polygon
difference with slope-aware inflation), feeding TipPlacer/PrintChecker/SupportAreaDetector,
configurable via `--min-island` / `--overhang` / `--layer`. Claude proposed tweaks, USER
DECISION PENDING, do not brief until they choose: multiple tips per large island
(`IslandAreaPerTipMm2`), weakly-supported detection (`MinSupportedFraction`), island
tracking through layers, `IslandTipAt` centroid|lowest|both, island painting in the
Support workspace.

**Start a persistent Monitor** in the new session polling ChatGPT's mailbox
(`F:\Git Repos\Danslicer-chatgpt\ChatGPT\` REPORT/QUESTIONS mtimes) and the
`grid-routing-prototype` head each minute; the old session's monitor dies with it.
Review protocol unchanged: detached scratch worktree, build + full tests, merge-test
against main, merge only with the user's per-merge approval, then push. ChatGPT's last
queue landed as three commits after the report — it was asked (REVIEW note) to commit
granularly this time.

**User-only outstanding:** screen-test brief 12 when it lands, the Reinforce visuals
(needs profile UI), the physical mirror-X test print, and re-running `/auto-mode-setup`
(broken earlier by a transient classifier outage).

## State as of 2026-09-03 late (SUPERSEDED by the section above)

**Grok is out of tokens and retired.** ChatGPT is the only implementing agent; it owns
both former lanes and the file-lock split is dissolved. **Claude's manager-only restriction
is relaxed** (the 5-hour usage window reset): Claude may implement again alongside managing
ChatGPT — pick work that cannot collide with ChatGPT's queue (different files/areas, own
branch, never ChatGPT's worktree), and keep reviewing/merging ChatGPT's commits as before
(detached scratch worktree: build + full tests, merge-test against main, then merge only
with the user's per-merge approval, then push).

**main is at `cab0d64` + this handover commit, 233 green tests, pushed.** Merged today,
in order: ChatGPT's brief 5+6 queue (Shift+H endpoint fix, visible+specific T-refusal
messages, per-tip routing refusal reasons ContactBlocked/NoClearStep/NoLanding/BelowPlate
with counts+coordinates in the route CLI, steep-contact neck departure along the outward
normal + deterministic 12-direction escape fan, padded steep model landings, single
Auto Drop toggle + offset box with config migration), then Grok's salvaged brief 5
(`--seat` CLI flag on all commands; cone/ball tip contact schema — `SupportTipShape`
capsule|cone, ConeLength, TipDiameter, BallDiameter, PenetrationDepth — frustum/sphere
slice sections, sphere collision primitives, defaults bit-identical; committed by Claude
after Grok died with it finished-but-uncommitted; one merge conflict in RouteCommand.cs's
JSON writer was resolved keeping Grok's dictionary form with ChatGPT's refusal fields).

**Benchmark trace (Drogon full-res, top-down, same 1113 tips):** unrouted 586 (baseline)
→ 434 (normal departure) → 302 (escape fan); ContactBlocked 364 → 44; NoClearStep 232 is
now dominant. The user added a 10× decimated fast-iteration model:
`test files\drogon collapse.stl` (`drogon-lo` in BENCHMARKS.md) — dev runs only, official
numbers stay on the full-res canonical pair.

**User decisions today (all recorded in `docs/SUPPORT-GEOMETRY-SPEC.md`):** support
anatomy base/trunk/branch/tip(+brace) is canonical vocabulary; tips get end diameter +
embedding depth; everything configurable, 45° defaults; recipes = complete geometry
bundles, user-editable; batched non-blocking generation with a progress bar above the
status bar, ONE undo step, cancel ROLLS BACK; **supports never land on the model for now**
(landing stays compiled but opt-in only — the padded-landing behaviour merged today put
blobs on Drogon's toes and is being disabled in brief 10).

**ChatGPT's queue (briefs in `F:\Git Repos\Danslicer-chatgpt\ChatGPT\`):**
1. `INSTRUCTIONS-10.md` — screen-test bugs: Shift+H must keep the whole connected support
   (`SupportGraph.Component`, bracing excluded), and disable model landing per the user
   decision above (expect Drogon unrouted to rise vs 302; accepted).
2. `INSTRUCTIONS-9.md` — Grok-lane handover: reconcile the user's embedding depth with
   Grok's `PenetrationDepth` (one concept, one name; cone continues past the contact),
   plus write the seated benchmark section Grok CLAIMED but never wrote (its REPORT lies
   about this — audit finding; run `--seat` matrix on both full-res models).
3. `INSTRUCTIONS-8.md` (non-blocking generation) and `INSTRUCTIONS-11.md` (Layout/Support/
   Slicing window modes, mode-dependent Ctrl+A = objects/supports/nothing, marquee box
   selection for supports) — ChatGPT picks the order, both touch MainWindow.

ChatGPT protocol notes: it once committed `ChatGPT/REPORT.md` into its branch (mailbox
files must stay untracked; strip at merge if it happens again — it was told to
`git rm --cached` it). Reviews go newest-at-top in `ChatGPT/REVIEW.md`; answers to
questions live there. Grok's worktree (`F:\Git Repos\Danslicer-grok`) is kept for
reference only (its REPORT/QUESTIONS/REVIEW hold the tip-geometry context) — nothing
runs there any more.

**Start a persistent Monitor** in the new session polling ChatGPT's mailbox
(REPORT/QUESTIONS mtimes) and `grid-routing-prototype`'s head each minute. The old
session's monitor is stopped.

**User-only outstanding:** screen-test after brief 10 lands (Shift+H whole-tree, no more
toe blobs, teeth T behaviour), the Reinforce visuals (needs profile UI), and the physical
mirror-X test print. A transient Anthropic classifier outage today also broke the user's
`/auto-mode-setup` runs — suggest re-running it when convenient.

## Multi-agent phase, state as of 2026-09-03 evening (SUPERSEDED by the section above)

Three AI agents work this repo in parallel; **Claude is manager/integrator ONLY** (user
directive after hitting a usage limit: no implementation, only briefs, reviews, merges, and
asking the user to test). ChatGPT and Grok run as local CLIs with file access.

**Mechanics.** Each agent has its own git worktree and branch: ChatGPT in
`F:\Git Repos\Danslicer-chatgpt` on `grid-routing-prototype`, Grok in
`F:\Git Repos\Danslicer-grok` on `region-generation`. Mailbox folders (`ChatGPT/`, `Grok/` in
each worktree, git-excluded via `.git/info/exclude`) hold numbered briefs
(`INSTRUCTIONS[-N].md`), the agent's `REPORT.md`/`QUESTIONS.md`, and Claude's `REVIEW.md`
(newest review at top; answers to their questions live there too). Agents commit to their
branch only, never launch the app or touch the screen (the user tests), never push. Claude
reviews every commit in a detached scratch worktree (build + full tests in isolation), then
— with the user's approval per merge, which they have granted promptly each time — merges to
`main` and pushes. `main` is at 212 green tests with both agents' brief 4 merged. Start a
persistent Monitor polling both mailboxes and branch heads (stat REPORT/QUESTIONS mtimes +
`rev-parse HEAD` each minute); a fresh session must restart it.

**Merged and working (headless-verified unless noted):** support capsule rendering
(user-verified on screen), config window with colour pickers/numeric boxes/window persistence
(user-verified), calibrated SpaceMouse (user's feel = multiplier 1.0, all axes inverted in
config), plate fade from below, configurable overhang checker (two solid colours + cell size),
support delete residue pruning, full generation stack (tip placement with derived-mesh-data
cache and BVH, grid + top-down routing, growth rules incl. attach-to-existing / keep-clean /
Reinforce with ring re-projection / Land, bracing stage, BVH + linear + composite collision
scenes), print checks (suction cups, proximity, islands, bounds), `SupportGenerator` pipeline
with grid wiring, T routes manual supports around the model (Shift+T = blind override),
auto-placement after transform commits (user-verified: Drop/Raise/Off header controls, one
undo step), Generate Supports (Ctrl+G, user-verified working), Shift+H hide-unselected
supports (buggy, see below), `MeshSlicer.LayerPolygons`/`NewbornIslands`, support-area
auto-detection (`SupportAreaDetector`), CLI: `tips` / `route` / `checks` / `areas`, and
`docs/BENCHMARKS.md` — the brief-4 baseline on the two canonical models.

**In flight (briefs written, agents working):**

- **ChatGPT** — order: brief 6 item 1 first (bug: Shift+H hides selected supports too; root
  cause diagnosed in the brief — `SupportRenderMesh.Build` skips segments whose endpoint
  nodes are hidden, and hide-unselected hides a selected segment's own endpoints), then
  brief 5 (user-reported T-routing bug: refusal message is clobbered by `UpdateStatus()` at
  the end of `OnKeyDown`, so refusals are silent; router needs per-tip refusal reasons
  plumbed to App + CLI, a concave-pocket repro fixture, then a deterministic fix — wider
  blocked-step search / padded steep landings / neck-clearance rethink), then brief 6 item 2
  (replace the three placement buttons with one "Auto Drop" toggle + offset edit box).
- **Grok** — brief 5: `--seat` CLI flag (drop lowest point to Z=0, centre XY) and a seated
  benchmark section in BENCHMARKS.md; then support tip contact geometry per the user's
  2026-09-03 spec: cone tips (base = support diameter, configurable contact diameter),
  optional snap-off ball at the contact, additive schema on `SupportNode` +
  `SupportSliceGeometry` (those two files are unlocked additively for this), defaults
  bit-identical to today. Rendering the new shapes is a future App-lane brief.

**Benchmark findings that drive current briefs:** 41% (grid) / 53% (top-down) of auto tips
fail to route on Drogon — the user's T-refusal bug at scale; top-down on the unseated
gripper (raw CAD coords ~3 km up) was killed at 39 min — hence `--seat`.

**User decisions 2026-09-03 evening:** generation-quality tuning (densities, what looks
right) is deferred until supports have real printable geometry and profiles; tip cone/ball
geometry is the prerequisite and is briefed now. Placement UI becomes a single Auto Drop
toggle + offset box.

**User has NOT yet screen-tested:** the T-routing refusal message and Shift+T override
(blocked on ChatGPT brief 5), Reinforce visuals (no profile UI yet). The physical test print
(mirror-X) remains user-only.

## Overnight session 2026-09-03 (while the user slept)

The user approved commit/merge/push and full autonomy mid-session. Everything below is **merged to
`main` and pushed** to the private repo `https://github.com/danilius/Danslicer` (created with the
user's gh credentials; all feature branches pushed too, old milestone branches deleted locally).

1. **Slicing stack-overflow fix** — the crash on large files: `stackalloc` inside the per-triangle
   loop in `MeshSlicer.CollectSegments` blew the 1 MB worker stack (0xC00000FD). Hoisted. Both
   ~1M-triangle test STLs slice (53 MB knocker dragon 18.6 s, 47 MB Drogon 5.1 s).
2. **OBJ import** — reader (v/f, v/vt/vn, negative indices, fan triangulation), `MeshFile`
   dispatch, picker/CLI/argument import; `ImportStl` renamed `ImportMesh`. Verified with the
   roof gripper.
3. **Lay flat on face** — F over a face (or Object menu, then click): picked triangle grows into a
   ≤3° cluster, model rotates face-down and rests exactly on the plate, one undo step. Verified on
   screen (Viper shell 180°, gripper fin compound rotation).
4. **Build-volume check** — slicing now refuses geometry past the plate in X/Y (it was silently
   cropped), like the existing Z checks; CLI reports it cleanly with exit 2.
5. **SpaceMouse (milestone 3 core)** — `ISixAxisInput` + `TdxSpaceMouse` late-bound 3DxWare COM
   backend, 66 Hz UI-thread polling, twist/tilt→orbit, slide→pan, push→zoom, roll locked; status
   bar shows "SpaceMouse". Connects to the real driver. **Motion signs/sensitivity await the
   user's hands**; constants in `ViewportControl` (SpaceMouseOrbitPixels etc.). Buttons and HID
   fallback not done.
6. **Overhang tint (milestone 4 start)** — Object > Overhang Tint colours faces past 45° from
   vertical, yellow at threshold to red on flat undersides, via world-space normals in the mesh
   shader. Verified on a table-shaped test mesh. Also fixed a latent bug: `MenuItem.IsChecked`
   binds one-way by default, so ALL menu checkboxes (gizmos, snapping) never wrote to the view
   model; now `Mode=TwoWay`.
7. **Hide/unhide** — H hides selection (deselects, undoable), Alt+H unhides all; menu items.
8. **Support graph foundation (milestone 5 start)** — `Supports/SupportGraph.cs`: typed nodes and
   segments with origin tags, pinned/hidden/disabled, referential integrity, component queries
   (bracing excluded = one support), unpinned-elements-of-region for regeneration; and
   `SupportSliceGeometry.cs`: analytic capsule cross-sections per layer (ellipse body + cap
   circles, 64-gon in Clipper units), `SectionsAt(graph, z)` ready to union into the slicer.
   Not yet wired into `Slicer.Slice` or any UI.

Also: rotate and scale gizmo drags verified on screen (the outstanding debt from the last session);
gizmo rotate drag committed "Rotate", scale drag committed "Scale", undo clean.

Verification notes for this machine: after the Alt-key foreground trick, press Escape before
typing — the menu bar is armed and F opens File. PowerShell tool calls don't share state; redefine
Add-Type classes with fresh names per call.

9. **Support slicing** — `Slicer.Slice` takes an optional graph; capsule sections union into every
   layer, print height extends to the tallest cap, `Document.Supports` exists and the app passes it.
10. **Manual supports** — press T over the model: a vertical tip-neck-pillar-base tree drops from
   the picked surface point to the plate, one undo step, drawn as depth-tested coloured lines
   (necks yellow, pillars blue, bracing green, tips orange crosses). Hidden elements skip drawing
   but still slice; disabled ones fade and don't slice. Verified on screen.

11. **Support selection, deletion, tip move** — click selects a support element (screen-space pick,
   depth-arbitrated against the model; Shift toggles; selected draws white); Delete removes it
   undoably (a node takes its segments); Esc clears support selection before object selection.
   G with one selected tip drags it along its contact object's surface, re-dropping the simple
   vertical tree live; LMB/Enter commits "Move tip", RMB/Esc cancels. All verified on screen.

Next candidates: whole-support selection (pick the tree, not the element); Shift+H hide unselected
on support elements; SpaceMouse buttons via COM connection points; SpaceMouse sign/sensitivity
tuning with the user; support render meshes (capsules, not lines); region painting and generation
(milestone 6); the test print (mirror-X question) is still the one thing only the user can do.
The 78-test suite is green on `main`.

**Auto-drop to plate (user request 2026-09-03, not yet built).** After any object transform
commit (move, rotate or scale; modal, gizmo or numeric), the object should re-seat on the plate
using the lowest point of the mesh in its current orientation. Three modes:

1. *Auto-drop* (the default): lowest point lands exactly on Z = 0 after every transform.
2. *Raise above plate*: like auto-drop but the lowest point lands at a user-entered height
   (edit box for the distance).
3. *Off*: the object stays wherever it is put (today's behaviour).

**Canonical support benchmarks (user decision 2026-09-03).** `test files/Drogon_flat_surface.stl`
(organic extreme, ~47 MB) and `test files/roof gripper T2.obj` (CAD extreme) are the definitive
test models for support work. Every significant support feature should be exercised against both
via the CLI (`tips`, `route`, `checks`) and the numbers recorded; they are deliberately NOT in
git (large binaries), they live in the repo directory on the user's machine.

**Support "recipes" (user rumination 2026-09-03, later stage).** Largely DESIGN.md's existing
profiles (§8.1: named parameter sets, "CAD clean" vs "organic dense") plus regions (§8.3), with
three genuinely new elements worth designing when profiles land:

1. *Auto-detection painting*: a tool that reads the model, finds areas needing support, and
   paints each detected area a distinct random colour; the user then selects an area and applies
   a recipe to it. (Overhang/island analysis from tip placement already computes the raw data.)
2. *Modifier-style attachment*: a recipe attached to an auto-detected area the way Blender
   modifiers attach — non-destructive, re-evaluated, per-area — rather than a one-shot apply.
3. *Import/export*: recipes as shareable files so users can exchange them.

Different recipes within one model type (several CAD recipes for different problems) should be
normal, not an edge case.

Design notes (user decision 2026-09-03): the drop applies after ANY transform commit,
including a G/Z move — the move happens exactly per the user's input, then the object snaps
back to the correct level; rotation likewise commits first, then drops. Never suppress an
axis. Ctrl+D (Drop to Plate) remains as the manual one-shot. The drop must be part of the
same undo step as the transform that triggered it.

## What this is

Danslicer is a resin (MSLA) slicer for power users, targeting the Anycubic Photon Mono X first.
C# on .NET 10, Avalonia 12 UI, Silk.NET OpenGL viewport, Clipper2 for polygons. The design
document is the source of truth for architecture, the support system and the milestone order.

## Repository state

- `main` holds milestones one and two, merged and clean.
- Branch `gizmos-and-layer-view` holds the gizmo and main-area layer view work, complete and verified, awaiting the merge decision (see below).
- Older branches `milestone-1-skeleton`, `numpad-emulation`, `milestone-2-first-print` are merged and can be deleted.

Solution `Danslicer.slnx` with projects:

| Project | Purpose |
| --- | --- |
| `src/Danslicer.Core` | Mesh, STL reader, scene, transforms, document and undo stack, expression parser, slicing, `.pwmx` writer and reader. No UI or GPU dependencies. |
| `src/Danslicer.Render` | OpenGL renderer, camera, shaders, line overlay batch. |
| `src/Danslicer.App` | Avalonia application: viewport control, modal transforms, view models, window. |
| `src/Danslicer.Cli` | `info`, `slice`, `inspect` commands over Core. |
| `tests/Danslicer.Tests` | xUnit, 50 tests, all passing at the last commit on `main`. |

Build and test:

```bash
dotnet build -c Debug
dotnet test --no-build
dotnet run --project src/Danslicer.App -- path/to/model.stl
dotnet run --project src/Danslicer.Cli -- slice model.stl -o out.pwmx
```

## Done so far

**Milestone one.** STL import (binary and ASCII), scene objects with transforms, document with
command stack and undo, studio-lit flat-shaded viewport with build plate and grid, turntable camera
with perspective and orthographic modes, click selection, Blender-style G/R/S modal transforms with
X/Y/Z axis and Shift-plane constraints and typed numeric values, expression fields with units in the
properties panel, numpad emulation on the digit row.

**Milestone two.** Mesh slicing to oriented contours, Clipper2 union and XY compensation, scanline
rasteriser with coverage anti-aliasing quantised to 16 grey levels, pw0Img run-length codec, parallel
slicing with layers kept RLE-encoded, Photon Workshop format version 516 writer plus a validation
reader, print settings panel, Slice (Ctrl+R), Export (Ctrl+E), layer preview.

**Not yet done from milestone two:** the actual test print. The writer mirrors layer images in X
because the published Mono X profiles do, and only a print of an asymmetric part confirms it. An
L-shaped test part was given to the user. If the print is mirrored, flip `MirrorX` in
`PrinterDefinition.PhotonMonoX`.

## Branch `gizmos-and-layer-view` (complete, awaiting the user's merge decision)

User requests from 2026-09-03 that drove this branch:

1. The main window area is used for positioning, support generation, slicing and layer preview, not
   the side panel. The centre now has a header strip and two modes: the 3D model view and the 2D layer
   view. `Tab` toggles, slicing switches to layers, any geometry change drops the stale slice and returns
   to the model view. The right panel keeps only object and print settings.
2. Visual gizmos for move, rotate and scale with two modes, free and snapping.
3. The pivot for move, rotate and scale is the centre of the selection's bounding box.

Implementation:

- `src/Danslicer.App/Editing/ModalTransform.cs`: pivot is the selection bounding-box centre; `Snap`
  flag with `MoveStep` 1 mm, `RotateStepDegrees` 5, `ScaleStep` 0.1; `Begin` takes an optional axis and
  plane so a gizmo handle can start it directly.
- `src/Danslicer.App/Editing/Gizmo.cs`: geometry, hit testing and overlay lines for move arrows and plane
  squares, rotation rings, scale cubes and a uniform-scale circle. Constant 90 px on screen. Hovered
  handle highlights yellow; a drag hands off to the modal transform and confirms on release.
- `src/Danslicer.App/Controls/ViewportControl.cs`: gizmo toggles and snap as styled properties bound to
  the view model, hover hit testing, drag handling, Ctrl inverts snapping while held, Shift+Tab toggles
  it, Tab raises `ToggleViewRequested`.
- `src/Danslicer.App/Controls/LayerPreviewControl.cs`: 2D layer view with zoom about the cursor, pan,
  Ctrl+wheel and Page Up/Down to step layers, Home to fit, Tab to toggle view.
- `MainViewModel`: `ViewMode`, gizmo and snap toggles, full-resolution `Gray8` layer bitmap.
- `MainWindow.axaml`: header strip with gizmo toggles, snap toggle and view switch; layer slider under
  the layer view; Object menu gains gizmo and snapping toggles.

Verified on screen with synthetic input: X-arrow drag moved the box with the live readout and axis
guide and committed one undo step; snapping toggled from the header; slicing switched to the layer
view and Tab switched back; a move after slicing invalidated the slice and disabled the Layers button.
Rotate and scale gizmo drags were not exercised by synthetic input; the user offered to test on screen.

Next after this branch: milestone three, SpaceMouse Pro via the 3DxWare COM interface.

## How to verify UI changes on this machine

Synthetic input from PowerShell works but has traps, recorded in memory as well:

- Force the window to the foreground with the Alt-key trick (`keybd_event 0x12` down and up) before
  `SetForegroundWindow`, and only send keys after `GetForegroundWindow()` equals the window handle.
  Otherwise keystrokes land in whatever window is in front, which has been the Claude Code app.
- Window position varies per launch, so derive click points from `GetWindowRect`. Launch with
  `-WindowStyle Maximized` and `ShowWindow(h, 3)` for predictable coordinates.
- Click inside the viewport before typing so it has keyboard focus.
- Set `DANSLICER_TRACE=1` and run `dotnet Danslicer.App.dll` with stderr redirected to log pointer and
  key events from the viewport.
- Kill the app before rebuilding; the exe and dll are locked while it runs.
- Capture the screen with `System.Drawing.Graphics.CopyFromScreen`, then view the PNG.
- Test meshes are generated by small Python scripts into the scratchpad, not stored in the repo. A torus
  plus box scene is the usual test.

## Decisions and lessons

- Avalonia `OpenGlControlBase` is invisible to hit testing until `Render` fills its bounds with a
  transparent brush. Without that the viewport gets no pointer events.
- `MenuItem.HotKey` only registers once its sub-menu has been opened. All shortcuts are explicit
  `Window.KeyBindings`, with import and export bindings added in code-behind.
- Quaternion composition uses `Quaternion.Concatenate(first, second)` throughout, verified by a test.
  Euler order is X then Y then Z.
- Contour orientation comes from face normals: walking along cross(+Z, normal) keeps the solid on the
  left, giving counter-clockwise outers and clockwise holes.
- The rasteriser writes 16 grey levels so the layer table's lit-pixel count matches the encoded image.
- Avalonia 12 is used, not 11 as first planned, because that is what the templates produce.
- UVtools is AGPL. It was read as a format specification only; nothing was copied or linked.

## User preferences

- Blender conventions everywhere. "Blender, not Word." No simplified mode, no wizards.
- Feature branch for every piece of work, named for the work. Merging is always the user's call:
  propose it when a branch is verified, never merge unprompted.
- Keyboard has no numpad: every numpad binding must exist on the digit row too.
- SpaceMouse Pro with 3DxWare installed. SpaceMouse support is milestone three, next after this branch.
- The user is willing to test on screen when asked and will do the real print later.
