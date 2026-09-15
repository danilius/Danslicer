# Parenting and bracing

Open **Support → Structure**. Parenting reorganizes contacts into shared trunks. Bracing adds cross-members between suitable stems. These are separate operations.

## Preview and actions

Select support elements to limit the preview, or leave the support selection empty to work on all supports of the target. Adjust settings while inspecting the preview, then **Apply** or **Cancel**. `Esc` cancels a structure preview.

The current preview also provides:

| Control | Effect |
| --- | --- |
| Remember these arrangement settings | On Apply, keep the relevant arrangement choices for later operations. Enabled by default. |
| Make these tips one candelabra | In Candelabra mode, attempt to combine the affected contacts around one chosen trunk centre. Routing constraints still apply. |
| Trunk centre X / Y | Position that trunk in mm and regenerate its branches. Disable the base grid when an exact position is needed. |
| Why supports remain independent | Explain routing constraints. Click an orange highlighted support to inspect its particular blocker. |

Apply is unavailable while the preview is updating or cannot be applied. A scene change can invalidate the preview; rebuild it before committing.

| Action | Key | Purpose |
| --- | --- | --- |
| Parent supports | J | Reorganize existing contacts using the selected parenting method. This explicit action does not add braces. |
| Brace | K | Add braces using the bracing settings. |
| Unbrace | Shift+K | Remove braces in the command's scope. |
| Select braces | Toolbar/menu | Select bracing elements for inspection or editing. |
| Edit support | Space | Toggle editing handles for a selected support. |

## Parenting methods

- **Candelabra** groups tips around central trunks. **Arrangement**, **Group width**, and **Maximum tips / trunk** control grouping. Smaller groups use more trunks. Arrangement changes group width and count, leaving contact/member dimensions as set.
- **Tree** builds a branching hierarchy.
- **Simple** uses the simpler trunk/branch arrangement. Advanced reach, angle, and search controls constrain routing.

**Auto-parenting** runs after manual placement, guided placement, and densify, including neighbours within the search range. Disable it to keep new supports single until you run `J`.

## Structure settings reference

| Setting | What it does |
| --- | --- |
| Arrangement | Changes group width and tip count only. Contact and member dimensions stay as set. |
| Group width | Maximum horizontal span of a group. Smaller groups use more central trunks. |
| Maximum tips / trunk | Maximum number of tips grouped onto a Candelabra trunk. |
| Auto-parenting | After T, a guided tool or densify, the new supports and their neighbours within the trunk search range are parented at once, as part of the same undo step. Off: supports stay single until J. |
| Max branch length | How far a tip may reach to join a trunk when parented (J), mm. 0 = automatic reach in Candelabra, otherwise the Members value. |
| Max branch angle ° | Maximum lean from vertical. Candelabra tries steeper branches when needed to separate neighbours. 0 = the Members angle. |
| Trunk search range | How far around a tip parenting looks for a trunk to join before raising its own, mm. 0 = the Members existing trunk range. |
| Min tips per trunk | A new trunk carrying fewer tips than this gets one more try with double range |
| Rounds | Re-route this many times with different seeds; the round with the fewest trunks wins |
| Max cone bend ° | How far the branch leaving a cone may bend from the cone's axis. 0 = the member angle. A cone on a leaning wall points outward, so joining a trunk sideways along the edge needs 60–90. |
| Max branches per trunk | How many branches one trunk may carry when parented. 0 = the growth rule's default (6). |
| Density | Sets brace pitch: Sparse 20 mm, Standard 10 mm, Dense continuous. Fixed pitch controls Diagonal bracing; Automatic fits each pair using Endpoint gap. Angle and clearance can limit the actual number of braces. |
| Method | Automatic fits a complete zigzag independently to each neighbouring pair using the endpoint gap. Diagonal deliberately produces parallel braces using fixed settings. |
| Endpoint gap | Vertical gap on the shared trunk between consecutive braces. 0 joins the endpoints; default 2 mm. |
| Brace diameter | Diameter of brace members in mm; Use branch diameter inherits the member setting. |
| Use branch diameter | Use the Branch diameter setting for braces instead of a separate value. |
| Auto-bracing | Brace after Generate Supports and automatic parenting during placement, as part of that command's undo step. Parent Supports (J) never adds braces. Off: add braces with K. |
| Max brace angle ° | The most a brace may lean from vertical. A rung that cannot fit at this angle is left out rather than laid flatter |
| Brace spacing | Vertical pitch between the braces of one pair of trunks. 0 = continuous: each brace starts where the last one ended |
| Lowest brace height | No brace foot below this height above the plate. 0 = the min branch height |
| Min support height | Only trunks rising at least this far above the plate are braced |
| Neighbour distance | Largest horizontal gap between two trunks that a brace may span |
| Max brace partners | How many other trunks one trunk may be braced to |
| Max stem lean ° | A branch continuing a trunk upward within this lean from vertical is braced as part of the trunk |
| Cluster gap | Trunks whose surfaces are closer than this are braced as one bundle: no braces inside the cluster, the row ties to its outer trunk. 0 = never |

## Manual braces

The September 15 development version adds **Manual brace** in Structure. Click a trunk or branch for the first endpoint, then a second member for the other endpoint. Review the preview; right click or `Esc` leaves the tool.

| Setting | Effect |
| --- | --- |
| Diameter (mm) | Thickness of newly created manual braces. |
| Snap 45° | Snap the second endpoint along its member to a 45° lean from vertical. |
| Avoid models | Reject braces crossing model geometry; enabled by default. |
| Avoid supports | Reject crossings with support geometry; disabled by default. |

Manual-brace settings affect new manual braces. If these controls are absent, your build predates the feature.

Routing constraints can prevent connections or braces. Check the preview and status instead of assuming a denser setting can always fit more geometry.
