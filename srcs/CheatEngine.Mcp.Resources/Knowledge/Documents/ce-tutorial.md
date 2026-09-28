# Cheat Engine tutorial map

Cheat Engine ships two practice targets: `Tutorial-x86_64.exe`, a form-based tutorial with steps 1 to 9, and
`gtutorial-x86_64.exe`, a small graphical game with levels 1 to 3. This page maps each step and level to the technique
it teaches, the workflow that coaches it and the tools that do the work, lists the addresses verified on the builds
that ship with CE 7.7, and ends with a smoke test for a new setup. The
[ce_tutorial_walkthrough](../Workflows/ce-tutorial-walkthrough.md) prompt coaches one step at a time.

## Before you start

- **Scope.** The tutorials are Cheat Engine's own practice programs, made to be modified, so they are always a fair
  target. Still explain each write, freeze, patch and debugger attach and get the user's consent, as on any target.
  The techniques carry over only to single-player or offline software the user owns or may modify, never to online,
  competitive or anti-cheat-protected games (see [safety](safety.md)).
- **Start.** Ask the user to open *Help > Cheat Engine Tutorial (x86_64)*, which starts `Tutorial-x86_64.exe`, or
  *Help > Cheat Engine Tutorial Games*, which starts `gtutorial-x86_64.exe` from 64-bit CE, or to run the file from
  the CE install folder. The folder also holds 32-bit builds: `gtutorial-i386.exe`, and `Tutorial-i386.cepack`, a
  packed file that *Help > Cheat Engine Tutorial* unpacks to `Tutorial-i386.exe`. Their pointers are 4 bytes and
  their offsets differ from this page.
- **Attach.** `process_list(nameContains="tutorial")`, confirm the process with the user, then
  `process_attach(process="Tutorial-x86_64.exe")` (or `gtutorial-x86_64.exe`). Through the gateway, call
  `instance_list` first. See [attach_and_orient](../Workflows/attach-and-orient.md).
- **Passwords.** From step 2 on, the first line of the step's text shows its password as `(PW=...)`. Typing it into
  step 1's password box and pressing OK or Enter jumps straight to that step, so have the user note it before steps 5
  to 9, where a debugger or injection mistake can crash the tutorial. The passwords in CE's source:

  | Step | 2 | 3 | 4 | 5 | 6 | 7 | 8 | 9 |
  |---|---|---|---|---|---|---|---|---|
  | Password | 090453 | 419482 | 890124 | 888899 | 098712 | 013370 | 525927 | 31337157 |

- **Addresses change on every run.** Find values again after a restart; never reuse an address from an earlier run.
  The module-relative addresses in the build notes below hold only for the same build.
- **Quote the module name.** `Tutorial-x86_64.exe` contains `-`. CE's parser splits an expression at `-` and then
  tries to rejoin the pieces into a module name, so the unquoted form usually resolves, but the quoted form, which CE
  itself displays, never depends on that: write `"Tutorial-x86_64.exe"+346C70`, and inside a JSON string escape the
  quotes: `"\"Tutorial-x86_64.exe\"+346C70"`. The `symbol` that `pointer_find_references` returns is not quoted
  (`Tutorial-x86_64.exe+346C70`), while the `expression` that `pointer_list_paths` returns is: add the quotes before
  you reuse a `symbol`. See [address expressions](address-expressions.md).
- **Debugger from step 5.** With consent, `debugger_attach(interface="windows")` once per tutorial run, check
  `activeInterface`, and keep it attached across steps. A process that used the VEH debugger (which also needs
  `Mcp:EnableTargetCodeExecution`) must restart before another attach. `debugger_detach()` fails with `busy` while
  any resource that `runtime_list_resources()` lists remains, such as a running capture, a patch, a named scanner or
  a pause: release those first. See [debugger](debugger.md).

## Tutorial-x86_64.exe

The win checks come from the tutorial's source; the Next button enables when the check passes.

