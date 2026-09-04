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

## Mini-supports (user dictation, 2026-09-03 late night)

Very fine support is important — teeth, barbs and other fine detail need it:

- **Mini-supports** are very fine rods (canonical name).
- Several mini-supports may **fan out from one branch end**.
- They have a **configurable maximum length**; past it, a new branch or trunk is
  required to carry them.

## Mini-tip clusters (user dictation, 2026-09-04)

- Density clustering is additional to mini-island classification. Regular contacts in a
  connected group of at least three, each linked within the configurable crowding distance,
  become mini-tip members instead of adjacent full-size cones. Mini-island contacts keep their
  existing classification and route pass.
- The proposed crowding-distance default is **1.25 mm**, derived as half the default 2.5 mm tip
  spacing. Each cluster location is the score-weighted centre of its member contacts.
- One purpose-built branch end below the cluster feeds one ascending mini rod per member. The
  carrier follows the ordinary branch-first policy: attach to a reachable trunk when possible,
  otherwise create a clear branch/trunk path to the plate.
- `MiniSupportMaxFanPerBranchEnd` is also the per-cluster cap. Larger connected groups split into
  deterministic, spatially compact follow-on clusters; no over-cap contact is silently dropped.
- Maximum mini length, maximum mini lean and ordinary collision clearance remain binding. An
  unroutable carrier reports its reason against every affected member contact.

## Still open

- Embedding depth: assumed measured along the tip axis past the contact point.
- Rafts are not implemented yet; "no base under raft" is recorded for when they
  are.
- Base grid details assumed pending the user's confirmation (offered a Blender
  mock-up): square grid aligned to the plate origin; a new trunk takes the nearest
  reachable grid point; a blocked grid point falls through to the next nearest.
- Mini-support geometry defaults (rod and tip diameter, max length, fan count)
  are implementation proposals until screen-tested.
