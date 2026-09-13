# Weekend autonomous run — session handover

**You are a fresh Claude session started by a scheduled task. You have no memory of any
previous session. This file is your entire context. Read it fully, do ONE task, stop.**

Authorised by the user on 2026-09-05 for an unattended run ending **Sunday 2026-09-06
at 09:00 local**. After that deadline, do nothing but write a line to the log and exit.

(The first attempt at this run, armed on 2026-09-04, never did any work: a fresh session
had no pre-approved tool permissions, so it stalled on its first prompt with nobody there
to answer. The project now carries `.claude/settings.json` with an allow list covering
dotnet, git, the file tools and the shell utilities these tasks need. If you find yourself
blocked on a permission prompt anyway, do not fight it: write what was blocked into
`Weekend\LOG.md` and exit, so the next session is not wasted the same way.)

## The single most important rule

**Do exactly one task per session, then stop.** Do not start a second. Do not "just also
fix" something you noticed — write it into `Weekend\QUEUE\` as a new task file instead.
Stopping cleanly with one task finished beats half-finishing two.

## Before you do anything

1. If `F:\Git Repos\Danslicer\Weekend\STOP` exists, exit immediately. That is the user's
   kill switch. Do not delete it.
2. If the current local time is after **2026-09-06 09:00**, append one line to
   `Weekend\LOG.md` saying the run has ended and exit.
