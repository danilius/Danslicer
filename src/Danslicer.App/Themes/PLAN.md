# Stylesheet proposal — branch `theme-claude`

**STATUS: DONE (2026-09-04).** ClassicTheme.axaml, CarbideTheme.axaml and (bonus) SlateTheme.axaml
are built and wired through ThemeCatalog/UserConfig.Theme/Preferences → Appearance, MainWindow's
viewport toolbar and popup-close glyphs are now IconSet PathIcons, and ThemeTests.cs covers token
parity, catalog listing and config round-trip. See CarbideTheme.rationale.md for the design writeup
and CLAUDE-LANE.md for the screen-test checklist. The plan below is kept as the as-designed record;
see the worker's final report for any place the built version diverged from it.

Original WIP note (parked 2026-09-04, session handover):

User directive: one or more complete app stylesheet proposals — dark, clear icons,
coherent, Blender-leaning; viewport + layout grid OUT of scope. Deliverable shape
(mirrors ChatGPT job 031 so the user can compare like-for-like): palette tokens +
restyled control templates + inline StreamGeometry icon set on a stated grid,
runtime-selectable in Preferences, today's look as default, rationale doc.

State: `IconSet.axaml` committed (shared 16x16-grid StreamGeometry icons: objects,
supports, visibility, rafts, view settings, close). Nothing else built yet.

Planned architecture (validated against the codebase, ready to execute):
1. Per-theme ResourceDictionary in this folder, all defining the SAME token keys
   (brush/color: AppSurface, AppSurfaceAlt, AppPanel, AppHeader, AppBorder,
   AppTextPrimary/Muted/Faint, AppAccent, AppPopupBackground; geometry: the IconSet
   keys, merged from IconSet.axaml) plus FluentTheme resource-key overrides
   (SystemAccentColor and per-control brushes — app is Dark-only, plain keys resolve
   ahead of FluentTheme's own).
   - `ClassicTheme.axaml` — today's exact hexes tokenized (#23262A panels, #1F2124
     header, #151719 splitter, #F523262A popups, #FFB35C accent): the default.
   - `CarbideTheme.axaml` — the Blender-4.x-leaning proposal: #1D1D1D window,
     #232323 panels, #191919 headers, #E6E6E6/#9E9E9E/#6E6E6E text, Blender orange
     #E87D0D accent, 3px corner radius, flat #2E2E2E controls.
   - Optional third palette (cool slate, blue accent) — cheap once tokens exist.
2. `ThemeCatalog.cs` (plain class): names → dictionary URIs; Apply() swaps a dedicated
   slot in Application.Resources.MergedDictionaries at startup and on change.
3. Config: `UserConfig.Theme` string, default Classic; Preferences gains an
   Appearance section with the selector (live apply via the Saved/redraw pattern).
4. Sweep MainWindow/ConfigWindow/SupportSettingsView/SupportPresetEditorWindow
   hardcoded hexes (~12 in MainWindow, fewer elsewhere) to {DynamicResource} tokens;
   viewportTool/popupClose buttons switch text glyphs to PathIcon + icon tokens.
5. Tests: token-key parity across theme axaml files (regex x:Key extraction),
   catalog listing, config round-trip.
6. Rationale doc per proposal for the user's screen comparison.
