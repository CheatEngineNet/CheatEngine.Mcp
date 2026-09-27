# Security model and responsible use

CheatEngine.Mcp gives an AI client the power of Cheat Engine: reading and writing the memory of any process the user can open, running code inside it, and loading a kernel driver and a hypervisor.
This page states what the project protects, what it deliberately does not, and how to use it responsibly.
For the moving parts, see [Architecture](architecture.md); for the settings named here, see [Configuration](configuration.md).

> **Status.** This page describes the 2.0.0 (v2) security model.
> Anything marked **(v2, in progress)** is an adopted design that the code does not implement yet, and the page says what applies today.
> The tool catalog is still the pre-2.0.0 one, so today's tool names, such as `execute_lua` and `list_instances`, appear where they matter.

## Threat model

### What is at stake

- The memory, code and behavior of the processes Cheat Engine (CE) attaches to.
- CE's own state: its address list, structures, breakpoints, patches and settings.
- The machine itself: kernel and hypervisor tools can crash or corrupt Windows.
- Files that tools read or write: cheat tables, memory dumps and saved executables.
- The per-activation bearer tokens that authenticate the gateway to each backend.
- Secrets that happen to be in process memory, which a read can surface into the AI conversation and, with it, into the AI provider's systems.

### Trust boundary

The trust boundary is the Windows user account.
The plugin, the gateway and the AI client all run as the same user, and anything running as that user is on the trusted side.

| Actor | Can it drive a backend? | Why |
|---|---|---|
| The AI client and the gateway it launched | Yes | It is the intended caller and acts with the user's authority. |
| Another process running as the same user | Yes, if it chooses to | It can read the discovery records in the user's profile, and it could equally open the target or CE directly. Authentication is not a boundary against it. |
| A web page in the user's browser | No | It can send requests to `127.0.0.1` but cannot read the token, and every request without the exact bearer token gets HTTP 401. |
| Another non-administrator user on the same machine | No | The port is reachable, but the token lives in a profile folder that user cannot read. |
| Another machine | No | Backends listen only on `127.0.0.1`. |
| Administrators, SYSTEM, or malware already running as the user | Out of scope | They already control the machine or the account. |

The AI model is a special case.
It acts with the user's authority, but it reads untrusted content through tools: strings in process memory, table descriptions, file and module names, and documentation.
Treat that content as data, and keep your AI client's approval prompts for tools that change state; see [Risk classes](#risk-classes).

### Transport controls

| Control | Implementation |
|---|---|
| Loopback only | `Mcp:Host` must be the literal `127.0.0.1`: it is validated when the backend factory is constructed, which fails the enable, and again just before binding. The gateway ignores any record whose endpoint is not `http://127.0.0.1:<port>/`. |
| Automatic port | `Mcp:Port` defaults to 0, so each backend receives a free port. |
| No ambient configuration | The backend is built from an empty ASP.NET Core builder in `Production`: `ASPNETCORE_URLS`, `Kestrel__Endpoints__*`, other `ASPNETCORE_*` and `DOTNET_*` variables, ambient `appsettings.json` files and hosting startup assemblies cannot add an endpoint or change the environment; `app.Urls.Add` is the only address source. |
| Per-activation token | 64 hexadecimal characters (256 bits) from `RandomNumberGenerator.GetHexString(64)`, generated on every enable and never reused. |
| Every request authenticated | `Authorization: Bearer <token>` is compared in constant time with `CryptographicOperations.FixedTimeEquals`, including the `/instance` identity endpoint; anything else gets HTTP 401. |
| Admission closes first | Once the activation's stopping token is set, every request gets HTTP 503, before discovery is withdrawn and resources are released. |
| Bounded requests | Request bodies are limited to 8 MiB. |
| Identity verification | Before listing an instance and before every routed call, the gateway checks the backend's instance ID, activation, CE process ID, process start time and plugin version against the record. |
| No redirects or proxies | Gateway connections disable automatic redirects and proxies, so a token only ever reaches the recorded loopback endpoint. |
| Tokens stay out of results | `list_instances` returns only the instance ID, display name, CE process ID and plugin version; routed errors contain no token or endpoint. |
| Tokens stay out of logs | The plugin log keeps the `Microsoft.AspNetCore`, `System.Net.Http` and `ModelContextProtocol` categories at `Information` or above whatever `Mcp:Logging:MinimumLevel` says. The gateway applies the same floor to its own log. `GatewayTokenLeakTests` runs an authenticated backend, the gateway and a client at `Trace` through list, call, forged-token and stopped-instance paths and asserts that no token appears in any log line, result or error; an instance record's `ToString()` redacts its token. |

