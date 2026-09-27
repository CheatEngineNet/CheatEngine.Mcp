# Cheat Engine tutorial map

Cheat Engine (CE) ships two practice targets in its install folder: `Tutorial-x86_64.exe`, a nine-step form-based tutorial, and `gtutorial-x86_64.exe`, a three-level graphical game (32-bit variants also exist). They are always-authorized targets, the right place to learn a technique, and a quick end-to-end smoke test for a CheatEngine.Mcp setup. The [ce_tutorial_walkthrough](workflows/ce-tutorial-walkthrough.md) prompt coaches one step through the workflow mapped below.

## Setup

- Ask the user to start the tutorial; then `process_list` with `nameContains: "tutorial"` and `process_attach` with the exact `processName`. Follow [attach_and_orient](workflows/attach-and-orient.md) for the instance checks.
- Every tutorial step shows a password; entering it on the first page jumps straight to that step. Have the user copy the current password before steps 5 to 9: debugger and injection mistakes can crash the tutorial.
- Addresses change on every run. Re-find values after a restart; never reuse old addresses.
- The debugger is needed from step 5 on. Attach it once per tutorial run and keep it attached across steps; after a VEH debugger has detached, the plugin refuses another attach until the tutorial is restarted (resume with the password). See [debugger](debugger.md).
- Numeric scan values are decimal, as in CE. The step numbers and example instructions below come from the wiki's x64 guide; confirm them with disassembly on the installed build.

## Tutorial-x86_64.exe

| Step | Task | Technique | Prompt | Key tools |
| --- | --- | --- | --- | --- |
| 1 | Welcome page; note the password | Orientation | [attach_and_orient](workflows/attach-and-orient.md) | `instance_list`, `runtime_get_info`, `process_attach` |
| 2 | Health starts at 100 and "Hit me" lowers it; set it to 1000 | Exact 4-byte scan | [find_known_value](workflows/find-known-value.md) | `scan_first` (`int32`, `exact`), `scan_next`, `scan_list_results`, `memory_write` |
| 3 | Health shown only as a bar; set it to 5000 | Unknown initial value, then decreased | [find_unknown_value](workflows/find-unknown-value.md) | `scan_first` (`unknown`), `scan_next` (`decreased`), `memory_write` |
| 4 | Health is a float and ammo a double; set both to 5000 | Float and double scans | [find_float_value](workflows/find-float-value.md) | `scan_first` (`float` or `double`), `scan_next`, `memory_write` |
| 5 | Stop the value from changing | Find what writes, then replace it with NOPs | [find_writer](workflows/find-writer.md), [nop_patch](workflows/nop-patch.md) | `debugger_attach`, `debugger_start_capture` (`write`), `debugger_poll_capture`, `asm_apply_code_patch` |
| 6 | Health moves after "Change pointer"; keep it frozen at 5000 | Single-level pointer | [manual_pointer_chain](workflows/manual-pointer-chain.md), [freeze_value](workflows/freeze-value.md) | `debugger_start_capture` (`access`), `pointer_find_references`, `pointer_read_chain`, `record_create`, `record_set_active` |
| 7 | "Hit me" should add 2 instead of subtracting 1 | Code injection | [aob_injection](workflows/aob-injection.md) | `code_disassemble`, `asm_generate_injection`, `asm_check`, `asm_apply` |
| 8 | Four-level pointer; after "Change pointer" keep health frozen at 5000 | Multilevel pointer, by hand or by scanner | [manual_pointer_chain](workflows/manual-pointer-chain.md), [pointer_scan](workflows/pointer-scan.md) | `pointer_create_map`, `pointer_find_paths`, `pointer_rescan_paths`, `pointer_read_chain`, `record_create` |
| 9 | Four players share one damage routine; enemies must die while your team survives | Shared-code filtering | [shared_code_filter](workflows/shared-code-filter.md) | `debugger_start_capture` (`execute`), `structure_compare`, `asm_generate_injection`, `asm_apply` |

## Step notes

