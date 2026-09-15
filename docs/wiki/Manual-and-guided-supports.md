# Manual and guided supports

Choose a model in **Support → Objects**. Placement tools belong to that target. The preview and status text explain where a contact can be placed and why a route is refused.

## Single contacts and editing

**Place supports** (`T`) toggles placement. Hover the model, inspect the preview, and left-click to add a contact. Repeat for more contacts; `T`, right click, or `Esc` leaves the tool. Release v1.0.0 Build 1 also allows placement on existing support members, using a fixed 45° tip and outward branch with clearance checks.

Select a contact and press **G** to move the tip along the surface. Left click or `Enter` confirms; right click or `Esc` cancels. **Space** toggles support editing with draggable handles; `Esc` cancels an active edit. **Delete** removes selected support elements and prunes dependent support geometry as required.

In support editing, drag a base, junction, or trunk in XY, or move the tip over the model surface. The base grid snaps base movement; hold **Shift** to move freely. Space or Esc leaves support editing.

## Guided tools

Open **Place supports** to choose a tool, or use its key in the viewport:

| Tool | Key | Workflow |
| --- | --- | --- |
| Line | L | Click points along the surface to build a run of evenly spaced contacts. Finish with Enter/Space or a double click. |
| Polygon | P | Click corners, then finish to fill the enclosed surface region with contacts and place tips along its boundary. Fewer than three corners behaves like a line. |
| Edge | E | Hover near a crease/sharp edge, inspect the traced preview, then click once to place. |
| Ring | R | Click the centre, move out to set the radius, then click to place. The ring projects onto the surface. |
| Contour | C | Hover to choose a Z height, then click to place contacts along downward-facing contours at that height. |
| Densify | D | Insert contacts between neighbouring tips in the selection, or the target's tips if nothing is selected. |
| Thin | Shift+D | Keep one contact in every configured number along the selected runs and remove the others with their supports. |

During a guided gesture, **wheel** changes pitch rather than zooming. Type a number for precise spacing. **Backspace** removes typed digits first, then the last vertex; with no vertices it cancels. **Enter/Space** commits and **Esc/right click** cancels. Middle-button orbit and Shift + middle-button pan remain available.

## Settings that affect these tools

| Setting | Effect |
| --- | --- |
| Spacing | Initial pitch for guided contacts. Smaller pitch gives more tips. |
| Guided tools ignore existing | Route/place independently of existing supports. Turn off to respect them. |
| Guided tip clearance | Minimum distance from an existing tip when existing supports are respected. |
| Densify: tips per gap | Number inserted between adjacent tips. |
| Thin: keep 1 in | Keep every Nth contact along a run. |
| Independent supports (ignore existing) | Manual model placements ignore existing supports while still avoiding models. Placements on supports retain clearance checks. |
| Auto-parenting | Groups new contacts and nearby supports as part of placement's undo step. |
| Auto-bracing | Adds bracing after automatic parenting when enabled. |

Contact dimensions and routing limits come from [Support settings](Support-settings.md). Guided previews can reject candidates because of contact filters, insufficient clearance, or unreachable bases; a visible path does not guarantee every candidate will route.

For a brace between two existing members, see [Manual braces](Parenting-and-bracing.md#manual-braces).