| Step | Task and win check | Technique | Workflow | Key tools |
|---|---|---|---|---|
| 1 | Welcome page with the password box | Attach, orientation | [attach_and_orient](../Workflows/attach-and-orient.md) | `process_list`, `process_attach`, `runtime_get_overview` |
| 2 | Health 100; "Hit me" subtracts 1 to 5; set it to exactly 1000 | Exact 4-byte scan | [find_known_value](../Workflows/find-known-value.md) | `scan_first`, `scan_next`, `scan_list_results`, `memory_write` |
| 3 | Health shown only as a bar, random start 0 to 499, each hit shows its loss (1 to 10); set exactly 5000 | Unknown initial value | [find_unknown_value](../Workflows/find-unknown-value.md) | `scan_first`, `scan_next`, `memory_write` |
| 4 | Health is a float and ammo a double, both 100; set both to 5000 or more | Float and double scans | [find_float_value](../Workflows/find-float-value.md) | `scan_first`, `scan_next`, `memory_write` |
| 5 | "Change value" writes a random 0 to 999; the value must stay the same through a click | Find what writes, NOP it | [find_writer](../Workflows/find-writer.md), [nop_patch](../Workflows/nop-patch.md) | `debugger_start_capture`, `debugger_poll_capture`, `code_disassemble`, `asm_apply_code_patch` |
| 6 | "Change pointer" moves the value to a new block and 3 s later checks for 5000 there | One-level pointer, frozen pointer record | [manual_pointer_chain](../Workflows/manual-pointer-chain.md), [freeze_value](../Workflows/freeze-value.md) | `pointer_find_references`, `pointer_read_chain`, `record_create`, `record_set_active` |
| 7 | "Hit me" subtracts 1; make each click add 2 (new value = old + 2) | AOB code injection | [aob_injection](../Workflows/aob-injection.md) | `aob_generate_signature`, `asm_generate_injection`, `asm_check`, `asm_apply` |
| 8 | Four-level pointer; after "Change pointer" the value at the new place must be 5000 | Multilevel pointer | [pointer_scan](../Workflows/pointer-scan.md), [manual_pointer_chain](../Workflows/manual-pointer-chain.md) | `pointer_create_map`, `pointer_find_paths`, `pointer_rescan_paths`, `record_create` |
| 9 | Four players share one damage routine; "Restart game and autoplay" must end with both enemies dead | Shared-code filter | [shared_code_filter](../Workflows/shared-code-filter.md) | `debugger_start_capture`, `structure_compare`, `asm_generate_injection`, `asm_apply` |

### Step notes

- **Step 2.** `scan_first(valueType="int32", comparison="exact", value="100")`, then poll `scan_get_status()` until
  `resultsReady` is true. After each "Hit me", `scan_next(comparison="exact", value="<new value>")`, poll again, then
  `scan_list_results()`. With consent, `memory_write(address="<address>", valueType="int32", value="1000")`. The main
  scanner uses CE's own scan settings, which `scan_get_status.settings` reports read-only; if nothing is found, see
  [troubleshooting scans](troubleshooting-scans.md).
- **Step 3.** `scan_reset()` (main refuses a first scan while an earlier scan exists), then
  `scan_first(valueType="int32", comparison="unknown")` and poll until the state is `BaselineReady`. After each hit,
  `scan_next(comparison="decreasedBy", value="<loss shown>")` is sharper than `decreased`; while idle,
  `scan_next(comparison="unchanged")` removes noise. Health below 0 restarts at a new random value: reset and rescan.
- **Step 4.** Health starts at exactly 100.0: after `scan_reset()`,
  `scan_first(valueType="float", comparison="exact", value="100")`. The window shows 4 significant digits, so after
  a hit scan the shown value plus or minus one unit of its last digit, such as
  `scan_next(comparison="between", value="95.36", upperValue="95.38")` for 95.37, or use `decreased`. A death resets
  health to 4000. Ammo loses exactly 0.5 per "Fire", so exact `double` scans work (reset first). The step's hint says
  to untick Fast Scan for the double. Main follows CE's setting, shown as `fastScan` and `alignment` in
  `scan_get_status.settings`, so if the double is not found, ask the user to untick it, or use a named scanner, which
  checks every address unless given an alignment:
  `scan_first(scannerName="tut4", valueType="double", comparison="exact", value="100")`, then
  `scan_next(scannerName="tut4", comparison="exact", value="<new value>")`, and `scan_delete(scannerName="tut4")`
  when done (a named scan blocks CE while it runs, and its session blocks a target change). With consent,
  `memory_write(address="<health address>", valueType="float", value="5000")` and
  `memory_write(address="<ammo address>", valueType="double", value="5000")`. See [value types](value-types.md).
