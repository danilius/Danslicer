# Multi-brand native writers

Completed 2026-09-11 on `codex/native-printer-writers`, following the Photon Workshop increment at `4178d7f`. The user authorized all nine additional brands discussed, with experimental profiles and physical validation left to testers.

## Coverage

Added **97 profiles across nine brands**, bringing the catalog to **115** including the existing 18 Anycubic profiles.

| Brand | Added profiles |
|---|---:|
| Elegoo | 22 |
| Phrozen | 17 |
| EPAX | 16 |
| Creality | 15 |
| Nova3D | 11 |
| Flashforge | 8 |
| Longer | 4 |
| Anet | 2 |
| Prusa | 2 |

These are selectable experimental export profiles, not hardware certifications or a claim to support every model and firmware from each brand. The complete model list is in `MultiBrandPrinterCatalog.cs` and the evidence JSON. Profiles use geometry and format associations from the pinned reference; firmware acceptance remains unverified.

| Native format identity | Implemented container | Profiles |
|---|---|---:|
| goo | GOO 3 | 10 |
| ctb | CTB 2 | 20 |
| ctb-encrypted | Encrypted CTB 5 | 21 |
| phz | PHZ 2 | 1 |
| cxdlp | CXDLP 3 | 10 |
| cxdlp-v4 | CXDLP v4 | 2 |
| sl1 | SL1 / SL1S ZIP | 2 |
| cws / cws-rgb | Grayscale / RGB CWS ZIP | 10 |
| chitu-zip | Chitubox ZIP | 7 |
| lgs | LGS / LGS30 / LGS120 / LGS4K | 4 |
| anet | N4 / N7 version 3 | 2 |
| svgx | SVGX DLP-II 1.1 | 8 |

Existing Photon Workshop versions 1 and 515–518 remain supported. GOO 5, other CTB variants, newer Anycubic archive families, and every remaining UVtools format are outside this increment. A shared suffix does not imply identical containers: encrypted CTB is selected by explicit format identity.

## Implementation and limits

Writers, binary/image helpers, checksums, compression, archives and encrypted CTB output are implemented in Danslicer using .NET facilities. UVtools is an external reference and validator; no UVtools source, library, executable or preset is bundled or required for normal export.

Both GUI and CLI route through `NativePrintWriter`. Printer format identity survives profile copying and persistence. Unsupported format/version/suffix combinations are rejected. File exports use a temporary sibling and replace the destination only after encoding succeeds. Changing the selected printer now invalidates a cached slice before export; the native UI workflow test exposed and verified this fix.

Separate bottom and normal lift settings are encoded where the format supports them. Mars 5 Ultra and Saturn 4 Ultra 12K/16K explicitly use firmware-controlled peeling and the required motion placeholders. The profile editor displays format limitations.

- CTB, PHZ and CXDLP v4 quantize grayscale to seven bits; Longer has 16 levels. Anet and SVGX are binary.
- CXDLP 3 rounds exposure to tenths, bottom exposure/lift heights/delays to whole units and speeds to whole mm/s, with a minimum one-second delay.
- Anet rounds exposure and lift settings to whole units, uses one lift setting for all layers, and leaves retract/delay behavior to firmware.
- Prusa firmware controls tilt and interprets the bottom count as exposure fade layers. SVGX firmware controls peeling and delays; Longer firmware controls retract motion.
- Physical printing, firmware/version acceptance, very large production jobs and printer-specific motion behavior still require user validation.

## Validation

**UVtoolsCmd 6.2.0 successfully read all 115 generated exports.** The permanent audit generates three asymmetric layers at each profile's actual LCD resolution, including isolated edge pixels, horizontal/vertical runs, grayscale patches and a fully blank middle layer. All **345 layer hashes** matched independently decoded pixels after the documented format quantization. The audit also checks layer count, Z, exposure, previews and applicable geometry/motion metadata, with deliberately different bottom/normal motion settings.

UVtools 6.2 has a CWS exposure-reader defect: its G-code parser discards `;<Delay>` exposure commands and reports zero exposure, including for its own generated CWS files. For those ten profiles, the audit checks the actual archive cure commands independently; UVtools still validates their decoded pixels and remaining layer metadata. This exception is labelled per profile in the report. This CLI build also returns exit code 1 after successful extraction, so the audit validates fresh outputs rather than trusting the exit code alone.

- Full regression suite: **1,053 passed, zero failed/skipped**.
- Native app capture: all **12 success markers**, including multi-brand profile selection/copying and export after changing printers.
- Actual GUI support/raft workflow: a **110-layer GOO** export successfully opened and extracted through UVtools.
- Actual CLI STL workflow: a **20-layer GOO** export using `--printer elegoo-mars-4-dlp` successfully opened and extracted through UVtools.
- Existing Photon Workshop dispatch is tested for byte-identical output.
- Final CXDLP field-limit validation additionally covers bottom lift settings; regression tests rerun after that check.

Evidence: [per-profile validation report](evidence/multi-brand-native-writers/validation.json), [profile editor capture](evidence/multi-brand-native-writers/settings-multi-brand.png), and the native UI success markers in that directory. Generated print files and downloaded references remain in ignored artifacts/temporary storage.

## Reproduce

Install .NET 10, Python with Pillow, and a separate working UVtoolsCmd. Use a new audit directory for every run; the validator refuses pre-existing extraction outputs.

```powershell
dotnet run --project tests/Danslicer.FormatAudit -c Release -- artifacts/audit-new
python tests/Danslicer.FormatAudit/validate.py artifacts/audit-new C:/path/to/UVtoolsCmd.exe
dotnet test tests/Danslicer.Tests -c Release --no-restore -p:UsedAvaloniaProducts=

Danslicer.Cli.exe printers
Danslicer.Cli.exe slice model.stl -o model.goo --printer elegoo-mars-4-dlp
UVtoolsCmd.exe --no-progress extract model.goo extracted
```

Reference snapshot: [UVtools 3e3c62faef56a4ae77e2c652cf4914472ec7aa66](https://github.com/sn4k3/UVtools/tree/3e3c62faef56a4ae77e2c652cf4914472ec7aa66), specifically factual format structures under `Scripts/010 Editor` / `UVtools.Core/FileFormats` and model parameters under `PrusaSlicer/printer`. [Official CLI documentation](https://github.com/sn4k3/UVtools#command-line).

This increment is independent of task-06 and the unrelated queued task documents. No changes were made to other checkouts or main, and nothing was pushed or merged.
