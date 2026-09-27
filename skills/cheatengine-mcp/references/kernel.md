# Kernel access (DBK/DBVM)

Kernel tools reach below Windows: physical memory, control registers and hypervisor watches. A mistake here does not
crash a game, it crashes the machine. Use them only when the user explicitly asks, on a machine they can afford to
crash, after they saved their work. User-mode tools answer almost every single-player question.

## Gate and scope

- Every `kernel_*` tool, and `debugger_attach(interface=3)`, needs `EnableKernelAccess`. It is on by default and is an
  exposure switch, not a safety net.
- The project never exercises kernel tools in its live qualification: treat them as unqualified.
- MCP never changes Cheat Engine settings and exposes no MSR writes, kernel code execution, cloaking, API redirection or
  system-wide DBVM speedhack. Do not reach those through `lua_execute` either.

## What DBK and DBVM are

- **DBK** is CE's kernel driver (`dbk64.sys`). It gives CE kernel-mode process access, virtual-to-physical translation,
  control registers and the kernel debugger. Windows refuses unsigned drivers by default; CE documents that loading
  fails on 64-bit Windows not booted with unsigned-driver support. Loading the driver is the user's decision in CE.
- **DBVM** is CE's own hypervisor: Windows keeps running as a guest while DBVM sits underneath. It gives physical memory
  access and page watches that the OS and the target cannot see. It needs CPU virtualization support, and Hyper-V and
  its components must be disabled.
- CE's wiki warns that each processor core offloaded to DBVM has roughly a one-in-five chance of a
  `CLOCK_WATCHDOG_TIMEOUT` blue screen, so a many-core machine is likely to crash; limiting the processor count at boot
  lowers the odds. CE shows a confirmation dialog before loading DBVM.

## Tools

| Tool                                                                                                                                     | Effect                                                                                                                                                                    |
|------------------------------------------------------------------------------------------------------------------------------------------|---------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| `kernel_get_status`                                                                                                                      | `dbkInitialized`, `dbvmInitialized`, CR0/CR3/CR4 and DBVM's real CR4 when available. Loads nothing. Call it first.                                                        |
| `kernel_initialize_dbvm(offloadOperatingSystem, reason)`                                                                                 | Loads DBVM. May show CE's dialog (a human must answer) and may blue-screen the machine; `offloadOperatingSystem=true` is the dangerous path. Never retry after a timeout. |
| `kernel_translate_address(address)`                                                                                                      | Virtual address of the current target to physical, through DBK.                                                                                                           |
| `kernel_read_physical(physicalAddress, size)`                                                                                            | Up to 4096 bytes through DBVM; the entire range must fit in unsigned 64-bit physical address space.                                                                       |
| `kernel_write_physical(physicalAddress, bytes)`                                                                                          | Up to 4096 bytes through DBVM, bypassing every protection; the entire range must fit in unsigned 64-bit physical address space.                                          |
| `kernel_start_watch(access, physicalAddress, byteSize, options, internalEntryCount)` -> `kernel_poll_watch(jobId, afterSequence, limit)` | DBVM page watch as a job; the range must fit in unsigned 64-bit physical address space; `runtime_stop_job(jobId)` disables the watch.                                     |

Physical reads, writes and watches require an initialized DBVM.
They refuse with `unsupported` or `invalid_state` before any device call when Cheat Engine reports DBVM unavailable.

## Physical memory rules

- The requested physical address plus the byte count must not cross `FFFFFFFFFFFFFFFF`; MCP rejects a wrapping range before it reaches DBVM.
- Translate per 4 KiB page. Consecutive virtual pages are rarely consecutive physically; a range that crosses a page
  boundary needs one translation per page.
- Translations go stale: pages get paged out, trimmed or remapped (copy-on-write on first write). Translate immediately
  before use, and treat a failed translation as "not resident", not as an error to force.
- Shared pages (DLL and executable images) are mapped into many processes: a physical write there changes every process,
  and possibly Windows itself.
- A wrong physical address can hit the kernel, a driver or another process. Kernel data changes can trigger Windows'
  Kernel Patch Protection bugcheck (`CRITICAL_STRUCTURE_CORRUPTION`) or silent data corruption. Read back what you wrote
  and restore original bytes when done.
- Prefer `memory_read`/`memory_write` for the target's own memory; physical access is for cases where user-mode access
  is blocked and the user accepted the risk.

## Watches

- `access` selects what to log: writes, executes, or reads (CE's read watch logs reads and writes).
- `options` bits: 0 logs the same RIP more than once (when registers differ); 1 ignores `byteSize` and logs the whole
  page; 2 records the FPU state; 3 adds a 4 KB stack snapshot per entry (large). Leave the others at zero.
- `internalEntryCount` bounds DBVM's own log; the job buffer is bounded too (`dropped` counts evictions).
- A physical watch sees every process and the kernel touching that page, not only the target.
- Poll with `afterSequence`; the watch stays armed between polls until the TTL (at most 300 s) or `runtime_stop_job`.

## Hazards checklist

- **Blue screens.** Loading DBVM, physical writes and kernel debugging can all bugcheck the machine and lose unsaved
  work.
- **Driver signing and security features.** Unsigned drivers require weakening Windows' driver signing; that is the
  user's system decision, never yours.
- **Anti-cheat.** Kernel anti-cheats look for DBK and DBVM; forum reports describe detection of `dbk64.sys`. Never use
  kernel tools on protected or online games.
- **"Query memory region routines".** A reported hazard, not documented CE behaviour: the README of the third-party
  cheatengine-mcp-bridge project tells users to keep CE Settings > Extra > "Query memory region routines" unchecked,
  attributing `CLOCK_WATCHDOG_TIMEOUT` blue screens to conflicts with DBVM and anti-cheat, and CE issue #3334 reports
  that blue screen on Windows 10 22H2 with DBVM loaded while that option (with the kernel read/write and open-process
  options) was enabled. The cause is unconfirmed. Before any kernel work, ask the user to check that the option is off;
  do not change it for them.
- **Hyper-V, virtualization-based security and other hypervisors** conflict with DBVM; the user must decide whether to
  disable them.

## Workflow

1. Confirm the user asked for kernel access, understands the crash risk, and saved their work.
2. `kernel_get_status`. If what you need is not loaded, explain what loading it means and let the user decide;
   `kernel_initialize_dbvm` only on explicit consent.
3. `kernel_translate_address` for each page, then `kernel_read_physical`. Write only with a stated reason, read back,
   and restore.
4. For watches: start, poll, stop. Stop every watch before switching targets or ending the session;
   `runtime_list_resources` lists what remains.
5. On `timeout` or `hostEffect:"unknown"`, do not retry: check whether the machine and CE still respond, then call
   `kernel_get_status`.

## Sources

- https://wiki.cheatengine.org/index.php?title=DBVM
- https://www.cheatengine.org/aboutdbvm.php
- https://wiki.cheatengine.org/index.php?title=Help_File:Debugger_options
- https://github.com/cheat-engine/cheat-engine/issues/3334
- https://github.com/miscusi-peek/cheatengine-mcp-bridge
- https://forum.cheatengine.org/viewtopic.php?p=5765469
- https://learn.microsoft.com/windows-hardware/drivers/debugger/bug-check-0x101---clock-watchdog-timeout
- https://learn.microsoft.com/windows-hardware/drivers/debugger/bug-check-0x109---critical-structure-corruption