- **Step 5.** Find the value (it starts at 100), attach the debugger, then
  `debugger_start_capture(address="<value address>", trigger="write", size=4)` and have the user click "Change value".
  `debugger_poll_capture(jobId="<jobId>", afterSequence=0)`: a data trap reports `ip` after the write, and
  `instructionAddress` is the candidate writer (`isHeuristic` true means it was inferred). Then
  `runtime_stop_job(jobId="<jobId>")` and confirm with
  `code_disassemble(address="<writer address>", before=2, count=4)`. With consent:
  `asm_apply_code_patch(address="<writer address>", expectedBytes="89 10", replacementBytes="90 90", name="tutorial step 5")`.
  Keep `patchId`; release it with `asm_release_patch(patchId="<patchId>")` once the step has passed. A freeze is no
  reliable fix: the tutorial compares the value right after its own write, and only a lucky fast freeze passes.
- **Step 6.** The value's holder is a static variable in the module's `.bss`, at offset 0.
  `pointer_find_references(target="<value address>", maxOffset=0, module="Tutorial-x86_64.exe")` returns it with its
  `symbol`; a capture of the writer shows the same offset, `mov [rdx],eax`. With the holder written as
  `"Tutorial-x86_64.exe"+<offset>` (or its hex `address`), check it with
  `pointer_read_chain(base="<holder>", offsets=["0"], valueType="int32")`, then
  `record_create(records=[{description="Step 6 health", address="<holder>", value="5000", variableType=2, offsets=["0"]}])`.
  The record writes its value last, through the chain; its `currentAddress` must be the value's address. Then
  `record_set_active(ids=[<id>], active=true)` before the user clicks "Change pointer"; an entry whose `active` stays
  false without `pending` carries a `failure` with CE's reason. Freeze the value through the pointer, not the holder
  itself: a frozen holder keeps pointing at the old block, which the tutorial rejects with "freezing the pointer is
  not really a functional solution".
- **Step 7.** Find health and its writer as in step 5 (`sub dword ptr [rsi+7E8],01` on the installed build), then
  `code_decode(address="<writer address>")` for the exact bytes and `aob_generate_signature(address="<writer address>")`
  for `pattern` and `offset` (`generator` is `cheat_engine` in a module this small). Generate the scaffold:
  `asm_generate_injection(module="Tutorial-x86_64.exe", signature="<pattern>", expectedBytes="83 AE E8 07 00 00 01", symbolName="step7hook", offset=<offset>)`.
  Replace the `db` line under `code:` with `add dword ptr [rsi+7E8],2`, then `asm_check(script="<edited script>")`
  must return `accepted` true. It refuses, as `unsupported`, scripts with Lua, C, `globalalloc` or other constructs
  beyond the Auto Assembler gate; this one has none. Consent, then
  `asm_apply(script="<edited script>", name="tutorial step 7")`; keep `patchId`. See [x64 injection](x64-injection.md).
- **Step 8.** Scanner route: `pointer_create_map(mapName="tut8")`, poll `pointer_list_maps()` until the map is ready,
  `pointer_find_paths(scanName="tut8", mapName="tut8", target="<value address>", maxDepth=4, staticRootsOnly=true)`,
  poll `pointer_list_scans()`, then `pointer_list_paths(scanName="tut8", sortBy="depth")`. Let the user click "Change
  pointer" once (the check fails, which is fine), find the value again and
  `pointer_rescan_paths(scanName="tut8", target="<new value address>", dropUnresolved=true)`. Hand route: from the
  value upward, `pointer_find_references(target="<address>", maxOffset=4096)` lists each level's holders with their
  `offset` (18, 0, 18, then 10 on this build) until a holder has a `symbol`; when several come back, prefer small
  offsets and confirm with `pointer_read_chain`. Reverse the collected list into dereference order, hex strings as
  `pointer_list_paths` returns them in `offsets`:
  `record_create(records=[{description="Step 8 health", address="<static base>", value="5000", variableType=2, offsets=["10","18","0","18"]}])`,
  freeze it, then have the user click "Change pointer". Keep CE's Unrandomizer off: the step reports "Unrandomizer
  detected". See [pointers](pointers.md).
