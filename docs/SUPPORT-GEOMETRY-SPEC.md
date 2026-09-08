# Support geometry and recipes — user spec, 2026-09-03 (afternoon dictation)

Captured verbatim in substance from the user; supplements DESIGN.md §8 and the
2026-09-03 "recipes" rumination in HANDOVER.md. Implementation may be now or later.

## Anatomy of one support (bottom to top)

1. **Base** — one of: a disc; a disc with a cone on top; or nothing at all when a
   raft is being used.
2. **First section** — a vertical cylinder rising from the base.
3. **Second section (optional)** — a cylinder added at the top of the first at an
   angle, to span a gap when object geometry is in the way.
4. **Conical tip** — added at an angle, from the second section if present,
   otherwise directly from the first. The tip's **end diameter** and **embedding
   depth** (how far it sinks into the model surface) are configurable.

Default angle for all angled elements: **45°**. Everything above is configurable.

## Structure

- Bases are placed in a **configurable grid pattern**.
- Multiple second-level cylinders may **branch** from one first-level cylinder,
  sharing one base — forming support **trees**.
- Useful names/labels for all these parts are wanted for discussion and UI.

## Support types ("recipes")

- A **support type** bundles every element parameter above as a single recipe.
- Users can **create, edit and use** support types for an operation.
- (From the earlier rumination, still standing: modifier-style attachment to
  detected areas, import/export as shareable files, several recipes per model
  type is normal.)

## No blocking operations (hard UI requirement)

- The UI must remain usable at all times.
- Auto-generation shows a **progress bar just above the bottom status bar**.
- Supports are added in **small batches** so hundreds of insertions never hog
  the UI thread.

## Decisions (user, 2026-09-03 afternoon)

- **Vocabulary adopted (canonical from now on):** **base** / **trunk** (first
  vertical section) / **branch** (optional angled second section, several may
  share one trunk) / **tip** (angled cone, end diameter + embedding depth),
  plus **brace** unchanged. Today's neck+pillar fold into trunk/branch/tip.
- **Undo:** one undo step for the whole generation — a series of batches
  representing one command collapses into a single step.
- **Cancel:** the progress bar carries a cancel button; cancelling **rolls
  back** everything the run placed (no partial result).
- **Sequencing:** embedding depth joins Grok's in-flight tip-geometry brief;
  trunk/branch/tip formalisation + base disc/cone geometry is the next Core
  brief; non-blocking batched generation with the progress bar is ChatGPT's
  next App brief after its current queue merges; recipes UI comes after the
  geometry exists.

## Routing policy (user decision, 2026-09-03 evening)

