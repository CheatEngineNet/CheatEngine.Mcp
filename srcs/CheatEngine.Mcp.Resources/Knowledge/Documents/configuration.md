# Configuration reference

This page lists every setting of the CheatEngine.Mcp plugin and gateway: where it is read, its default, the values it
accepts and what it changes. Read it when a call answers `capability_disabled` or `busy`, when a file or table path is
refused, or when the user runs several Cheat Engine (CE) instances. No tool changes a setting: the user edits the
files, and the assistant explains what to change. Two results show settings: `runtime_get_info.gates` (also in
`runtime_get_overview.runtime`) holds the four switches, and `table_list_files.roots` the table folders. Terms are in
the [glossary](glossary.md).

## Where settings come from

The plugin reads its settings at every enable, from these layers; a later layer wins:

1. Built-in defaults, the values on this page.
2. Optional `appsettings.json` beside the plugin DLL; defaults are built in and no settings file ships with the DLL.
3. `appsettings.json` in the data directory, `%APPDATA%\CheatEngine.Mcp`, or the absolute folder named by
   `MCP_DATA_DIRECTORY`. Personal settings belong here, because they survive updates.
4. The environment variables `MCP_HOST`, `MCP_PORT`, `MCP_INSTANCE_NAME` and `MCP_INSTANCE_DIRECTORY`, mapped onto the
   matching `Mcp` keys.

- Both files are optional, an absent key keeps its default, and keys ignore case. No other source is read: gates, file
  and table roots, execution limits and the log level are set only in a JSON file.
- Refusal hints say "set ... in appsettings.json": edit the user file in the data directory, because a key it sets
  overrides the plugin folder's file.
- Nothing reloads. Disable and re-enable the plugin in CE to apply a change. Variables are read from CE's own
  environment, so set them before CE starts.
- An invalid value fails the enable before any backend starts, and the message usually names the key. A non-integer
  `MCP_PORT` fails too, with a plain format error; it never falls back to another port.
- Ambient ASP.NET Core and .NET host settings in CE's environment (`ASPNETCORE_URLS`, `DOTNET_*`, Kestrel endpoints) are
  ignored: only the `Mcp` keys choose the listener.

A user file that allows memory dumps and table files:

```json
{
  "Mcp": {
    "Files": { "AllowedRoots": [ "C:\\CheatEngine\\Files" ] }
  },
  "CheatEngineClient": {
    "AllowedTableRoots": [ "C:\\CheatEngine\\Tables" ]
  }
}
```

## Listener, identity and log

- `Mcp:Host`, default `127.0.0.1`: must be exactly `127.0.0.1`, not `localhost`.
- `Mcp:Port`, default `0`: 0-65535. 0 picks a free port at each enable; a fixed port must differ between running
  instances, or the later enable fails.
- `Mcp:ServerName`, default `CheatEngine.Mcp`: not blank. The backend's `serverInfo.name`, which only the gateway sees.
- `Mcp:InstanceName`, default `Cheat Engine <CE process id>`: not blank, at most 128 characters. The `name` that
  `instance_list` shows; names may repeat, so route by `instanceId`.
- `Mcp:InstanceDirectory`, default `%LOCALAPPDATA%\CheatEngine.Mcp\instances`: an absolute path. The discovery
  registry, which the gateway must read too (see "Gateway options" below).
- `Mcp:Logging:MinimumLevel`, default `Information`: `Trace`, `Debug`, `Information`, `Warning`, `Error`, `Critical` or
  `None`. `Trace` and `Debug` are verbose and may record call details, so use them briefly. The ASP.NET Core, HTTP
  client and MCP SDK categories never log below `Information`, which keeps the access token out of the log.

## Capability gates

Four exposure switches under `Mcp`, all `true` by default. `runtime_get_info.gates` reports them as `autoAssembler`,
`unsafeLua`, `targetCodeExecution` and `kernelAccess`. A tool publishes its fixed need in `_meta`
`cheatengine/requires`, using the contract name given in brackets. A tool whose fixed need is off stays listed, but its
call answers `capability_disabled` with `hostEffect` `not_started` before its arguments are bound. The "also" needs
depend on an argument, on content or on CE's state; they are checked after binding, before anything changes. Either
way the message names the setting. For content it names only the first switch that is off, and why, so another one
may still be missing after the user turns that one on.

