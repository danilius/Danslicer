# Getting started

## Download and launch

**[Download the latest release](https://github.com/danilius/Danslicer/releases/latest)** for a ready-to-run app. The current release is **[v1.0.0 — Build 1](https://github.com/danilius/Danslicer/releases/tag/v1.0.0)** for Windows x64.

1. Download **[Danslicer-build-1-win-x64.zip](https://github.com/danilius/Danslicer/releases/download/v1.0.0/Danslicer-build-1-win-x64.zip)** from the release's Assets section.
2. Extract the **entire archive** into a folder.
3. Run **Danslicer.App.exe** from that folder. Keep all extracted files together.

No installer or separate .NET installation is required. GitHub's automatically generated **Source code** archives are for developers; choose the Windows ZIP to run the app. A `SHA256SUMS.txt` file is available on the release page for verifying the download.

Once the app opens, continue with **Your first project** below.

## Build from source (optional)

From the repository root, with the .NET 10 SDK installed:

```powershell
dotnet build src/Danslicer.App/Danslicer.App.csproj -c Debug
dotnet run --project src/Danslicer.App/Danslicer.App.csproj -c Debug --no-build
```

Debug output is in `src/Danslicer.App/bin/Debug/net10.0`. For self-contained bundles, see the repository's `docs/CROSS-PLATFORM-BUILDS.md` and `build/publish.ps1`. Windows x64, Linux x64, and macOS ARM64 are publishing targets; target-machine verification is separate.

## Your first project

1. Open **Layout**. Use **File → Import STL…**, the insert-object command, or `Ctrl+I`. The mesh loader accepts STL and OBJ; the menu retains the STL label.
2. Open **Print settings**, select the exact printer profile, and choose a resin recipe. Confirm the profile's usable plate, resolution, and compatibility note.
3. Select the object in **Objects**. Orbit with the middle mouse button, pan with `Shift` + middle mouse button, and zoom with the wheel.
4. Arrange the model. `G` moves, `R` rotates, `S` scales. Press an axis key to constrain, enter a number if needed, and confirm with `Enter`. `F` lets you pick a face to lay flat.
5. Decide the height above the plate. Auto drop seats the object; raised placement leaves room for supports. See [Layout and projects](Layout-and-projects.md).
6. Switch to **Support** and choose the model to work on in **Objects**. Choose a support preset, then **Generate supports**. Inspect the status for contacts that could not be routed.
7. Use **Detect islands → Run detection** and inspect the findings. Add island supports or [manual contacts](Manual-and-guided-supports.md) where needed. Review the underside and tight areas using isolation and visibility controls.
8. Optionally [parent and brace](Parenting-and-bracing.md) the supports and [add a raft](Rafts.md).
9. Save with `Ctrl+S` as a `.danslicer` project.
10. Switch to **Slicing**, review print settings, and click **Slice** (`Ctrl+R`). Inspect the layer slider and estimates, then **Export** (`Ctrl+E`). Use the selected printer's extension.

Reslice after changing geometry or print settings. A saved project is the editable scene; the exported print file is the printer-ready result.

## Finding controls

The left toolbar changes with the workspace. Open a tool to show its settings popout; its close control or `Esc` dismisses it. Toolbar labels and width help when learning the icons. Preferences includes search, and the status bar shows contextual hints and operation results.

## Editing numeric settings

Click a rectangular numeric field (or focus it and press Enter) to type a value or arithmetic expression such as `12/2`. Drag to scrub the value; hold **Shift** for finer adjustment. **Escape** cancels the edit. Units beside the value distinguish mm, degrees, pixels, and scalar values. Preview-enabled settings update the scene while editing, then commit on completion.

Next: [Layout and projects](Layout-and-projects.md) · [Shortcut reference](Keyboard-shortcuts.md)