- **Supports never land on the model, for now.** Every support routes to the plate or
  refuses. Model landing stays in the code as an opt-in for a future profile setting;
  no default path enables it. (Reversal of the brief-5-era padded-landing behaviour,
  after screen testing showed pad blobs on the Drogon's toes.)

## Bases and structure (user decisions, 2026-09-03 late night screen test)

- **Bases sit on an imaginary grid** with a configurable pitch. This resolves the
  earlier "still open" question — it IS a bases-on-grid-points rule, not merely a
  recipe parameter. *Default pitch: 6 mm (user decision D1, 2026-09-04, after the
  20 mm A/B showed the coarse lattice strangled coverage; grid stays ON by default.)*
  - *Amended (user decision, 2026-09-03 evening, after the seated benchmark A/B):* the
    grid is now **optional** — `UseBaseGrid` on/off joins the pitch as configuration,
    default ON (the dictated rule stands until the user chooses otherwise). Grid-off
    restores free base placement for the user's screen experiments; the A/B numbers
    live in BENCHMARKS.md ("optional base grid A/B").
- **Branch-first**: a new support first looks for an existing trunk within branch range
  and joins it; only when none is reachable does it create its own base and trunk (on a
  grid point). Both the preference and the range are configurable.
- **Bases never shrink to fit.** A base that would collide with the model is placed
  further away instead (a shrunken base is likely to fail on the plate). The configured
  base diameter is a guarantee, not a maximum.
- **DiscCone tops match their member**: the cone (and any rounded transition) where a
  trunk or branch meets its base must have exactly the diameter of that member.
- **Configurability directive**: anything that can be configured should be exposed in the
  support configuration.

## Cone orientation and joints (user decisions, 2026-09-07)

Reference: the user's Blender drawings of a cone on a ball on a cylinder. Every joint
between members is a sphere of the parent member's diameter (the capsule cap); the
base is the one exception.

- **The cone points along the contact's outward normal**, clamped to the member angle
  (45° by default) from vertical. Fully vertical is always allowed.
- **A normal steeper than 45° simply gets a 45° cone** (user drawing 2026-09-07: a
  tilted block on a vertical trunk, cone at 45°). When that direction is blocked, or
  nothing can follow from it, the cone goes vertical and the whole route is retried;
  only after vertical does the router swing the cone around the vertical at 45°.
- **The bend at the ball is limited to the member angle.** The branch (or trunk) leaving
  the cone's junction may turn by at most 45° from the cone's own axis, so a branch never
  doubles back on the cone it grows from (the "Z" kink seen on the 2026-09-06 screen
  test is gone). The branch that simply continues the cone's axis is offered first.
- **A trunk may be raised to meet a member-angle branch.** When a nearby trunk's top is
  too low, a shallower branch to its current top is tried first; only when that is
  refused (range, bend, collision) is the trunk extended upward by a new segment to a
  new top junction. Branches already on the trunk keep both their ends. A trunk whose
  top carries its own cone tip is never raised, since the raise would run up inside
  the cone. A branch can still attach anywhere below a trunk's top.
- **The cone is the whole tip member, and its base is the ball's diameter.** One taper
  from the contact radius to the radius of the sphere it grows from, base ring at that
  sphere's centre, so the base simply rotates about the sphere's centre (user drawing
  2026-09-07). There is no separate neck: the old "cone length" that tapered to the tip
  member's own diameter is gone from the panel, and "Cone length" now names the tip
  member length. The ring keeps the few-percent draw-in over a short buried run that
  stops its rim showing through the ball's facets.
- **One cone per ball, and cones keep their bases apart.** A tip member is kept clear of
  other supports by its base radius (the ball's), not by its narrow neck, and there is no
  short stub member for crowded contacts: a contact that cannot take a whole cone is
  refused (user screen test 2026-09-07: stub cones piled up on one ball as a fan).
- **No stub branches.** A junction within half a cone length of a trunk axis, or of a grid
  drop line, is first offered a snap onto that line: the cone is re-aimed at it with its
  configured length (at most 30° off its normal) and lands on the trunk directly, splitting
  the trunk or raising it as needed. A trunk top already carrying a cone is never shared.
  Only when the snap is impossible may a branch bridge the gap, and never a branch that
  starts within one branch radius of the line it descends to: that would be shorter than
  its own ball.
- **Nearer base beats farther trunk.** Grid mode joins an existing trunk only when its
  branch is at most half a grid pitch longer than the branch a fresh trunk at the nearest
  free lattice point would need (user screen test 2026-09-07: a 7 mm branch reached past a
  free lattice point 2 mm away). Free mode still joins any reachable trunk when the
  preference is on, as before.
- **Free mode: every tip is a whole support, blind to the others** (user decision
  2026-09-07, after a second tip joined a first tip's branch end). With the base grid off a
  contact never joins another support and never avoids one, existing or new, even if they
  collide; other supports are not obstacles for it. Sharing trunks, snapping onto them and
  keeping members apart are grid-mode behaviours.
- **Supports may touch each other.** A cone is checked against other supports as the
  frustum it is (two capsules), and no member keeps the model clearance from another
  member: supports that meet fuse. The optional member-separation setting is the one
  rule that keeps members apart. (User screen test 2026-09-07: a manual cone between two
  generated ones was refused as "no clear path".)
- **The grid is a preference, not a reason to refuse.** When no lattice point is reachable
  and no existing trunk can be joined, the support is routed as in free mode, its base
  standing wherever the trunk falls. (User, 2026-09-07: refusing a contact whose trunk
  could drop straight under it is absurd.)
- **Mini supports are removed; old project files that still carry them load with those
  members dropped.**
- **Existing supports count at full size.** Supports already in the document (an earlier
  generation, manual placements) are seeded into later routing with each cone at the radius
  of its ball, so a later pass cannot crowd them.
- **One member draws each joint's ball.** In the viewport the widest member at a node
  (trunk before branch, then lowest id) draws the ball; the others tuck their end caps
  inside it, so no two surfaces coincide and the seams no longer flicker.
- **A cone is straight.** The normal lead-in (a short run along the contact normal before
  bending toward the junction) is set to zero in generation and manual placement and its
  setting is gone from the panel (user screen test 2026-09-07: a bent cone is wrong).
## Mini-supports (removed 2026-09-07)

Mini supports (fine rods fanning from a branch end, mini-island tips, density clusters and
fine-feature minis) were removed on 2026-09-07 at the user's decision: not worth dealing
with now. The code, settings, CLI options and tests are gone. Islands at or above the
minimum area get regular cone tips; smaller ones are ignored. Old project files that still
carry mini segments load with those members dropped.

## Island-first generation and island tools (user dictation, 2026-09-04)

- **Auto support order: islands first, then everything else.** Islands are the
  print-killers; they get tips and routing priority before other strategies place
  anything.
- **Island Support** (Support-mode toolbar button): generates island supports ONLY.
- **Island Detection** (Support-mode toolbar button): may run before supports exist, listing every
  bare-model island, or after generation/manual edits, listing only islands not reached by an
  active support contact. It marks each finding with a **red sphere** in the viewport.
- A pop-out tunes Island Detection and Island Support parameters.
- Working goal: the low-res drogon is the canonical push-until-well-supported test
  subject for auto + manual support quality.

## Guided tip placement (user discussion, 2026-09-07)

Semi-automated placement sits between one-click manual tips and whole-region generation:
the user describes *where* with a few clicks and the tool fills in the tips. DESIGN 8.8
"Support lines" is the first of these; this section generalises it.

### One pipeline, many gestures

Every guided tool is the same three stages with a different first stage:

1. **Candidates.** A pure Core function turns the gesture (a mesh, a few surface points, a
   pitch) into a list of `TipCandidate`s on the support target. No viewport, no document.
2. **Preview.** While the gesture is live the candidates are drawn as ghost tips (existing
   preview shapes); nothing is routed and nothing enters the graph.
3. **Commit.** On confirmation all candidates are routed as one batch through the existing
   generation path and land as **one undo step**. Candidates that cannot be routed are
   skipped and counted: the status line says "10 of 12 placed", never a silent drop.

The support target policy is checked once, when the gesture begins. The object under the
first click is the contact object for the whole gesture; picks on any other object are
ignored while the gesture is live, exactly as a tip drag ignores them.

### Modal tools, Blender style

Each gesture is one modal tool with Begin / Update / Commit / Cancel and a preview it
exposes; the viewport hosts one active tool at a time instead of a fresh set of drag
fields per interaction (the existing tip drag is the model and becomes the first such tool).
Keys, all Support mode only:

- Left click adds a point or confirms; Enter confirms; Escape and right click cancel the
  whole gesture; Backspace removes the last point of a multi-point gesture.
- Pitch is adjustable during the gesture: scroll wheel steps it, typed digits set it
  (Blender modal numeric input). Default pitch is the profile's `SpacingMm`.
- The status line shows the tool, the candidate count and the current pitch.
- The first point may snap to an existing tip: clicking on a displayed tip starts the
  gesture from that tip's contact, so a line can extend a support already placed.

### The tools

- **Line / polyline.** First click anchors; the cursor rubber-bands a surface path from the
  last vertex; each click adds a vertex; Enter or double-click finishes. Tips at pitch along
  the whole path, the anchor included. A straight line is the one-vertex case.
- **Polygon fill.** Three or more vertices closed by Enter. The closed loop of surface paths
  bounds a face set (the enclosed edge-connected patch, at face granularity like painting)
  which goes straight into `RegionGridSampler`. Pitch is the sampler's spacing.
- **Ring** (R). Click a centre, move to set the radius, click to place; tips on the
  circumference at pitch. The circle is drawn in the tangent plane of the centre's face and
  each point projected to the nearest surface point, so on a curved underside the ring hugs
  the surface (decision 2026-09-07).
- **Contour** (C). Hover to choose a height — the cursor's own height on the surface — and
  click to place; tips along the target's contour at that Z, on faces with a downward
  component only (a vertical wall gets none, as with the polygon grid), each connected run
  spaced on its own.
- Stroke (freehand drag) was dropped from the list on 2026-09-07 (user decision).
- **Edge follow.** Click near a crease (dihedral above the sharp-edge angle in
  `MeshFeatures`) and tips track the feature line at pitch in both directions until it ends
  or turns sharper than a limit. Overhang edges are where supports matter most.
- **Overhang perimeter.** Click an overhang patch; tips around its boundary at pitch.
- **Array / mirror.** Selected tips repeated along a direction, or mirrored across the
  model's midplane, each copy re-picked onto the surface.
- **Densify (D) / thin (Shift+D).** Commands, not gestures: they work on the selected tips
  of the target, or all of its tips when nothing is selected (user decision 2026-09-08).
  Neighbours are the edges of the minimum spanning tree over the tips, minus any edge over
  2.5× the median, so separate lines stay separate. Densify inserts "tips per gap" (default
  1) evenly along the surface path between each pair of neighbours and places them as
  guided tips, never on an operand. Thin keeps one tip in N (default 2) along each run,
  walked from an end so a line keeps its ends, and removes the rest with their supports.
  Both live under the "Guided" expander of the Supports pop-out with the ignore-existing
  rows. Toolbar buttons are still owed for every guided key (user rule 2026-09-08).
- **Stamp.** One click drops a small cluster at pitch around the point (the manual
  replacement for the removed mini clusters).

### Existing supports (user decision, 2026-09-07 screen test)

A guided gesture **ignores existing supports by default**: every sampled tip is placed and
routed as if no other support existed, exactly as free mode routes. Whether a gesture
defers to what is already there is the user's explicit choice, never the tool's — the
screen test that decided this laid an edge of supports, then an edge beside it, and got two
tips because the rest were refused for colliding with the first edge's members.

The panel setting "Guided tools ignore existing" (default on) turns this off. When off the
gesture is existing-aware: no guided tip lands within "Guided tip clearance" (default
2.5 mm) of an existing tip, and routing treats existing supports as obstacles and trunks to
share, as generation does.

### Surface path definition (decision)

A path between two surface points is the contour of the mesh cut by the **vertical plane
through the two points**, walked from one to the other along the connected piece that
contains both. It is surface-true, deterministic, and lives in Core where it is testable.
Only when that plane cut yields no connected contour between the points (the two lie on
patches the plane does not join) does the tool fall back to projecting the screen-space
segment onto the surface with the pick ray at many samples. The path follows the mesh
across folds and never bleeds through a wall, for the same reason the region brush does
not: it is built from edge-connected faces.

Sampling at pitch is by arc length from the anchor, so the anchor always carries a tip and
the last tip may be short of the end by less than one pitch; the end vertex gets a tip only
when it is at least `MinSpacingMm` from the previous one.

### Build order

1. Line / polyline, because the surface path is the primitive the polygon and ring
   reuse, and the modal-tool host it introduces carries every later tool.
2. Polygon fill, mostly composition of the path with the painting pipeline.
3. Edge follow, the highest print value but needing crease tracing.
4. Ring, contour.
5. Array, mirror, densify, thin, stamp.

## Parenting (user direction, 2026-09-08; spec for approval)

Guided placement ignores existing supports by default, so a line, polygon or edge produces
one trunk per tip. **Parenting** is the explicit command that turns a crowd of single
supports into trees: branches are parented to trunks so fewer trunks stand. The user runs it
(J) or not (user decision 1); with **Auto-parenting** on (below, user directive 2026-09-08) the
same plan also runs by itself after every placement.

### What it operates on

- The selected tips of the support target, or every tip of the target when nothing is
  selected (user decision 2). Selecting any element of a support selects that support's
  tip for this purpose.
- Each operand tip's **whole support** (cone, branch, trunk, base) is taken down and the
  tip is routed again. Supports that are not operands stay exactly as they are; they act
  as trunks the operands may join and as obstacles, as in generation.

### How it works

Parenting is **generation's routing applied to existing tips**: the operand tips are
routed together through the tree router with trunk sharing on, in the router's own
deterministic order. The three outcomes the user asked for (decision 3) all follow from
that one re-route:

- **Merged.** Two operand tips whose trunks stood apart now share one trunk, the second
  reaching it by a branch.
- **Removed.** A trunk whose tip found an existing trunk within branch range no longer
  exists; its base goes with it.
- **Moved.** In grid mode every surviving trunk stands on a lattice point, chosen by the
  grid rules (nearest reachable, nearer base beats farther trunk). In free mode a trunk
  stands where the router puts it, under its first tip; a later stage may move a shared
  trunk to the centroid of the tips it carries — see "Later".

**A tip never loses its support to parenting.** A tip the re-route refuses keeps the
support it had. The whole command is one undo step, named "Parent supports", and the
status line reports "n supports → m trunks (k unchanged)".

### Hierarchical tree (user direction, 2026-09-08, from a reference image)

The reference shows tips pairing into junctions, junctions pairing again, and one trunk
carrying the lot; the user's words: it should be possible to have every tip of a run on
one trunk. The tree router only ever joins a tip straight onto a trunk, so with a steep
branch-angle limit its branches just grow long (the 10° screen test). Parenting therefore
builds the tree itself when **Hierarchical tree** (default on) is set:

1. Every tip's cone ends at a junction, along the contact normal clamped to 45° from
   vertical (else straight down), if clear of the model and other supports.
2. The two junctions whose merge costs the least branch length are joined, either at a
   new junction under their midpoint where both branches lean at most the branch angle, or
   by the higher junction sending a branch straight into the lower junction's own position
   (or directly below it, as far as that branch's angle needs). Whichever stays highest
   wins, because height is what later merges spend; the into-the-lower form is how a long
   run on a sloping edge ends up on one trunk. Repeat until nothing can merge within the
   length, angle, cone-bend, height and clearance limits.
   *Probe, roof gripper lower edge, 44 tips over a 64 mm rise, 2026-09-08:* at 45° four
   trees, at 60° one tree, plus the six lowest tips as singles — a tip whose cone would end
   under the 10 mm floor cannot have a junction at all, and a pair whose merge point would
   fall under the floor cannot join. The floor and the branch angle are the remaining
   limits, and both are the user's settings.
3. Each surviving junction first tries the trunks of the supports left standing, nearest
   first within the trunk search range: the branch attaches as high as the branch angle
   allows, never above the trunk's top and never below the min branch height or the base
   top; below the top the trunk is split at the attach point, at the top the branch joins
   the top node. Failing that it drops a trunk of its own: straight down, or in grid mode
   by a branch to the nearest reachable lattice point. A cluster with no clear trunk keeps
   its old supports.

Off, parenting joins each tip straight onto a trunk with the tree router, as before.

**Minimum branch height** (Members, default 10 mm, user decision 2026-09-08): branches may
connect at almost any height on a trunk, but never below this height above the plate,
and no junction is made below it. It applies to generation, manual placement and both
parenting modes.

### Modes

- **Grid mode** (base grid on): trunks on lattice points; sharing, snapping onto trunk
  axes, member separation and every other grid-mode rule apply.
- **Free mode** (base grid off): normally every support is blind to every other. Parenting
  is the one operation that turns sharing on in free mode, because sharing is what the
  user asked for by running it. Trunks are not moved to any lattice.

### Auto-parenting (user directive 2026-09-08)

With **Auto-parenting** on (Parenting expander, default on), every placement — T, a guided
commit, densify — is followed at once by a parenting of the tips just placed together with
the tips of the target's existing supports within the trunk search range of any new tip.
The plan is the ordinary one (hierarchical or router, the same settings); supports out of
range are not touched, and a new tip that finds nothing within range simply stands alone.
The placement and its parenting are **one undo step** under the placement's name, so one
gesture is one undo. The status line reports the placed count and the trunks now under
those tips: "Support line: 12 placed → 3 trunks". Off, supports stay single until J.

### Configuration ("Parenting" expander of the Supports pop-out)

- **Max branch length** (mm, default the Members value): how far a tip may reach to join a
  trunk.
- **Max branch angle** (°, default the Members value): the steepest branch allowed; a
  shallower limit keeps branches short and stiff.
- **Trunk search range** (mm, default the Members "Existing trunk range"): how far around
  a tip the router looks for a trunk to join before raising its own.
- **Min tips per trunk** (default 1): after the re-route, a trunk carrying fewer tips than
  this is re-routed once more with the range doubled; if it still stands alone it stays.
  Guards against a parenting pass that merges nothing.
- **Rounds** (default 3): the re-route is run this many times with different seeds and
  the round with the fewest trunks wins (ties: fewest refusals). The router's choices
  depend on its seed, so a poor first outcome is not the last word (user, 2026-09-08).

These default to the Members values so that parenting and generation agree unless the
user says otherwise (configurability directive).

### UI

- Key **J** (join) in Support mode and a **Parent** button in the Supports pop-out (every
  key has a button). No modal: the command runs at once.
- Undo restores every original support element, including bases, with their ids, so
  selections and hidden flags survive an undo.

- **Max cone bend** (°, default 0 = the member angle): how far the branch leaving a cone
  may bend from the cone's own axis. The no-Z-kink rule of 2026-09-07 caps this at the
  member angle for generation and manual placement; a cone on a leaning wall points
  outward, so a join sideways along the edge needs 60–90°, and the first screen test
  (2026-09-08, roof gripper, ~40 trunks for ~80 tips) was capped by exactly this. Raising
  it is the user's explicit choice for parenting only.
- **Max branches per trunk** (default 0 = the growth rule's 6): the second cap that
  screen test hit — six branches per trunk means at least one trunk per six tips.

### Later, not in the first cut

- ~~Auto-parenting~~ — done 2026-09-08, see "Auto-parenting" above. The plan is not
  incremental: it re-routes the new tips with their in-range neighbours, which the probe
  put under 100 ms per placement on the roof gripper.
- **Centroid trunks** in free mode: a shared trunk moved to the XY centroid of its tips,
  with the branches re-fitted, when every branch then meets the angle and length limits.
- ~~Bracing~~ — specified 2026-09-09, see "Bracing" below.

## Bracing (user-approved spec, 2026-09-09)

Tall, thin supports sway while the print peels from the film, and a swaying support
prints a wavy trunk or lets go of its tip. **Bracing** ties neighbouring supports
together with short cross-members so a forest of single trunks behaves like one frame.
The anatomy dictation of 2026-09-03 kept **brace** as a member type; this section says
where braces go, how they are made, and how the user drives them. DESIGN 8.4 calls this
stage 3, "runs after routing and can be rerun on its own".

### What a brace is

- A brace is a straight member of its own diameter between two **different supports**.
  It never counts toward a support: a "whole support" (selection, hide, delete, parenting
  operands) is still the connected tree with braces removed, as the graph already
  defines it. Deleting either support removes the brace with it; deleting a selected
  brace removes only the brace.
- **Braces are added on; they never split a trunk** (user decision 2026-09-09). Each end
  of a brace is a **brace-end node** that sits on the trunk's axis and belongs to that
  trunk (it records the trunk segment's id), so the trunk stays one segment from base to
  top and every routing, parenting and editing rule that reads trunks is untouched. The
  brace's own ball at that node sits inside the trunk's body; supports that touch fuse,
  so the slice is simply the union. Brace-end nodes are not junctions: the orphan
  pruning that peels dead-end junctions ignores them, a support's tip count ignores
  them, and a trunk taken down takes its brace ends and their braces with it.
- Braces join **stems**: the trunk from its base upward, plus whichever member continues it
  most nearly vertically while it leans at most **Max stem lean** (default 30°) from
  vertical — parenting leaves a short trunk with a near-vertical branch above it, and the
  user's drawing of 2026-09-09 braces those as one member. A brace end on a leaning member
  sits on that member's axis. Cones and bases are never brace ends; branches leaning more
  than the limit wait for the next round (see "Later").
- Braces slice and draw exactly like other members (capsule cross-sections, the existing
  bracing colour and the "Show bracing" display toggle), and they are obstacles for every
  later routing, guided placement and parenting pass.

### Which pairs get braced

Bracing walks the operand trunks as a **chain** (user drawing 2026-09-09): from an end of
the row (the trunk with the fewest neighbours in range, then the lowest X, Y), each trunk
pairs with its nearest unvisited neighbour, and the walk continues from that neighbour;
when no neighbour is left a new chain starts. Consecutive trunks of a chain are a pair, and
a pair is braced when all of the following hold:

1. **Both trunks are tall enough.** Each rises at least **Min support height** (default
   20 mm) above the plate. Short supports do not sway and a brace on them is only more
   to remove.
2. **They are neighbours.** The horizontal distance between the two trunk axes is at most
   **Neighbour distance** (default 10 mm; on the 6 mm grid that reaches the diagonal
   neighbour but not the next lattice row). Neither trunk may already carry
   **Max brace partners** (default 3) other trunks: a trunk braced to three neighbours
   is a frame, a fourth adds nothing.
3. **Only the model is in the way** (user, 2026-09-09). A brace that would pass through the
   model is refused; braces may run through branches, other trunks and other braces, so a
   row is tied wherever the geometry allows and one pair's ladder crosses the next pair's
   freely, which is what makes the lattice in the drawing.

**Two chosen supports** (user direction 2026-09-09): when exactly two supports are selected
and K is pressed, those two are braced no matter what stands between them or how far apart
they are — other supports are not obstacles, and the neighbour distance and partner cap do
not apply. The brace angle still does: a pair too far apart for its stems' height gets no
brace.

Both grid and free mode brace, because bracing, like parenting, is an explicit act on
supports that already stand, not a routing preference.

### How the braces are laid

For a pair the braces climb the two trunks as a ladder:

- **Max brace angle** (default 45°) is the most a brace may lean from vertical (user
  decision 2026-09-09): every rung is laid at exactly that lean (on a 6 mm gap the rise is
  6 mm), a steeper rung would be allowed, a flatter one never is. Ladders are laid
  **top-down** (user, 2026-09-09): the first rung reaches as high as both stems allow (its
  head at the top of the stem it rises to, or lower when the stem it leaves is shorter),
  the next rung ends where that one started and comes back, and so on down to **Lowest
  brace height** (default 0 = the Members min branch height, 10 mm). A rung that would
  start under the floor cannot be laid at the angle and is left out, never flattened (the
  user's screen test 2026-09-09 circled three flat bottom rungs). **Brace spacing** (default 0 = continuous) sets a
  vertical pitch instead, leaving gaps between the braces of a pair. Pattern **Zigzag**
  (default) is that alternation; **Diagonal** sends every brace the same way. No X
  bracing and no horizontal rungs (user decision 2026-09-09).
- **Consecutive pairs run in opposite directions** (user drawing 2026-09-09): the first
  pair's ladder starts at its first trunk, the second pair's at its far trunk, and so on.
  Where two pairs share a trunk, one ladder arrives as the other leaves, so along a row the
  braces read as a continuous lattice of diamonds. Which way the first pair goes is
  arbitrary (left to right today).
- A brace is refused individually when its capsule touches the model (with the model
  clearance), another support, or another brace. The ladder simply skips that bay.
- Nothing attaches below the min branch height, as for branches, and nothing attaches
  within one brace radius of a trunk end.
- **Brace diameter** (default 0 = the branch diameter). Braces need to hold, not carry;
  a thinner brace snaps off more cleanly, and the user chooses.

### Operations

- **Brace** command: key **K** in Support mode and a **Brace** button in the Supports
  pop-out (every key has a button). Operands are the supports containing the selection,
  or every support of the target when nothing is selected, exactly as parenting takes
  them. Existing braces between operands stay (re-running adds only what is missing);
  the status line says "Bracing: 14 braces added, 9 supports tied". One undo step,
  "Brace supports".
- **Unbrace**: **Shift+K** and an **Unbrace** button remove every brace touching an
  operand support. One undo step.
- **Select braces** (user request 2026-09-09): a **Select braces** button in the Supports
  pop-out (and Object menu item) replaces the selection with every visible brace of the
  target, or of the operand supports when supports are selected, so Delete, H and
  Shift+H then act on braces alone. Whole-support selection never gathers braces; this
  is the way to get at them as a set.
- **Auto-bracing** (Bracing expander, default on): the Brace command runs by itself at
  the end of Generate Supports, and after every parenting (J or auto-parenting) over the
  supports the parenting touched, folded into that command's undo step as auto-parenting
  is. Manual (T) and guided placements do not brace by themselves: a support placed one at
  a time is the user's own arrangement until they parent or brace it, and auto-parenting
  already re-braces whatever it rebuilds. Off, braces exist only when the user presses K.
- Undo restores braces and brace-end nodes with their ids, so selections and hidden
  flags survive.

### Configuration ("Bracing" expander of the Supports pop-out)

| Setting | Default | Meaning |
| --- | --- | --- |
| Auto-bracing | on | Brace after generation and after parenting. |
| Pattern | Zigzag | Zigzag or Diagonal. |
| Brace diameter | 0 (= branch diameter) | Member diameter of every brace. |
| Max brace angle ° | 45 | The most a brace may lean from vertical; rungs are laid at exactly this. |
| Brace spacing | 0 (= continuous) | Vertical pitch between braces of one pair; 0 = each brace starts where the last ended. |
| Lowest brace height | 0 (= min branch height) | No brace foot below this. |
| Min support height | 20 mm | Only trunks at least this tall are braced. |
| Neighbour distance | 10 mm | Max horizontal gap between braced trunks. |
| Max brace partners | 3 | Trunks one trunk may be braced to. |
| Max stem lean ° | 30 | A branch continuing a trunk within this lean is braced as part of it. |

Zero means "use the Members value" where a Members value exists, as in Parenting. The
defaults are a first guess to be tuned on screen (user, 2026-09-09).

### What changes in the code

`SupportBraceStage` and `BraceGrowthRule` exist from the growth-rule framework (2026-09-03)
but nothing calls them: they join existing nodes only, never lay a ladder, and score by
a slenderness ratio the user never sees. They are replaced by a Core `SupportBracing`
builder in the shape of `HierarchicalParenting` (a `SupportGraphEdit` of added nodes and
segments, obstacle capsules by segment id so a trunk is not its own obstacle), a
`SupportNodeType.BraceEnd` carrying its trunk's segment id, a `Document.BraceSupports` /
`UnbraceSupports` / `SelectBraces` set with one undo step per edit, `SupportConfig` fields
with the `Bracing` prefix, the expander, the key and buttons, and the auto hook in
generation and parenting. The slenderness rule goes.

### Build order

1. ~~Core builder~~ — done 2026-09-09 (chains, continuous zigzag, brace-end nodes,
   chosen pair; the roof gripper probe is in HANDOVER).
2. ~~Document commands, undo, status line; K / Shift+K, the three buttons; the expander.~~
3. ~~Auto-bracing after generation and parenting.~~
4. ~~Diagonal pattern.~~
5. Braces to branches (user, 2026-09-09: "once this has been sorted").

### Later, not in the first cut

- Braces to **branches** and between the two branches of one tree (a wide V braced
  across its throat).
- **Manual bracing**: click two supports and get one brace where the cursor is.
- Braces that **move with edited trunks** once manual support editing exists.
- Bracing across **objects**: two models' forests braced to each other (the collision
  scene already holds every object, so it is a policy question only).

## Still open

- Embedding depth: assumed measured along the tip axis past the contact point.
- Rafts are not implemented yet; "no base under raft" is recorded for when they
  are.
- Base grid details assumed pending the user's confirmation (offered a Blender
  mock-up): square grid aligned to the plate origin; a new trunk takes the nearest
  reachable grid point; a blocked grid point falls through to the next nearest.
