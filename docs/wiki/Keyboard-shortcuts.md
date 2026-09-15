# Keyboard shortcuts

These are the shipping defaults. Application shortcuts can be changed in **Preferences → Keymap**. Viewport keys remain fixed. Click the viewport before using its shortcuts; active text fields, dialogs, and tools can consume them.

## Application actions

| Action | Default |
| --- | --- |
| Undo | `Ctrl+Z` |
| Redo | `Ctrl+Shift+Z` |
| Redo (alternate) | `Ctrl+Y` |
| Delete | `Delete` |
| Duplicate Objects | `Shift+D` |
| Mirror Objects on X | `Ctrl+Shift+1` |
| Mirror Objects on Y | `Ctrl+Shift+2` |
| Mirror Objects on Z | `Ctrl+Shift+3` |
| Drop to Plate | `Ctrl+D` |
| Generate Supports | `Ctrl+G` |
| Select All | `Ctrl+A` |
| Hide Unselected Supports | `Shift+H` |
| Slice | `Ctrl+R` |
| Save Project | `Ctrl+S` |
| Save Project As | `Ctrl+Shift+S` |
| Open Project | `Ctrl+O` |
| Import Mesh | `Ctrl+I` |
| Export Print File | `Ctrl+E` |
| Preferences | `Ctrl+,` |
| Region: Select Faces Pointing Down | `Ctrl+Shift+F` |
| Region: Invert | `Ctrl+Shift+I` |
| Region: Grow | `Ctrl++` |
| Region: Shrink | `Ctrl+-` |
| Region: Select Connected | `Ctrl+Shift+K` |

Scope matters: object transforms/duplicate/mirror/drop belong to Layout; support generation and region actions belong to Support; slicing belongs to Slicing. `Shift+D` duplicates objects in Layout and thins supports in Support.

## Camera and workspace

| Key | Action |
| --- | --- |
| Home | Frame all |
| Period / decimal | Frame selected |
| 1 / Ctrl+1 | Front / back |
| 3 / Ctrl+3 | Right / left |
| 7 / Ctrl+7 | Top / bottom |
| 5 | Toggle perspective/orthographic |
| Tab | Toggle workspace/Slicing |

View numbers work on the number row and numpad.

## Layout

| Key | Action |
| --- | --- |
| G / R / S | Move / rotate / scale |
| X / Y / Z during transform | Constrain axis |
| Shift+X / Shift+Y / Shift+Z | Constrain to the plane excluding the axis |
| Digits, minus, decimal | Enter transform amount |
| Backspace | Correct numeric input |
| Enter / Space / left click | Confirm transform |
| Esc / right click | Cancel transform |
| F | Pick a face to lay flat |
| Shift+Tab | Toggle snapping |
| Ctrl while dragging | Temporarily invert snapping |
| A / Alt+A | Select all / clear selection |
| H / Alt+H | Hide selection / unhide all |

## Support

| Key | Action |
| --- | --- |
| T | Toggle single-support placement |
| L | Guided line |
| P | Guided polygon |
| E | Guided edge |
| R | Guided ring |
| C | Guided contour |
| D / Shift+D | Densify / thin |
| G with a selected tip | Move contact on the surface |
| J | Parent supports |
| K / Shift+K | Brace / unbrace |
| Space | Toggle support editing (outside a guided gesture) |
| A / Alt+A | Select all supports in scope / clear support selection |
| B | Arm border selection |
| H / Shift+H / Alt+H | Hide selected / hide unselected / unhide |
| Delete | Delete selected support elements |
| Esc | Cancel current edit/tool, then clear selection as applicable |

### During guided placement

| Gesture | Action |
| --- | --- |
| Left click | Add a vertex or place a ready ring/contour |
| Double click | Finish a line-style gesture |
| Enter / Space | Commit |
| Esc / right click | Cancel |
| Wheel | Change contact pitch |
| Digits / decimal | Type pitch |
| Backspace | Remove input digit, then vertex; cancel if empty |

## Mouse

| Gesture | Action |
| --- | --- |
| Middle drag | Orbit |
| Shift + middle drag | Pan |
| Wheel | Zoom, except during guided placement |
| Left click | Select or operate the active tool |
| Shift-click | Modify selection; erase when painting regions |
| Drag with region brush armed | Paint region |
| Shift-drag with region brush armed | Erase region |
| View cube click / drag | Align view / orbit |

The status bar shows the active tool's current hints. In particular, `R`, `G`, `Space`, and `Shift+D` have different meanings across modes.
