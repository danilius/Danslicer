# Techy theme — design rationale

Techy is the third panel of the user's four-up mockup sheet, rebuilt against the existing token
system. As with the other palettes, the brief is chrome/controls/icons only — the 3D viewport and
the layout grid are untouched by any theme.

## What it's going for

Instrument-panel darkness with thin bright outlines: the chrome drops close to black, and controls
are defined by their *border* rather than by a filled body, so a dense settings panel reads as a
grid of outlined cells instead of a stack of grey slabs. The amber accent is used sparingly and
always means "state" — focused, checked, active, filled.

- **Window / surface** `#1C1E22` — the mockup's own base hex; a cool near-black with a faint blue cast.
- **Alt surface** `#15171A` — text fields, combo boxes, list rows. Techy inverts Carbide's step
  direction: inputs are *recessed* (darker than the panel), which is what makes the bright outline
  around them read as an edge rather than a highlight.
- **Panels** `#212429` — properties strip, Preferences body, popups, tooltips.
- **Headers / toolbars** `#191B1F` — a darker band separating menu/status chrome from panel content.
- **Borders** `#0A0B0D` for structural dividers, but control outlines use `#3A424C` — a deliberately
  *visible* cool grey. This is the theme's whole idea, so it is the one place the palette spends contrast.
- **Text** `#F0F4F8` primary / `#A5AEB8` muted / `#6C757E` faint. The primary is brighter than any
  other theme's (near-white on near-black is the look), with a clearly dimmer second tier so long
  label columns stay skimmable.
- **Accent** `#F59E0B` — the mockup's amber. Focus rings, checked toggles and checkboxes, slider
  fill, selected combo row, active tab pipe. Never used as a surface fill, so it never competes with
  the model in the viewport.

## Deliberate non-goals

- **No new control templates.** Corner radius, padding, borders thickness and the monospace numeric
  styling suggested by the mockup all live in control templates, not in the palette tokens. Techy
  gets its outlined character purely from colour values; anything further belongs to a separate
  styling task rather than a palette.
- **No derived per-element colours.** Every value here is written out explicitly, following the
  user's feedback on the view-cube work that derived per-element colouring was unnecessary.
- Classic remains the default; nothing here changes what an existing install looks like until the
  user picks Techy in Preferences > Appearance.
