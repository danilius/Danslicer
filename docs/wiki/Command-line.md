# Command line

Run the CLI from the repository root:

```powershell
dotnet run --project src/Danslicer.Cli/Danslicer.Cli.csproj -- printers
```

In the examples below, `danslicer` means the published CLI executable. When using the source tree, replace it with `dotnet run --project src/Danslicer.Cli/Danslicer.Cli.csproj --`.

## Commands

| Command | Input and purpose |
| --- | --- |
| `printers` | List built-in profile IDs, names, extensions, formats, and compatibility notes. |
| `info <file>` | Inspect STL, OBJ, or `.danslicer` scene information. |
| `slice <mesh>...` or `slice <project>` | Slice meshes or a saved project and export native output. |
| `inspect <file.pwmx>` | Inspect a Photon Workshop print file; not a universal reader for all exported formats. |
| `tips <mesh>` | Generate contact candidates, optionally as JSON. |
| `route <mesh> --tips <tips.json>` | Route contact candidates and report the resulting support graph, collisions, and refusals. |
| `areas <mesh>` | Analyse connected support areas and features. |
| `checks <mesh>...` | Run print checks without modifying geometry. |
| `checks <project> --islands-after-supports` | Check islands using an existing project's supports. |
| `bench` | Run the developer benchmark suite against supplied sample meshes. |

## Slicing

```powershell
danslicer slice model.stl -o model.pwmx --printer anycubic-photon-mono-x --layer 0.05
danslicer slice scene.danslicer -o scene.pwmx
```

| Option | Meaning |
| --- | --- |
| `-o <path>` | Output path, with the selected printer's extension. |
| `--printer <id>` | Built-in profile ID from `printers`; a project can provide its saved printer. |
| `--layer <mm>` | Layer height. |
| `--exposure <s>` | Normal exposure. |
| `--bottom-exposure <s>` | Bottom exposure. |
| `--bottom-layers <count>` | Initial layer count using bottom settings. |
| `--xy <mm>` | Contour offset: negative shrinks, positive expands. |
| `--no-aa` | Disable layer anti-aliasing. |
| `--allow-out-of-bounds` | Explicitly allow the job to proceed despite geometry outside the printable volume; outside portions cannot be represented on the plate raster. |

## Contact candidates

```powershell
danslicer tips model.stl --seat --spacing 2.5 --json > tips.json
danslicer route model.stl --seat --tips tips.json --strategy tree --json
```

Use consistent seating for the mesh and candidate coordinates. `--seat` centres the mesh in XY and places its lowest point on the plate.

| Tips option | Meaning |
| --- | --- |
| `--json` | Machine-readable result on standard output. |
| `--seat` | Centre/seat the input mesh before processing. |
| `--spacing <mm>` | Spacing between new contact candidates. |
| `--min-spacing <mm>` | Minimum permitted tip spacing. |
| `--overhang <degrees>` | Overhang threshold measured from vertical. |
| `--min-island <mm²>` | Minimum island area. |
| `--layer <mm>` | Layer height for island analysis. |
| `--tip <mm>` | Contact diameter. |
| `--tip-shape capsule\|cone` | Candidate contact geometry. |
| `--cone-length <mm>` | Cone length for cone-shaped tips. |
| `--ball-diameter <mm>` | Optional tip ball; 0 disables it. |
| `--penetration-depth <mm>` | Cone contact penetration into the surface. |
| `--edge <value>` | Bias placement toward edges. |
| `--force-edges` | Enable forced edge placement. |
| `--sharp-edge <degrees>` | Dihedral threshold for an edge to count as sharp. |
| `--contact-angle <degrees>` | Maximum face angle from straight down; 90 accepts every downward face. |
| `--contact-sees-plate` | Require a clear straight-down path to the plate. |
| `--keep-clean-distance <mm>` | Clearance from keep-clean regions when applicable. |
| `--grid square\|hex` | Lattice used for grid-aware placement. |
| `--grid-spacing <mm>` | Grid pitch. |
| `--grid-offset-x <mm>`, `--grid-offset-y <mm>` | Grid translation on the plate. |
| `--grid-rotation <degrees>` | Grid orientation. |
| `--seed <integer>` | Reproducible random seed. |

## Routing

| Route option | Meaning |
| --- | --- |
| `--tips <path>` | Required contact-candidate JSON. |
| `--strategy grid\|topdown\|tree` | Routing algorithm. |
| `--base-grid on\|off` | Enable or disable base lattice use where supported by the strategy. |
| `--island-first on\|off` | Prioritize island-origin candidates. |
| `--reinforce on\|off` | Enable reinforcement rules. |
| `--min-member-separation <mm>` | Minimum clearance between non-incident members; 0 disables this constraint. |
| `--step-height <mm>` | Vertical stepping for top-down routing. |
| `--spacing <mm>` | Routing lattice spacing, not the tips command's contact spacing. |
| `--lattice square\|hex` | Base lattice geometry. |
| `--offset-x <mm>`, `--offset-y <mm>` | Lattice origin offsets. |
| `--rotation <degrees>` | Lattice rotation. |
| `--snap <mm>` | Lattice snap tolerance. |
| `--seed <integer>` | Reproducible routing seed. |
| `--seat`, `--json` | Seat the mesh; request JSON output. |

Routing reports are diagnostics, not a saved GUI project or printer file. Strategy-specific options may not affect every router. The parser does not accept every option mentioned in old design notes; the table above follows the implemented parser.

## Areas and print checks

```powershell
danslicer areas model.stl --seat --json
danslicer checks model.stl --seat --layer 0.05 --json
danslicer checks scene.danslicer --islands-after-supports --json
```

`areas` accepts `--json`, `--seat`, `--overhang <degrees>`, `--min-area <mm²>`, `--layer <mm>`, `--min-island <mm²>`, and `--sharp-edge <degrees>`. Minimum area filters support areas; minimum island filters layer islands; sharp edge controls feature classification.

`checks` accepts `--json`, `--seat`, `--layer <mm>`, `--min-island <mm²>`, and `--overhang <degrees>`, plus:

| Option | Meaning |
| --- | --- |
| `--min-suction <mm³>` | Minimum trapped volume reported as a suction concern. |
| `--drain <mm>` | Drain-opening threshold used by suction analysis. |
| `--support-spacing <mm>` | Support-to-support proximity threshold. |
| `--model-clearance <mm>` | Support-to-model proximity threshold. |
| `--object-spacing <mm>` | Object-to-object proximity threshold. |
| `--islands-after-supports` | Requires exactly one `.danslicer` project; analyse remaining islands with supports. |

## Benchmarks

`bench` accepts `--drogon <path>`, `--gripper <path>`, `--output <summary.json>`, `--reinforce on|off`, `--island-first on|off`, and `--min-member-separation <mm>`. The named paths select benchmark mesh fixtures; output writes the summary. See the repository's `docs/BENCHMARKS.md` for benchmark context.

CLI decimal input uses a decimal point. Review diagnostics and process exit status when scripting; a report that contains refusals is not a guarantee that all requested supports were created.