### The discovery registry

- Each activation writes `%LOCALAPPDATA%\CheatEngine.Mcp\instances\<activation ID>.json`, which contains its endpoint and bearer token.
- The plugin writes the record atomically once its listener runs and deletes it when disabling starts.
- A record left behind by a crash is ignored, because the gateway requires the recorded CE process to be alive with the same start time, and its token is useless once its backend is gone.
- The code sets no explicit access control list: the record inherits the permissions of the user's `%LOCALAPPDATA%`, which by default grant access to the user, SYSTEM and Administrators only.
- If you override the directory with `Mcp:InstanceDirectory`, `MCP_INSTANCE_DIRECTORY` or the gateway's `--instance-directory`, keep it just as private: never a shared, synced or world-readable folder.
- Never paste a record, or any part of a token, into an issue, a chat or a log.

### What authentication does not protect against

- Any program running as the same Windows user: it can read the records and call the backends, or simply use CE or the target directly.
- The AI client itself: once registered, it can call every exposed tool, so the gates and the client's approval prompts are the operator's controls.
- A malicious cheat table, Auto Assembler script or Lua script that you run: it executes with CE's rights; see [Cheat tables run code](#cheat-tables-run-code).
- Two CE instances attached to the same target: each can change that target, and their writes can collide.

## Capability gates

Four `Mcp` settings switch groups of capabilities on or off.
All four are **enabled by default**.

| Setting | Default | Covers in v2 | Today |
|---|---|---|---|
| `Mcp:EnableUnsafeLua` | `true` | `lua_execute`; Lua inside Auto Assembler (`{$lua}`, `{$luacode}`, `luacall`); tables that carry Lua; the `loadTable` Lua API. | Only `execute_lua`, through the Client's unsafe Lua capability. The other fixed-script tools ignore it. |
| `Mcp:EnableAutoAssembler` | `true` | `asm_check`, `asm_apply`, `asm_apply_code_patch` and activating script records. | Auto Assembler checks and patches (`auto_assemble`, `auto_assemble_check`), through the Client's Auto Assembler capability. |
| `Mcp:EnableTargetCodeExecution` | `true` | `exec_*`, `process_create`, `speedhack_set_speed`, `mono_attach`, `mono_compile_method`, `mono_invoke_method`, the VEH debugger, C code, `loadlibrary` or `createthread` in Auto Assembler, and any attach or table load that would start CE's Mono collector. | Not implemented (v2, in progress): the corresponding tools, such as `inject_library` or `execute_remote_code`, cannot be switched off. |
| `Mcp:EnableKernelAccess` | `true` | `kernel_*` and the kernel debugger interface. | Not implemented (v2, in progress): the `dbk_*` and `dbvm_*` tools cannot be switched off. |

- Set a gate to `false` in the user `appsettings.json`; settings are read once per enable, so disable and re-enable the plugin afterwards.
- An existing settings file with `false` keeps its value; the default only applies when the key is absent.
- A refused call fails with the `capability_disabled` error kind, `hostEffect: not_started`, `retryable: false` and a `hint` naming the setting; the error type is in Core, while today's tools still report a refusal in-band.
- The catalog is static: a disabled tool is still listed, and only its calls are refused.

In v2 the gates are enforced in layers (v2, in progress):

- `[RequiresFeature]` on a tool method is copied into the tool's `_meta`, so the gateway's catalog shows which gate a tool needs.
- The `EnforceFeatures` call filter refuses a gated tool before argument binding and before any dispatch.
- `McpFeatureGate.Require` handles gates that depend on a parameter: for example, attaching the kernel debugger needs kernel access, and attaching the VEH debugger, which injects a DLL, needs target code execution.
- The dispatch's Lua checkpoint scans every fixed script for sensitive CE APIs and requires the matching gate, as a defense in depth.
- Process attach, process creation, opening a file as a process and table loading first check CE's "Uses Mono" option: when CE would attach its Mono collector, target code execution is required and the host effect `mono_auto_attach` is reported.
- Auto Assembler scripts additionally need unsafe Lua for `{$lua}`, `{$luacode}` and `luacall(`, and target code execution for `{$c}`, `{$ccode}`, `loadlibrary`, `createthread` or any command a Lua script registered.
- One options instance feeds both the Client opt-ins and the gates, and tests assert that a disabled gate causes zero mutating Client calls.

