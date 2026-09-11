# Native printer writers — implementation recovery

Started 2026-09-11 after the user explicitly chose independent native writers and authorized implementation using UVtools repository details.

- Worktree: C:/Users/plane/.codex/worktrees/a19b/Danslicer-chatgpt
- Branch: codex/native-printer-writers
- Base: 6adcd39b0a8bbbbbb6caaf478146b7f32068c701 (task-07 report; source baseline dc4664c).
- Other work: task-06 implementation is not on this branch. Keep writer/profile changes separable for integration. Untracked task-08-queued.md belongs to other work and remains untouched; this is not task 08.
- Plan: implement explicit native format/version contracts, validate before creating output, preserve Mono X 516 compatibility, support documented Photon Workshop layouts, add evidence-labelled printer profiles, validate bytes against independent binary specifications and manufacturer samples, run focused/full regression checks, commit coherent changes. No UVtools code copied or linked; source is used to extract factual format structure and model parameters.
- Reference: UVtools 3e3c62faef56a4ae77e2c652cf4914472ec7aa66, AnycubicFile.cs and Scripts/010 Editor/PhotonWorkshop.bt; source/sample manifest from task-07.md.
- Initial audit found arbitrary version/extension values and conflated raster/plate dimensions. Both are addressed by this increment.
- Scope preference asked asynchronously (Photon Workshop first versus all families). Proceed with the existing family first unless user steers otherwise.

## Implemented checkpoint — 2026-09-11

The first native-writer increment covers **18 selectable profiles** (existing Mono X plus 17 experimental profiles) and the **pw0Img** containers for versions **1, 515, 516, 517 and 518**. This is not all 153 UVtools profiles, nor every format bearing the Photon Workshop name. The asynchronous scope question received no answer before proceeding with the announced Photon Workshop-first scope.

| Model | Extension | Version |
|---|---|---:|
| Photon Mono X | pwmx | 516 |
| Photon Mono | pwmo | 515 |
| Photon Mono SE | pwms | 1 |
| Photon Mono SQ | pmsq | 515 |
| Photon Mono 4K | pwma | 516 |
| Photon M3 | pm3 | 516 |
| Photon M3 Max | pm3m | 516 |
| Photon Mono X 6K | pwmb | 516 |
| Photon Mono X2 | pmx2 | 517 |
| Photon Mono X 6Ks | px6s | 517 |
| Photon M3 Plus | pwmb | 517 |
| Photon M3 Premium | pm3r | 517 |
| Photon Mono 2 | pm3n | 517 |
| Photon Mono M5 | pm5 | 517 |
| Photon Mono M5s | pm5s | 518 |
| Photon Mono M5s Pro | m5sp | 518 |
| Photon Ultra | dlp | 516 |
| Photon D2 | dl2p | 517 |

All models are Anycubic. Profiles describe native file generation, not physical firmware certification. The UI explicitly labels new/custom profiles experimental. Recipe selection/calibration remains necessary. PrinterCatalog.cs holds factual raster dimensions, resolution, orientation, format, and usable-area choices; it copies no UVtools source or preset file contents.

### Code and migration

- PhotonWorkshopFormat checks supported extension/version combinations, resolution, dimensional bounds, machine text, numeric/preview values and 32-bit file capacity before output is opened. Wrong output suffix is rejected. CLI reports export errors instead of throwing past its command boundary.
- PhotonWorkshopWriter varies the mark/HEADER/table layout; 517 adds SOFTWARE/MODEL; 518 adds expanded MACHINE with two pixel pitches, SUBIMGS, and a second 330x190 RGB565 preview. High-speed intelligent mode and TSMC remain disabled. Existing single-stage settings and baked 16-level pixels are used.
- PerLayerSettings and MachinePropertyFields are persistable. Legacy defaults remain true/1; new profiles use false/7 (false/15 for 518), informed by primary samples and the format reference. No claim is made that every firmware accepts these fields identically.
- PrintWidthMm/PrintHeightMm are optional centred usable-area limits independent of display dimensions. Rasterization uses full LCD pitch and masks margins. Null retains legacy full-display behaviour. Conservative limits: Mono/SE 80x130; Mono 4K 132.9x80; M3 163.84x102; M3 Max 298x164; Mono 2 143.36x89.1. Other profiles retain upstream display extents pending hardware envelope checks.
- Preferences exposes print width/depth, native format status and experimental notice. Built-in edits create copies. Existing custom IDs that collide with newly introduced catalog IDs are preserved, not overwritten; old embedded projects retain legacy options through defaults. The original Mono X built-in recreation rule remains.
- High-resolution slicing bounds raster worker concurrency with a conservative allowance for pooled-buffer rounding (256 MiB raster budget, at least one worker); total application memory is not guaranteed by that budget. Invalid/overflowing raster dimensions are rejected before allocation.
- Reader now rejects truncated/out-of-range tables, unsupported codecs and headers too short for their advertised version; exposes key machine/header fields for inspection. It remains a strict pw0Img reader, not a universal importer.

### Reference provenance and new evidence

