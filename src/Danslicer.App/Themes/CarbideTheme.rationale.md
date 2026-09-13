# Carbide theme — design rationale

Carbide is the Claude-lane's Blender-leaning proposal for the dark-theme bake-off. The brief was
chrome/controls/icons only — the 3D viewport and the layout grid are untouched by any theme.

## What it's going for

Blender 4.x's own UI is the reference point: a flatter, cooler-neutral dark grey than Danslicer's
current chrome, a clear step between "chrome" and "content" panels via a darker header/toolbar band,
and a single warm accent colour reserved for state — the active tool, a checked toggle, the selected
list row, a focused text field. Everything else stays deliberately quiet so the accent (and the
3D content, still out of scope here) is what draws the eye.

- **Window / surface** `#1D1D1D` — near-black, slightly warmer than pure grey.
- **Panels** `#232323` — the right-hand properties strip, Preferences body, popups.
- **Headers / toolbars** `#191919` — darkest band, separating menu/toolbar chrome from panel content
  (Blender's header bars read as a distinct strip from the properties editor beneath them).
- **Alt surface** `#262626` — text fields, combo boxes, list rows: a touch lighter than the panel so
  interactive surfaces read as "sitting on" the chrome rather than blending into it.
- **Borders** `#101010` — hairline dividers, kept nearly invisible so structure comes from the flat
  colour steps above, not from drawn lines (another very Blender trait).
- **Text** `#E6E6E6` primary / `#9E9E9E` muted / `#6E6E6E` faint — three legible steps without ever
  reaching pure white, which reads as harsh against this palette.
- **Accent** `#E87D0D` — Blender's own orange, used only for the checked/selected/focused state
  (workspace toggle, tool buttons, checkboxes, slider fill, selected combo row, tab pipe). Nothing
  else in the chrome is coloured, so the accent stays meaningful.

## What actually changed vs. Classic

Both palettes plug into the same token set (`AppSurface`, `AppPanel`, `AppHeader`, `AppBorder`,
`AppText*`, `AppAccent`, `AppPopupBackground`) and the same FluentTheme resource-key overrides
(`SystemAccentColor*`, `Button*`, `ComboBox*`, `CheckBox*`, `TextControl*`, `Slider*`,
`MenuFlyout*`, `TabItem*`, `ToolTip*` — see ClassicTheme.axaml/CarbideTheme.axaml for the exact
list), so switching the Preferences → Appearance → Theme dropdown re-skins every window without a
restart. The viewport toolbar and popup-close glyphs are now `PathIcon` shapes from the shared
`IconSet.axaml` instead of hardcoded text characters (▦, Y, ◉, ≋, ⚙, ×) — both because it looks
sharper at any DPI and because a plain text glyph can't recolour itself per theme the way a
`PathIcon` bound to `AppTextPrimary` can.

## Deliberate compromises (given the scope)

- Not every hardcoded hex in the swept files became a token — a couple of one-off accents
  (the viewport-tool button's `#7A858F` border, the keymap hint text's `#666C72`, the header
  separator's `#444`, and the validation-error red `#EF5350`) were left literal because they're
  either genuinely theme-independent (semantic red) or changing them risked a visible regression
  in Classic's pixel-for-pixel look for a marginal gain.
- The FluentTheme override list covers the controls actually visible in this app (buttons, toggle
  buttons, combo boxes, checkboxes, text fields, sliders, menus, tabs, tooltips) rather than every
  key Fluent exposes — deep control states (e.g. every disabled/pressed permutation) inherit
  Fluent's own dark-mode values, which already read fine against either palette in testing.
- `ControlCornerRadius` was left at Fluent's own default (3px) for both themes — Blender's own
  corners are close enough to that already that a further override wasn't worth the risk of an
  unintended layout shift elsewhere.

## Slate (bonus third option)

Once the token plumbing existed, a third palette was nearly free: same flat, Blender-shaped
structure as Carbide, but a cooler blue-grey neutral and a desaturated blue accent (`#5B9BD5`)
instead of warm orange — a calmer, more "CAD tool" read for anyone who finds Blender's orange too
loud. It is not the primary proposal; it exists for the user to try if Carbide's orange doesn't
land.
