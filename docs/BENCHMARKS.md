# Support-generation benchmarks

Canonical models (user decision 2026-09-03). They are large binaries and **must not** be copied
into this repo. Read them from these paths on the machine:

| Key | Path | Kind | Triangles | AABB min (mm) | Size (mm) |
| --- | --- | --- | ---: | --- | --- |
| drogon | `F:\Git Repos\Danslicer\test files\Drogon_flat_surface.stl` | organic extreme | 943,666 | −67.35, −70.00, 0.56 | 134.70 × 140.00 × 52.81 |
| gripper | `F:\Git Repos\Danslicer\test files\roof gripper T2.obj` | CAD extreme | 34,800 | 291.68, 1180.14, 3068.38 | 115.35 × 111.08 × 90.96 |
| drogon-lo | `F:\Git Repos\Danslicer\test files\drogon collapse.stl` | organic, fast iteration | 94,366 | −67.36, −69.96, 0.55 | 134.73 × 139.93 × 52.86 |

`drogon-lo` (added by the user 2026-09-03) is a 10× decimation of drogon with the same
footprint and the same 0.55 mm plate offset. Use it for development iteration — repeated
`tips`/`route` runs while working on a fix. It is NOT a regression reference: official
before/after numbers are recorded on `drogon` and `gripper` only.

Photon Mono X plate is 192 × 120 × 245 mm, origin-centred in X/Y, Z from 0.

These two files are the definitive regression reference for support work. Later runs append a
new dated section with the **same table columns** so numbers stay comparable. Do not rewrite
old rows.

## How to rerun

Debug CLI (`dotnet build -c Debug`). Run the canonical seated tree-routing matrix with one
command:

```powershell
$cli = "src/Danslicer.Cli/bin/Debug/net10.0/Danslicer.Cli.dll"
dotnet $cli bench --output benchmark-summary.json
```

`bench` generates fresh seated tips once for each canonical model, routes those candidates with
the base grid both on and off, writes one structured JSON summary to `--output`, and prints a
ready-to-paste table using the Results columns below. The JSON and table both include wall times,
subcommand exit codes, candidate/topology counts, refusal breakdowns, maximum lean and collision
status. Override either machine-local model path with `--drogon <path>` or `--gripper <path>`.
Without `--output`, JSON is written to stdout and the table to stderr so stdout can be redirected
directly to a machine-readable file.

For individual probes, `route` accepts `tips --json` output directly (an object with `candidates`)
as well as the older JSON array:

```
$cli = src/Danslicer.Cli/bin/Debug/net10.0/Danslicer.Cli.dll
$drogon = "F:\Git Repos\Danslicer\test files\Drogon_flat_surface.stl"
$gripper = "F:\Git Repos\Danslicer\test files\roof gripper T2.obj"

dotnet $cli tips   $drogon --json
dotnet $cli route  $drogon --tips <tips.json> --strategy grid --json
dotnet $cli route  $drogon --tips <tips.json> --strategy topdown --json
dotnet $cli checks $drogon --json
dotnet $cli areas  $drogon --json

dotnet $cli tips   $gripper --json
dotnet $cli tips   $gripper --json --edge 1
dotnet $cli route  $gripper --tips <tips.json> --strategy grid --json
dotnet $cli route  $gripper --tips <tips.json> --strategy topdown --json
dotnet $cli checks $gripper --json
dotnet $cli areas  $gripper --json
```

Defaults: layer 0.05 mm, overhang 45°, spacing 2.5 mm (tips) / 5 mm (grid lattice), min-island
0.5 mm². `route` exit 2 means unrouted tips or a collision.

---

## 2026-09-03 — `region-generation` brief 4 baseline

- Branch: `region-generation` (brief 4: LayerPolygons helper, generator grid wiring, `areas` CLI).
- Config: Debug, net10.0.
- Machine: AMD Ryzen 9 7950X, 64 GB, Windows. Single-process, no other CLI jobs.
- Models read from the paths above; not copied.

### Results

| Model | Command | Flags | Wall s | Exit | Counts | Notes |
| --- | --- | ---: | ---: | ---: | --- | --- |
| drogon | `tips` | defaults `--json` | 26.467 | 0 | **1113** candidates (Island 77, LocalMinimum 224, Corner 281, Edge 198, Overhang 333). Spacing min 2.500 / median 2.627 / mean 2.810 | Mesh sits 0.56 mm off the plate, so the first solid layers are islands. |
| drogon | `route` | `--strategy grid --json` (fed `tips --json`) | 8.053 | 2 | nodes 1544, segs 1308 (neck 654, pillar 58, trunk 596, brace 0), **unrouted 459 / 1113**, bases 236, max lean 35.0°, collisionFree **false** | Poisson tips vs lattice: 41% fail to snap/route. |
| drogon | `route` | `--strategy topdown --json` (same tips) | 10.793 | 2 | nodes 5102, segs 4585 (neck 527, pillar 4029, trunk 29, brace 0), **unrouted 586 / 1113**, bases 517, max lean 45.0°, collisionFree **false** | More elements, still 53% unrouted. |
| drogon | `checks` | defaults `--json` | 22.996 | 0 | **172** findings (Island 171, OutsideVolume 1) | Y size 140 mm > 120 mm plate. Island count ≠ placement (77): checks report every newborn polygon, placement drops failed projections / spacing. |
| drogon | `areas` | defaults `--json` | 16.414 | 0 | **410** areas, 2653 mm² (High 157, Medium 199, Low 54) | Organic: many small patches. Largest area 5752 faces / 412 mm². |
| gripper | `tips` | defaults `--json` | 3.057 | 0 | **444** candidates (Island 37, Overhang 296, Edge 111). Spacing min 2.501 / median 2.822 / mean 3.079 | Tips at z ≈ 3093 mm. Model is raw CAD coordinates, not on the plate. |
| gripper | `tips` | `--edge 1 --json` | 3.073 | 0 | **600** candidates (Island 37, Edge 562, Overhang 1). Spacing min 2.502 / median 2.538 / mean 2.778 | Edge preference replaces interior overhang as intended; islands unchanged. |
| gripper | `route` | `--strategy grid --json` (fed default tips) | 0.391 | 2 | nodes 1020, segs 786 (neck 393, pillar 108, trunk 285, brace 0), **unrouted 51 / 444**, bases 234, max lean 35.0°, collisionFree **false** | Bases land at z = 0 with XY still around (300, 1235): ~3 m pillars. |
| gripper | `route` | `--strategy topdown --json` (same tips) | **2330** | −1 | **no output** — process killed after 38.8 min of one-core CPU (~2300 s), 188 MB RSS | Worst hotspot. 444 tips × ~3 m of 2 mm steps against a growing collision scene. |
| gripper | `checks` | defaults `--json` | 4.077 | 0 | **73** findings (Island 72, OutsideVolume 1) | First island at layer **61375** (z 3068.78). Layer stack starts at z = 0 → 63k layers, most empty. Far outside volume in X, Y and Z. |
| gripper | `areas` | defaults `--json` | 2.763 | 0 | **54** areas, 4053 mm² (High 14, Low 40) | CAD patches stay separate (good). Several High areas are single 544 mm² triangles at 33° overhang, included only because the whole part is an island. |

