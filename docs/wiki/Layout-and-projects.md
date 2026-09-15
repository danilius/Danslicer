# Layout and projects

## Files

| Function | How to use it |
| --- | --- |
| New Project | File → New Project starts a fresh scene; respond to the unsaved-changes prompt if shown. |
| Import | `Ctrl+I` adds STL or OBJ geometry. STL may be binary or ASCII. |
| Open Project | `Ctrl+O` restores a `.danslicer` project. |
| Save / Save As | `Ctrl+S` saves; `Ctrl+Shift+S` saves to another project path. |
| Recent projects | Use the project selector to reopen recent work or open another project. |
| Reload object | Use the reload button beside an object to replace its mesh from its source file; changed geometry can invalidate attached support data. Save before reloading substantial model changes. |
| Export Print File | Exports the current slice result. It does not replace Save Project. |

Projects preserve mesh/scene data and print-related settings. Support and resin presets in Preferences are reusable recipes; selecting a recipe and saving the project are separate actions.

## Select and organize

Click a model or an entry in **Objects**. `Shift`-click changes membership in a selection; `A` or `Ctrl+A` selects all in the current editing scope, and `Alt+A` clears it. Layout commands operate on objects; Support commands operate on support elements or the chosen support target.

`H` hides selected objects and `Alt+H` unhides. Hiding is an editing/display aid; it is not a way to remove geometry from a print. Use **Delete** to remove an object. Undo and redo are available with `Ctrl+Z`, `Ctrl+Shift+Z`, or `Ctrl+Y`.

## Move, rotate, and scale

| Control | Meaning |
| --- | --- |
| Move / `G` | Translate selected objects. Position fields use millimetres and report the bounding-box centre in XY and bottom in Z. |
| Rotate / `R` | Rotate selected objects. Angles use degrees. |
| Scale / `S` | Resize selected objects. A factor of 1 preserves size. |
| `X`, `Y`, `Z` during a transform | Restrict movement/rotation/scaling to an axis. |
| `Shift+X/Y/Z` | Use the plane excluding that axis. |
| Numeric input | Type the transform amount; Backspace corrects it. |
| Enter, Space, or left click | Confirm the active transform. |
| Escape or right click | Cancel the active transform. |
| Snap / `Shift+Tab` | Toggle snapping: 1 mm movement, 5° rotation, 0.1× scale. Hold Ctrl to invert snapping during a drag. |

Gizmo toggles show move, rotate, and scale handles. Numeric fields also allow precise editing; expression-aware fields accept arithmetic such as `12/2`. Units are shown by the field.

## Placement and copies

- **Drop to Plate** (`Ctrl+D`) seats the selected object at the plate.
- **Lay Flat on Face** (`F`) arms a face picker: click the face that should lie on the plate; right click or `Esc` cancels.
- **Auto drop** places transformed objects automatically. Placement modes are **AutoDrop**, **RaiseAbovePlate**, and **Off**; the height is the lowest-point offset above the plate. Off preserves manual vertical positioning.
- **Duplicate** (`Shift+D`, Layout) copies selected objects.
- **Mirror X/Y/Z** (`Ctrl+Shift+1/2/3`) mirrors selected objects on that axis.

Moving across the plate and rotating about world Z carry supports with the object. Vertical movement, tilting, and scaling discard supports because the existing structure no longer fits. Mirroring maps the support geometry with the model. Inspect the result and regenerate as needed; Undo restores a mistaken operation.

Next: [Navigation and visibility](Navigation-and-visibility.md) · [Support settings](Support-settings.md)