- **Step 9.** Health is a float at `+8` of each player object (Dave and Eric 100, HAL and KITT 500). Find one player's
  health and capture its writer (`movss [rbx+08],xmm0`). Then watch that instruction the way CE's "Find out what
  addresses this instruction accesses" does:
  `debugger_start_capture(address="<writer address>", trigger="execute", groupByEffectiveAddress=true, maximumHits=8)`
  while the user presses each player's "Attack": each item's `effectiveAddress` is one player's health, so the base is
  that address minus 8 (without the grouping, RBX in each hit's `registers` is the base).
  `runtime_stop_job(jobId="<jobId>")`, then
  `structure_compare(groupA=["<Dave base>", "<Eric base>"], groupB=["<HAL base>", "<KITT base>"], size=32, granularity=4, interpretAs="signed", mode="discriminate")`
  finds the team at `+14` (1 for your team, 2 for the enemies); health at `+8` also separates the teams until hits
  make the values inside a team differ. Get the signature as in step 7 and hook exactly the 5-byte `movss`:
  `asm_generate_injection(module="Tutorial-x86_64.exe", signature="<pattern>", expectedBytes="F3 0F 11 43 08", symbolName="step9hook", offset=<offset>)`.
  Edit it with the body below, check it, apply it with consent, and have the user press "Restart game and autoplay",
  which creates new player objects. The step text asks you to win without freezing your health (the code does not
  check it). Next on this step closes `Tutorial-x86_64.exe` and starts `gtutorial-x86_64.exe`, so release this run's
  patches and jobs first. See [structures](structures.md).

The step 9 `newmem:` block for the `asm_generate_injection` scaffold (declare `label(enemy)` next to the scaffold's
other labels); `code:` keeps the original `movss` bytes:

```
newmem:
  cmp dword ptr [rbx+14],1   // team 1: your players
  jne enemy
  movss xmm0,[rbx+08]        // keep the current health
  jmp return                 // and skip the write
enemy:
  xorps xmm0,xmm0            // enemies: health 0
code:
  db F3 0F 11 43 08          // movss [rbx+08],xmm0
  jmp return
```

## Build notes

Verified on CE 7.7.0.10621 with `Tutorial-x86_64.exe` of 3,543,936 bytes and `gtutorial-x86_64.exe` of 4,153,760
bytes, by disassembly and CE's source. Other builds move the code: confirm each address with `code_disassemble` before
a patch. The wiki's older x64 guide, for example, shows `[rsi+780]` in step 7.

| Where | Address (CE notation) | Bytes and instruction | Use |
|---|---|---|---|
| Step 5 writer | `"Tutorial-x86_64.exe"+495D8` | `89 10` `mov [rax],edx` | NOP with `90 90` |
| Step 6 value | `["Tutorial-x86_64.exe"+346C70]+0` | writer `+49B8C`: `89 02` `mov [rdx],eax` | holder in `.bss`; pointer record, offset `"0"` |
| Step 7 writer | `"Tutorial-x86_64.exe"+4A557` | `83 AE E8 07 00 00 01` `sub dword ptr [rsi+7E8],01` | 7 bytes, unique AOB |
| Step 8 chain | `[[[["Tutorial-x86_64.exe"+346CA0]+10]+18]+0]+18` | offsets `["10","18","0","18"]` for pointer tools and records alike | 32-bit layout: C, 14, 0, 18 |
| Step 9 writer | `"Tutorial-x86_64.exe"+4BBDD` | `F3 0F 11 43 08` `movss [rbx+08],xmm0` | hook exactly these 5 bytes |
| gtutorial level 1 | `"gtutorial-x86_64.exe"+3FE19` | `83 43 6C 01` `add dword ptr [rbx+6C],01` | shot counter, unique AOB; NOP with `90 90 90 90` |
| gtutorial level 1 | `"gtutorial-x86_64.exe"+3EE23` | `89 50 70` `mov [rax+70],edx` | the target's health setter: damage and heal alike |
| gtutorial level 2 | `"gtutorial-x86_64.exe"+42D10` | `48 89 C8` `mov rax,rcx`, `29 50 60` `sub [rax+60],edx`, `C3` | shared `Damage`, health at `+60`; hook the first 6 bytes |

At the step 9 writer, `jp` and `jae` at `+4BBCC` and `+4BBCE` jump to `+4BBDA` (`movaps xmm0,xmm1`) and a `jmp` at
`+4BBD8` lands on `+4BBDD` itself, so a hook that starts at `+4BBDA` and spans the `movss` would be entered in the
middle of its jump.

## gtutorial-x86_64.exe

Each level shows its task in an info box that the info button reopens. After the first won level the game opens on a
level-select screen (shoot the "Level N" target). A thread checksums the game's code and notices patches, but that
only changes the window caption and the closing message. The game's clock is `GetTickCount64`, one of the clocks CE's
[speedhack](speedhack.md) hooks.

| Level | Task (in-game text) | What the source shows | Workflow | Key tools |
|---|---|---|---|---|
| 1 | Every 5 shots you reload and the target heals; destroy it | Target health int32 100, 24 per hit; the fifth shot starts the 2 s reload before it lands and the target heals back to 100, so one clip deals at most 96 | [find_known_value](../Workflows/find-known-value.md), [freeze_value](../Workflows/freeze-value.md), [nop_patch](../Workflows/nop-patch.md) | `scan_first`, `scan_next`, `memory_write`, `record_set_active`, `asm_apply_code_patch` |
| 2 | Two enemies have more health and hit harder; "Enemy and player are related" | Player 100 (shown), enemies 200; one `Damage` routine for all; when one enemy dies the other heals 20, and after a 3-second countdown homing bombs deal the player's health plus 1 | [find_writer](../Workflows/find-writer.md), [shared_code_filter](../Workflows/shared-code-filter.md) | `debugger_start_capture`, `structure_compare`, `asm_generate_injection`, `asm_apply` |
| 3 | Mark all platforms green to unlock the door; enemies kill in one hit | 12 platforms (the ground included) and an integer green counter; float x/y in -1 to 1 with y growing downward; enemies block the door once it unlocks | [find_position](../Workflows/find-position.md), [trace_logic](../Workflows/trace-logic.md), [nop_patch](../Workflows/nop-patch.md) | `scan_first`, `scan_next`, `debugger_start_trace`, `debugger_poll_trace`, `asm_apply_code_patch` |

- **Level 1.** A/D or the arrow keys rotate, Space fires. Routes: find the target's health with exact int32 scans
  (100, 76, 52, …) and write it low outside a reload; or find the shot counter: the screen shows "Ammo till reload" as
  5 minus the shots fired, but the stored counter counts up (and returns to 0 at the fifth shot), so scan `increased`
  after each shot, then freeze it at 0 or NOP its `add`. Every damage and heal goes through one health setter, so
  NOPing its store blocks damage too. The health bar's percentage is a display copy; freezing it does nothing.
- **Level 2.** A/D or the arrows rotate, W or Up moves, Space fires. Freezing your own health fails, because the bombs
  deal your health plus 1. Find the player's health (shown as a number, exact int32 scan) and its writer, the shared
  `Damage`, then `debugger_start_capture(address="<Damage address>", trigger="execute")` while both sides take hits:
  RCX holds each object. Or group by the `sub`:
  `debugger_start_capture(address="<Damage address>+3", trigger="execute", groupByEffectiveAddress=true)` gives each
  object's health, at `+60`, as `effectiveAddress`. Separate the player from the enemies with `structure_compare`:
  the source keeps an `isenemy` flag in the player class, and the maximum health differs (100 against 200). The game
  creates a new player object after a death, so filter on such a field, never on the player's address. Filter the
  injection so only the player is spared, or make the enemies' damage larger.
- **Level 3.** A/D or the arrows walk; Space, W or Up jumps. The hint names the routes: find the collision check
  with enemies, teleport, or fly. Scan the green counter with exact int32 values as platforms turn green, the
  player's coordinates with `float` unknown and changed/unchanged scans, and [trace](../Workflows/trace-logic.md)
  from the collision check to the call that kills the player to NOP it. Once the door unlocks the enemies move to
  block it; the flag that triggers this is another patch point.

## Smoke test

Run it on `Tutorial-x86_64.exe` after installing or updating CheatEngine.Mcp, never on a real game. Each stage proves
one layer:

1. [attach_and_orient](../Workflows/attach-and-orient.md): discovery (`instance_list` through the gateway),
   `runtime_get_overview()`, `process_attach`. `runtime_get_info()` reports the switches in `gates`; stage 4 needs
   `autoAssembler` true.
2. Step 2 with [find_known_value](../Workflows/find-known-value.md): the main scanner, status polling, `memory_write`.
3. [freeze_value](../Workflows/freeze-value.md) on that address, then `record_set_active(ids=[<id>], active=false)` and
   `record_delete(ids=[<id>])`: the address list.
4. Step 5 with [find_writer](../Workflows/find-writer.md) and [nop_patch](../Workflows/nop-patch.md), then
   `asm_release_patch(patchId="<patchId>")`: debugger jobs and the Auto Assembler gate.
5. Step 8 with [pointer_scan](../Workflows/pointer-scan.md): managed jobs, pointer maps and rescans; then
   `pointer_delete_scan(scanName="tut8")` and `pointer_delete_map(mapName="tut8")`.
6. [cleanup_session](../Workflows/cleanup-session.md): afterwards `runtime_list_resources()` and `asm_list_patches()`
   list nothing, `runtime_list_jobs()` shows no running job and `debugger_get_status()` reports `attached` false.

Report each stage as passed or failed with its tool results and any error `kind` and `hostEffect`; for `internal`,
quote `details.errorId`, which the user finds in the instance's log. The first failing stage points to the broken
layer: discovery or routing, the scanner, the address list, the debugger, a gate or jobs. See
[connection troubleshooting](connection-troubleshooting.md) and [errors and recovery](errors-and-recovery.md).

## Sources

- CE tutorial source: `Unit1.pas` is the welcome page, `Unit2.pas` and `Unit3.pas` are steps 2 and 3, `Unit5.pas` to
  `Unit10.pas` are steps 4 to 9 and `Unit4.pas` is the end screen:
  https://github.com/cheat-engine/cheat-engine/tree/master/Cheat%20Engine/Tutorial
- CE graphical tutorial source (`gametutorial1.pas` to `gametutorial3.pas`, `target.pas`, `playerwithhealth.pas`,
  `gameobjectwithhealth.pas`, `levelselect.pas`, `unit1.pas`):
  https://github.com/cheat-engine/cheat-engine/tree/master/Cheat%20Engine/Tutorial/graphical
- CE Help menu entries and what they start: `MainUnit.lfm` and `MainUnit.pas` in
  https://github.com/cheat-engine/cheat-engine/tree/master/Cheat%20Engine
- CE expression parser, where an unquoted name split at `-` is rejoined into a module name: `symbolhandler.pas`
  (`getAddressFromName`) in https://github.com/cheat-engine/cheat-engine/tree/master/Cheat%20Engine
- Cheat Engine wiki, Cheat Engine Tutorial Guide x64:
  https://wiki.cheatengine.org/index.php?title=Tutorials:Cheat_Engine_Tutorial_Guide_x64
- Disassembly of the installed `Tutorial-x86_64.exe` and `gtutorial-x86_64.exe` (CE 7.7.0.10621).
