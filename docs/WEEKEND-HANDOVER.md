# Weekend autonomous run — session handover

**You are a fresh Claude session started by a scheduled task. You have no memory of any
previous session. This file is your entire context. Read it fully, do ONE task, stop.**

Authorised by the user on 2026-09-04 for an unattended run ending **Saturday 2026-09-05
at 21:00 local**. After that deadline, do nothing but write a line to the log and exit.

## The single most important rule

**Do exactly one task per session, then stop.** Do not start a second. Do not "just also
fix" something you noticed — write it into `Weekend\QUEUE\` as a new task file instead.
Stopping cleanly with one task finished beats half-finishing two.

## Before you do anything

1. If `F:\Git Repos\Danslicer\Weekend\STOP` exists, exit immediately. That is the user's
   kill switch. Do not delete it.
2. If the current local time is after **2026-09-05 21:00**, append one line to
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
5. If the queue is empty, append a line to `Weekend\LOG.md` saying so, release the lock,
   and exit. Do not invent work.

## Governance for this run

- **You MAY merge and push to `main`.** The user granted this explicitly for the weekend.
  It does not extend past the deadline above.
- Merge only when: the solution builds clean, the FULL test suite passes, and you verified
  both with process exit codes. If anything is red, do not merge — commit to the branch,
  record it in the log, and stop.
- Work on a branch named for the task, then merge to `main` with `--no-ff` and push.
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

## Screen use

The user has authorised using the screen from **2026-09-04 19:00 onward**. Before that
time, headless only. When you do drive the app, see the `danslicer-ui-testing` memory for
the foreground/focus/hit-test pitfalls. If a task's value depends on a visual judgement,
take a screenshot, do your honest best, and record in the log what you were unsure about —
the user has said they can correct it afterwards.

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
