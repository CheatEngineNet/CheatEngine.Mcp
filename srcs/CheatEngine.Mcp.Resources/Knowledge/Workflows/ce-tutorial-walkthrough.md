# Cheat Engine tutorial walkthrough

## Goal

Coach step {step} of the {tutorial} target: tutorial is Tutorial-x86_64.exe (steps 1 to 9), gtutorial is
gtutorial-x86_64.exe (levels 1 to 3). Both ship with Cheat Engine as practice targets the user may modify: the safe
place to learn a technique and a quick smoke test of a setup. The step section under Decisions gives the task, the win
condition and the workflow to follow; use its part for {tutorial}.

## Steps

1. Ask the user to start the target from Cheat Engine's Help menu: "Cheat Engine Tutorial (x86_64)" or "Cheat Engine
   Tutorial Games". A tutorial step's password, typed on the first page, jumps to that step; gtutorial offers a level
   select once a level has been won.
2. Attach: `process_list(nameContains="tutorial")`, confirm the row with the user, then
   `process_attach(process="<processId>")`. On a new setup, run [attach and orient](attach-and-orient.md) first.
3. Read the step text in the tutorial window with the user and match it to the step section below. Have the user note
   the password on its first line, "(PW=...)": a debugger or injection mistake can crash the tutorial.
4. Follow the mapped workflow one call at a time and say which button to press between scans (the step window names
   them). A main-scanner scan returns at once: poll `scan_get_status(scannerName="main")` until `isScanning` is false.
5. Ask consent before every write, freeze, patch, injection and debugger attach, even here. Keep the ids the workflow
   returns: record ids, patchId, jobId.
6. Check the win condition with the user: the tutorial enables its Next button; gtutorial ends the level.
7. Before the next step, undo what is no longer needed: `runtime_stop_job(jobId="<jobId>")`,
   `record_set_active(ids=[<id>], active=false)`, then `asm_release_patch(patchId="<patchId>")`. The
   [cleanup](cleanup-session.md) workflow orders the rest.

## Decisions

- Each new search starts with `scan_reset()`. Nothing found: check `state`, `count`, `error` and `settings` (range,
  Fast Scan) in `scan_get_status(scannerName="main")` and the value type, then reset and scan again. Too many rows:
  change the value in the game and narrow again.
- `capability_disabled` in a patch or injection workflow: Mcp:EnableAutoAssembler is off (`runtime_get_info()`
  reports it in `gates`); report it and stop there.
- The tutorial crashed: restart it and type the saved password. Deactivate this session's records (their addresses
  belong to the old process), then `runtime_list_resources()` and, with consent, `runtime_release_resources()`. An
  entry left `cleanup_failed` because the old process is gone has nothing to undo: tell the user, then
  `runtime_release_resources(acknowledgeIds=["<id>"])`, which releases the rest too. Attach to the new process.
- Addresses below are offsets in the target's module (+495D8 is `"Tutorial-x86_64.exe"+495D8`), read from the
  installed build: confirm them with `code_disassemble(address="<address>")`, which prints absolute hex.

### step: 1

- tutorial: the welcome page; attaching (Steps 1 and 2) is the whole task, then Next.
- gtutorial level 1: the target has int32 health 100 and a hit removes 24. After 5 shots the gun reloads for 2 s
  while the target shields and heals to 100, so a clip deals at most 96. Routes: target health with
  [known value](find-known-value.md) (100, then 76 after a hit), write a low value, then shoot outside a reload; or
  the shot counter (it counts up while the screen shows shots left) held at 0 with [freeze](freeze-value.md), or its
  `add dword ptr [rbx+6C],01` found with [find what writes](find-writer.md) and removed with
  [NOP patch](nop-patch.md). Heal and damage go through one health store (`mov [rax+70],edx`): NOPing it blocks
  damage too. The health bar's float is a display copy.

### step: 2

