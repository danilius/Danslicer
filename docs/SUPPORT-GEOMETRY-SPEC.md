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

## Still open

- Base grid: assumed to be the existing routing lattice surfaced as a recipe
  parameter, unless the user meant a stricter bases-only-on-grid-points rule.
- Embedding depth: assumed measured along the tip axis past the contact point.
- Rafts are not implemented yet; "no base under raft" is recorded for when they
  are.
