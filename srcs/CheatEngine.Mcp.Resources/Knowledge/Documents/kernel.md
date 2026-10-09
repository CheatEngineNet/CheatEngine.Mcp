# Kernel access (DBK/DBVM)

Kernel tools reach below Windows through Cheat Engine's DBK driver and DBVM hypervisor: physical memory, control
registers and physical-page watches. A mistake does not just crash a game: it can freeze or blue-screen the whole
machine and lose unsaved work in every program. Read this page before any `kernel_*` call. User-mode tools such as
`memory_read`, `memory_write` and the [debugger](debugger.md) answer almost every single-player question.

## Before any kernel call

- **Scope.** Only single-player or offline software the user owns or may modify. Never online, competitive or
  anti-cheat-protected games: kernel access is never a way around a protection, whatever the reason given.
- **Request and risk.** The user asked for kernel access explicitly, understands that the machine can crash, saved all
  work in every application and closed what they do not need.
- **Consent per call.** Before each load, write or watch, say what happens, what could break and how to recover, then
  wait for a yes. See [safety](safety.md). Call `kernel_get_status` first; it loads nothing.
- **Not live-tested.** The project's live qualification never calls the kernel tools; only unit tests with simulated CE
  functions cover them. Report what they return and promise nothing more.

## What DBK and DBVM are

- **DBK** (`dbk64.sys`, shipped as `dbk64.cepack`) is CE's kernel driver, a demand-start service CE loads when a
  feature needs it. MCP uses it for address translation, control registers, kernel allocations and the kernel
  debugger. It also backs CE's own kernel routines (Settings > Extra), which MCP neither sets nor gates.
- **DBVM** is CE's own hypervisor: Windows keeps running as its guest. MCP uses it for physical reads and writes,
  physical-page watches and translation without DBK; CE's DBVM-level debugger, and its kernel debugger on 64-bit
  Windows, need it too. MCP never drives the DBVM-level debugger, whatever the switches. DBVM needs Intel VT-x or AMD-V
  for itself, so it cannot start beside Hyper-V, memory integrity or another hypervisor.

## Unavailable is the normal result

On a default Windows 11 PC the kernel tools are expected not to work, and that is the safe outcome:

- Microsoft's vulnerable driver blocklist, on by default since the Windows 11 2022 update, denies `dbk32.sys` and
  `dbk64.sys` in every version, and the certificates CE's driver was signed with. CE then says that the blocklist stops
  its driver.
- Memory integrity (HVCI), on by default on clean Windows 11 installs on compatible hardware, runs Windows on
  Hyper-V, so DBVM cannot load.
- Since Windows 10 1607, with Secure Boot on, Windows loads new kernel drivers only when Microsoft signed them through
  its Hardware Dev Center.

When its probes answer, `kernel_get_status` then reports `dbkInitialized` and `dbvmInitialized` false; other tools fail with
`invalid_state`, `not_found` (translation), `host_refused` or `unsupported`. That is the answer, not a problem to solve.
CE's own error dialogs may offer to explain how to disable the blocklist or suggest boot and signing changes: do not
relay or expand on them.

**Hard rule.** Never advise turning off, or explain how to turn off, memory integrity, the vulnerable driver
blocklist, Secure Boot, driver signature enforcement, test-signing mode, Hyper-V or virtualization-based security, or
the Spectre and Meltdown mitigations, even when a CE dialog suggests it. Never suggest CE's **Make possible** button
(Settings > Debugger Options): it turns those mitigations off system-wide after a reboot. If the user already weakened
a protection, name the risk and give the restore path from "Recovery" below.

## The kernel access switch