- tutorial (password 090453): int32 health 100; "Hit me" removes 1 to 5. Win: exactly 1000. With
  [known value](find-known-value.md): `scan_first(valueType="int32", comparison="exact", value="100")`, "Hit me",
  `scan_next(comparison="exact", value="<new value>")` until one address remains, then with consent
  `memory_write(address="<address>", valueType="int32", value="1000")`.
- gtutorial level 2: you have 100 health (shown as a number), two enemies have 200; your shots do 1, theirs 2. When
  one enemy dies the other heals 20 and homing bombs follow that deal your health plus 1, so freezing your health
  fails. One damage routine serves everyone (`mov rax,rcx`, `sub [rax+60],edx`, `ret` at +42D10): find your health
  with [known value](find-known-value.md), run [find what writes](find-writer.md), then
  [filter shared code](shared-code-filter.md) with your health as the player address. Filter on a field (the source's
  `isenemy` flag, or the maximum health, 100 against 200), never on your address: a death creates a new player
  object. Hook at the `mov rax,rcx`: the `sub` and `ret` after it are only 4 bytes.

### step: 3

- tutorial (password 419482): int32 health 0 to 499, shown only as a bar; each loss shows briefly. Win: exactly 5000.
  With [unknown value](find-unknown-value.md): `scan_first(valueType="int32", comparison="unknown")`, "Hit me",
  `scan_next(comparison="decreasedBy", value="<loss>")` or `scan_next(comparison="decreased")`, repeat, then write
  5000 with consent. Below 0 the value restarts at random: `scan_reset()` and scan again.
- gtutorial level 3: landing on the 12 red platforms from above turns them green; then the door (bottom right)
  unlocks, the enemies barricade it, and touching an enemy kills and restarts the level. Routes, combinable: the green
  count (int32, [known value](find-known-value.md)); the player's float x and y (-1 to 1, y grows downward) with
  [position](find-position.md) to teleport or fly; the enemy collision check with [trace logic](trace-logic.md); the
  barricade flag (set at the unlock, cleared on death) with [flag](find-flag.md), then [NOP](nop-patch.md) the code
  that sets it.

### step: 4

- tutorial (password 890124): health is a float (100, "Hit me" takes fractions), ammo a double (100, "Fire" takes
  0.5); the window rounds them. Win: both at 5000 or more. Follow [float value](find-float-value.md) for each, one
  after the other, starting with between scans:
  `scan_first(valueType="float", comparison="between", value="<shown - 1>", upperValue="<shown + 1>")` for health and,
  after `scan_reset()`,
  `scan_first(valueType="double", comparison="between", value="<shown - 1>", upperValue="<shown + 1>")` for ammo.
  The step's hint says to untick Fast Scan for the double. The main scanner follows Cheat Engine's setting (`settings`
  in `scan_get_status(scannerName="main")`): ask the user to untick it, or scan the double with a named scanner, which
  checks every address,
  `scan_first(scannerName="tut4", valueType="double", comparison="between", value="<shown - 1>", upperValue="<shown + 1>")`,
  narrow with the same name and end with `scan_delete(scannerName="tut4")`.

### step: 5

- tutorial (password 888899): int32 100 on the heap; "Change value" writes a random 0 to 999. Win: the value stays the
  same after a click. Find it with [known value](find-known-value.md), run [find what writes](find-writer.md) on it
  (`mov [rax],edx` at +495D8), then [NOP patch](nop-patch.md) that instruction, which needs
  Mcp:EnableAutoAssembler. Keep the patchId and release it after Next. A freeze passes only by luck: the tutorial
  checks right after its own write.

### step: 6

