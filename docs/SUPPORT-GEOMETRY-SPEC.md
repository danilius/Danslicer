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
- **Supports may touch each other.** A cone is checked against other supports as the
  frustum it is (two capsules), and no member keeps the model clearance from another
  member: supports that meet fuse. The optional member-separation setting is the one
  rule that keeps members apart. (User screen test 2026-09-07: a manual cone between two
  generated ones was refused as "no clear path".)
- **The grid is a preference, not a reason to refuse.** When no lattice point is reachable
  and no existing trunk can be joined, the support is routed as in free mode, its base
  standing wherever the trunk falls. (User, 2026-09-07: refusing a contact whose trunk
  could drop straight under it is absurd.)
- **Saved mini supports are dropped on load** together with a carrier branch left holding
  nothing: drawn with the new geometry, a cluster of three thin rods looked like a fan of
  three full cones on one ball.
- **Existing supports count at full size.** Supports already in the document (an earlier
  generation, manual placements) are seeded into later routing with each cone at the radius
  of its ball, so a later pass cannot crowd them; the saved project from before this
  change still shows its old mini fans until regenerated.
- **One member draws each joint's ball.** In the viewport the widest member at a node
  (trunk before branch, then lowest id) draws the ball; the others tuck their end caps
  inside it, so no two surfaces coincide and the seams no longer flicker.
- **A cone is straight.** The normal lead-in (a short run along the contact normal before
  bending toward the junction) is set to zero in generation and manual placement and its
  setting is gone from the panel (user screen test 2026-09-07: a bent cone is wrong).
- **Mini-supports are removed for now** (user decision 2026-09-07): generation never
  classifies a contact as a mini, and the mini settings are gone from the support
  panel. Islands at or above the minimum area get regular cone tips; smaller ones are
  ignored as before minis existed. The mini code, its configuration fields and the CLI
  options remain for when they return.

## Mini-supports (user dictation, 2026-09-03 late night)

Very fine support is important — teeth, barbs and other fine detail need it:

- **Mini-supports** are very fine rods (canonical name).
- Several mini-supports may **fan out from one branch end**.
- They have a **configurable maximum length**; past it, a new branch or trunk is
  required to carry them.

### Mini-tip clusters (user dictation with Blender mock-up, 2026-09-04)

Where a regular tip goes, one or more mini-tips may go instead — a **cluster**: a
group of fine rods converging near one contact location, sharing the branch end a
single regular tip would have used. The mock-up shows a Y-shaped support: right
branch ends in one regular cone tip, left branch ends in a cluster of four mini
rods spreading to nearby contact points.

- A cluster is **one or more** mini-tips at one location.
- Use case: places where regular tips would be **too clustered** — several fine
  contacts spread the load without the bulk of adjacent full-size cones.
- Configurable: **max length**, **diameter**, and **max mini-tips in one cluster**.
- (Existing knobs map: MiniSupportMaxLength, MiniSupportDiameter/TipDiameter, and
  MiniSupportMaxFanPerBranchEnd becomes the per-cluster cap.)

#### Implemented semantics (job 023 — proposals pending screen test)

- Density clustering is additional to mini-island classification. Regular-size contacts,
  explicitly including required `Island` contacts, in a connected group of at least three each
  linked within the configurable crowding distance become mini-tip members instead of adjacent
  full-size cones. Each member retains its source strategy as metadata so island coverage remains
  auditable after reclassification. Mini-island contacts keep their existing classification and
  route pass.
- After density clustering, an isolated `Island` or `LocalMinimum` contact whose measured local
  cross-section is no greater than `FineFeatureMaxAreaMm2` becomes a one-member mini cluster.
  Islands use their already-computed first-appearance area. Local minima use the connected solid
  section in a horizontal slice 0.5 mm above the contact; if that fixed-height probe does not
  contain the contact XY, the contact stays regular rather than guessing. Mini-island
  classification still wins, and crowded density groups are never split by this pass.
- The proposed `FineFeatureMaxAreaMm2` default is **1.0 mm²**. On `drogon-lo`, the four isolated
  bottom-spike minima measure 0.36, 0.62, 0.68 and 0.78 mm² at the 0.5 mm probe; the next measured
  local minimum is 4.01 mm², leaving a clean gap around the round-number default.
- After density clustering, an isolated `Island` or `LocalMinimum` contact whose measured local
  cross-section is no greater than `FineFeatureMaxAreaMm2` becomes a one-member mini cluster.
  Islands use their already-computed first-appearance area. Local minima use the connected solid
  section in a horizontal slice 0.5 mm above the contact; if that fixed-height probe does not
  contain the contact XY, the contact stays regular rather than guessing. Mini-island
  classification still wins, and crowded density groups are never split by this pass.
- The proposed `FineFeatureMaxAreaMm2` default is **1.0 mm²**. On `drogon-lo`, the four isolated
  bottom-spike minima measure 0.36, 0.62, 0.68 and 0.78 mm² at the 0.5 mm probe; the next measured
  local minimum is 4.01 mm², leaving a clean gap around the round-number default.
- The proposed crowding-distance default is **1.25 mm**, derived as half the default 2.5 mm tip
  spacing. Each cluster location is the score-weighted centre of its member contacts.
- One purpose-built branch end below the cluster feeds one ascending mini rod per member. The
  carrier follows the ordinary branch-first policy: attach to a reachable trunk when possible,
  otherwise create a clear branch/trunk path to the plate.
- `MiniSupportMaxFanPerBranchEnd` is also the per-cluster cap. Larger connected groups split into
  deterministic, spatially compact follow-on clusters; no over-cap contact is silently dropped.
- Maximum mini length, maximum mini lean and ordinary collision clearance remain binding. An
  unroutable carrier reports its reason against every affected member contact.

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

## Still open

- Embedding depth: assumed measured along the tip axis past the contact point.
- Rafts are not implemented yet; "no base under raft" is recorded for when they
  are.
- Base grid details assumed pending the user's confirmation (offered a Blender
  mock-up): square grid aligned to the plate origin; a new trunk takes the nearest
  reachable grid point; a blocked grid point falls through to the next nearest.
- Mini-support geometry defaults (rod and tip diameter, max length, fan count)
  are implementation proposals until screen-tested.