### Observations (screen-less)

1. **Neither model is a seated print.** Drogon is 0.56 mm off the plate and 20 mm too wide in Y
   for the Mono X. The gripper is a CAD dump at (≈350, 1240, 3090) mm — three metres off the
   plate and well outside the 192 × 120 × 245 mm volume. Counts are still the regression
   reference for *these files as stored*; they are not a picture of a printable layout.
2. **Grid routing of Poisson tips is a mismatch.** Drogon unrouted 459/1113; that is the
   generator-wiring motivation (tips should already sit on lattice verticals when the strategy
   is grid). This CLI path still does `tips` then `route` as two stages, so the baseline
   records the un-wired behaviour.
3. **Top-down on the unseated gripper is the worst hotspot** and did not finish. Grid on the
   same tips returned in 0.4 s because it drops a vertical to z = 0 with almost no collision
   work in empty space. Top-down walks every millimetre.
4. **Island inflation vs placement.** `checks` islands (171 / 72) ≫ `tips` Island strategy
   (77 / 37). Same `IslandFinder`; placement then projects onto downward faces and applies
   spacing, so many polygons never become a tip.
5. **`areas` grain.** Drogon 410 areas (organic, sharp-edge splits) vs gripper 54 (CAD
   patches). Gripper High-severity areas include 33° faces that are only “must support”
   because the mesh is airborne. After a drop-to-plate those should vanish.
6. **No suction-cup findings** on either model at defaults. Not absurd; Drogon is an open
   organic shell, the gripper is a mechanical part in empty space.
7. **Spacing on tips looks healthy** (min ≈ 2.50 mm on both). No stacked-on-the-same-point
   bug in the candidate list.

### Hot spots (for the next pass)

| Rank | Where | Evidence |
| --- | --- | --- |
| 1 | Top-down routing of long drops | gripper `route --strategy topdown`: killed at 38.8 min, 0 bytes out |
| 2 | Layer stack from z = 0 when `MinZ` ≫ 0 | gripper checks/areas walk ~63k layers (first solid at 61375); still cheap when empty, but the index is absurd |
| 3 | Grid routing of off-lattice tips | drogon grid: 459 unrouted, collisionFree false |
| 4 | Organic area fragmentation | drogon 410 areas / 2653 mm² |

---

## 2026-09-03 — seated canonical matrix, model landing disabled

- Branch: `grid-routing-prototype` after merging `main` at `20c41f`.
- Config: Debug, net10.0; same machine and single-process conditions as the baseline.
- Every command below used `--seat`. `tips --seat` supplied the translated candidate JSON to
  `route --seat`; route translates the mesh only, so translating the tips again would double-shift
  them.
- Model landing was disabled in both default top-down entry points. The landing rule remains
  available only to tests/future profiles that opt in explicitly.

### Results

| Model | Command | Flags | Wall s | Exit | Counts | Notes |
| --- | --- | ---: | ---: | ---: | --- | --- |
| drogon | `tips` | `--seat --json` | 29.113 | 0 | **1087** candidates (Island 59, LocalMinimum 193, Corner 282, Edge 210, Overhang 343). Spacing min 2.500 / median 2.629 / mean 2.817 | Seat offset (0, 0, −0.561) removes the false first-layer islands. |
| drogon | `route` | `--seat --strategy grid --json` | 8.396 | 2 | nodes 1546, segs 1312 (neck 656, pillar 54, trunk 602, brace 0), **unrouted 431 / 1087**, bases 234, max lean 35.0°, collisionFree **false** | All refusals are NoClearStep; NoLanding is 0. |
| drogon | `route` | `--seat --strategy topdown --json` | 20.266 | 2 | nodes 7814, segs 7091 (neck 747, pillar 6242, trunk 102, brace 0), **unrouted 340 / 1087**, bases 723, max lean 89.4°, collisionFree **false** | Refusals: ContactBlocked 21, NoClearStep 319, NoLanding 0. Every accepted support reaches the plate. |
| drogon | `checks` | `--seat --json` | 25.719 | 0 | **152** findings (Island 151, OutsideVolume 1) | Still 20 mm wider than the Mono X Y extent. |
| drogon | `areas` | `--seat --json` | 19.028 | 0 | **426** areas, 2293 mm² (High 170, Medium 202, Low 54) | Largest area 5754 faces / 412 mm². |
| gripper | `tips` | `--seat --json` | 3.049 | 0 | **445** candidates (Island 36, Edge 113, Overhang 296). Spacing min 2.501 / median 2.812 / mean 3.094 | Seat offset (−349.351, −1235.674, −3068.376). |
| gripper | `tips` | `--seat --edge 1 --json` | 3.168 | 0 | **604** candidates (Island 36, Edge 566, Overhang 2). Spacing min 2.502 / median 2.539 / mean 2.769 | Edge preference remains deterministic after seating. |
| gripper | `route` | `--seat --strategy grid --json` | 0.460 | 2 | nodes 1007, segs 780 (neck 390, pillar 99, trunk 291, brace 0), **unrouted 55 / 445**, bases 227, max lean 35.0°, collisionFree **true** | All 55 refusals are NoClearStep; NoLanding is 0. |
| gripper | `route` | `--seat --strategy topdown --json` | 5.909 | 2 | nodes 7142, segs 6750 (neck 393, pillar 6340, trunk 17, brace 0), **unrouted 52 / 445**, bases 392, max lean 56.5°, collisionFree **true** | The seated run finishes in seconds instead of the unseated 38.8-minute kill. NoLanding is 0. |
| gripper | `checks` | `--seat --json` | 4.171 | 0 | **53** findings (Island 53) | No OutsideVolume finding after centring and seating. |
| gripper | `areas` | `--seat --json` | 2.865 | 0 | **53** areas, 3508 mm² (High 13, Low 40) | Largest area remains one 545 mm² CAD triangle. |

### Landing-off comparison on the unchanged Drogon input

For an apples-to-apples comparison with the landing-enabled 302 / 1113 result, the unseated
Drogon was also rerun with the same 1113-tip default JSON. Top-down completed in 19.970 s with
**343 / 1113 unrouted**: ContactBlocked 24, NoClearStep 319, NoLanding 0. Disabling model
landing therefore adds 41 honest refusals (+3.7 percentage points) instead of terminating those
supports on the model. Nodes rose to 8009 and segments to 7262 because every accepted route now
continues to a plate base (747 bases).

