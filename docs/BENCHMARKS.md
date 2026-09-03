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

Debug CLI (`dotnet build -c Debug`). `route` accepts `tips --json` output directly (object
with `candidates`) as well as the older JSON array.

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
3. The like-for-like router A/B is the Drogon-low development run recorded in `ChatGPT/REPORT.md`:
   437 → 422 unrouted on the same 1195 tips, with NoClearStep 244 → 218.