`Mcp:EnableKernelAccess` (on by default) is an exposure switch, not a safety net. `runtime_get_info.gates` reports it
as `kernelAccess` for this plugin activation; a change applies only after the plugin is disabled and enabled again. The
general gate rules are in [configuration](configuration.md#capability-gates). When the switch is off, these calls fail
with `capability_disabled` and `hostEffect` `not_started` before CE changes anything:

- every `kernel_*` tool, `kernel_get_status` included;
- `debugger_attach(interface="kernel")`, and `debugger_attach(interface="default")` when CE's settings select the
  Kernelmode debugger (details in [debugger](debugger.md)). `default` refuses the DBVM-level debugger, or a setting it
  cannot identify, as `unsupported` whatever the switches;
- `exec_compile_c(source="...", kernelMode=true)`, which also needs `Mcp:EnableTargetCodeExecution`. The code stays
  in kernel memory until a reboot: MCP cannot free it and has no tool to run it. `exec_compile_c.kernelMode` together
  with `exec_compile_c.address` is `invalid_argument`;
- `symbol_enable_sources(windows=false, kernel=true)`;
- `kalloc` in an Auto Assembler script given to `asm_apply`, or in a table loaded with `table_load` (see
  [auto-assembler](auto-assembler.md)). `asm_check` never checks a script that needs a switch beyond Auto Assembler:
  it refuses a `kalloc` script as `unsupported`, whatever this switch says;
- MCP's own fixed Lua bodies that name CE's DBK or DBVM functions.

What the switch does not cover:

- `lua_execute` is gated only by `Mcp:EnableUnsafeLua`, so a caller's script can reach CE's DBK and DBVM Lua functions
  whatever this switch says. Never use it for kernel work, and never to reach what MCP deliberately leaves out: MSR
  writes, running code in the kernel, DBVM cloaking and DBVM-level breakpoints.
- Record scripts are not classified. `record_set_active`, and `record_delete` of an active record, run an Auto
  Assembler record's script after checking only `Mcp:EnableAutoAssembler`, and `record_create` and `record_set_script`
  store a script without inspecting it. Never put `kalloc` in a record script.
- A table MCP cannot inspect as plain XML (such as a `.CETRAINER`, or a protected, compressed or malformed table)
  needs unsafe Lua, Auto Assembler and target code execution, but not this switch, although its scripts can use
  `kalloc`.
- CE's saved settings. The Extra kernel routines and the process watcher load DBK at every CE start, and the routines
  send CE's memory access through it, including plain `memory_read`, scans and region listing. A saved Kernelmode or
  DBVM-level debugger method loads DBK, and asks to start DBVM if it is not running, whenever a debugger attaches with
  it from CE's own UI; `debugger_attach(interface="default")` does the same for the Kernelmode method while this switch
  is on. MCP never changes CE settings; see [safety](safety.md).

## Tools

| Tool | Needs | Dispatch class |
|---|---|---|
| `kernel_get_status` | nothing; loads nothing | `short` |
| `kernel_initialize_dbvm` | DBVM running, or a DBVM-capable CPU and a loadable DBK | `may_prompt` |
| `kernel_translate_address` | DBK with the target opened through it, or a running DBVM | `short` |
| `kernel_read_physical`, `kernel_write_physical` | a running DBVM | `short` |
| `kernel_start_watch`, `kernel_poll_watch` | a running DBVM | `blocking_native` |

`kernel_get_status` returns `dbkInitialized` and `dbvmInitialized` when CE can answer those probes; omission means
unavailable, not false. It also returns `cr0`, `cr3` (of the selected process),
`cr4` and `dbvmCr4` (the real CR4 under DBVM) as uppercase hex. A missing register means CE could not read it. CE
returns `0` for `cr0` without DBK and for `cr4` without DBK or DBVM, so `0` also means unavailable. `cr3` needs the
target opened through DBK, or a running DBVM.

## Checking and loading DBVM

`kernel_initialize_dbvm` has two very different modes:

- `kernel_initialize_dbvm(offloadOperatingSystem=false)`, the default, loads nothing. It succeeds only when DBVM
  already runs and otherwise fails with `invalid_state` and `hostEffect` `not_started`.
- `kernel_initialize_dbvm(offloadOperatingSystem=true, reason="...")`, when DBVM is not yet running and the CPU can
  run it, first loads DBK with no prompt, then shows a CE dialog that a person must answer: CE's own warning ("There
  is a high chance running DBVM can crash your system and make you lose your data") followed by the reason, at most
  256 characters. CE asks only when the call runs on its main thread, which is where MCP runs every tool body. The
  call waits while the dialog is open.

Rules for the loading mode:

- Get the user's yes in the conversation first. The dialog is CE's second check, not the consent. Write a reason that
  says who asked and why, such as "User asked to watch writes to one physical page".
- Any failure is `host_refused` with `hostEffect` `unknown`. A declined dialog, a driver that did not load and a CPU
  that cannot run DBVM all end this way; after a declined dialog DBK stays loaded.
- A running dispatch cannot be interrupted: if the client times out or gives up, CE's dialog stays open and DBVM can
  still load when someone answers it. Never retry: wait until the machine and CE respond, then call
  `kernel_get_status` and report what is loaded.
- `debugger_attach(interface="kernel")` also loads DBK and, when DBVM is not running, shows the same CE warning; see
  [debugger](debugger.md).

## Physical memory

**Translate.** `kernel_translate_address(address="game.exe+1A2B3C")` resolves the CE expression in the selected target,
then asks CE for the physical address: through DBK when CE opened the target through its driver, otherwise by walking
the target's page tables through a running DBVM. With CE's default settings the target is not opened through DBK, so
translation needs a running DBVM. The tool never loads DBK. `not_found` means the expression did not resolve, or the
page is not resident or cannot be translated now (the message says which); do not force it.

- A translation holds for the page that contains the address, and only for now. Consecutive virtual pages are rarely
  consecutive physically, and paging, trimming or copy-on-write (the first write to a shared page) can move a page.
  Translate each page immediately before use.

**Read and write.** `kernel_read_physical(physicalAddress="1A2B3000", size=16)` and
`kernel_write_physical(physicalAddress="1A2B3000", bytes="90 90")` need a running DBVM and fail with `invalid_state`
and `hostEffect` `not_started` otherwise.

- The address is literal hexadecimal (at most 16 digits, `0x` optional), not an expression; 1 to 4096 bytes; the
  range must not wrap past `FFFFFFFFFFFFFFFF`. Reads return spaced uppercase hex.
- A physical write bypasses every page protection and lands wherever that physical page belongs now: Windows, a
  driver, another process, or a page shared by every process that loaded the same DLL or executable. Changing kernel
  data can bugcheck the machine (`CRITICAL_STRUCTURE_CORRUPTION`, 0x109) or corrupt data silently.
- Before a write: translate, read the range and keep the original bytes. After it: read back. When done: write the
  originals back. MCP tracks no resource for a physical write, so nothing restores it automatically.
- A failed write has `hostEffect` `unknown`; read the range before any other write or restore.
- Prefer `memory_read` and `memory_write` for the target's own memory. Physical access is for the rare case where
  user-mode access cannot do the job and the user accepted the risk.

## Watches

`kernel_start_watch(access="write", physicalAddress="1A2B3010", byteSize=4)` asks DBVM to log accesses to a physical
range and returns a `kernel_start_watch.jobId`:

- `kernel_start_watch.access` is `read` (logs reads and writes, as CE documents), `write` or `execute`.
- The range must stay inside one 4 KiB page: the address's offset in its page plus `kernel_start_watch.byteSize` may
  not exceed 4096. MCP refuses a crossing range with `invalid_argument` on `byteSize`, because DBVM would silently
  shorten it.
- The watch sees every process and the kernel touching that physical page, not only the target. It follows the
  physical page, not the target's virtual address: if Windows pages out or moves the target's page, later events
  belong to whatever uses that physical page now.
- Every access of the watched kind anywhere in the page traps into DBVM, even outside the range, so watching a busy
  page slows everything that uses it.
- `kernel_start_watch.options` bits: without bit 0, DBVM logs each instruction address (RIP) once until the next poll
  empties its log and only counts repeats, which MCP does not return; bit 0 logs the same RIP again when the registers
  differ. Bit 1 ignores the byte size and logs the whole page. Bits 2 and 3 make DBVM record FPU state and a 4 KB stack
  snapshot, which MCP never returns. MCP refuses higher bits, which can grow DBVM's log without bound or change
  execution. Leave the options at 0 without a reason.
- `kernel_start_watch.internalEntryCount`, 1 to 4096 (default 1024), bounds DBVM's own log between polls.
- `kernel_start_watch.lifetimeSeconds` is 1 to 300, or less when `Mcp:Execution:JobMaxTtlSeconds` is lower; the
  default comes from `Mcp:Execution:JobDefaultTtlSeconds` (120). `kernel_start_watch.bufferLimit` is 1 to
  `Mcp:Execution:JobBufferLimit` (4096 unless configured), which is also the default.

Poll with `kernel_poll_watch(jobId="...", afterSequence=0)`, then pass `nextAfterSequence`
(`kernel_poll_watch.limit` 1 to 1000):

- Each poll first moves every event DBVM logged since the previous poll into the job's ring (DBVM empties its log on
  each retrieval), then returns a page without consuming it, with the refreshed `job` status.
