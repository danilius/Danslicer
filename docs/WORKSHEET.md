# Outstanding work worksheet — 2026-09-04 (rev 3, queue reprioritized support-first)

User's working goal: **push auto + manual supports on drogon-lo until it is
well-supported** — support-quality jobs now lead the queue. All D1–D7 decisions are in
(rev 2 of this file recorded them; unchanged).

## User-only / screen tasks

| # | Task | Status |
|---|------|--------|
| U1 | Screen-test overnight batch checklists | in progress (several finds fixed same-day) |
| U3 | Mirror-X physical test print | open |
| U4 | Restart Unsloth for the Qwen lane | open |
| U5 | Re-run `/auto-mode-setup` | open |
| U6 | Branch X-crossing gone? (Lychee target) | open — gates deep quality tuning (W6) |
| U7 | Regenerate drogon-head supports: no downward tips | **CONFIRMED** (user, 2026-09-04 PM) |
| U8 | Job 019 on screen: 6 mm grid pitch on regeneration | **CONFIRMED — 6 mm is a good default**; follow-ups queued: grid changes auto-save into active preset (023c), pop-outs stay open with title+X header (023b) |

## Merged today (all pushed, 481 green at `ef5b582`)

Preset feedback-loop crash fix · mini-support descent fix · W1 deferred clip+waterline
(user-verified) · support panel polish (scrollbar/-0) · job 019 grid 6 mm (by
supervisor session) · D2 flip: **Deferred is the default render path**.

## ChatGPT queue (renumbered 2026-09-04 PM, support work first)

| Job | Work item | Status |
|-----|-----------|--------|
| 020 | Reinforce visuals | completed, awaiting supervisor review/merge proposal |
| 021 | Cross-platform builds | completed, awaiting supervisor review/merge proposal |
| 022 | Floating context-sensitive viewport toolbar | IN FLIGHT (its "job 023" dup/mirror reference now means 026) |
| 023 | Mini-tip clusters (spec dictated today; user wants a review flag when testable) | queued next |
| 024 | Island-first generation + Island Support + Island Detection (red spheres, list, tuning pop-out) | queued |
| 025 | Cap clipped interiors (sliced caps incl. supports, style switch) | queued |
| 026 | Duplicate + Mirror commands | queued |
| 027 | Out-of-plate red warning all sides + slice-with-warning | queued |
| 028 | UVtools check button + configurable path | queued |

## Claude lane

| Item | Status |
|------|--------|
| W2 ID-buffer picking (deferred) | IN PROGRESS — queued-pick design |
| W3 View cube + settings icon + display-toggles panel (incl. wireframe overlay, D2) | after W2 |
| Painted (screen-space) cap style for the deferred path | after job 025 defines config keys |
| D5 island-search tweaks (approved) | brief after job 024 lands (extends its tuning pop-out) |
| W9 recipes/regions design pass | with user, when they want it |

## Dormant

SpaceMouse HID fallback + unbound buttons; Grok worktree reference-only; W6 deep
branch-shaping tuning gated on U6.
