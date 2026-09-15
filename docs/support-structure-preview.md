# Support structure preview

Parent Supports (J) and Brace (K) open a fixed floating panel on the right of the viewport, alongside the isolation controls. It is part of the main window and cannot be dragged. The viewport displays a detached graph while the original document and undo history remain unchanged. Orbit with the middle mouse button and scroll to zoom. Apply commits the displayed result in one undo step; Cancel or Escape discards it. Editing the model or selection invalidates a pending plan. Leaving Support mode closes the panel.

Switching from Parent to Brace (or back) commits the current valid preview as its own operation before opening the next preview. Undo while previewing reverses that visible operation first and closes the panel; Redo restores it. Further Undo steps reverse earlier operations individually. A preview that cannot make a change is simply dismissed by Undo, leaving earlier history intact.

## Parenting

- Candelabra tests nearby trunk candidates and builds compatible subsets by actual branch and trunk clearance, height, angle, length and group limits. Nearby tips are ordered by 3D distance, so height contributes to grouping. It chooses the largest subset found in its bounded candidate search, then repeats with the remaining tips. Every contact connects directly to a vertical trunk; no branch hosts another branch. Existing contact shape parameters are retained.
- Candelabra branches on the same trunk may intersect and fuse. Each accepted group prefers a trunk centred on its own contact-cone endpoints, with branches on both sides where the contacts permit. Base-grid placement, model and other-support clearance, and route limits still apply; a clear offset trunk remains the fallback when centring is blocked.
- Straight trunk sections of equal diameter render as one continuous cylindrical shell, with caps only at the ends of the run. Branch attachment nodes remain in the editable graph; they do not add rings or necks to the trunk. Individually hidden, disabled or selected sections retain their display state.
- Existing contact endpoints are tried first and checked against current obstacles, with vertical alternatives tested per contact. Search is bounded to 64 nearby tips, up to nine free-position candidates or twelve grid candidates, and three branch angles per grouping pass. Explicitly positioned trunks retain all-or-nothing routing.
- If preferred tilted contact cones prevent lateral branches from fitting, it retries collision-checked vertical cones before splitting. It retains the configured cone-bend limit. Why supports remain independent counts only explanations attached to the remaining orange components. Click an orange member in the preview to inspect that component without changing document selection or undo history. A search failure describes the tested candidates, not proof that no possible arrangement exists.
- Group width bounds the horizontal span of a group; Maximum tips / trunk bounds its contact count. Compact, Balanced and Wide change these two values only.
- Advanced retains the angle, length and cone bend controls. A zero maximum branch length allows an automatic limit of twice the group width in Candelabra; Tree and Simple retain their inherited Members limit.
- Make these tips one candelabra exposes trunk-centre X/Y. A forced group must fit the width, count, angle, length and clearance limits. Otherwise its original supports remain. The base grid still applies; turn it off for an exact centre.
- Independent or unchanged operand supports are highlighted in orange in the mesh view. The status explains when original supports have been retained because no route fits the limits.
- Tree retains hierarchical branching. Simple retains nearby-trunk sharing. Legacy presets keep their original style; newly created configurations default to Candelabra.

Explicit parenting never adds braces. Automatic bracing during generation and automatic parenting during placement remains controlled by its existing toggle.

## Bracing

Automatic is the default for new settings. Starting with a neighbouring pair, it fits a complete top-down zigzag below the shorter trunk's top, then continues to the nearest unvisited neighbour while retaining one trunk of the previous pair. Adjacent pairs prefer opposite starting directions. Each pair chooses its own number of rungs and rise, preferring angles near 45° and checking a 65° maximum lean from vertical. There is no automatic neighbour-distance or brace-length cutoff, and no redundant extra pair is added between already tied trunks.

Endpoint gap is the vertical distance on the shared trunk between consecutive brace endpoints. It defaults to 2 mm and is configurable, including zero for meeting endpoints. The planner fits the rung rise around this gap to reach the available base height. It tries alternative rung counts, starting directions and top offsets before falling back to the longest uninterrupted clear zigzag when an obstruction prevents a full ladder. It never chooses parallel diagonals or a collection of scattered rungs in Automatic mode. Search is bounded to 256 rungs per trial and does not guarantee a route exists when no candidate fits.

Saved Zigzag settings migrate to Automatic on load and when opening the Brace preview. The obsolete fixed-pitch Zigzag choice is no longer offered: crossover rejection could remove alternating rungs and leave parallel diagonals. Automatic exposes endpoint gap and diameter as the main adjustments and hides fixed pitch, angle, height, neighbour range and partner limits. Diagonal remains an explicit choice for parallel braces. Clustering still treats tightly grouped stems as a bundle. Rebuild in the Brace preview to replace existing braces.

All methods reject brace crossovers, checking cylinder thickness within each ladder, against already planned ladders, and against retained active braces. Shared attachment points remain valid joints. Automatic scores the clear routes across its alternative layouts; fixed methods omit conflicting rungs. Rebuild existing braces in the preview to remove old crossovers. These checks use 3D geometry, so separated braces can still overlap in a particular camera view.

In fixed methods, Sparse uses 20 mm vertical pitch, Standard 10 mm, and Dense continuous rungs. Changing endpoint gap or fixed density replaces only braces with both ends carried by the operand supports in the detached preview. Connections from a selected support to an unselected neighbour remain unchanged, including any shared endpoint nodes. With no selection, the operands are all supports of the target model. Brace diameter displays the effective value, with a separate Use branch diameter checkbox.

Remember these arrangement settings copies only parenting or bracing settings for future operations. It does not change contact, trunk, base or placement dimensions. Existing full support presets remain available.

## Auto Drop

The dropdown beside Auto drop exposes the saved height above the plate and Drop now. Drop now uses that height even when automatic dropping is off and is undoable. The existing transform height field edits the same value.

## Verification

`dotnet test tests/Danslicer.Tests --no-restore -p:UsedAvaloniaProducts=` runs the suite. New tests cover centred horizontal and sloping rows, direct topology, bounded groups, obstruction fallback, forced centres, style migration, detached preview, selection invalidation, Apply/Undo/Redo, replacement bracing and explicit dropping with Auto Drop off.

`Danslicer.App.exe --structure-capture <output-directory>` uses isolated preferences and checks the real Parent/Brace entry points, editing, Apply, Undo, Cancel and Auto Drop flyout. It renders control PNGs for visual inspection; these PNGs do not include the native OpenGL surface. Geometry assertions use synthetic fixtures; the user's original photographed model was not available as a source mesh for this validation.

## Manual braces

In Support mode, choose **Structure > Manual brace**. Click a trunk or branch at the first attachment height, then click a member of another support for the second end. Endpoints snap onto member axes. The cyan preview shows a valid brace; red indicates a blocked or duplicate connection. **Snap brace 45°** moves the second endpoint to the nearest 45-degree attachment on that member, refusing placement when no such point fits. **Structure > Manual settings** exposes a separate manual brace diameter, Avoid models (on by default), and Avoid supports and braces (off by default). Support crossings and connections within one connected structure are allowed for manual work. Duplicate braces are still rejected. These saved settings affect new manual braces only; automatic bracing settings remain separate.

Each second click adds exactly one brace as its own undo step and leaves the tool ready for another pair. Existing braces remain unchanged. Right-click or Escape leaves the tool. Switching to another placement tool or leaving Support mode cancels the pending pair.