- Step 2: first scan 100, press "Hit me", scan the new exact value, repeat until one address remains, then write 1000.
- Step 3: the first scan is `unknown`; after each hit use `decreased`; narrow with `unchanged` while idle if many results remain.
- Step 4: health is a `float`, ammo a `double`. If an exact float scan misses, use `between` around the displayed value.
- Step 5: the write capture reports the instruction after the write; confirm the writer with `code_disassemble`. NOP it with `asm_apply_code_patch` (`nopInstructions`), check that the value no longer changes, then release it with `asm_release_patch` when done.
- Step 6: an access capture shows the register holding the object base (offset `+0` in the guide). Find what points to that base with `pointer_find_references`; the module-relative hit is the static pointer, `["Tutorial-x86_64.exe"+X]+0` in CE notation.
- Step 7: the guide's build decrements with `sub dword ptr [rsi+780],01`; the injected replacement is `add dword ptr [rsi+780],2`. Save the password first.
- Step 8: the guide's chain is `[[[["Tutorial-x86_64.exe"+X]+10]+18]+0]+18`, which the tools express as base `"Tutorial-x86_64.exe"+X` with offsets `10`, `18`, `0`, `18` in dereference order. The pointer scanner reaches the same result: map, find paths, press "Change pointer", find health again, then `pointer_rescan_paths`.
- Step 9: compare player structures with `structure_compare`; the guide finds a team field at `+14`. Filter the shared instruction with a `cmp` on that field so it only damages the enemy team, then test with the tutorial's "Restart game and autoplay" button.

## gtutorial-x86_64.exe

| Level | Task | Technique | Prompt | Key tools |
| --- | --- | --- | --- | --- |
| 1 | Destroy the target; every 5 shots you must reload, and the target shields and heals meanwhile | Integer health (starts at 100, 24 damage per hit), ammo counter, heal code | [find_known_value](workflows/find-known-value.md), [freeze_value](workflows/freeze-value.md), [nop_patch](workflows/nop-patch.md) | `scan_first`, `scan_next`, `memory_write`, `record_set_active`, `asm_apply_code_patch` |
| 2 | Two enemies with 200 health each outgun you (100 health); player and enemies share one damage routine | Shared code | [find_writer](workflows/find-writer.md), [shared_code_filter](workflows/shared-code-filter.md) | `debugger_start_capture`, `debugger_poll_capture`, `structure_compare`, `asm_apply` |
| 3 | Turn all 12 platforms green, then reach the door; enemies kill on contact | Float positions and velocity, collision logic, progress counter | [find_float_value](workflows/find-float-value.md), [trace_logic](workflows/trace-logic.md), [nop_patch](workflows/nop-patch.md) | `scan_first` (`float`), `structure_read`, `debugger_start_trace`, `debugger_poll_trace`, `asm_apply_code_patch` |

- Level 1: the target's health is a 4-byte integer; the ammo counter and the heal routine are alternative handles. Several solutions are valid.
- Level 2: player and enemies are the same class, so one write instruction handles both. Separate them by a field that differs (the constructor takes an enemy flag) before patching.
- Level 3: player `x`, `y` and vertical velocity are floats (teleport or fly), a green-platform counter is an integer, and enemy contact triggers the player's death through a collision check that [trace_logic](workflows/trace-logic.md) can locate. The game names several valid approaches.

## Smoke test

Use the console tutorial to check a new installation end to end; never smoke-test on a real game.

1. [attach_and_orient](workflows/attach-and-orient.md) on `Tutorial-x86_64.exe`: discovery, identity, gates.
2. Step 2 with [find_known_value](workflows/find-known-value.md): main scanner, polling, `memory_write`.
3. [freeze_value](workflows/freeze-value.md) on that address, then unfreeze: address list.
4. Step 5 with [find_writer](workflows/find-writer.md) and [nop_patch](workflows/nop-patch.md), then `asm_release_patch`: debugger job, Auto Assembler gate.
5. Step 8 with [pointer_scan](workflows/pointer-scan.md): managed jobs, pointer maps, rescans.
6. [cleanup_session](workflows/cleanup-session.md); `runtime_list_resources` and `runtime_list_jobs` should then be empty.

Report each stage as passed or failed with its tool outcomes and any `hostEffect`. A failure localizes the broken layer: discovery, gateway routing, a gate, the debugger or jobs.

## Sources

- https://wiki.cheatengine.org/index.php?title=Tutorials:Cheat_Engine_Tutorial_Guide_x64
- https://fearlessrevolution.com/viewtopic.php?t=1870
- https://raw.githubusercontent.com/cheat-engine/cheat-engine/master/Cheat%20Engine/Tutorial/graphical/gametutorial1.pas
- https://raw.githubusercontent.com/cheat-engine/cheat-engine/master/Cheat%20Engine/Tutorial/graphical/gametutorial2.pas
- https://raw.githubusercontent.com/cheat-engine/cheat-engine/master/Cheat%20Engine/Tutorial/graphical/gametutorial3.pas
