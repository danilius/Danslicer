# Support structure preview

Parent Supports (J) and Brace (K) open a fixed floating panel on the right of the viewport, alongside the isolation controls. It is part of the main window and cannot be dragged. The viewport displays a detached graph while the original document and undo history remain unchanged. Orbit with the middle mouse button and scroll to zoom. Apply commits the displayed result in one undo step; Cancel or Escape discards it. Editing the model or selection invalidates a pending plan. Leaving Support mode closes the panel.

Switching from Parent to Brace (or back) commits the current valid preview as its own operation before opening the next preview. Undo while previewing reverses that visible operation first and closes the panel; Redo restores it. Further Undo steps reverse earlier operations individually. A preview that cannot make a change is simply dismissed by Undo, leaving earlier history intact.

## Parenting

- Candelabra tests nearby trunk candidates and builds compatible subsets by actual branch and trunk clearance, height, angle, length and group limits. Nearby tips are ordered by 3D distance, so height contributes to grouping. It chooses the largest subset found in its bounded candidate search, then repeats with the remaining tips. Every contact connects directly to a vertical trunk; no branch hosts another branch. Existing contact shape parameters are retained.
- Existing contact endpoints are tried first and checked against current obstacles, with vertical alternatives tested per contact. Search is bounded to 64 nearby tips, up to nine free-position candidates or twelve grid candidates, and three branch angles per grouping pass. Explicitly positioned trunks retain all-or-nothing routing.
- If preferred tilted contact cones prevent lateral branches from fitting, it retries collision-checked vertical cones before splitting. It retains the configured cone-bend limit. Why supports remain independent counts only explanations attached to the remaining orange components. Click an orange member in the preview to inspect that component without changing document selection or undo history. A search failure describes the tested candidates, not proof that no possible arrangement exists.
- Group width bounds the horizontal span of a group; Maximum tips / trunk bounds its contact count. Compact, Balanced and Wide change these two values only.
- Advanced retains the angle, length and cone bend controls. A zero maximum branch length allows an automatic limit of twice the group width in Candelabra; Tree and Simple retain their inherited Members limit.
- Make these tips one candelabra exposes trunk-centre X/Y. A forced group must fit the width, count, angle, length and clearance limits. Otherwise its original supports remain. The base grid still applies; turn it off for an exact centre.
- Independent or unchanged operand supports are highlighted in orange in the mesh view. The status explains when original supports have been retained because no route fits the limits.
- Tree retains hierarchical branching. Simple retains nearby-trunk sharing. Legacy presets keep their original style; newly created configurations default to Candelabra.

Explicit parenting never adds braces. Automatic bracing during generation and automatic parenting during placement remains controlled by its existing toggle.

## Bracing

Sparse uses 20 mm vertical pitch, Standard 10 mm, and Dense continuous rungs. Actual bracing also obeys angle, height, neighbour and clearance constraints. Changing density replaces the operand supports' previous braces in the detached preview. Advanced exposes exact values and Cluster gap; tightly packed trunks may be treated as one bundle, with no internal bracing. Brace diameter displays the effective value, with a separate Use branch diameter checkbox.

Remember these arrangement settings copies only parenting or bracing settings for future operations. It does not change contact, trunk, base or placement dimensions. Existing full support presets remain available.

## Auto Drop

The dropdown beside Auto drop exposes the saved height above the plate and Drop now. Drop now uses that height even when automatic dropping is off and is undoable. The existing transform height field edits the same value.

## Verification

`dotnet test tests/Danslicer.Tests --no-restore -p:UsedAvaloniaProducts=` runs the suite. New tests cover centred horizontal and sloping rows, direct topology, bounded groups, obstruction fallback, forced centres, style migration, detached preview, selection invalidation, Apply/Undo/Redo, replacement bracing and explicit dropping with Auto Drop off.

`Danslicer.App.exe --structure-capture <output-directory>` uses isolated preferences and checks the real Parent/Brace entry points, editing, Apply, Undo, Cancel and Auto Drop flyout. It renders control PNGs for visual inspection; these PNGs do not include the native OpenGL surface. Geometry assertions use synthetic fixtures; the user's original photographed model was not available as a source mesh for this validation.