- **`Mcp:EnableUnsafeLua`** (`unsafe_lua`): `lua_execute`. Also: `table_load` of a table with a Lua script or forms,
  and Auto Assembler (AA) scripts with `{$lua}`, `{$luacode}`, `luacall`, `include`, `loadbinary`, or a command or
  directive the server does not know.
- **`Mcp:EnableAutoAssembler`** (`auto_assembler`): `asm_check`, `asm_apply`, `asm_apply_code_patch`. Also:
  `record_create` with an AA record or a script, `record_set_script`, `record_set_active` on an AA record or on a
  record whose children option reaches one, `record_delete` of an active AA record, `record_clear` of a list holding
  an active AA record, and `table_load` of a table with AA scripts.
- **`Mcp:EnableTargetCodeExecution`** (`target_code_execution`): `exec_call_local`, `exec_call_method`,
  `exec_call_remote`, `exec_compile_c`, `exec_inject_dotnet`, `exec_inject_library`, `mono_attach`,
  `mono_compile_method`, `mono_invoke_method`, `speedhack_set_speed`. Also:
  - `debugger_attach(interface="veh")`, or `default` when CE's settings select the VEH debugger;
  - AA `{$luacode}`, `{$c}`, `{$ccode}`, `loadlibrary`, `createthread`, `createthreadandwait`, `include`,
    `loadbinary`, or a command or directive the server does not know;
  - `process_attach`, `process_create` and `process_open_file` when the current table's UsesMono option would make CE
    inject its Mono data collector into the opened process;
  - `table_load` of a table that sets UsesMono, or while a process is open and the current table sets it.
- **`Mcp:EnableKernelAccess`** (`kernel_access`): `kernel_get_status`, `kernel_initialize_dbvm`, `kernel_read_physical`,
  `kernel_write_physical`, `kernel_translate_address`, `kernel_start_watch`, `kernel_poll_watch`. Also:
  `debugger_attach(interface="kernel")` (or `default` selecting the kernel debugger),
  `exec_compile_c(kernelMode=true)` (on top of target code execution), `symbol_enable_sources(kernel=true)` and AA
  `kalloc`.

Details that decide a refusal:

- `debugger_attach(interface="default")`, also used when `interface` is omitted, always reads the debugger selected in
  CE's settings first. The VEH and kernel debuggers need their switch (`capability_disabled`); the DBVM debugger, a
  ceserver connection or a setting it cannot identify is refused as `unsupported` whatever the switches, since MCP
  never drives them. `debugger_attach(interface="windows")` needs no switch. See [debugger](debugger.md).
- `asm_apply` and `table_load` (for each script of the table) classify AA scripts. The scan is lexical and errs toward
  more switches: comments hide nothing, a `define` alias counts as the command it stands for, and a command or
  directive that is not built into CE 7.7 (including the Lua-registered `USEMONO`, `FINDMONOMETHOD` and
  `GETMONOSTRUCT`) needs unsafe Lua and target code execution. A script with more overlapping `define` aliases than the
  server can follow needs unsafe Lua, target code execution and kernel access. See [auto assembler](auto-assembler.md).
- `asm_check` needs only `Mcp:EnableAutoAssembler`. CE runs parts of a script while checking it, so `asm_check`
  refuses as `unsupported` (`not_started`), whatever the switches, a script that would need another switch, uses
  `globalalloc`, or writes `$` before anything but hex digits. It ignores commands inside `//` and `/* */` comments,
  but not directives or brace comments. Review such a script by reading it.
- Record scripts are not classified: `record_create`, `record_set_script` and `record_set_active` check only
  `Mcp:EnableAutoAssembler`, so a stored `{$lua}` block or `createthread` runs on activation even with the other
  switches off. `record_set_active` also needs that switch when a listed record's `moActivateChildrenAsWell`
  (activating) or `moDeactivateChildrenAsWell` (deactivating) option reaches a nested AA record; it walks at most 4096
  nested records and treats a larger subtree as reaching one. Read `record_get.script` before activating a record.
- A table that cannot be inspected as plain XML (`.CETRAINER`; protected, compressed or binary; obfuscated, malformed
  or with a document type declaration; over 64 MiB) needs unsafe Lua, Auto Assembler and target code execution
  together.