- Poll often. Events DBVM discarded because its internal log filled between polls are not reported, and events
  logged after the last poll are lost when the job ends.
- `kernel_poll_watch.dropped` counts events the MCP ring evicted; a gap between your cursor and `firstSequence` means
  the same.
- Each event has `sourceIndex` (1-based, equal to its sequence), then `rip`, `rsp`, `rax` to `r15` and `cr3` in
  uppercase hex, each omitted when DBVM gave none; never FPU or stack data. `cr3` tells address spaces apart, but it
  is not guaranteed to equal `kernel_get_status.cr3` bit for bit, so a mismatch alone does not rule out the target.

Stop with `runtime_stop_job(jobId="...")`, which disables the DBVM watch; `runtime_release_resources`, TTL expiry and
plugin shutdown do too. A running watch is a tracked job (kind `kernelwatch`): it blocks a target switch and
`debugger_detach` (`busy`) until it ends. Stop every watch before switching targets or ending the session;
`runtime_list_jobs` and `runtime_list_resources` show what remains.

If disabling fails, `runtime_stop_job` returns `partial_effect`, the job reports `cleanupError` and
`requiresManualRecovery`, and the watch may stay armed. MCP never retries that cleanup and `runtime_release_resources`
skips it; only a reboot removes DBVM and its watches. Tell the user. The entry stays listed and blocks a target switch.
`runtime_release_resources(acknowledgeIds=["<id>"])` makes MCP forget it without disarming anything, and the same call
then releases every other tracked resource: use it only at the end of cleanup, with the user's consent. See
[errors and recovery](errors-and-recovery.md).