3. Check `Weekend\LOCK`. If it exists, read it. It records a task name and an ISO start
   time.
   - If the start time is **less than 90 minutes ago**, another session is working. Exit
     immediately and silently. Do not touch anything.
   - If it is **older than 90 minutes**, that session died. Move its task file from
     `Weekend\working\` back to `Weekend\QUEUE\`, append a line to `Weekend\LOG.md`
     recording the reclaim, delete the lock, and continue.
   (Do not trust file modification times for this — a moved file keeps its old mtime. That
   mistake produced a false "stale" alarm during the day session. Use the timestamp
   written inside the lock file.)
4. Take the lock: write `Weekend\LOCK` containing the task file name and the current time
   in ISO 8601. Then claim the **lowest-numbered** file in `Weekend\QUEUE\` by MOVING it to
   `Weekend\working\`.

   **Then keep re-stamping it while you work.** The 90-minute rule above assumes a session
   that stops writing has died. A session that is merely *slow* — a big task, a long test
   run, waiting on a usage-window reset — would otherwise be declared dead and have its
   task handed to a second session working the same files in the same checkout. So rewrite
   `Weekend\LOCK` with your task name and the **current** ISO time after every significant
   step (a build, a test run, a commit), and immediately before AND after any wait. A lock
   is stale only when its timestamp is more than 90 minutes old; re-stamping is what makes
   that mean "dead" rather than "busy".
5. If the queue is empty, append a line to `Weekend\LOG.md` saying so, release the lock,
   and exit. Do not invent work.

## Governance for this run — BRANCH ONLY, DO NOT MERGE

- **Do NOT merge to `main`. Do NOT push.** Work on a branch named for the task, commit
  there, and stop. The user merges in the morning.
- This replaces the earlier "you MAY merge and push" grant, and it is not a loss of trust:
  writes to `main` are gated by the permission classifier on this machine, so a session that
  reaches a merge step **stalls on a prompt nobody is awake to answer** and wastes its
  firing. That happened on 2026-09-05 at 23:19. A branch costs the user ten seconds in the
  morning; a stalled session costs a whole task.
- Everything else about verification is unchanged: build clean and the FULL suite green,
  both confirmed by process exit codes, before you consider the task done. Red means you
  say so in the log — it does not mean you keep going.
- In `Weekend\LOG.md` always record the **branch name and final SHA**, so the user can find
  and merge your work without hunting for it.
- **Never force-push. Never rewrite published history. Never delete a branch.**
- **Never force-push. Never rewrite published history. Never delete a branch that has not
  been merged.**
- End every commit message with:
  `Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>`

## Verification protocol — this project has bitten people here

A `dotnet test --no-build` run after a build that silently failed reports a **stale green**.
That shipped a non-compiling `main` once. So, every time:

```
dotnet build Danslicer.slnx --nologo          # then CHECK $LASTEXITCODE
dotnet test Danslicer.slnx --no-build --nologo # then CHECK $LASTEXITCODE
```

Report real numbers in the log. Never write "green" without having seen both exit codes.

## Screen use — FORBIDDEN for this run

**This run is headless. Do not launch, drive, screenshot or focus the app. Ever.** The user
withdrew screen permission on 2026-09-05 at 23:05, after the screen-driving parts of a
session produced a stream of permission prompts they had to answer by hand. That defeats the
point of an unattended run, so the screen is simply out of scope now.

This OVERRIDES the "Screen check" section that several task files still carry. When you meet
one, do not perform it. Instead:

- Do everything the task can prove headless — build, full test suite, and any behaviour you
  can pin in a unit test. Prefer adding a test to leaving a property unverified.
- Write in `Weekend\LOG.md` exactly which visual judgement you could NOT make, in enough
  detail that the user can make it in one minute at the app: what to look at, what you
  intended it to look like, and what number or value to change if it is wrong.
- Then decide honestly whether the change is safe to merge on tests alone. If its whole
  value is a visual judgement, leave it on its branch, say so, and let the user look. A
  branch waiting for a two-minute human check is a fine outcome; a merged change nobody
  ever looked at is not.

Do not "just check quickly". There is no quick check — every one of them costs the user a
prompt and their attention, which is the thing this run exists to save.

## Where things are

- Main checkout and build: `F:\Git Repos\Danslicer` (branch `main`).
- Your scratch review worktree, if you want one: `F:\Git Repos\.danslicer-review5`.
- Worker worktrees `F:\Git Repos\Danslicer-claude` and `-claude-b` exist; either is fine to
  work in, but `main`'s own checkout is usually simplest for a single task.
- Do NOT touch `F:\Git Repos\Danslicer-chatgpt` — another agent's lane, parked but not
  yours.
- Test models live in `F:\Git Repos\Danslicer\test files\` and are NOT in git. The two that
  matter: `drogon collapse.stl` (referred to as drogon-lo, the user's tuning subject) and
  `roof gripper T2.obj` (a chunky CAD part).

## Project context you will need

Read `docs\WORKSHEET.md` first — it is the live board and carries the user's decisions
D1-D14. Then `docs\DESIGN.md` for the design, and `docs\SUPPORT-GEOMETRY-SPEC.md` for the
dictated support spec. `docs\BENCHMARKS.md` holds every measurement; append, never rewrite.

Decisions that constrain you (from D8-D14):
- The overhang default **stays 45 deg**. Do not change defaults; flag them in docs instead.
- **Stop pushing automatic support generation.** W6 deep branch shaping is DROPPED. Do not
  revive it. Support-quality work is fine only where it also helps manually placed supports.
- New behaviour ships **off by default** unless the user asked otherwise, so an existing
  project is unchanged until they opt in.
- Rotating or scaling a supported model silently discards its supports; mirror keeps them,
  because a reflection maps contacts exactly.

## When you finish the task

1. Append to `Weekend\LOG.md`: task name, branch, final SHA, build and test exit codes and
   counts, whether you merged, and anything the user should look at or decide. Be honest
   about what you could not verify. A task that turned out to be a bad idea, written up
   clearly, is a good outcome.
2. Move the task file from `Weekend\working\` to `Weekend\done\` (or `Weekend\failed\`).
3. Delete `Weekend\LOCK`.
4. Stop. Do not pick up another task.

## If the task turns out to be wrong

If a brief is mistaken about the codebase, or the work turns out to be a bad idea, do NOT
force it through. Stop, write what you found in the log, and move the task to
`Weekend\failed\` with a note. The day session made exactly this mistake twice — asserting
a root cause without checking — and both times the check took two minutes and changed the
answer. Verify before you fix.