---

## 2026-09-03 — tree strategy (spec anatomy), seated canonical pair

- Branch: `support-geometry` (TreeSupportRouter: base/trunk/branch/tip per
  docs/SUPPORT-GEOMETRY-SPEC.md; supports never land on the model).
- Config: Debug, net10.0; same machine and single-process conditions as the baseline.
- Same seated tips as the seated matrix (`tips --seat --json`: drogon 1087, gripper 445 —
  counts reproduced exactly). `route --seat --strategy tree --json`, all defaults
  (trunk/branch Ø 1.2, member angle 45°, tip member 2 mm, max branch 8 mm, disc bases Ø 4).

### Results

| Model | Command | Flags | Wall s | Exit | Counts | Notes |
| --- | --- | ---: | ---: | ---: | --- | --- |
| drogon | `route` | `--seat --strategy tree --json` | 11.192 | 2 | nodes 2062, segs 1753 (tip 691, branch 443, trunk 619, brace 0), **unrouted 396 / 1087**, bases 309, max lean 45.0°, collisionFree **true** | Refusals: ContactBlocked 113, NoClearStep 283, NoLanding 0. First collision-free Drogon run on any strategy. 691 tips share 309 trunks. |
| gripper | `route` | `--seat --strategy tree --json` | 0.654 | 2 | nodes 1183, segs 1014 (tip 393, branch 231, trunk 390, brace 0), **unrouted 52 / 445**, bases 169, max lean 45.0°, collisionFree **true** | Refusals: ContactBlocked 1, NoClearStep 51 — the same 52-count as seated top-down, at a ninth of the wall time. 393 tips share 169 trunks. |

### Observations

1. **Anatomy is exactly the spec**: every member is a tip, branch or trunk; trunks are
   vertical; max lean is exactly the 45° member angle on both models (the step routers
   recorded 89.4° and 56.5°).
2. **Collision-free on both models** — the step routers never achieved that on Drogon.
   Element counts are a third of top-down's (1753 vs 7091 segments on Drogon) because one
   trunk replaces dozens of 2 mm steps, which is also where the wall-time win comes from.
3. **Refusal trade**: seated Drogon tree refuses 396 vs top-down's 340. The tree shape is
   deliberately more rigid (no per-step detouring); ContactBlocked rose 21 → 113 because
   the tip member insists on the clamped-normal departure fan rather than top-down's wider
   escape search. Candidates for a later pass, recorded not briefed: a second branch level,
   and reusing top-down's short-departure fallback for rough contacts.
4. **Trunk sharing is healthy**: 2.2 tips per base (Drogon), 2.3 (gripper).

---

## 2026-09-03 night — dense-branch routing after island coverage

- Branch: `grid-routing-prototype` at `f17b740`, after merging main `ae009c1` (island
  coverage, fitted bases and short tip fallback).
- Config: Debug, net10.0; same machine and single-process conditions as the earlier seated run.
- Fresh `tips --seat --json` output was used for each model. Main's island-coverage change means
  these candidate sets are intentionally larger than the earlier 1087 / 445 reference sets.
- Tree routing adds shallower 30° / 15° fans and permits sibling branches to fuse only within
  0.5 mm of their shared trunk axis; all other collision checks are unchanged.

### Results

| Model | Command | Flags | Wall s | Exit | Counts | Notes |
| --- | --- | ---: | ---: | ---: | --- | --- |
| drogon | `tips` | `--seat --json` | 29.649 | 0 | **1479** candidates (Island 639, LocalMinimum 116, Corner 275, Edge 180, Overhang 269). Spacing min 0.502 / median 2.524 / mean 2.041 | Guaranteed island representatives account for the increase and may sit inside ordinary spacing. |
| drogon | `route` | `--seat --strategy tree --json` | 16.916 | 2 | nodes 2507, segs 2192 (tip 850, branch 581, trunk 761, brace 0), **unrouted 629 / 1479**, bases 315, max lean 45.0°, collisionFree **true** | Refusals: ContactBlocked 233, NoClearStep 396, NoLanding 0. 850 tips share 315 trunks. |
| gripper | `tips` | `--seat --json` | 4.042 | 0 | **460** candidates (Island 91, Edge 93, Overhang 276). Spacing min 0.512 / median 2.739 / mean 2.747 | Fresh post-island-coverage candidate set. |
| gripper | `route` | `--seat --strategy tree --json` | 1.484 | 2 | nodes 1127, segs 993 (tip 376, branch 244, trunk 373, brace 0), **unrouted 84 / 460**, bases 134, max lean 45.0°, collisionFree **true** | Refusals: ContactBlocked 34, NoClearStep 50, NoLanding 0. 376 tips share 134 trunks. |

### Observations

1. Both canonical runs remain collision-free and keep the one-branch maximum anatomy.
2. Candidate-count changes dominate comparison with the prior table: the island-coverage merge
   adds 392 Drogon and 15 gripper candidates, so these totals are not an isolated router A/B.
3. The like-for-like router A/B is a drogon-lo development run (same 1195 tips, not a
   regression reference): 437 → 422 unrouted, with NoClearStep 244 → 218.

---

## 2026-09-03 late night — 20 mm grid bases and mini-support pass

- Branch: `grid-routing-prototype` at `85efcef` plus CLI benchmark plumbing `a262264`.
- Config: Debug, net10.0; same machine and single-process conditions as the earlier seated runs.
- Fresh `tips --seat --json` output was used for each model, followed by
  `route --seat --strategy tree --json` with defaults. Mini-island placement is enabled in the
  CLI to mirror app generation.
- New defaults in this run: plate-origin square base grid at 20 mm pitch; full 4 mm bases never
  shrink; existing-trunk preference on with 8 mm range; mini rod Ø 0.6, contact Ø 0.25, cone
  length 1 mm, maximum length 5 mm, maximum fan 4.

### Results

| Model | Command | Flags | Wall s | Exit | Counts | Notes |
| --- | --- | ---: | ---: | ---: | --- | --- |
| drogon | `tips` | `--seat --json` | 27.908 | 0 | **1961** candidates (Island 639, MiniIsland 492, LocalMinimum 106, Corner 275, Edge 180, Overhang 269). Spacing min 0.499 / median 1.053 / mean 1.599 | Fine islands are below 0.1 mm² but at least the 0.049 mm² contact footprint; ordinary placement retains priority. |
| drogon | `route` | `--seat --strategy tree --json` | 8.654 | 2 | nodes 462, segs 445 (tip 87, mini-support 184, branch 87, trunk 87, brace 0), **unrouted 1690 / 1961**, bases **17**, max lean 89.9°, collisionFree **true** | Refusals: ContactBlocked 17, NoClearStep 1673. Mini pass routes 184 fine/refused contacts. |
| gripper | `tips` | `--seat --json` | 3.173 | 0 | **482** candidates (Island 91, MiniIsland 22, Edge 93, Overhang 276). Spacing min 0.512 / median 2.712 / mean 2.622 | 22 physically viable below-threshold islands join the previous 460-candidate distribution. |
| gripper | `route` | `--seat --strategy tree --json` | 0.491 | 2 | nodes 305, segs 288 (tip 79, mini-support 51, branch 79, trunk 79, brace 0), **unrouted 352 / 482**, bases **17**, max lean 88.0°, collisionFree **true** | Refusals: ContactBlocked 0, NoClearStep 352. Mini pass routes 51 fine/refused contacts. |

