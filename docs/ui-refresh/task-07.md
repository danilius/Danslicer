# Task 07 — recovery and audit log

## Checkpoint 1 — 2026-09-11
- Scope: documentation-only printer compatibility assessment, explicitly dispatched in parallel with task 06; dispatch supersedes old sequential queue wording.
- Worktree: C:/Users/plane/.codex/worktrees/a19b/Danslicer-chatgpt
- Branch: codex/ui-refresh-07
- Exact base / last good source commit: dc4664cd7321030cfe04b866815427b6b4f8a531
- Initial status clean and HEAD detached; created dedicated branch. Sandbox retry authorized shared Git metadata bookkeeping only, no other checkout files edited.
- Read START, SPEC, TASKS, RESUME, CLAUDE-NOTE, architecture printer/export sections and relevant task-05 handoff. No AGENTS.md found in repository inventory.
- Plan before report edits: trace actual writer/profile/raster/resin capabilities; research manufacturer data and independent format implementer source; classify conservatively; verify citations/line references; commit only task-specific docs.
- Findings: one built-in Mono X; fixed pw0Img/516-style writer despite editable version. Historical HANDOVER lines 11–13 records user-confirmed physical mirror-X print (2026-09-10); task-05 did not repeat hardware validation. Do not generalize that print to other models/firmware.
- Sources started: Anycubic firmware/software catalogue; UVtools format source (old PhotonWorkshopFile.cs URL now 404); locate current class and pin upstream revision before final assessment.
- Outstanding: exact format/version/codec/orientation/metadata matching, shortlist and limitations, final docs review and commit.
- Next: resolve authoritative upstream source and manufacturer model specifications. Shared SPEC/TASKS/RESUME remain untouched.

## Checkpoint 2 — source/primary evidence complete, 2026-09-11
- Same worktree/branch/base as checkpoint 1. Last-good documentation commit: dac2ceecd6655a0eddd4ab44dba53c7cf3840a95. Source remains exactly dc4664cd7321030cfe04b866815427b6b4f8a531.
- Retrieved public UVtools source and eleven Anycubic profiles into %TEMP%/danslicer-task07 only. Upstream API revision: 3e3c62faef56a4ae77e2c652cf4914472ec7aa66. Current class is AnycubicFile.cs; cached web master content differed from API-pinned source, so all report source/profile links use the immutable revision.
- Manufacturer catalogue: https://ca.anycubic.com/pages/firmware-software. Followed its official test-file links for Mono 4K, M3, Mono SQ and Mono SE; downloaded as inert binary data. No firmware downloads, install, conversion or hardware operation.
- Result: A only Mono X; B empty under strict requested threshold. Mono 4K and M3 sample flags differ from non-configurable writer flags; SQ/SE sample layouts differ. D shortlist retained, not falsely classified incompatible. C examples cover 517/518, CTB and CXDLP needs.
- Report drafted in printer-compatibility-report.md. Detailed sources and proposed configuration values are there. Shared handoff documents and all application/test code untouched.
- Next: review factual/citation/line consistency and scope, commit the two dedicated documents, verify clean status. Physical validation and implementation remain outside authorization.

## Downloaded evidence manifest

Accessed 2026-09-11. Local files are disposable %TEMP%/danslicer-task07 inputs, not committed vendor models. Re-download from the linked public source and check SHA-256 before reproducing observations. Downloads contain ANYCUBIC followed by four NUL bytes at offsets 0–11.

| File | Bytes | SHA-256 | Official catalogue target |
|---|---:|---|---|
| TEST.pwma | 11839408 | 5769F1CEBECABE81376FEE4D7BF79DE6F71022D823A17847283E5F85879519FF | https://drive.google.com/file/d/1HX9YYRI6KGxk4ySAX1ZUHL4SQaXS-9EZ/view |
| TEST.pm3 | 7940992 | 4C94506F7BA8A53876471479E8AD0CB43194856A40C713B9E3BE5899AC17EEA4 | https://drive.google.com/file/d/13QaDV5VJf76fvYYGY_U_OzZ1WrnGKF6T/view |
| TEST.pmsq | 8166194 | F650352EBF8EB03FDC06B2E96E4C6BC7FF63A769B23F593E0C200A4669636B44 | https://drive.google.com/file/d/1lJrFGVIHZm3L8Tx8k8Z3W6r1361h3aEy/view |
| TEST.pwms | 7202620 | 2D8D07C0FAD53379543119DD7930CAA3DB4BD62DC72081133F8C325F30082842 | https://drive.google.com/file/d/1jA3q1xK50SQeRyhYLS5AWuTwk9mDBPmw/view |

