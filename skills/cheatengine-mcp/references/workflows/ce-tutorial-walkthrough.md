# Cheat Engine tutorial walkthrough

## Goal

Coach the user through step `{step}` of the `{tutorial}` target (tutorial = Tutorial-x86_64.exe, steps 1-9; gtutorial =
gtutorial-x86_64.exe, levels 1-3) with the matching workflow. The tutorials are safe practice targets and a good smoke
test.

## Steps

1. Ask the user to start the tutorial from Cheat Engine's Help menu (or the gtutorial executable in the CE folder),
   then [attach](../workflows/attach-and-orient.md): `process_list(nameContains="tutorial")`, confirm the process,
   `process_attach(process=...)`.
2. Find `{step}` in the map under Decisions and read the step text in the tutorial window with the user.
3. Coach the mapped workflow, telling the user which tutorial button to press between scans ("Hit me", "Change value",
   "Change pointer", "Fire").
4. Before steps 5 to 9, have the user write down the step password shown at the bottom of the window; debugger and
   injection mistakes can crash the tutorial.
5. Success enables the tutorial's Next button; confirm it with the user.
6. Before the next step, run the relevant part of [cleanup](../workflows/cleanup-session.md): stop captures, release
   patches.

## Decisions

Map of steps:

- Tutorial 1: attach only.
- Tutorial 2: exact int32 value (health 100): [known value](../workflows/find-known-value.md), then write the requested
  value with consent.
- Tutorial 3: unknown initial value, then decreased scans: [unknown value](../workflows/find-unknown-value.md).
- Tutorial 4: a float and a double: [float value](../workflows/find-float-value.md).
- Tutorial 5: code that changes the value: [find what writes](../workflows/find-writer.md),
  then [NOP patch](../workflows/nop-patch.md).
- Tutorial 6: pointer: find what accesses, then a [manual chain](../workflows/manual-pointer-chain.md) and a frozen
  pointer record ([freeze](../workflows/freeze-value.md)).
- Tutorial 7: code injection that adds instead of subtracting: [AOB injection](../workflows/aob-injection.md).
- Tutorial 8: four-level pointer: [pointer scan](../workflows/pointer-scan.md) or the manual chain, then freeze through
  the pointer.
- Tutorial 9: shared code between two teams: [filter shared code](../workflows/shared-code-filter.md).
- gtutorial 1: integer target health that heals on reload: known value, then [freeze](../workflows/freeze-value.md) or a
  NOP patch.
- gtutorial 2: player and enemies share the damage code: filter shared code.
- gtutorial 3: platforms, collisions and float positions: float
  value, [trace logic](../workflows/trace-logic.md), [dissect](../workflows/dissect-structure.md).

Other branches:

- The user is stuck on a scan: check `scan_get_status(scannerName="main")` and the value type first.
- The tutorial crashed: restart it, enter the saved password, release owned resources, then attach to the new process.

## Pitfalls

- Values and addresses change on every run; never reuse addresses from an earlier run.
- After a VEH debugger attach, the tutorial must restart before another attach.
- Every write, freeze and patch still needs the user's consent, even on the tutorial; after an error whose `hostEffect`
  is not `not_started` or `not_applied`, read the state before retrying.

## Report

Step completed or not, the technique used, addresses and ids found, the password reminder, and the owned resources to
release before moving on (`runtime_list_resources()`, then the cleanup workflow). See [CE tutorial](../ce-tutorial.md).