### Observations

1. **Base counts changed by design.** The prior dense-tree run used 315 Drogon / 134 gripper
   off-grid bases. Requiring the existing 8 mm maximum branch to reach a 20 mm square lattice
   leaves only 17 viable bases on each model; blocked or unreachable lattice points are now honest
   refusals rather than off-grid or shrunken bases.
2. The grid/range combination is correspondingly restrictive: total refusals rise from 629 / 84
   to 1690 / 352. These numbers should guide screen testing of the proposed 20 mm pitch and existing
   8 mm branch-length default; the implementation does not silently relax either user-visible value.
3. Mini-supports recover 184 Drogon and 51 gripper contacts by fanning from actual branch ends,
   capped at four per end. Their unrestricted fine-rod direction accounts for max lean above 45°.
4. Both outputs remain collision-free, and every emitted base is full-size and exactly on the
   plate-origin grid.

---

## 2026-09-03 late night — explicit mini classification, regular fallback disabled

- Branch: `grid-routing-prototype` at `664895f`; regular-tip fallback to mini supports now
  defaults OFF, while genuine `MiniIsland` contacts still use the mini pass.
- Config: Debug, net10.0; same machine and single-process conditions as the earlier seated runs.
- Fresh `tips --seat --json` output was used for each model, followed by
  `route --seat --strategy tree --json` with defaults. The default mini-island upper bound is
  0.1 mm², independently configurable from the regular-island threshold.

### Results

| Model | Command | Flags | Wall s | Exit | Counts | Notes |
| --- | --- | ---: | ---: | ---: | --- | --- |
| drogon | `tips` | `--seat --json` | 27.156 | 0 | **1961** candidates (Island 639, MiniIsland 492, LocalMinimum 106, Corner 275, Edge 180, Overhang 269) | Candidate classification is bit-identical to the preceding run at the unchanged 0.1 mm² defaults. |
| drogon | `route` | `--seat --strategy tree --json` | 8.403 | 2 | nodes 393, segs 376 (tip 87, mini-support **115**, branch 87, trunk 87, brace 0), **unrouted 1759 / 1961**, bases 17, max lean 88.8°, collisionFree **true** | Refusals: ContactBlocked 31, NoClearStep 396, NoReachableGridPoint 1085, NoBranchEndInRange 247. |
| gripper | `tips` | `--seat --json` | 3.121 | 0 | **482** candidates (Island 91, MiniIsland 22, Edge 93, Overhang 276) | Candidate classification is bit-identical to the preceding run at the unchanged 0.1 mm² defaults. |
| gripper | `route` | `--seat --strategy tree --json` | 0.474 | 2 | nodes 260, segs 243 (tip 79, mini-support **6**, branch 79, trunk 79, brace 0), **unrouted 397 / 482**, bases 17, max lean 45.0°, collisionFree **true** | Refusals: ContactBlocked 6, NoClearStep 35, NoReachableGridPoint 342, NoBranchEndInRange 14. |

### Observations

1. With refused-regular fallback OFF, Drogon mini segments fall **184 → 115 (−69, −37.5%)**
   and honest refusals rise **1690 → 1759 (+69)**. Gripper mini segments fall
   **51 → 6 (−45, −88.2%)** and refusals rise **352 → 397 (+45)**. In each case the
   refusal increase exactly equals the regular contacts no longer downgraded.
2. The remaining 115 / 6 mini segments originate only from genuine below-threshold fine-island
   contacts. The input candidate sets and 17 grid bases per model are unchanged, isolating the
   delta to classification policy rather than placement or base routing.
3. Both outputs remain collision-free. The refusal breakdown now distinguishes unreachable grid
   points and mini contacts with no branch end in range from generic routing-step failures.

---

## 2026-09-03 late night — optional base grid A/B

- Branch: `grid-routing-prototype` at `fc8540c`; `UseBaseGrid` defaults ON and OFF restores the
  pre-grid free trunk-top fan and near-plate base-relocation fan.
- Config: Debug, net10.0; same machine and single-process conditions as the earlier seated runs.
- The fresh seated 1961-candidate Drogon and 482-candidate gripper tip files from the immediately
  preceding mini-classification run were reused unchanged. Each model was routed once grid-on and
  once grid-off with `route --seat --strategy tree --base-grid on|off --json`.
- Regular-tip fallback to mini supports remained at its default OFF in every run.

### Results

| Model | Command | Flags | Wall s | Exit | Counts | Notes |
| --- | --- | ---: | ---: | ---: | --- | --- |
| drogon | `route` | `--seat --strategy tree --base-grid on --json` | 8.427 | 2 | nodes 393, segs 376 (tip 87, mini-support 115, branch 87, trunk 87), **unrouted 1759 / 1961**, bases **17**, max lean 88.8°, collisionFree **true** | Bit-identical to the preceding grid-on result. Refusals: ContactBlocked 31, NoClearStep 396, NoReachableGridPoint 1085, NoBranchEndInRange 247. |
| drogon | `route` | `--seat --strategy tree --base-grid off --json` | 18.320 | 2 | nodes 2555, segs 2365 (tip 710, mini-support 369, branch 576, trunk 710), **unrouted 882 / 1961**, bases **190**, max lean 89.6°, collisionFree **true** | Refusals: ContactBlocked 193, NoClearStep 641, NoReachableGridPoint 0, NoBranchEndInRange 48. |
| gripper | `route` | `--seat --strategy tree --base-grid on --json` | 0.436 | 2 | nodes 260, segs 243 (tip 79, mini-support 6, branch 79, trunk 79), **unrouted 397 / 482**, bases **17**, max lean 45.0°, collisionFree **true** | Bit-identical to the preceding grid-on result. Refusals: ContactBlocked 6, NoClearStep 35, NoReachableGridPoint 342, NoBranchEndInRange 14. |
| gripper | `route` | `--seat --strategy tree --base-grid off --json` | 0.935 | 2 | nodes 1137, segs 1008 (tip 372, mini-support 19, branch 245, trunk 372), **unrouted 91 / 482**, bases **129**, max lean 45.0°, collisionFree **true** | Refusals: ContactBlocked 34, NoClearStep 55, NoReachableGridPoint 0, NoBranchEndInRange 2. |

