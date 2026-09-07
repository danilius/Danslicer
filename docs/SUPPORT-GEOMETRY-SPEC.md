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

## Still open

- Embedding depth: assumed measured along the tip axis past the contact point.
- Rafts are not implemented yet; "no base under raft" is recorded for when they
  are.
- Base grid details assumed pending the user's confirmation (offered a Blender
  mock-up): square grid aligned to the plate origin; a new trunk takes the nearest
  reachable grid point; a blocked grid point falls through to the next nearest.