### Gates are not a sandbox

- Gates are exposure switches for an AI client, not a security boundary.
- With every gate off, dedicated tools still read and write memory, change page protection, allocate and free memory, control the debugger, pause processes and write files.
- With gates on, Lua and code injection can do anything the CE process can do: read and write files, start processes, load libraries and reach the network.
- Cheat Engine commonly runs with administrator rights, and everything the plugin does then runs with them too.
- Turning a gate off narrows what an AI client can reach through MCP; it does not make the machine safe from a malicious client, table or script.

## File policies

| Operation | Today | v2 |
|---|---|---|
| Load or save a cheat table | The path must be absolute and under a root in `CheatEngineClient:AllowedTableRoots`, a CheatEngine.Client policy. The list is empty by default, which denies all table access, and relative, blank or duplicate roots fail the enable. | The same roots, plus the table inspector described below (v2, in progress). |
| Write a file, such as a memory dump or the saved image of a file opened as a process | No root policy: the tool writes any path the CE process can write. | Only under `Mcp:Files:AllowedRoots`, which is empty by default, so writes are refused until a root is configured (v2, in progress). |
| Read a file, such as a file opened as a process, a symbol module or a module file for patch detection | CE opens the path as given. | The same path rules as writes, without a root requirement (v2, in progress). |

The v2 path rules (`McpFilePaths.RequireAllowed`, v2, in progress):