### Observations

1. Grid-on is behaviorally unchanged: both canonical summaries reproduce the preceding table's
   nodes, segment counts, refusal breakdowns, bases, maximum lean and collision-free status.
2. Free placement removes the grid-reachability bottleneck. Refusals fall **1759 → 882** on
   Drogon and **397 → 91** on the gripper, while full-size bases rise **17 → 190** and
   **17 → 129** respectively. The trade is denser, less regular plate contact geometry.
3. Every emitted base still uses the configured full-size geometry and both grid-off outputs remain
   collision-free. `NoReachableGridPoint` correctly disappears when no lattice constraint applies;
   geometry-bound `ContactBlocked` and `NoClearStep` refusals remain explicit.

---

## 2026-09-03 overnight — branch shaping and mini-angle cap

- Branch: `grid-routing-prototype` at `9d95faa`; compact branch selection and projected near-pass
  avoidance are in `ea7d708`, with the configurable 75° mini-support limit in `9d95faa`.
- Config: Debug, net10.0; same machine and single-process conditions as the preceding runs.
- Fresh `tips --seat --json` output was generated for both canonical models. Each candidate set was
  routed once with `route --seat --strategy tree --base-grid on|off --json`; regular-tip fallback
  to mini supports remained at its default OFF.
- The stricter visual near-pass rule is derived from the configured branch diameter. It rejects
  projected X crossings even when their Z separation would be collision-clear, so increased honest
  refusals are an expected structural trade rather than collision regressions.

### Results

| Model | Command | Flags | Wall s | Exit | Counts | Notes |
| --- | --- | ---: | ---: | ---: | --- | --- |
| drogon | `tips` | `--seat --json` | 26.960 | 0 | **1961** candidates (Island 639, MiniIsland 492, LocalMinimum 106, Corner 275, Edge 180, Overhang 269) | Fresh candidate set is identical to the preceding A/B. |
| drogon | `route` | `--seat --strategy tree --base-grid on --json` | 8.358 | 2 | nodes 363, segs 346 (tip 85, mini-support 105, branch 85, trunk 71), **unrouted 1771 / 1961**, bases **17**, max lean 73.8°, collisionFree **true** | Refusals: ContactBlocked 34, NoClearStep 389, NoReachableGridPoint 1086, NoBranchEndInRange 262. |
| drogon | `route` | `--seat --strategy tree --base-grid off --json` | 19.640 | 2 | nodes 2400, segs 2163 (tip 675, mini-support 342, branch 488, trunk 658), **unrouted 944 / 1961**, bases **237**, max lean 74.8°, collisionFree **true** | Refusals: ContactBlocked 186, NoClearStep 709, NoReachableGridPoint 0, NoBranchEndInRange 49. |
| gripper | `tips` | `--seat --json` | 3.160 | 0 | **482** candidates (Island 91, MiniIsland 22, Edge 93, Overhang 276) | Fresh candidate set is identical to the preceding A/B. |
| gripper | `route` | `--seat --strategy tree --base-grid on --json` | 0.440 | 2 | nodes 243, segs 226 (tip 76, mini-support 6, branch 76, trunk 68), **unrouted 400 / 482**, bases **17**, max lean 45.0°, collisionFree **true** | Refusals: ContactBlocked 7, NoClearStep 36, NoReachableGridPoint 342, NoBranchEndInRange 15. |
| gripper | `route` | `--seat --strategy tree --base-grid off --json` | 0.957 | 2 | nodes 1130, segs 989 (tip 370, mini-support 18, branch 231, trunk 370), **unrouted 94 / 482**, bases **141**, max lean 45.0°, collisionFree **true** | Refusals: ContactBlocked 34, NoClearStep 58, NoReachableGridPoint 0, NoBranchEndInRange 2. |

### Observations

1. All four outputs remain collision-free and every refusal retains a concrete routing reason.
2. The mini-angle cap removes the near-horizontal canonical outliers: Drogon maximum lean falls
   from 88.8° to 73.8° with the grid and from 89.6° to 74.8° without it. Gripper was already
   bounded by ordinary 45° members in both modes.
3. Projected near-pass avoidance and compact branch ordering trade acceptance for less tangled
   geometry. Against the preceding A/B, refusals rise by 12 / 62 on Drogon (grid on / off) and by
   3 / 3 on the gripper. The large Drogon grid bottleneck remains `NoReachableGridPoint`; shaping
   does not conceal or relax it.
4. Free placement now uses more independent short, shallow branches and trunks: Drogon bases rise
   190 → 237 while branches fall 576 → 488; gripper bases rise 129 → 141 while branches fall
   245 → 231. That moves the topology toward the mined Lychee reference's dense, freely placed
   near-vertical trunks, at the cost of plate density that the user should judge on screen.

---

## 2026-09-04 — 6 mm base-grid default A/B

- Branch: `grid-routing-prototype` at `999edde`; `UseBaseGrid` remains default ON and
  `BaseGridPitch` now defaults to 6 mm instead of the 20 mm used by the preceding A/B rows.
- Config: Debug, net10.0; same machine and single-process conditions as the preceding runs.
- `danslicer bench` generated fresh seated tips for both canonical models and routed each candidate
  set once with the 6 mm grid on and once with free base placement. Regular-tip fallback to mini
  supports remained at its default OFF.

### Results

| Model | Command | Flags | Wall s | Exit | Counts | Notes |
| --- | --- | ---: | ---: | ---: | --- | --- |
| drogon | `tips` | `--seat --json` | 18.188 | 0 | **1961** candidates (Island 639, MiniIsland 492, LocalMinimum 106, Corner 275, Edge 180, Overhang 269) | Spacing min 0.499 / median 1.053 / mean 1.599. |
| drogon | `route` | `--seat --strategy tree --base-grid on --json` | 9.275 | 2 | nodes 1948, segs 1807 (tip 509, mini-support 349, branch 509, trunk 440), **unrouted 1103 / 1961**, bases **141**, max lean 74.7°, collisionFree **true** | 6 mm grid. Refusals: ContactBlocked 141, NoClearStep 688, NoReachableGridPoint 205, NoBranchEndInRange 69. |
| drogon | `route` | `--seat --strategy tree --base-grid off --json` | 18.573 | 2 | nodes 2400, segs 2163 (tip 675, mini-support 342, branch 488, trunk 658), **unrouted 944 / 1961**, bases **237**, max lean 74.8°, collisionFree **true** | Free placement. Refusals: ContactBlocked 186, NoClearStep 709, NoBranchEndInRange 49. |
| gripper | `tips` | `--seat --json` | 2.616 | 0 | **482** candidates (Island 91, MiniIsland 22, Edge 93, Overhang 276) | Spacing min 0.512 / median 2.712 / mean 2.622. |
| gripper | `route` | `--seat --strategy tree --base-grid on --json` | 0.395 | 2 | nodes 1238, segs 1105 (tip 367, mini-support 20, branch 367, trunk 351), **unrouted 95 / 482**, bases **133**, max lean 45.0°, collisionFree **true** | 6 mm grid. Refusals: ContactBlocked 32, NoClearStep 53, NoReachableGridPoint 8, NoBranchEndInRange 2. |
| gripper | `route` | `--seat --strategy tree --base-grid off --json` | 0.800 | 2 | nodes 1130, segs 989 (tip 370, mini-support 18, branch 231, trunk 370), **unrouted 94 / 482**, bases **141**, max lean 45.0°, collisionFree **true** | Free placement. Refusals: ContactBlocked 34, NoClearStep 58, NoBranchEndInRange 2. |

