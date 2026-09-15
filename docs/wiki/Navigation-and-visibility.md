# Navigation and visibility

## Camera

| Action | Gesture |
| --- | --- |
| Orbit | Middle-button drag |
| Pan | Shift + middle-button drag |
| Zoom | Mouse wheel, except while a guided tool uses it to change spacing |
| Frame all | Home |
| Frame selection | `.` (period/decimal) |
| Front / back | 1 / Ctrl+1 |
| Right / left | 3 / Ctrl+3 |
| Top / bottom | 7 / Ctrl+7 |
| Perspective / orthographic | 5 |
| Workspace / Slicing | Tab |

Number-row and numpad view keys are accepted. Click a view-cube face for an aligned view, or drag the cube to orbit. Orthographic view removes perspective size changes and is useful for alignment.

## Support visibility

In **Support → Visibility**, choose:

| Mode | Displays |
| --- | --- |
| Full | Solid support geometry |
| Contact points | Contact markers for inspecting placement |
| Lines | Simplified support connections |
| Tips | Tip geometry without the full forest |
| Transparent | Translucent supports, optionally with contact points overlaid |

The **Tips, Branches, Trunks, Bases, Bracing, Rafts** switches affect Full and Transparent modes. The focused modes have their own fixed scope. These settings change viewport presentation only; they do not change slice geometry.

`H` hides selected support elements; `Shift+H` hides unselected elements; `Alt+H` unhides. Layout shows the complete object/support arrangement even when Support mode has individually hidden elements. Returning to Support restores those editing visibility choices.

**Select Through** allows support picking through the model instead of stopping at its surface. `B` arms border selection in Support mode. Use `Space` to enter or leave editing for a selected support.

## Isolation and waterline

The vertical range control in Support mode clips geometry to a height interval. Adjust its lower and upper limits to expose an interior or a crowded contact layer; **Reset** restores the full range. Height editing provides precise limits.

**Cap** closes visible horizontal cuts for inspection. Preferences offers **Painted** or **Sliced** cap styles. Painted uses deferred rendering; Classic falls back to exact sliced caps. Neither modifies the printed mesh.

**Hover waterline** draws the layer line at the height beneath the cursor across the model. It is useful when choosing a contour or comparing nearby contacts.

See [Preferences](Preferences.md) for shading, overhang tint, wireframe, shadows, and renderer choices.