- The path is absolute, local and on a fixed drive.
- UNC paths (`\\server\share`), `\\?\` and `\\.\` device paths, device names and alternate data streams (`file:stream`) are refused.
- Every ancestor is resolved, and a reparse point such as a symbolic link or junction is refused.
- Roots are compared with a trailing separator and case-insensitively, so a root `C:\Tables` never admits `C:\TablesOld`.
- The instance registry directory and the MCP data directory are always refused, even inside a root: the first holds bearer tokens, and the second holds the settings, including the gates, and the logs, so a write there could re-enable a gate for the next activation.

Never configure a root that covers a system folder, a whole profile, or a shared or synced folder.

## Cheat tables run code

A cheat table is not just data.

- A `.CT` file can carry a Lua script that CE runs when the table loads; depending on CE's own settings, CE may ask first or not.
- Script records run Auto Assembler code in the target when activated.
- A table can set the "Uses Mono" option, which makes CE attach its Mono data collector, a DLL injected into the target.
- A `.CETRAINER` file is a packaged trainer meant to run.

Loading a table therefore means running code with CE's rights.
Load only tables you trust, from roots you control.
Loading can also raise a CE dialog that a person must answer.

Today, table loading is limited by the allowed roots and by CE's own prompts.
The v2 table inspector adds these checks (v2, in progress):

- It reads the file once and loads the inspected bytes, so the file cannot change between inspection and loading.
- `.CETRAINER` files and compressed or unreadable tables are refused unless both unsafe Lua and target code execution are enabled.
- A table script requires unsafe Lua, the "Uses Mono" option requires target code execution, and script records require Auto Assembler.

## Risk classes

Each v2 tool sets all four MCP annotation hints, and the [tool reference](reference/tools.md) derives a risk class from them (v2, in progress).
Annotations are hints that let an AI client decide what to confirm; they are not controls.
Today's tools publish no annotations, so clients treat every one of them as potentially destructive and open-world.

| Class | Derived from | Meaning | Typical verbs | Advice |
|---|---|---|---|---|
| Read-only | `readOnlyHint: true` | Changes neither the target nor CE. It can still expose secrets held in memory. | list, get, read, find, resolve, disassemble, poll | Reasonable to auto-approve for a target you are entitled to inspect. |
| Mutating | `readOnlyHint: false`, `destructiveHint: false` | Adds state, such as a record, a scan, an allocation, a patch or a job, that a matching delete, release or stop undoes. | create, start, allocate, apply | Confirm, and clean up at the end of the session. |
| Destructive | `destructiveHint: true` | Overwrites or removes state. | write, set, update, delete, release, stop | Confirm, and read the previous value first. |
| Host-affecting | `openWorldHint: true` | Reaches beyond the attached target and CE: the host filesystem or other processes, such as process creation, injection, table load and save, and file dumps. | create, inject, load, save, dump | Review every path and argument. |
| Kernel | The `Mcp:EnableKernelAccess` gate | Uses CE's kernel driver or hypervisor and can crash or corrupt the whole machine. | Any kernel tool | Only on explicit request, on a machine that can afford to crash. |

- Classes combine: a destructive tool can also be host-affecting.
- An annotation describes the intended effect, not every side effect: for example, an Auto Assembler check can run the script's `{$lua}` blocks.
- Every poll is read-only and idempotent, and every stop or release is idempotent; the startup validator enforces both.

## Kernel, hypervisor and debugger hazards

- CE documents that its kernel driver (`dbk64.sys`) loads on 64-bit Windows only when Windows is booted with unsigned-driver support, which weakens the whole system; loading it is the user's decision in CE.
- CE's hypervisor, DBVM, needs hardware virtualization with Hyper-V disabled; CE's wiki warns that each processor core it takes over carries roughly a one-in-five chance of a `CLOCK_WATCHDOG_TIMEOUT` blue screen, and CE asks for confirmation before loading it.
- Physical memory writes bypass every protection; shared pages change every process that maps them, and kernel data changes can trigger a Kernel Patch Protection bugcheck or silent corruption.
- A blue-screen hazard is reported when CE's "Query memory region routines" setting is combined with DBVM; MCP never changes CE settings.
- After the kernel debugger interface has been used, CE's kernel module unloader or a reboot is needed before another debugger interface works.
- The VEH debugger injects CE's DLL into the target, and a target that has used it must be restarted before any new attach; MCP refuses an unsafe reattachment.
- Kernel, hypervisor and code-execution tools are never exercised by the live qualification, so treat them as untested; see [Compatibility](compatibility.md#qualification-levels).
- Until `Mcp:EnableKernelAccess` lands, today's kernel and DBVM tools cannot be switched off by configuration.

The operator skill's [kernel guide](../skills/cheatengine-mcp/references/kernel.md) gives the AI client the detailed rules.

## Responsible use

- Use CheatEngine.Mcp only on software you own or are authorized to modify: your own programs, single-player or offline games, the CE tutorials and disposable test targets.
- Respect each program's end-user license agreement and terms of service; Cheat Engine's publisher offers it for private and educational use.
- Do not use it to cheat in multiplayer or online modes, to gain an advantage over other players, to tamper with online economies or leaderboards, to bypass anti-cheat, DRM or license checks, or to harvest other people's data; the project does not support such use, and its operator skill refuses it.
- Anti-cheat systems detect CE, its driver, its debuggers, the speedhack and injected code, and bans can be permanent; close CE before starting a protected game and never attach to one "just to look".
- Do not attach to unrelated processes such as browsers, password managers or chat clients: their memory holds credentials, and anything a tool reads goes to the AI client and its model provider.
- Save your work, and the game's, before patches, injections or kernel tools; a mistake can crash the target or the machine.
- Undo what a session created, newest first: disabling the plugin is not cleanup, because CE-owned state such as freezes, breakpoints, speed and patches survives it.
- The operator skill's [safety guide](../skills/cheatengine-mcp/references/safety.md) turns these rules into instructions for the AI client.

## Logs and bug reports

- Each CE process logs to `%APPDATA%\CheatEngine.Mcp\CheatEngine.Mcp.<CE process ID>.log`, or under `MCP_DATA_DIRECTORY` when it is set; the gateway logs to standard error, which the AI client usually keeps in its MCP log.
- At `Trace`, the plugin log can contain tool arguments and results, including addresses and memory contents; share only the lines you need.
- Never include tokens, discovery records or complete settings files in a report, and redact user names and paths.

## Reporting a vulnerability

Report vulnerabilities privately as described in [SECURITY.md](../SECURITY.md), not in a public issue.
Examples include a way around a capability gate or a file policy, a token that reaches a result or a log, a backend that listens beyond `127.0.0.1`, a bypass of the gateway's identity check, and caller input that becomes Lua source.
Vulnerabilities in Cheat Engine itself, CheatEngine.Client or CheatEngine.SDK belong to those projects.