### Observations

1. Against the preceding shaped 20 mm grid-on reference, the 6 mm pitch reduces refusals from
   **1771 → 1103** on Drogon and **400 → 95** on the gripper. `NoReachableGridPoint` falls from
   1086 → 205 and 342 → 8 respectively, while both outputs remain collision-free.
2. The denser lattice raises grid-on bases from 17 → 141 on Drogon and 17 → 133 on the gripper.
   That approaches the acceptance of free placement while retaining regular plate alignment.
3. Grid-off rows are bit-identical to the preceding shaped A/B reference apart from wall time,
   confirming that the default pitch change has no effect when `UseBaseGrid` is off.

---

## 2026-09-03 overnight — flush tip junction geometry

- Branch: `grid-routing-prototype` at `7b94636`; cone-tip render and slice geometry now transitions
  from the taper-rule body diameter to the exact incident branch/trunk diameter at the junction.
- Config: Debug, net10.0; same machine and single-process conditions as the preceding runs.
- The unchanged seated 1961-candidate Drogon and 482-candidate gripper tip files were rerouted with
  `route --seat --strategy tree --base-grid on --json`. Reusing the fixed inputs isolates the
  geometry-only change from placement variation.

### Results

| Model | Command | Flags | Wall s | Exit | Counts | Notes |
| --- | --- | ---: | ---: | ---: | --- | --- |
| drogon | `route` | `--seat --strategy tree --base-grid on --json` | 8.832 | 2 | nodes 363, segs 346 (tip 85, mini-support 105, branch 85, trunk 71), **unrouted 1771 / 1961**, bases **17**, max lean 73.8°, collisionFree **true** | Bit-identical to the branch-shaping grid-on reference. Refusals: ContactBlocked 34, NoClearStep 389, NoReachableGridPoint 1086, NoBranchEndInRange 262. |
| gripper | `route` | `--seat --strategy tree --base-grid on --json` | 0.520 | 2 | nodes 243, segs 226 (tip 76, mini-support 6, branch 76, trunk 68), **unrouted 400 / 482**, bases **17**, max lean 45.0°, collisionFree **true** | Bit-identical to the branch-shaping grid-on reference. Refusals: ContactBlocked 7, NoClearStep 36, NoReachableGridPoint 342, NoBranchEndInRange 15. |

### Observations

1. Both routing summaries are bit-identical to the pre-change grid-on reference: node and segment
   counts, segment taxonomy, refusals, base count, maximum lean and collision status all match.
2. The changed defaults are derived render/slice surfaces only. Routing topology, configured graph
   diameters and collision decisions remain unchanged; both canonical graphs remain collision-free.

---

## 2026-09-04 — Reinforce growth rule A/B

- Branch: `grid-routing-prototype` after `0dfa69a`; Reinforce is now built from persisted support
  config. These runs use its existing defaults: lowest-object seed, 3 ring tips, 2 mm radius and
  1.25× tip diameter. Reinforce remains OFF by default.
- Config: Debug, net10.0; same machine and serial, single-process conditions as the preceding runs.
- `danslicer bench --reinforce off|on` generated fresh seated tips for each run. Both candidate
  sets reproduced the canonical 1961-tip Drogon and 482-tip gripper distributions.

### Results

| Model | Command | Flags | Wall s | Exit | Counts | Notes |
| --- | --- | ---: | ---: | ---: | --- | --- |
| drogon | `route` | `--seat --strategy tree --base-grid on --reinforce off --json` | 9.247 | 2 | nodes 1948, segs 1807 (tip 509, mini-support 349, branch 509, trunk 440), **unrouted 1103 / 1961**, bases **141**, max lean 74.7°, collisionFree **true** | Default-off control; bit-identical topology to the 6 mm reference. |
| drogon | `route` | `--seat --strategy tree --base-grid on --reinforce on --json` | 9.442 | 2 | nodes 1948, segs 1807 (tip 509, mini-support 349, branch 509, trunk 440), **unrouted 1104 / 1961**, bases **141**, max lean 74.7°, collisionFree **true** | Segment delta **0**; wall +0.195 s (+2.1%). One projected ring tip was below the seated plate and was honestly refused. |
| drogon | `route` | `--seat --strategy tree --base-grid off --reinforce off --json` | 18.311 | 2 | nodes 2400, segs 2163 (tip 675, mini-support 342, branch 488, trunk 658), **unrouted 944 / 1961**, bases **237**, max lean 74.8°, collisionFree **true** | Default-off control; bit-identical topology to the free-placement reference. |
| drogon | `route` | `--seat --strategy tree --base-grid off --reinforce on --json` | 18.615 | 2 | nodes 2400, segs 2163 (tip 675, mini-support 342, branch 488, trunk 658), **unrouted 945 / 1961**, bases **237**, max lean 74.8°, collisionFree **true** | Segment delta **0**; wall +0.304 s (+1.7%). The same below-plate ring tip was refused. |
| gripper | `route` | `--seat --strategy tree --base-grid on --reinforce off --json` | 0.400 | 2 | nodes 1238, segs 1105 (tip 367, mini-support 20, branch 367, trunk 351), **unrouted 95 / 482**, bases **133**, max lean 45.0°, collisionFree **true** | Default-off control; bit-identical topology to the 6 mm reference. |
| gripper | `route` | `--seat --strategy tree --base-grid on --reinforce on --json` | 0.399 | 2 | nodes 1238, segs 1105 (tip 367, mini-support 20, branch 367, trunk 351), **unrouted 95 / 482**, bases **133**, max lean 45.0°, collisionFree **true** | Segment delta **0**; wall −0.001 s (−0.3%), within timer noise. Ring samples did not re-project onto this lowest boundary contact. |
| gripper | `route` | `--seat --strategy tree --base-grid off --reinforce off --json` | 0.759 | 2 | nodes 1130, segs 989 (tip 370, mini-support 18, branch 231, trunk 370), **unrouted 94 / 482**, bases **141**, max lean 45.0°, collisionFree **true** | Default-off control; bit-identical topology to the free-placement reference. |
| gripper | `route` | `--seat --strategy tree --base-grid off --reinforce on --json` | 0.758 | 2 | nodes 1130, segs 989 (tip 370, mini-support 18, branch 231, trunk 370), **unrouted 94 / 482**, bases **141**, max lean 45.0°, collisionFree **true** | Segment delta **0**; wall −0.001 s (−0.1%), within timer noise. Ring samples did not re-project. |

