# Support-generation benchmarks

Canonical models (user decision 2026-09-03). They are large binaries and **must not** be copied
into this repo. Read them from these paths on the machine:

| Key | Path | Kind | Triangles | AABB min (mm) | Size (mm) |
| --- | --- | --- | ---: | --- | --- |
| drogon | `F:\Git Repos\Danslicer\test files\Drogon_flat_surface.stl` | organic extreme | 943,666 | −67.35, −70.00, 0.56 | 134.70 × 140.00 × 52.81 |
| gripper | `F:\Git Repos\Danslicer\test files\roof gripper T2.obj` | CAD extreme | 34,800 | 291.68, 1180.14, 3068.38 | 115.35 × 111.08 × 90.96 |

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