Primary format reference: [UVtools binary template](https://github.com/sn4k3/UVtools/blob/3e3c62faef56a4ae77e2c652cf4914472ec7aa66/Scripts/010%20Editor/PhotonWorkshop.bt) and [Anycubic format implementation](https://github.com/sn4k3/UVtools/blob/3e3c62faef56a4ae77e2c652cf4914472ec7aa66/UVtools.Core/FileFormats/AnycubicFile.cs). Model facts: [pinned printer profiles](https://github.com/sn4k3/UVtools/tree/3e3c62faef56a4ae77e2c652cf4914472ec7aa66/PrusaSlicer/printer). Consult the previous task-07 report for the manufacturer product/manual links and four original sample hashes. All accessed 2026-09-11.

Two additional manufacturer-catalogue downloads:

- [TEST.pm3n](https://cdn.shopify.com/s/files/1/0245/5519/2380/files/TEST.pm3n?v=1682231451), 17,172,442 bytes, SHA256 4618F83577BB2574C3B1914A1237569C065B40DE4B03ADEE2FCF2F9C1F32F4D6: actually version **516**, not 517. This new observation qualifies the historical report's Mono 2 version assumption. Writer permits both for pm3n; seeded profile follows the current 517 profile. No universal firmware guarantee follows.
- [TEST.px6s](https://cdn.shopify.com/s/files/1/0245/5519/2380/files/TEST.px6s?v=1684829964), 16,897,538 bytes, SHA256 55DC66F8778BEC25EBD24E704BC5E7AE554BB8D4126B3A5BBA964BB641AA8822: version **517**.

Manufacturer M5s Drive links attempted here returned 404. There is no manufacturer-518 binary validation claim. Structural 518 evidence is the primary format description and independent UVtools decode. An initial exploratory loop continued after the missing M5s file and printed stale buffer values; those values were discarded and are not evidence. Only the successful independently hashed downloads above were used.

### Completed validation

- Focused initial run: 48 passed, one old assertion failed because deleting a custom copy now selects the preceding built-in (D2) rather than the formerly sole Mono X. Updated assertion preserves the intended rule: selected entry remains built-in and cannot be deleted.
- Full Release regression run: **1,030 passed, zero failed/skipped**. Includes version-specific fixed byte offsets, layer data and independent small-image decoder, malformed-version/truncated-file rejection, validation before overwrite, suffix mismatch, margin rasterization, catalog serialization and custom-ID collision preservation.
- Separate temporary audit harness generated two layers at each model's **real LCD resolution**. A baseline writer compiled from our own dc4664c source generated the same Mono X file **byte for byte** (file SHA256 73712022E3E66FF8455E54C8235EFC338A40D5DE831FAB44BF1FBD4279673603).
- Official portable **UVtools v6.2.0**, running separately from %TEMP%/danslicer-native-validation, extracted all 18 exports. Python/Pillow independently compared **36 PNG layers**, full pixel buffers, dimensions and primary/secondary thumbnail sizes to the original asymmetric images. All passed. Evidence: evidence/native-printer-writers/independent-validation.json and uvtools-extract.log. The portable CLI returns exit 1 even on successful extraction; actual output files and pixel hashes determined success, not an assumed zero exit code.
- Installed C:/Program Files/UVtools CLI failed at startup with MissingMethodException in System.CommandLine. It was left untouched. Official portable ZIP was downloaded/extracted only to temporary storage; not bundled or linked with Danslicer.
- Our strict reader decoded first layers of manufacturer M3, Mono 2, SQ and 4K samples. The old SE sample has trailing payload beyond its raster and is rejected by the strict decoder; no change was made to silently accept arbitrary trailers. That foreign-input limitation is distinct from the newly written version-1 files, which UVtools independently decoded correctly.
- Native workspace capture passed with printer-specific assertions: selected Mono 4K, verified its experimental/native-format notices, edited print width through ExpressionBox, confirmed creation and persistence of a custom copy with unchanged LCD dimensions, then deleted the copy. Visual review found a misplaced mirror hint overlapping the printer list; moved it under the mirror controls and verified the corrected screenshot. Final evidence is in evidence/native-printer-writers/settings-printers.png and the capture markers alongside it. All ten workspace/settings capture success markers were produced with no error marker, using isolated configuration.
- Existing compiler warnings remain. Initial restricted NuGet signature lookup failed; approved restore succeeded. No new external package dependencies added.

### Reproduce and integrate

Build/test from this worktree:

```powershell
dotnet test tests/Danslicer.Tests -c Release -p:UsedAvaloniaProducts= --nologo
& "C:/Users/plane/.codex/worktrees/a19b/Danslicer-chatgpt/src/Danslicer.App/bin/Release/net10.0/Danslicer.App.exe" --workspace-capture "C:/Users/plane/.codex/worktrees/a19b/Danslicer-chatgpt/artifacts/native-writer-ui-recheck"
```

The native capture isolates user configuration. Normal main-app launch is the same executable without capture arguments. To recheck with external UVtools, generate an export, run its portable `extract <file> <new-directory>`, then compare decoded layers and metadata; do not resave the input when validating writer output.

Recovery: same branch/worktree/base as above. Implementation and evidence are committed together; the source hash is recorded in the completion checkpoint below. Task-06 estimates and task-08/09/10 queued work are not included; no main merge/push or other checkout files touched. Integration must preserve task-06 changes deliberately, particularly Slicer.cs and export consumers. Only small Preferences additions were made; a future dedicated printer editor should retain these fields/status labels.

Limits: no physical print/firmware/LCD-revision certification, full production-sized job qualification, model-specific intelligent/auto-refill features, transition ramps or two-stage motion. PWS bitplanes, legacy CTB/Photon, newer Anycubic ZIP formats (such as M7/P1), CTB/GOO/CXDLP and other families remain unimplemented and are rejected. They require subsequent native writers and validation; this increment does not claim all UVtools printers.