- By default CE sets the current table's UsesMono option after a successful Mono attach (a Mono extension setting
  controls this), so after `mono_attach` later process opens and table loads can need target code execution too.
  `process_attach`, `process_create`, `process_open_file` and `table_load` return `monoAutoAttach` true when CE may
  inject its collector. See [Mono and .NET](mono-and-dotnet.md).
- Fixed Lua bodies are scanned as well: a tool whose own script names a sensitive CE API needs the matching switch.
- The switches are not a sandbox. `Mcp:EnableUnsafeLua` is a superset of the others, because caller Lua can call any CE
  API, and every enabled tool acts with CE's full rights. Address expressions can run Lua whatever the switches: CE
  evaluates a `$` token not followed by hex digits as Lua, and the shipped `luasymbols.lua` evaluates an unknown name.
  Only `asm_check` screens for this, so keep Lua out of addresses. See
  [safety](safety.md#gates-are-switches-not-a-sandbox).
- To lock a session down, set a switch to `false` in the user file and re-enable the plugin, then confirm it in
  `runtime_get_info.gates`. A `false` left in an old user file overrides the shipped `true`.

## Execution limits

The `Mcp:Execution` section is absent from the shipped file; add it to the user file to change a limit.

| Key                          | Default | Range                             |
|------------------------------|--------:|-----------------------------------|
| `DispatchBudgetMilliseconds` |     100 | 1-10000                           |
| `MaxConcurrentDispatches`    |       4 | 1-64                              |
| `MaxJobs`                    |      16 | 1-64                              |
| `JobDefaultTtlSeconds`       |     120 | 1-300, at most `JobMaxTtlSeconds` |
| `JobMaxTtlSeconds`           |     300 | 1-300                             |
| `JobBufferLimit`             |    4096 | 1-65536                           |

- `DispatchBudgetMilliseconds`: how long one dispatch should hold CE's main thread. A longer one is logged (event 3001),
  never interrupted. `code_find_references`, `code_find_strings` and `lua_find_api` check it and stop with `timeout`
  (`hostEffect` `completed`) once over it, and pointer map capture works in slices of half the budget.
- `MaxConcurrentDispatches`: dispatches admitted at once per instance. Tool calls and live resource reads each take
  one, and so do the module and structure reads behind completions. The next one answers `busy` with `not_started`
  (retryable). Calls share CE's main thread, so a higher value does not make CE faster.
- `MaxJobs`: jobs retained at once, finished ones included until stopped or expired; one more start answers `busy` with
  `not_started`. Stop finished jobs with `runtime_stop_job`.
- `JobDefaultTtlSeconds` is the job lifetime when `lifetimeSeconds` is omitted, and `JobMaxTtlSeconds` the largest
  value accepted; more is `invalid_argument`. No job lives longer than 300 s. The code, debugger, kernel watch and
  pointer jobs and the .NET and Mono instance searches take `lifetimeSeconds`; each default follows
  `JobDefaultTtlSeconds`, even where a tool description says 120.
- `JobBufferLimit`: items a job keeps for polling. Older items are evicted and counted in the poll's `dropped`. On top
  of each tool's own bound it caps `code_start_search.maximumResults` and `kernel_start_watch.bufferLimit` (both default
  to it), `debugger_start_capture.maximumHits`, `debugger_start_trace.maximumSteps`,
  `dotnet_start_instance_search.maximumResults` and `mono_start_instance_search.maximumResults`. Set below a tool's
  default (256 capture hits, 32 trace steps, 100 instances), it makes that start fail with `invalid_argument` unless
  the value is passed explicitly.

Answer `busy` by waiting or stopping jobs; raise a limit only when the user asks. More on jobs:
[workflows](workflows.md).

## Host files and tables

**`Mcp:Files:AllowedRoots`** (default `[]`) lists the absolute local folders where tools may create files:
`memory_dump_to_file(path=...)`, `process_save_file(filename=...)`, `pointer_save_map(path=...)` and
`pointer_save_scan(path=...)`. An empty list refuses every write.

- The file must lie strictly inside a root and must not name a folder. The comparison ignores case and respects folder
  boundaries: `C:\root2` is not inside `C:\root`.
- The root and every folder down to the file must already exist and be real folders, not symbolic links or junctions;
  the tools create no folders.
- `process_save_file` never replaces a file. `memory_dump_to_file`, `pointer_save_map` and `pointer_save_scan` replace
  one only when `overwrite=true`; their writes are committed atomically.
