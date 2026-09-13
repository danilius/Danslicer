# Island detection investigation — 11 September 2026

## Decisions

- Standalone Island Detection (viewport and CLI `--islands-after-supports`) now reports new disconnected solid layer components. Positive-area overlap with the preceding model layer establishes continuity. Overhang-angle inflation must not hide an independent nearby component, and connected overhang strips must not produce repeated island markers.
- Support generation and the existing broad print checks retain their overhang-strip candidates. This deliberately preserves coverage under cantilevers; the detection marker list is not a replacement for an overhang/support adequacy analysis.
- Contour joining now chooses the closest eligible endpoint, including an exact match before a merely nearby endpoint. The horse exposed large false islands caused by the old first-match search consuming the wrong segment and discarding a broken contour. The existing 5 micrometre tolerance is unchanged.
- Polygon components use Clipper's containment tree, assigning holes only to their immediate parent. Markers use a hole-aware area centroid, falling back to the centroid of the largest interior triangle for concave/hollow regions.
- Existing supports clear a marker only when their printable section has positive-area intersection with the actual island footprint. An equivalent-area circle could incorrectly clear nearby islands or miss a support near the end of a long island.
- Plate exemption requires actual mesh contact with the plate rather than allowing a full layer of clearance.

## Integration update — 13 September 2026

The integration preserves main's strict disconnected-component policy for support generation as well as standalone detection. Broad print checks still use overhang strips. Starts below the minimum area are reconsidered on later layers until they meet the threshold. These decisions supersede the original generation and minimum-area notes below. Main's contour recovery is retained alongside the nearest-endpoint fix. Marker caching includes islands supported before detection, so removing those supports restores the markers.

## Historical pre-integration horse-bust measurements

Input: `F:/Git Repos/Danslicer/test files/horse-bust-2.stl`, 997,864 triangles. Original bounds span Z=-50 to +50 mm. Validation translates the minimum Z to the plate, without scaling. Minimum island area 0.1 mm²; no supports; angle argument 45 degrees.

| Seated, upright | Before markers | After markers | After maximum area | After detection time |
|---|---:|---:|---:|---:|
| 0.1 mm layers | 1,054 | 23 | 1.312 mm² | 2.07 s |
| 0.05 mm layers | 961 | 12 | 0.478 mm² | 2.67 s |

Counts alone are not proof of accuracy. The isolated disconnected-component change still reported a false 1,177.863 mm² island at Z=39.375 mm; fixing contour joining removed this and the other large contour-dropout artifacts. Synthetic regression cases verify the geometry decisions independently of the horse's marker count.

The reproducible benchmark also rotates the horse 30 degrees about X and seats it again: 47 islands at 0.1 mm (maximum area 1.064 mm²), and 33 at 0.05 mm (maximum area 0.615 mm²). Every configuration is detected twice to check exact determinism, and every marker is checked against the solid slice, including hole winding. JSON output includes full positions for inspection. Timing excludes loading, repeated detection, and validation.

## Reproduce

```powershell
dotnet run --project build/IslandDetectionBenchmark -c Release -- `
  'F:/Git Repos/Danslicer/test files/horse-bust-2.stl' `
  artifacts/island-analysis/verified

dotnet test tests/Danslicer.Tests --no-restore -c Release -p:UsedAvaloniaProducts=
```

The benchmark accepts any STL path. Source data is not copied into the repository. The Avalonia property disables its build telemetry task for the sandboxed test run; it does not alter application functionality.

Validation: all 654 tests passed, including seven new regression tests covering connected expansion, close disconnected components, birth/gap behavior, ring markers/supports, elongated support footprints, nested holes, and exact segment continuation.

## Remaining limits

- Minimum area still applies on the first disconnected layer. A feature below that threshold at birth is filtered even if it grows later. Lower the minimum when investigating very fine features.
- This is layer-sampled geometry, so changing layer height changes which fine features appear independently. Features entirely between sample planes cannot be detected.
- A positive-area overlap establishes connectivity, not sufficient mechanical strength. Support checks do not prove an entire support graph is rooted, printable, or strong enough.
- The contour fix improves the measured mesh and regression cases; it is not a general repair of non-manifold/open STL files.
- Validation was computational; no physical resin print or interactive viewport review was performed.