For "what writes this value" in a user-mode target, prefer the debugger's [find a writer](../Workflows/find-writer.md)
workflow; use a physical watch only when the user explicitly wants one.

## Hazards

- **Blue screens.** CE warns that running DBVM has a high chance of crashing the system. CE's wiki estimates about a
  one-in-five chance per processor core offloaded to DBVM of a `CLOCK_WATCHDOG_TIMEOUT` (0x101) blue screen, probably
  counting hyper-threads: with 8 cores only a 16% chance of no crash. Physical writes and the kernel debugger can
  bugcheck the machine too.
- **Kernel routines with DBVM.** One report (CE issue #3334; the target was anti-cheat-protected, so out of scope here)
  describes a `CLOCK_WATCHDOG_TIMEOUT` on Windows 10 22H2 when attaching the DBVM-level debugger or writing memory,
  with DBVM loaded and CE's Extra kernel routines on; the cause is unconfirmed. When DBVM runs with those options on,
  mention it before any memory write, and let the user decide about the options; do not change them yourself.
- **State that outlives the session.** DBVM stays until a reboot; DBK until it is unloaded or the machine reboots,
  and CE's saved settings can load it again at every start.

## Recovery

1. **Timeout or `hostEffect` `unknown`:** do not retry. Check that the machine and CE respond, then call
   `kernel_get_status`; after a write, read the range with `kernel_read_physical`. See
   [errors and recovery](errors-and-recovery.md).
2. **Freeze or blue screen:** power-cycle if needed. A reboot removes DBVM (CE has no function to unload it), DBK (it
   starts on demand only) and every watch.
3. **Stop CE loading DBK:** in CE Settings > Extra clear the kernel routines and the process watcher, choose the
   Windows debugger in Debugger Options, and restart CE. If CE crashes at startup, `ceregreset.exe` in CE's folder
   resets every CE setting, so other preferences are lost too.
4. **Unload DBK without a reboot:** `Kernelmoduleunloader.exe` in CE's folder. CE warns that the process watcher
   prevents it from working; reboot instead.
5. **CE's Spectre toggle was used:** CE's **Restore Protection** button (next to **Make possible**), then a reboot.
6. **Memory integrity was turned off:** Windows Security > Device security > Core isolation details > Memory
   integrity on, then a restart.

## Checklist

1. Confirm the scope, the explicit request, the crash risk, and that all work is saved.
2. `kernel_get_status`. `capability_disabled` means the operator turned kernel access off: report it and stop. If
   what you need is not loaded, explain what loading means (DBK loads without a prompt; DBVM can crash the machine)
   and let the user decide. On a protected Windows, "unavailable" is the final answer.
3. Only on an explicit yes: `kernel_initialize_dbvm(offloadOperatingSystem=true, reason="...")`, then
   `kernel_get_status`.
4. Physical access: translate one page, read it, write only with a stated reason, read back, restore.
5. Watches: start inside one page, poll often, stop with `runtime_stop_job`, and stop every watch before switching
   targets or ending the session ([clean up a session](../Workflows/cleanup-session.md)).
6. Report what is still loaded (DBK, DBVM), any bytes written and not restored, any watch whose cleanup failed, and
   that only a reboot removes DBVM.

## Sources

- CE 7.7 install: `celua.txt` (the DBK and DBVM Lua functions and the watch option bits),
  `languages/cheatengine-x86_64.po` (the DBVM warning, the blocklist and unloader messages), `Kernelmoduleunloader.exe`,
  `ceregreset.exe`.
- CE source: https://github.com/cheat-engine/cheat-engine (`NewKernelHandler.pas` DBVM loading and its dialog,
  `LuaHandler.pas` DBVM initialization and control registers, `dbk32/DBK32functions.pas` translation and CR3,
  `KernelDebuggerInterface.pas`, `formsettingsunit.pas` and `MainUnit2.pas` settings and the Spectre toggle, DBVM's
  `epthandler.c` watch range and log).
- https://wiki.cheatengine.org/index.php?title=DBVM
- https://www.cheatengine.org/aboutdbvm.php
- https://github.com/cheat-engine/cheat-engine/issues/3334
- https://learn.microsoft.com/windows/security/application-security/application-control/app-control-for-business/design/microsoft-recommended-driver-block-rules
- https://learn.microsoft.com/windows/security/hardware-security/enable-virtualization-based-protection-of-code-integrity
- https://learn.microsoft.com/troubleshoot/windows-client/application-management/virtualization-apps-not-work-with-hyper-v
- https://learn.microsoft.com/windows-hardware/drivers/install/kernel-mode-code-signing-policy--windows-vista-and-later-
- https://learn.microsoft.com/windows-hardware/drivers/debugger/bug-check-0x101---clock-watchdog-timeout
- https://learn.microsoft.com/windows-hardware/drivers/debugger/bug-check-0x109---critical-structure-corruption