### Observations

1. Reinforce OFF remains bit-identical to the job-019 6 mm reference in both grid modes: every
   topology count, refusal bucket, base count, maximum lean and collision result matches.
2. The canonical files are seated, so their absolute lowest contact lies on or at a model boundary
   near the plate. The default lowest-object ring therefore adds no printable segments: Drogon
   produces one explicit `BelowPlate` refusal and gripper has no ring sample within projection
   reach. This is an honest zero-segment delta, not a hidden fallback.
3. Route-time differences range from −0.3% to +2.1% and are timer noise at this scale. Reinforce
   does visibly add routed segments on the raised bridge, sphere and dome preset-preview fixtures,
   where the selected lowest contact has printable clearance beneath it.

---

## 2026-09-04 — density-based mini-tip clusters

- Baseline: `5560a69`; clustered implementation: `f9218ad`, with the 1.25 mm crowding-distance
  default and `MiniSupportMaxFanPerBranchEnd = 4` reused as the cluster cap.
- Config: Debug, net10.0; same machine and serial, single-process conditions as preceding runs.
- Both passes generated fresh seated candidates. Candidate totals and nearest-neighbour spacing
  are unchanged; the clustered pass reclassifies crowded regular contacts as `MiniCluster` and
  routes each bounded group through one purpose-built branch end.

### Results

| Model | Pass | Command | Wall s | Counts | Refusals / notes |
| --- | --- | --- | ---: | --- | --- |
| drogon | before | `tips --seat --json` | 17.927 | **1961** candidates (Island 639, MiniIsland 492, LocalMinimum 106, Corner 275, Edge 180, Overhang 269) | No density clusters. |
| drogon | after | `tips --seat --json` | 17.850 | **1961** candidates (Island 190, MiniIsland 492, **MiniCluster 449**, LocalMinimum 106, Corner 275, Edge 180, Overhang 269), **132 clusters** | Same 0.499 / 1.053 / 1.599 mm min/median/mean spacing. |
| drogon | before | `route --seat --strategy tree --base-grid on --json` | 9.093 | tip 509, mini 348, branch 509, trunk 440; **1104 refused** | ContactBlocked 141, NoClearStep 689, NoReachableGridPoint 205, NoBranchEndInRange 69; collision-free. |
| drogon | after | `route --seat --strategy tree --base-grid on --json` | 9.483 | tip 424, mini 474, branch 487, trunk 433; **1063 refused** | ContactBlocked 24, NoClearStep 773, NoReachableGridPoint 184, NoBranchEndInRange 82; collision-free. |
| drogon | before | `route --seat --strategy tree --base-grid off --json` | 17.751 | tip 675, mini 338, branch 488, trunk 658; **948 refused** | ContactBlocked 186, NoClearStep 713, NoBranchEndInRange 49; collision-free. |
| drogon | after | `route --seat --strategy tree --base-grid off --json` | 18.589 | tip 532, mini 528, branch 439, trunk 598; **901 refused** | ContactBlocked 24, NoClearStep 806, NoBranchEndInRange 71; collision-free. |
| gripper | before | `tips --seat --json` | 2.376 | **482** candidates (Island 91, MiniIsland 22, Edge 93, Overhang 276) | No density clusters. |
| gripper | after | `tips --seat --json` | 2.396 | **482** candidates (Island 82, MiniIsland 22, **MiniCluster 9**, Edge 93, Overhang 276), **3 clusters** | Same 0.512 / 2.712 / 2.622 mm min/median/mean spacing. |
| gripper | before | `route --seat --strategy tree --base-grid on --json` | 0.398 | tip 367, mini 20, branch 367, trunk 351; **95 refused** | ContactBlocked 32, NoClearStep 53, NoReachableGridPoint 8, NoBranchEndInRange 2; collision-free. |
| gripper | after | `route --seat --strategy tree --base-grid on --json` | 0.386 | tip 362, mini 29, branch 365, trunk 349; **91 refused** | ContactBlocked 28, NoClearStep 53, NoReachableGridPoint 8, NoBranchEndInRange 2; collision-free. |
| gripper | before | `route --seat --strategy tree --base-grid off --json` | 0.763 | tip 370, mini 18, branch 231, trunk 370; **94 refused** | ContactBlocked 34, NoClearStep 58, NoBranchEndInRange 2; collision-free. |
| gripper | after | `route --seat --strategy tree --base-grid off --json` | 0.749 | tip 365, mini 27, branch 231, trunk 368; **90 refused** | ContactBlocked 30, NoClearStep 58, NoBranchEndInRange 2; collision-free. |

### Observations

1. Clustering converts exactly the crowded regular members without changing total candidate count
   or placement spacing. The canonical pair produces 132 four-or-fewer-member Drogon clusters and
   3 gripper clusters; oversized connected groups split instead of refusing excess members.
2. Grid-on refusals fall **1104 → 1063** on Drogon and **95 → 91** on the gripper. Grid-off falls
   **948 → 901** and **94 → 90**. The large drop in `ContactBlocked` is partly exchanged for
   per-member `NoClearStep`/`NoBranchEndInRange`, preserving honest failure accounting.
3. Every output remains collision-free and maximum lean stays within the configured 75° mini cap.
   Route timings move from 9.093 → 9.483 s / 17.751 → 18.589 s on Drogon and remain within timer
   noise on the gripper.
4. The required drogon-lo iteration run found 398 clustered members in 123 clusters from 1492
   candidates. Grid-on routed in 1.222 s with 757 refusals; grid-off in 3.637 s with 601 refusals;
   both outputs were collision-free.

---

## 2026-09-04 — island identity in density clusters

- Baseline: current merged `main` at `dac3d7a`; implementation: `f953532` and `08ef893`.
- Config: Debug, net10.0; same machine and serial execution as the preceding run.
- Finding: job 023 already clustered `Island` contacts—the original eligibility filter excluded
  only `MiniIsland` and existing `MiniCluster` members, and its regression used island inputs.
  This pass makes island eligibility explicit, preserves every member's source strategy, and adds
  a six-tooth placement regression proving the post-dedup contact count and positions survive
  clustering. No topology change is expected or observed.

### Results

