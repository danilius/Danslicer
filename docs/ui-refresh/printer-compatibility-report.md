# Other printers Danslicer can support now

Assessment date: **11 September 2026**. Implementation audited: **dc4664cd7321030cfe04b866815427b6b4f8a531**, the completed task-05 baseline. Task 06 is independent and was not inspected. This report changes no printer support.

**The Photon Mono X is the only established Danslicer target. No additional model met the requested strict configuration-only threshold.** The best next investigations are **Photon Mono 4K, Photon M3 and Photon Mono SQ**. Their pixel formats are promising, but manufacturer test files expose unresolved header/firmware differences. “Unverified” below does not mean incompatible; it means there is insufficient evidence to tell someone to print directly from this baseline.

## Practical model list

A = existing tested implementation; B = all required format details matched, only physical validation pending; C = needs code/new format support for the documented output; D = unresolved evidence. Each model name is exact; suffixes such as 4K, X, X2 and 6Ks are not interchangeable.

| Class | Exact model | Native output / evidence | Decision now |
|---|---|---|---|
| A | Anycubic Photon Mono X | `.pwmx`, 516; built-in 192 × 120 × 245 mm, 3840 × 2400, mirror X only | Use existing profile. Software export tests exist; historical user-reported mirror test passed. No exact firmware ID or full hardware certification recorded. [Manufacturer specifications](https://store.anycubic.com/products/photon-mono-x-resin-printer). |
| B | **None established** | Matching an extension and resolution is insufficient | Manufacturer sample differences below must first be resolved. |
| C | Anycubic Photon Mono 2 | `.pm3n`, profile version 517 | Version-aware writer changes required. [Profile](https://github.com/sn4k3/UVtools/blob/3e3c62faef56a4ae77e2c652cf4914472ec7aa66/PrusaSlicer/printer/Anycubic%20Photon%20Mono%202.ini), [manufacturer Workshop manual, p. 10](https://wiki.anycubic.com/photon-mono-m7-pro/anycubic_photon_workshop-en-v0.2.0-b.pdf). |
| C | Anycubic Photon Mono M5 | `.pm5`, 517; 11520 × 5120, unequal X/Y pixel pitch | Needs correct newer layout and rectangular-pixel metadata. [Profile](https://github.com/sn4k3/UVtools/blob/3e3c62faef56a4ae77e2c652cf4914472ec7aa66/PrusaSlicer/printer/Anycubic%20Photon%20Mono%20M5.ini). |
| C | Anycubic Photon Mono M5s | `.pm5s`, 518 | Requires newer tables; high-speed/intelligent features are outside this baseline. [Profile](https://github.com/sn4k3/UVtools/blob/3e3c62faef56a4ae77e2c652cf4914472ec7aa66/PrusaSlicer/printer/Anycubic%20Photon%20Mono%20M5s.ini). Anycubic provides model-specific files in its [download catalogue](https://ca.anycubic.com/pages/firmware-software). |
| C | ELEGOO Mars 3 | `.ctb` | No CTB writer. Manufacturer describes its firmware/CTB integration in its [Mars 3 statement](https://www.elegoo.com/blogs/news/newest-statement-of-the-chitubox-board-firmware-on-mars-3). |
| C | Creality HALOT-MAGE S | `.cxdlp` | No CXDLP writer. [Manufacturer manual](https://cdn.creality.com/ow/official/7fc0a409-41d5-46b6-a404-074b46702730.pdf) instructs saving CXDLP. |
| D | Anycubic Photon Mono 4K | `.pwma`, official sample 516 | **First investigation:** same codec/layout family, but non-configurable flags and printable-area metadata differ. See setup and binary evidence below. |
| D | Anycubic Photon M3 | `.pm3`, official sample 516 | **Second investigation:** same codec/layout family; fixed flags differ and machine-name/display-width conventions need resolution. |
| D | Anycubic Photon Mono SQ | `.pmsq`, official sample 515; UVtools profile 516 | **Third investigation:** exact pixel grid matches, but no verified firmware acceptance of Danslicer's 516 output. |
| D | Anycubic Photon Mono SE | `.pwms`, official sample 1; UVtools profile 516 | Must establish accepted firmware/version and usable screen margins; retaining 516 is speculative. |
| D | Anycubic Photon Mono | `.pwmo`, UVtools profile 516 | Similar lead to SE, but no manufacturer binary audited here; do not rotate the profile's 1620 × 2560 raster to match marketing dimension order. [Profile](https://github.com/sn4k3/UVtools/blob/3e3c62faef56a4ae77e2c652cf4914472ec7aa66/PrusaSlicer/printer/Anycubic%20Photon%20Mono.ini), [manufacturer downloads](https://ca.anycubic.com/pages/firmware-software). |
| D | Anycubic Photon M3 Max | `.pm3m`, UVtools profile 516 | Full raster 298.08 × 165.6 mm differs from manufacturer 298 × 164 mm build area. Baseline cannot describe those separately. Auto-refill behaviour and full-size memory use unvalidated. [Profile](https://github.com/sn4k3/UVtools/blob/3e3c62faef56a4ae77e2c652cf4914472ec7aa66/PrusaSlicer/printer/Anycubic%20Photon%20M3%20Max.ini), [manufacturer dimensions/resolution](https://uk.anycubic.com/blogs/news/2022-top-picks-from-all3dp-anycubic-kobra-photon-m3-max). |
| D | Anycubic Photon Mono X 6K | `.pwmb`, profile 516, Workshop 3.2.2 observation 517 | Conflicting version evidence; no verified firmware subset. [Profile](https://github.com/sn4k3/UVtools/blob/3e3c62faef56a4ae77e2c652cf4914472ec7aa66/PrusaSlicer/printer/Anycubic%20Photon%20Mono%20X%206K.ini), [format investigation](https://github.com/sn4k3/UVtools/discussions/865). |
| D | Anycubic Photon Mono X2 | `.pmx2`, profile 517; format library also permits 516 | Standard profile needs writer changes; theoretical older-version acceptance lacks model/firmware proof. [Profile](https://github.com/sn4k3/UVtools/blob/3e3c62faef56a4ae77e2c652cf4914472ec7aa66/PrusaSlicer/printer/Anycubic%20Photon%20Mono%20X2.ini). |

This is a prioritized assessment, not an exhaustive list of every resin printer. Models absent from the table have no support claim. External conversion is a separate workflow requiring its own validation; the UVtools launcher does not add writers to Danslicer.

## What the baseline actually implements

Repository evidence below is pinned to the baseline commit, with one-based source lines:

| Capability | Implementation evidence and limit |
|---|---|
| Printer collection | `src/Danslicer.Core/Printers/PrinterDefinition.cs:7`, `:49`; `Config/UserConfig.cs:817`: one built-in Mono X. Arbitrary custom names, extension, dimensions, resolutions, two mirror flags and a version integer are persistable. Normalization repairs positive values; it does not verify compatibility. |
| Setup UI | `src/Danslicer.App/ViewModels/PrinterEditorViewModel.cs:27`, `:138`, `:182`: Preferences printer editor can add/duplicate/edit. Editing built-in creates a copy. Preview size is persisted in the record (`PrinterDefinition.cs:34`) but has no editor field. |
| Actual export dispatch | `src/Danslicer.App/ViewModels/MainViewModel.cs:1369` always calls `PhotonWorkshopWriter`; `Views/MainWindow.axaml.cs:887` uses the extension only for the file picker. CLI `SliceCommand.cs:49` defaults to Mono X; `:63` reads a project's embedded printer; `:122` calls the same writer. No implemented multi-format writer interface was found, despite the design document describing one. |
| Container/metadata | `src/Danslicer.Core/IO/PhotonWorkshopWriter.cs:16`, `:39`, `:116`, `:151`: fixed 52-byte file mark, 84-byte HEADER payload, 8 tables, 32-byte layer entries, pw0Img, MACHINE property 1, background 6506241, per-layer flag 1, no SOFTWARE/MODEL table. The version input only changes two integers. MACHINE name is ASCII, truncated to 95 bytes. |
| Pixel geometry | `src/Danslicer.Core/Slicing/LayerRasterizer.cs:38`, `:52`: row-major grayscale, independent X/Y scaling and mirrors; no explicit transpose, rotation or hardware margin field. `PrinterDefinition.cs:38` makes build volume equal full display dimensions. Writer `:42` stores only X pitch in the header. |
| Exposure/motion | `src/Danslicer.Core/Slicing/ResinSettings.cs:8`, `:22`: bottom/normal exposure and lift height/speed, shared retract speed and light-off delay; mm/min converted to mm/s in writer. No configurable second motion stage, transition exposure ramp, UV power, independent bottom retract, rest-after-lift/retract, tilt mechanism or intelligent resin handling. Writer forces transition count/type and advanced mode to zero; second-stage heights are zero despite stage-count fields of two. Firmware interpretation must be checked. |
| Images/resources | `PhotonRle.cs:4`: 4-bit grayscale pw0Img RLE; AA toggle yields 1/16 levels. Default preview 224 × 168 RGB565. Arbitrary large grids can be entered, but no high-resolution production memory/performance qualification exists; offsets are 32-bit. |
| Verification strength | `tests/Danslicer.Tests/SlicingTests.cs:132` checks output via the in-repo reader; `:194` checks synthetic dimensions/mirrors and that 517 is echoed. The latter does **not** prove a valid 517 structure. `docs/ui-refresh/RESUME.md:18` records task-05's 1013 passing tests and a 110-layer decode comparison; these were not rerun here. |

`docs/HANDOVER.md:11` records the user's successful physical Mono X mirror-X print on 2026-09-10. That is stronger than the stale “to be confirmed” source comment, but narrower than full printer/firmware certification. Task-05's separate hardware checks remained unperformed (`task-05-acceptance.md:97`). No new printing occurred in task 07.

For the external structural comparison, UVtools' pinned [Anycubic format implementation](https://github.com/sn4k3/UVtools/blob/3e3c62faef56a4ae77e2c652cf4914472ec7aa66/UVtools.Core/FileFormats/AnycubicFile.cs#L1412) distinguishes versions 1/515/516/517/518. Its encoder at line 2090 changes table counts and header sizes, and 517/518 add fields/tables. It permits 516 for the older extensions discussed here, but that extension-wide policy is not a firmware certificate. The fixed Danslicer writer cannot acquire those structures by changing FormatVersion.

## Concrete configuration leads, not qualified presets

Use **Preferences → Printers → Duplicate**, rename the copy, edit the fields below, then select it in the project. These are proposed investigation values, **not changes performed by this task**. Use 516, mirror X on/Y off and the default preview for these leads. Re-slice after selecting/changing the machine; never rename an already-sliced Mono X file. Select a separately calibrated resin recipe; Mono X exposure/motion defaults do not establish another printer's recipe.

| Candidate | Proposed MachineName / extension | Full raster X × Y mm; Z mm | Pixels X × Y | Unresolved setup limit |
|---|---|---|---|---|
| Mono 4K | `Photon Mono 4K` / `pwma` | 134.4 × 84; 165 | 3840 × 2400 | 35 µm profile pitch; official sample MACHINE reports 132.9 × 80. Do not shrink full raster dimensions to the printable area: that changes pixel scale. Needs an independently enforced usable envelope. [Pinned profile](https://github.com/sn4k3/UVtools/blob/3e3c62faef56a4ae77e2c652cf4914472ec7aa66/PrusaSlicer/printer/Anycubic%20Photon%20Mono%204K.ini), [manufacturer 4K specifications](https://de.anycubic.com/products/anycubic-photon-mono-4k). |
| M3 | `Anycubic Photon M3` / `pm3` (sample name) | 163.84 × 102.4; 180 | 4096 × 2560 | 40 µm profile pitch; sample width 163.92, source fallback name `Photon M3`. Establish which metadata is informational. [Pinned profile](https://github.com/sn4k3/UVtools/blob/3e3c62faef56a4ae77e2c652cf4914472ec7aa66/PrusaSlicer/printer/Anycubic%20Photon%20M3.ini), [manufacturer M3](https://store.anycubic.com/products/photon-m3). |
| Mono SQ | `Photon Mono SQ` / `pmsq` | 120 × 128; 200 | 2400 × 2560 | Manufacturer lists 128 × 120 and 2560 × 2400 in physical presentation order; sample confirms the profile raster order. Need 516 firmware acceptance first. [Pinned profile](https://github.com/sn4k3/UVtools/blob/3e3c62faef56a4ae77e2c652cf4914472ec7aa66/PrusaSlicer/printer/Anycubic%20Photon%20Mono%20SQ.ini), [manufacturer SQ specifications](https://store.anycubic.com/products/photon-mono-sq). |

For Mono/SE, profile screen dimensions are 82.62 × 130.56, 1620 × 2560, Z 165/160 respectively; [SE profile](https://github.com/sn4k3/UVtools/blob/3e3c62faef56a4ae77e2c652cf4914472ec7aa66/PrusaSlicer/printer/Anycubic%20Photon%20Mono%20SE.ini). These remain lower-priority because exact usable margins and firmware acceptance are unresolved.

## Manufacturer binary checks and qualification gate

Read-only inspection of four files linked by Anycubic's [firmware/software catalogue](https://ca.anycubic.com/pages/firmware-software), downloaded on 2026-09-11, found:

| Official sample | Version / tables / HEADER payload | Raster; preview | Relevant difference from Danslicer |
|---|---|---|---|
| [TEST.pwma](https://drive.google.com/file/d/1HX9YYRI6KGxk4ySAX1ZUHL4SQaXS-9EZ/view) | 516 / 8 / 84 | 3840 × 2400; 224 × 168 | PerLayer=0, MACHINE property=7; Danslicer forces 1/1. pw0Img and background 6506241 match. MACHINE area 132.9 × 80 × 165. |
| [TEST.pm3](https://drive.google.com/file/d/13QaDV5VJf76fvYYGY_U_OzZ1WrnGKF6T/view) | 516 / 8 / 84 | 4096 × 2560; 224 × 168 | PerLayer=0, property=7; name `Anycubic Photon M3`; area 163.92 × 102.4 × 180. pw0Img/background match. |
| [TEST.pmsq](https://drive.google.com/file/d/1lJrFGVIHZm3L8Tx8k8Z3W6r1361h3aEy/view) | 515 / 5 / 80 | 2400 × 2560; 224 × 168 | Different version/layout, PerLayer=0. |
| [TEST.pwms](https://drive.google.com/file/d/1jA3q1xK50SQeRyhYLS5AWuTwk9mDBPmw/view) | 1 / 4 / 80 | 1620 × 2560; 224 × 168 | Different version/layout, PerLayer=0. |

These are actual downloaded header observations, not firmware execution results or a complete bitmap/CRC validation. Sample publishing dates and target firmware revisions are not embedded in this assessment. A sample's version proves that sample's structure, not exclusion of other accepted versions. Exact hashes and parsing offsets are in `task-07.md`.

The [format maintainer's April 12, 2024 explanation](https://github.com/sn4k3/UVtools/discussions/865) explicitly leaves some metadata semantics uncertain and notes per-layer flag problems on some machines. This does not establish that the older candidates fail, but prevents treating flag differences as harmless without evidence.

To qualify an additional model: record its firmware and LCD revision; compare a freshly generated manufacturer-slicer file against Danslicer for raster geometry, all header/table/codec/preview fields and flags; establish which differences firmware permits. Verify independent decoding and asymmetric X/Y/margin geometry. Only then perform a separately authorized small physical validation of orientation, dimensions, bottom/normal exposure, lift/retract and waits. If a mandatory fixed flag or layout differs, that candidate moves to C and needs an implementation task. Task 07 authorizes none of those implementation or hardware actions.

Sources were accessed 2026-09-11. Manufacturer pages are live and generally undated; historical dates above refer to explicitly dated discussions or the repository's recorded test. UVtools source/profiles were pinned to **3e3c62faef56a4ae77e2c652cf4914472ec7aa66** using GitHub's API, because cached `master` web results differed from the current file. UVtools is an independent primary format implementer, not an Anycubic firmware specification; no upstream code was copied into Danslicer.
