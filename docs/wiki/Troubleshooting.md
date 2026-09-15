# Troubleshooting

| Symptom | Check |
| --- | --- |
| Shortcut does something unexpected | Check Layout/Support/Slicing and keyboard focus. R is rotate in Layout and ring in Support. Esc exits a modal tool. |
| A command is unavailable | Select a model/support target and switch to the command's workspace. Generation and detection also disable while busy. |
| Supports look missing | Check Visibility mode, individual element switches, H/Shift+H hiding, and the isolation interval. Reset isolation and unhide. |
| Cannot pick a support behind the model | Enable Select Through, rotate underneath, or isolate the relevant height. Turn region painting off before selecting. |
| Some contacts were refused | Read the status/preview counts. Check downward-face filter, line of sight, member reach/angle, minimum gaps, branch height, and base clearance. |
| A settings change did not alter existing supports | Use the Structure preview and Apply, or regenerate. Loading/saving a preset alone is not a rebuild. |
| Raft did not update | Press Add raft again to take a fresh settings snapshot. |
| Supports disappeared after a transform | Some transforms invalidate supports. Undo if accidental, or regenerate after completing layout. |
| A model imports at the wrong size | Verify its source units and resize in Layout. Check physical dimensions before slicing. |
| Island detection finds nothing or misses a detail | Check target, layer height, minimum island area, and whether existing supports already cover the findings. Inspect slice layers too. |
| Cannot export | Slice in Slicing mode; check stale results, bounds warnings, selected printer, filename extension, and profile/version validation. |
| Exported print is mirrored | Check printer layer-image mirroring separately from object mirroring. Use the exact printer profile. |
| UV check is disabled | Configure a valid UVtools executable and export the current slice first. |
| Viewport effects are absent | Some effects require Deferred. Classic or an automatic fallback will omit them. Wireframe and basic plate effects work on both paths. |
| SpaceMouse is absent | Input integration is Windows-only. Check device connection and the repository's SpaceMouse diagnostics. |

## Reporting a bug

Include the app build/commit, operating system, printer profile, exact operation, and expected versus actual result. A small reproducible mesh/project and screenshot are helpful when shareable. Include refusal/error text rather than only saying the operation failed.

[Report an issue](https://github.com/danilius/Danslicer/issues) · [Guide home](Home.md)
