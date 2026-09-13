# Outstanding work worksheet — 2026-09-04 (rev 4, after the ten-job merge)

User's working goal: **push auto + manual supports on drogon-lo until it is
well-supported** — support-quality jobs lead the queue. D1–D7 decisions are all in (rev 2).

## Merged today — main `642e17e`, 573 green, pushed

**ChatGPT lane (ten jobs, `9e351b9`):** 023d support-panel removal + vertical clip slider ·
023e island identity in clusters · 023f isolated fine-feature minis · 023g pop-outs survive
mode changes · 024 island-first routing + Island Support/Detection tools · 024b fine-feature
minis fall back to regular cones · 025 clip caps (`Viewport.CapInterior`, `CapStyle`
Sliced|Painted) · 026 duplicate + mirror · 027 all-side build-volume warnings + permissive
slicing.

**Claude lane (`642e17e`):** contact-face filter — `MaxContactFaceAngleDegrees` (default 90)
and `RequireContactSeesPlate` (default false), both inert by default.

**Support-quality arc on drogon-lo** (refusals, grid on / off): 757/601 before the
fine-feature pass → 777/627 after 023f → 723/576 after 024 → **702/549** after 024b. Net
**55 / 52 better than baseline** while keeping 023f's recovered contacts.

## User decisions taken 2026-09-04 PM

| # | Decision |
|---|----------|
| D8 | Overhang default **stays 45°** despite 40° testing better on gripper. Defaults are a docs matter, to be flagged to users in the docs pass. Record: the comparison is a strict `>`, so a face at exactly 45° is NOT an overhang. |
| D9 | Contact-face filter: **keep as built, stop tuning.** Support painting / regions (W9) is the right mechanism for the "no side supports" problem, not geometric filters. |
| D10 | Internal void supports: **simple struts first**, one per contact, no branching. Niche; scheduled last. |
| D11 | Export orientation **verified against Lychee in UVtools** — U3 closed without a physical print. Treat orientation changes as regressions from here. |
| D12 | **Stop pushing automatic generation.** W6 deep branch shaping toward the Lychee reference is DROPPED, not deferred. Support-quality work continues only where it also benefits manually placed supports (tip geometry, junctions, member separation, mini shape). Manual/UX work and W9 support painting are promoted. |
| D13 | Layout: rotating or scaling a supported model **silently discards** its supports (no dialog); one undo restores transform and supports together. Mirror keeps them, since a reflection maps contacts exactly. |
| D14 | Mini tips become **configurable as rods** (`Capsule`), defaulting to `Cone`. A correction — the spec already called minis "very fine rods". |

## User-only / screen tasks

| # | Task | Status |
|---|------|--------|
| U1 | Screen-test merged batch (ten jobs) | open — checklists in the result files |
| U3 | Mirror-X test print | **CLOSED** (D11) |
| U5 | Re-run `/auto-mode-setup` | open |
| U6 | Branch X-crossing | 028c COMPLETE; awaiting 028c2 then a screen verdict on the separation default |
| U7 | Drogon-head supports: no downward tips | CONFIRMED |
| U8 | 6 mm grid pitch | CONFIRMED |

## ChatGPT queue

| Job | Work item | Status |
|-----|-----------|--------|
| 028 | UVtools check button + configurable path | COMPLETE, reviewed green, awaiting merge |
| 028b | Tip contact defects | **COMPLETE**; leaning fixed, duckbill proved not a defect. Regression found on screen -> 028b2 |
| 028c | Branch X-crossing constraint (U6) | **COMPLETE**, reviewed green; refusal figures superseded by 028c2 |
| 028b2 | Tip cone truncation regression (found on screen) | queued — blocks merging 028b |
| 028c2 | Member separation must measure the SURFACE gap | queued — blocks merging 028c |
| 028c3 | Mini tips configurable as rods (D14) | queued |
| 028d | Stylesheet proposal (was 031, moved up for the bake-off) | queued |
| 028e | Layout support lifecycle (D13) | queued |
| 029 | Multi-model hiding | queued |
| 030 | Support-target selection | queued |

## Claude lane

| Item | Status |
|------|--------|
| Stylesheet proposal | BUILT on `theme-claude` (`dfafd0c`, 524 green): Classic/Carbide/Slate, runtime-switchable, Classic default. **Held unmerged** pending job 031 so the bake-off stays like-for-like |
| Painted cap style (deferred path) | **BUILT** on `painted-clip-caps` (`df5d60c`, 585 green): stencil cross-section in the deferred pass, Classic falls back to Sliced. Needs a GPU screen check — the stencil path has never run against a real driver |
| View cube: labels, size, drag-orbit | **LANDED** on `main` — legible full-word labels and a configurable size (default 120), then drag-orbit: press-and-drag turns the camera at ~1.42 deg/px on the default cube, a press under 4px still snaps. Rate derived from the cube size; say if it feels fast |
| pwmx preview image | backlog — currently a flat top-down height map from `Slicer.cs`; wants a 3D render like Lychee's |
| Internal void supports (sealed cavities) | last, simple struts only (D10) |
| D5 island-search tweaks | brief when wanted — 024 has landed |
| W9 recipes/regions (support painting) | **PROMOTED by D9 and D12** — the main remaining direction. Needs a design pass with the user before it can be briefed |

## Backlog from screen testing 2026-09-06

Recorded from the user's testing of the layout-and-support-ux batch. None of these are
started; they are the user's own words turned into work items.

| # | Item | Notes |
|---|------|-------|
| B1 | Supports in Layout take the model's colour | Selected and unselected, per model — supports are part of the model there, so they should not keep their own support palette. Follows from a model and its supports being one object in Layout. |
| B2 | Model/support collision checker (Layout) | A *checking tool*, on demand: warns that models intersect each other, and that one model's supports intersect another model. Supports intersecting supports after moving models is an ADVISORY, not a blocker. Nothing is prevented — the user may do as they like; the tool only reports. |
| B4 | Support generation progress bar is not smooth | It jumps to about half, then to done. The two phases (compute, then commit in batches) are weighted 0.8/0.2 and the compute phase reports too coarsely. |
| B5 | Toolbar pop-outs should be mutually exclusive | Clicking a toolbar button while another pop-out is open should close that one first. |
| B6 | A viewport click in Support mode must not change the object selection | Clicking a support currently deselects the active model. Only the Objects pop-out selects or deselects models. |
| B7 | The plate stops being transparent near the model | **FIXED on `fix-plate-fade-from-below`, awaiting a screen check.** The fade tested the eye's height, so zooming in from below lifted the eye back over z = 0 and the plate snapped solid mid-approach, hiding the supports. It now ramps on the view direction (`PlateFade`), solid past 12° of look-down. Watch for the plate reading too faint on ordinary low side-on views. |
| B3 | Replace the Layout right-hand panel with a placement pop-out | The right-hand window goes. A toolbar pop-out gives precise placement instead: edit boxes for X/Y/Z position, rotation and scale, with a **uniform** checkbox for scaling. |

## Dormant

SpaceMouse HID fallback + unbound buttons; Grok worktree reference-only.

**W6 deep branch-shaping tuning is DROPPED** by D12 — not dormant, not deferred. Do not
revive it from older notes without the user saying so.