| Model | Pass | Tips / cluster provenance | Grid on | Grid off |
| --- | --- | --- | --- | --- |
| drogon | before | 1961 candidates; Island 190, MiniIsland 492, MiniCluster 449; 132 clusters; source provenance not emitted | 1063 refusals, 144 bases, collision-free | 901 refusals, 228 bases, collision-free |
| drogon | after | 1961 candidates; Island 190, MiniIsland 492, MiniCluster 449; 132 clusters; **449 island / 0 regular members** | 1063 refusals, 144 bases, collision-free | 901 refusals, 228 bases, collision-free |
| gripper | before | 482 candidates; Island 82, MiniIsland 22, MiniCluster 9; 3 clusters; source provenance not emitted | 91 refusals, 133 bases, collision-free | 90 refusals, 139 bases, collision-free |
| gripper | after | 482 candidates; Island 82, MiniIsland 22, MiniCluster 9; 3 clusters; **9 island / 0 regular members** | 91 refusals, 133 bases, collision-free | 90 refusals, 139 bases, collision-free |
| drogon-lo | before | 1492 candidates; Island 181, MiniIsland 356, MiniCluster 398; 123 clusters; source provenance not emitted | 757 refusals, 130 bases, collision-free | 601 refusals, 210 bases, collision-free |
| drogon-lo | after | 1492 candidates; Island 181, MiniIsland 356, MiniCluster 398; 123 clusters; **398 island / 0 regular members** | 757 refusals, 130 bases, collision-free | 601 refusals, 210 bases, collision-free |

The before/after candidate, cluster, topology, refusal and base counts are bit-identical. The new
provenance answers the brief's requested split directly: every density-cluster member in all three
fixtures originated as an island contact, while `MiniIsland` counts remain unchanged. On drogon-lo,
the 398 island members provide the headless proof available under the no-app-launch protocol; the
user should locate them in the head/teeth view after regenerating supports, because an existing
graph is not retroactively reclassified.

---

## 2026-09-04 — isolated fine-feature mini tips

- Baseline and implementation were run at `cd60d2d`, toggling only
  `--fine-feature-max 0` versus the proposed **1.0 mm²** default. Candidate positions and totals
  are unchanged; only isolated island/local-minimum contacts left over after density clustering
  are reclassified as one-member mini clusters.
- Islands reuse their first-appearance area. Local minima use their connected horizontal section
  0.5 mm above the contact. On `drogon-lo`, the four converted local minima measured 0.36, 0.62,
  0.68 and 0.78 mm²; the next measured minimum was 4.01 mm². This is the basis for the flagged
  1.0 mm² default.

### Results

| Model | Pass | Tips / conversions | Grid on | Grid off |
| --- | --- | --- | --- | --- |
| drogon | before | 1961 candidates; Island 190, MiniIsland 492, MiniCluster 449; 132 density clusters | 1063 refusals, 144 bases, collision-free | 901 refusals, 228 bases, collision-free |
| drogon | after | 1961 candidates; Island 20, MiniIsland 492, MiniCluster 620; **171 fine singles** (170 island, 1 local minimum) | 1087 refusals, 137 bases, collision-free | 906 refusals, 221 bases, collision-free |
| gripper | before | 482 candidates; Island 82, MiniIsland 22, MiniCluster 9; 3 density clusters | 91 refusals, 133 bases, collision-free | 90 refusals, 139 bases, collision-free |
| gripper | after | 482 candidates; Island 33, MiniIsland 22, MiniCluster 58; **49 fine singles** (all island) | 83 refusals, 137 bases, collision-free | 82 refusals, 141 bases, collision-free |
| drogon-lo | before | 1492 candidates; Island 181, MiniIsland 356, MiniCluster 398; 123 density clusters | 757 refusals, 130 bases, collision-free | 601 refusals, 210 bases, collision-free |
| drogon-lo | after | 1492 candidates; Island 34, MiniIsland 356, MiniCluster 549; **151 fine singles** (147 island, 4 local minima) | 777 refusals, 126 bases, collision-free | 627 refusals, 207 bases, collision-free |

The default converts the targeted four isolated `drogon-lo` spike minima while leaving the next
much broader minimum regular. Density clusters remain unchanged and take precedence, mini-island
counts are unchanged, and every candidate still has exactly one auditable source strategy. The
trade-off is model-dependent: gripper refusals improve by 8 in both modes; Drogon rises by 24
grid-on and 5 grid-off, and `drogon-lo` rises by 20/26. All six route outputs remain collision-free
and within the configured 75° mini lean limit.

---

## 2026-09-04 — isolated fine-feature mini tips

- Baseline and implementation were run at `cd60d2d`, toggling only
  `--fine-feature-max 0` versus the proposed **1.0 mm²** default. Candidate positions and totals
  are unchanged; only isolated island/local-minimum contacts left over after density clustering
  are reclassified as one-member mini clusters.
- Islands reuse their first-appearance area. Local minima use their connected horizontal section
  0.5 mm above the contact. On `drogon-lo`, the four converted local minima measured 0.36, 0.62,
  0.68 and 0.78 mm²; the next measured minimum was 4.01 mm². This is the basis for the flagged
  1.0 mm² default.

### Results

| Model | Pass | Tips / conversions | Grid on | Grid off |
| --- | --- | --- | --- | --- |
| drogon | before | 1961 candidates; Island 190, MiniIsland 492, MiniCluster 449; 132 density clusters | 1063 refusals, 144 bases, collision-free | 901 refusals, 228 bases, collision-free |
| drogon | after | 1961 candidates; Island 20, MiniIsland 492, MiniCluster 620; **171 fine singles** (170 island, 1 local minimum) | 1087 refusals, 137 bases, collision-free | 906 refusals, 221 bases, collision-free |
| gripper | before | 482 candidates; Island 82, MiniIsland 22, MiniCluster 9; 3 density clusters | 91 refusals, 133 bases, collision-free | 90 refusals, 139 bases, collision-free |
| gripper | after | 482 candidates; Island 33, MiniIsland 22, MiniCluster 58; **49 fine singles** (all island) | 83 refusals, 137 bases, collision-free | 82 refusals, 141 bases, collision-free |
| drogon-lo | before | 1492 candidates; Island 181, MiniIsland 356, MiniCluster 398; 123 density clusters | 757 refusals, 130 bases, collision-free | 601 refusals, 210 bases, collision-free |
| drogon-lo | after | 1492 candidates; Island 34, MiniIsland 356, MiniCluster 549; **151 fine singles** (147 island, 4 local minima) | 777 refusals, 126 bases, collision-free | 627 refusals, 207 bases, collision-free |

The default converts the targeted four isolated `drogon-lo` spike minima while leaving the next
much broader minimum regular. Density clusters remain unchanged and take precedence, mini-island
counts are unchanged, and every candidate still has exactly one auditable source strategy. The
trade-off is model-dependent: gripper refusals improve by 8 in both modes; Drogon rises by 24
grid-on and 5 grid-off, and `drogon-lo` rises by 20/26. All six route outputs remain collision-free
and within the configured 75° mini lean limit.
