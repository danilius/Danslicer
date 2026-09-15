# Regions and islands

## Paint where supports belong

Select the target model, open **Regions**, and enable **Paint faces (click)**. Click a surface to add its patch; Shift-click removes it. Enable **Brush** to paint by dragging; Shift-drag erases. A brush stroke is one undo operation.

| Setting or command | Effect |
| --- | --- |
| Brush radius (px) | Screen-space radius. The cursor ring keeps its screen size while zooming. |
| Edit keep-clean region | Edits faces on which support contacts should be excluded instead of the positive support region. |
| Patch angle | Patch growth crosses an edge only when adjacent faces turn by no more than this angle. Larger values spread farther over bends. |
| Down angle | Threshold for downward-face selection; measured from vertical, 0° wall and 90° flat underside. |
| Select faces pointing down | Select eligible downward faces using the angle threshold. |
| Invert | Replace the edited set with its complement. |
| Grow / Shrink | Expand or contract the edited face set by adjacency. |
| Connected | Extend selection over connected faces. |
| Clear | Empty the edited region. An empty positive region returns generation to all eligible faces; keep-clean exclusions still apply. |

Painting controls the contact placement region. Generation still applies contact-angle, spacing, island, and routing constraints. For painted regions, **Painted row pitch (Z)** controls vertical row spacing and **Painted row spacing** controls distance along the surface within a row.

Region commands have [keyboard shortcuts](Keyboard-shortcuts.md). Turn painting off when you want to select support elements again.

## Detect and support islands

An island is a printable region that appears without sufficient material beneath it in the previous layer. Small regions below the minimum-area threshold do not provide support for the layer above.

1. Open **Detect islands** and click **Run detection** for the selected model.
2. Inspect the findings list and markers; click a finding to inspect its location.
3. Use **Support all detected islands** to add contacts, or place them manually.
4. Review the generation result for unplaced contacts and unrouted tips. Findings update as support contacts change; rerun detection after changing the model or detection inputs.
5. **Clear markers** removes the displayed findings; it does not delete supports.

| Setting | Effect |
| --- | --- |
| Min island area (mm²) | Ignore smaller components. Lower values find smaller features and increase work. |
| Island spacing (mm) | Contact spacing inside islands. Separate from normal overhang spacing. |
| Layer height | Controls where the model is sampled; it is also a print setting. |

Detection is an analysis aid. Check the actual slice layers, especially first appearances of small parts. The CLI additionally exposes proximity, bounds, and suction checks; see [Command line](Command-line.md).
