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

## User-only / screen tasks

| # | Task | Status |
|---|------|--------|
| U1 | Screen-test merged batch (ten jobs) | open — checklists in the result files |
| U3 | Mirror-X test print | **CLOSED** (D11) |
| U4 | Restart Unsloth for the Qwen lane | open |
| U5 | Re-run `/auto-mode-setup` | open |
| U6 | Branch X-crossing | **briefed as job 028c** — user confirmed it on screen |
| U7 | Drogon-head supports: no downward tips | CONFIRMED |
| U8 | 6 mm grid pitch | CONFIRMED |

## ChatGPT queue

| Job | Work item | Status |
|-----|-----------|--------|
| 028 | UVtools check button + configurable path | completed, reviewed green, awaiting merge |
| 028b | Tip contact defects: leaning tips + duckbill pairs | queued (user screen findings) |
| 028c | Branch X-crossing constraint (U6) | queued — highest-value support-quality item |
| 029 | Multi-model hiding | queued |
| 030 | Support-target selection | queued |
| 031 | Stylesheet proposal (ChatGPT half of the bake-off) | queued |

## Claude lane

| Item | Status |
|------|--------|
| Stylesheet proposal | BUILT on `theme-claude` (`dfafd0c`, 524 green): Classic/Carbide/Slate, runtime-switchable, Classic default. **Held unmerged** pending job 031 so the bake-off stays like-for-like |
| Painted cap style (deferred path) | UNBLOCKED — 025 defined `Viewport.CapStyle = Painted`, wired but producing no cap |
| View cube face labels + configurable size | backlog (user request; labels were a W3 deferral) |
| pwmx preview image | backlog — currently a flat top-down height map from `Slicer.cs`; wants a 3D render like Lychee's |
| Internal void supports (sealed cavities) | last, simple struts only (D10) |
| D5 island-search tweaks | brief when wanted — 024 has landed |
| W9 recipes/regions (support painting) | **promoted by D9** — now the answer to the side-support problem; design pass with the user when they want it |

## Dormant

SpaceMouse HID fallback + unbound buttons; Grok worktree reference-only; W6 deep
branch-shaping tuning, still gated on U6 (028c) landing and being judged on screen.