- Each root is checked at enable: an absolute drive-letter path, no UNC or device path (`\\server\share`, `\\?\`,
  `\\.\`), no alternate data stream, device name or wildcard, no repeat. An invalid root fails the enable.

Reads need no root but follow the same path rules: `process_create`, `process_open_file`, `memory_load_from_file`,
`pointer_load_map`, `pointer_load_scan`, `symbol_add_module`, `exec_inject_library`, `exec_inject_dotnet`, `table_load`,
and `module_find_patches` for the module file. A path must be absolute and on a local fixed drive (not a network,
removable or virtual drive); symbolic links, junctions and other reparse points, 8.3 short names, wildcards and
segments ending in a space or a period are refused. The MCP data directory (settings and logs) and the instance registry
are always refused, even under a root.

**`CheatEngineClient:AllowedTableRoots`** (default `[]`) lists the folders for `table_load`, `table_save` and
`table_list_files`; an empty list refuses every table file. CheatEngine.Client validates the entries at enable (each
absolute, not blank and distinct, or the enable fails) and enforces the list on its own; the MCP tools check it too and
apply the path rules above to each root and table path, so a root that breaks them makes these tools answer
`invalid_argument`. A table path must end in `.CT`, `.XML` or `.CETRAINER`. `table_list_files` considers at most 10000
files and skips a root that is missing, linked or unreadable (`table_list_files.exact` is then false). Its paths are
ready for `table_load`, which also checks the gates the table's content needs. See [cheat tables](cheat-tables.md).

Suggest dedicated folders such as `C:\CheatEngine\Files`; never a drive root, a system folder or a folder of unrelated
user data.

## Client memory budgets

`CheatEngineClient:MemoryResourceLimits` holds CheatEngine.Client's per-request budgets, checked before CE is called:
`MaximumReadBytes` and `MaximumWriteBytes` (1048576 each), `MaximumStringBytes` and `MaximumBatchPayloadBytes` (65536
each), and `MaximumBatchOperationCount` (1024, also its ceiling). Values must be positive, or the enable fails. They
are not in the shipped file; leave them at their defaults, since a lower value makes larger tool requests fail.

## Environment variables

| Variable                           | Read by            | Sets                                                  |
|------------------------------------|--------------------|-------------------------------------------------------|
| `MCP_DATA_DIRECTORY`               | plugin             | the folder of the user settings file and the logs     |
| `MCP_HOST`, `MCP_PORT`             | plugin             | `Mcp:Host`, `Mcp:Port`                                |
| `MCP_INSTANCE_NAME`                | plugin             | `Mcp:InstanceName`                                    |
| `MCP_INSTANCE_DIRECTORY`           | plugin and gateway | `Mcp:InstanceDirectory`, and the gateway's registry   |
| `MCP_GATEWAY_CALL_TIMEOUT_SECONDS` | gateway            | the call timeout                                      |

- `MCP_DATA_DIRECTORY` must be absolute; a relative value fails the enable.
- The plugin applies a variable whenever it is set, even when blank, and a blank value fails the enable; the gateway
  treats a blank variable as unset. Remove a variable rather than blanking it.
- To name two instances that share one plugin folder, start each CE from a shell that sets its own
  `MCP_INSTANCE_NAME`, or its own `MCP_DATA_DIRECTORY`. Keep `Port` at 0.
- A variable reaches only processes started after it is set. Setting `MCP_INSTANCE_DIRECTORY` once as a user variable,
  then restarting CE and the AI client, keeps every plugin and the gateway in agreement.

## Gateway options

`CheatEngine.Mcp.Gateway.exe` takes two options. An argument beats its variable, which beats the default; a blank
variable counts as unset.

- `--instance-directory <absolute path>` or `MCP_INSTANCE_DIRECTORY`, default
  `%LOCALAPPDATA%\CheatEngine.Mcp\instances`. It must equal every plugin's `Mcp:InstanceDirectory`: a plugin that
  changed only the JSON key is invisible to a gateway started without the argument.
- `--call-timeout-seconds <seconds>` or `MCP_GATEWAY_CALL_TIMEOUT_SECONDS`: a whole number from 5 to 3600, default 45.
  It bounds each routed tool call and live resource read while the instance works on it, including while a CE dialog
  waits for the user. An expired call answers `timeout` with `hostEffect` `unknown` and is never sent again: inspect
  the state before repeating a change. A completion forwarded to an instance waits at most 2 s, or the timeout if
  shorter.
- Any other argument, a missing value, a relative directory or a timeout that is not a whole number in range stops the
  gateway at startup. The reason goes to standard error, which most AI clients show in their MCP server log.

In the AI client's server entry (the layout varies by client; use the folder where the release was unpacked):

```json
{
  "mcpServers": {
    "cheatengine": {
      "command": "C:\\CheatEngine.Mcp\\CheatEngine.Mcp.Gateway.exe",
      "args": [ "--call-timeout-seconds", "120" ]
    }
  }
}
```

Raise the timeout only for a known long call; long work belongs in jobs. Setup and symptoms:
[connection troubleshooting](connection-troubleshooting.md).

## Logs and discovery files

- **Plugin log:** `CheatEngine.Mcp.<CE process id>.log` in the data directory. It rolls at 10 MiB, keeps five archives
  (`.1.log` is the newest) and truncates one entry at 32768 characters; these bounds are fixed. When the writer falls
  behind or the file is unavailable, entries are dropped and their count is logged.
- **Events:** 3001 (a dispatch over budget), 3003 (cancelled after CE work started), 3004 (a dispatch's unexpected
  failure), 3002 and 3006 (a tool's unexpected exception, or an internal error it reported), 3005 and 3007 (the same
  for a resource). The 3002, 3005, 3006 and 3007 entries hold the tool or resource name, the exception type, the
  operation (3006, 3007) and an `errorId`; never the message, stack, paths or tokens.
- **Error ids:** an `internal` tool error carries `details.errorId` (16 hex characters) and a failed resource read
  `error.data.errorId`. Quote it; the user finds the matching entry by searching the log of the instance that failed.
  Through the gateway the id comes from that backend. See [errors and recovery](errors-and-recovery.md).
- **Gateway log:** standard error, which the AI client captures. It records the error events above only for what the
  gateway serves itself, such as its documents; a routed call's error is logged by its backend.
- **Registry:** one JSON record per enabled plugin in the instance directory, withdrawn on disable, holding the access
  token the gateway uses. Never paste or share these files.

## Cheat Engine settings MCP follows

Some tools follow CE's own settings, which the user changes in CE (Edit > Settings or the table options); no tool
changes them, and the switches above do not override them.

- The debugger method selects what `debugger_attach(interface="default")` attaches (see "Capability gates").
- The scan settings (Start and Stop, the protection boxes, Fast Scan, the MEM_PRIVATE, MEM_IMAGE and MEM_MAPPED region
  types) drive the main scanner; `scan_get_status(scannerName="main")` shows them in `scan_get_status.settings`. A
  named scanner sets its own range, protection filter and alignment but still follows the region types. See
  [value scans](value-scans.md).
- The table's Uses Mono option and CE's setting that ignores it decide the Mono gate above.
- CE's setting for a table's Lua script asks the user by default before it runs an unsigned table's Lua (a
  `.CETRAINER`'s Lua always runs), so `table_load` can wait on a dialog. See [cheat tables](cheat-tables.md).

## Responsible use

- Settings are the user's decision. Explain what a change allows and let the user make it; never ask to widen a root
  or turn a gate back on to get around a refusal.
- With every gate on, the tools act with CE's full rights. Use them only on single-player or offline software the user
  owns or may modify, never on online, competitive or anti-cheat-protected games, and get consent before each change.
  See [safety](safety.md) and [errors and recovery](errors-and-recovery.md).

## Sources

- Configuration layering in .NET: https://learn.microsoft.com/dotnet/core/extensions/configuration
- Log levels: https://learn.microsoft.com/dotnet/core/extensions/logging
- Windows path forms: https://learn.microsoft.com/windows/win32/fileio/naming-a-file
- CE's Mono auto attach (`UsesMono`, `IgnoreUsesMono`, `EnableUsesMonoOptionOnAttach`): `autorun/monoscript.lua` in the
  CE 7.7 install, and
  https://github.com/cheat-engine/cheat-engine/blob/master/Cheat%20Engine/bin/autorun/monoscript.lua
- CE's Lua fallback for unknown symbols: `autorun/luasymbols.lua` in the CE 7.7 install.
