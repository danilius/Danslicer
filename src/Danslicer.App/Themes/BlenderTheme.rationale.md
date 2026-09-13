# Blender theme — design rationale

Blender is the "BLENDER-LIKE" panel of the user's four-up mockup sheet. It sits alongside Carbide,
which is *also* Blender-leaning, so the first question is why both exist.

## Blender vs Carbide

Carbide reads Blender as *restraint*: near-black neutral greys, controls almost flush with the
surface, and orange reserved strictly for active state. This theme reads Blender as its **button
work**: a lighter, bluer chrome with flat mid-grey buttons that clearly sit *above* the panel, the
way Blender's own operator buttons and dropdowns do. Same family, opposite emphasis — one is quiet,
one is tactile.

- **Window / surface** `#24292E` — the mockup's base hex; grey with a distinct blue cast, noticeably
  lighter than Carbide's `#1D1D1D`.
- **Alt surface / headers** `#1E2226` — inputs, list rows, the menu and status bands.
- **Panels** `#2B3037` — properties strip, Preferences body, popups, tooltips.
- **Controls** `#3A4149` resting, `#454D56` hover, `#2B3037` pressed. This mid-grey step is the
  theme's signature: buttons, toggles and slider tracks all read as raised objects.
- **Borders** `#14171A` — dark hairlines, so the structure comes from the colour steps.
- **Text** `#E8EAED` primary / `#A9AFB6` muted / `#767C83` faint.
- **Accent** `#FF9F43` — the mockup's warmer orange (Carbide uses Blender's literal `#E87D0D`).
  Checked toggles, checkboxes, slider fill and thumb, selected combo row, focused field, active tab
  pipe. Text on an accent fill is `#20252A`, not white — the accent is bright enough that dark text
  is the legible pairing.

## The one thing from the mockup that is NOT here

The mockup shows **filled orange section-header bars** above each group of property rows. There is
no "section header" in the app's chrome to colour: the existing tokens cover the window, panel,
header band, borders, text tiers and accent, and `AppHeader` is the *menu and status* band — turning
that orange would give the app a full-width orange menu bar, which is not what the mockup shows.

Delivering the mockup's grouped-property look properly means adding a section-header control (a
styled `HeaderedContentControl` or similar) and a token for it, then adopting it across the settings
panels — a control-styling task, not a palette one, and explicitly out of scope for this change.
It is flagged in the weekend log for the user to decide on.

## Deliberate non-goals

- No new control templates, no corner-radius changes: colours only.
- No derived per-element colours; every value is explicit.
- Classic remains the default and is untouched.