- tutorial (password 098712): a static pointer holds the value's block; "Change pointer" writes a random value to a
  new block, waits 3 s (the window does not respond), then checks. Win: 5000 in the new block. Find the value, run
  [find what writes](find-writer.md) and click "Change value" (`mov [rdx],eax` at +49B8C: offset 0, RDX holds the
  block), then the [manual chain](manual-pointer-chain.md) to the static `"Tutorial-x86_64.exe"+346C70`. Before
  "Change pointer", with consent:
  `record_create(records=[{description="Step 6 health", address="<static>", value="5000", variableType=2, offsets=["0"]}])`
  (its `currentAddress` must be the value's address), then `record_set_active(ids=[<id>], active=true)`
  ([freeze](freeze-value.md)): `active` must read true; otherwise, unless `pending`, `failure` gives the reason.

### step: 7

- tutorial (password 013370): "Hit me" subtracts 1 from int32 health 100. Win: a click adds 2. Find the value, run
  [find what writes](find-writer.md) (`sub dword ptr [rsi+000007E8],01` at +4A557, 7 bytes), then
  [AOB injection](aob-injection.md) with the goal "add 2 instead of subtracting 1" (the `db` line under `code:`
  becomes `add dword ptr [rsi+7E8],2`), which needs Mcp:EnableAutoAssembler. Keep its record id or patchId to
  disable it after Next.

### step: 8

- tutorial (password 525927): health is a random int32 below 4000 behind four pointer levels, offsets 10, 18, 0, 18
  in dereference order from the static `"Tutorial-x86_64.exe"+346CA0`; "Change pointer" zeroes the old nodes,
  allocates new ones, then checks after 3 s. Win: 5000 at the new location. Use [pointer scan](pointer-scan.md) (map,
  find paths, "Change pointer", find health again, rescan) or the [manual chain](manual-pointer-chain.md) level by
  level. Verify with `pointer_read_chain(base="<static>", offsets=["10","18","0","18"], valueType="int32")`; a record
  takes the same hexadecimal offsets:
  `record_create(records=[{description="Step 8 health", address="<static>", value="5000", variableType=2, offsets=["10","18","0","18"]}])`;
  freeze it with `record_set_active(ids=[<id>], active=true)` before "Change pointer". Keep Cheat Engine's
  Unrandomizer off: the step detects it.

### step: 9

- tutorial (password 31337157): four players, your team (Dave, Eric) and the enemies (HAL, KITT), lose health through
  one instruction, `movss [rbx+08],xmm0` at +4BBDD (5 bytes; hook exactly it: a `jmp` at +4BBD8 lands on it). Health
  is a float at +8 and the team an int at +14 (1 yours, 2 enemies). Win: after "Restart game and autoplay" the
  enemies die and your team lives; the step rules out freezing your health. Find a player's health with
  [float value](find-float-value.md) (its "Attack" button hits it) and its writer with
  [find what writes](find-writer.md). Then
  `debugger_start_capture(address="<writer address>", trigger="execute", groupByEffectiveAddress=true, maximumHits=8)`
  while the user presses each "Attack"; in `debugger_poll_capture(jobId="<jobId>", afterSequence=0)` each
  `effectiveAddress` minus 8 is a player base, then `runtime_stop_job(jobId="<jobId>")`. Compare the bases with
  [dissect](dissect-structure.md), then [filter shared code](shared-code-filter.md) on the team field. Next exits the
  tutorial and starts gtutorial: deactivate the records and release jobs and patches first.

## Pitfalls

- Values and addresses change on every run; never reuse an address from an earlier run.
- Attach the debugger once per run with `debugger_attach(interface="windows")` and keep it for later steps; a process
  that used the VEH debugger must restart before another attach.
- In steps 6 and 8, create and freeze the pointer record before "Change pointer"; a frozen plain address fails the
  check.
- After an error whose `hostEffect` is not `not_started` or `not_applied`, read the state first and never repeat the
  change blindly.

## Report

A table with columns item, address or expression, type and id (record id, patchId, jobId); whether the win condition
was met and the technique used; the password to resume; what remains active (frozen records, patches, the debugger,
jobs from `runtime_list_resources()`) and how to undo each before the next step. Background:
[CE tutorial](../Documents/ce-tutorial.md).
