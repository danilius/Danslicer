# Printer compatibility

The selected profile defines physical dimensions, usable plate area, screen resolution, mirroring, native writer, and format version. **Matching a filename extension alone is insufficient.**

The Mono X profile records user-confirmed mirror orientation. Other profiles are labelled **experimental: physical printing unverified**. Automated file validation and successful export do not establish physical printer compatibility.

## Native writers

| Family | Supported writer identifiers / extensions | Material limitations |
| --- | --- | --- |
| Photon Workshop | `photon-workshop`; profile-specific extensions such as `.pwmx` | Profile determines accepted extension/version; grayscale support varies by format. |
| GOO | `goo`; `.goo`, version 3 | Uses the GOO native writer. |
| Chitu | `ctb` v2, `ctb-encrypted` v5 (`.ctb`); `phz` v2 (`.phz`) | 7-bit layer grayscale. Tilt-vat profiles leave peeling to firmware. |
| Creality | `cxdlp` v3 (`.cxdlp`), `cxdlp-v4` v4 (`.cxdlpv4`) | v3 quantizes exposure to 0.1 s; bottom exposure, lift height, and delay to whole units; speeds to whole mm/s; minimum delay 1 s. v4 uses 7-bit grayscale. |
| Prusa | `sl1`; `.sl1` / `.sl1s`, version 1 | Firmware controls tilt; bottom count is interpreted as exposure fade layers. |
| Archives | `cws` / `cws-rgb` (`.cws`), `chitu-zip` (`.zip`), version 1 | Container identity matters even where suffixes are shared. |
| Longer | `lgs`; `.lgs`, `.lgs30`, `.lgs120`, `.lgs4k`, version 1 | 16-level grayscale; firmware controls retract motion. |
| Anet | `anet`; `.n4` / `.n7`, version 3 | Binary layers; exposure/height round up to whole units and lift speed to whole mm/s. One lift setting; firmware controls retract/delay. |
| Flashforge | `svgx`; `.svgx`, version 1 | Binary vector layers; firmware controls peel and delays. |

List the exact built-in profiles using:

```powershell
dotnet run --project src/Danslicer.Cli/Danslicer.Cli.csproj -- printers
```

Use the closest actual profile as the basis of a user copy. The GUI does not turn arbitrary extension/version combinations into new writer implementations. Export validation rejects unsupported combinations and field limits.

For developers, the repository contains format audits and detailed compatibility notes in `docs/ui-refresh/printer-compatibility-report.md`, `native-printer-writers.md`, and `multi-brand-native-writers.md`.