Method: PowerShell ReadAllBytes and BitConverter little-endian UInt32/Single reads, ASCII NUL-terminated strings. Version at 12, table count at 16, HEADER address at 20, PREVIEW at 28. Relative to HEADER: length +12, pixel pitch float +16, resolution +60/+64, per-layer flag +80. Relative to PREVIEW: width +16, height +24. For 516 only: MACHINE address at 44; relative name +16 (96 bytes), codec +112 (16), property +132, dimensions floats +136/+140/+144, max version +148, background +152. Read only observed fields within each file; did not interpret version-1 data using a version-516 MACHINE address.

Observed 4K: version 516, tables 8, header address 52, payload 84, pitch 35, 3840x2400, PerLayer 0, Advanced 0, preview 224x168; machine Photon Mono 4K, pw0Img, property 7, 132.9x80x165, max version 516, background 6506241, layers 1481.
Observed M3: version 516, tables 8, header payload 84, pitch 40, 4096x2560, PerLayer 0, preview 224x168; machine Anycubic Photon M3, pw0Img, property 7, 163.92x102.4x180, background 6506241.
Observed SQ: version 515, tables 5, payload 80, pitch 50, 2400x2560, PerLayer 0, preview 224x168.
Observed SE: version 1, tables 4, payload 80, pitch 51, 1620x2560, PerLayer 0, preview 224x168.

Important inference boundary: differing fields are evidence of unresolved acceptance, not proof that a printer rejects the baseline. No mandatory-field semantics or exact firmware acceptance was invented. No additional B model was promoted merely from UVtools' extension-wide version allowance.

## Verification and final recovery checkpoint — 2026-09-11
- Reviewed actual implementation rather than relying on DESIGN.md's planned IPrinterFileWriter or UVtools validation statements. Found direct writer calls and in-repo round-trip tests; documented those limits.
- Historical Mono X physical mirror confirmation is explicitly credited to HANDOVER.md/user (2026-09-10), not to task 07 or task 05's unrun hardware checks.
- Checks performed: source/profile audit; four manufacturer sample magic/version/header reads and SHA-256s; report source-line/citation review; git diff --check and documentation-only diff scope review. No new build/tests or UVtools execution: source is unchanged, and these would not resolve physical firmware acceptance.
- Failed/recovered access: initial branch creation denied by filesystem sandbox because worktree refs live in shared Git metadata; approved retry succeeded without touching checkout files. Initial public HTTP PowerShell access was sandbox-blocked; approved read-only retrieval succeeded. Old upstream class URL was 404; resolved renamed source. Some browser fetches of pinned raw profiles/Drive pages missed cache; direct public read-only downloads succeeded. No automatic approval-review rejection remained.
- Deliverable commit is the documentation descendant at codex/ui-refresh-07 HEAD (resolve with git rev-parse HEAD); exact final hash supplied in task response. Last-good source remains dc4664cd7321030cfe04b866815427b6b4f8a531; preceding committed recovery point dac2ceecd6655a0eddd4ab44dba53c7cf3840a95.
- Handoff: cherry-pick both task-07 commits in order onto task-06 lineage: dac2ceecd6655a0eddd4ab44dba53c7cf3840a95 then the final report commit supplied in the response. The second commit modifies task-07.md, so do not cherry-pick it alone onto a baseline missing that file. No main merge/push performed; no later task created by this task.
- Outstanding qualification work, if separately authorized: exact target firmware/LCD revision; fresh manufacturer-slicer sample comparison; usable-area versus screen mapping; flag semantics; independent bitmap decoder validation; physical asymmetric calibration and motion/exposure checks. Next action now is user review, then coordinator incorporation into task-06 lineage. Do not implement support on resuming this assessment without a new instruction.

- Concurrent unrelated file discovered during final status check: docs/ui-refresh/task-08-queued.md. It was absent at the initial checkpoint and was not authored, staged, changed or deleted here. Task-07 tracked changes will be clean after commit; this unrelated untracked file may remain in the worktree. No task-08 instruction from that file was executed.
