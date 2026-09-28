# CheatEngine.Mcp

A Windows x64 Cheat Engine plugin and MCP gateway built
on [CheatEngine.Client](https://github.com/CheatEngineNet/CheatEngine.Client). One MCP connection can control multiple
named Cheat Engine instances.

Each Cheat Engine process loads its own plugin, with its own Client activation and target state. Enabling publishes an
authenticated loopback HTTP backend on an automatically assigned port. The stdio gateway discovers those backends and
routes each tool call using an explicit `instanceId`. Disabling withdraws discovery, closes request admission, and lets
Client release its resources without blocking Cheat Engine's main thread.

[Install and connect](#install-and-connect) · [First use](#first-use) · [Knowledge](#knowledge-prompts-and-live-resources) · [Multiple instances](#multiple-cheat-engine-instances) · [Configuration](#configuration) · [Troubleshooting](#troubleshooting) · [Build from source](#build-from-source) · [Tests](#verification)

## Install and connect

### 1. Get the deployment files

Use the files from a build artifact, or publish them from this repository with .NET SDK **10.0.401** and PowerShell 7
installed:

```powershell
pwsh -NoProfile -File eng/Publish.ps1
# Or build the Debug distribution:
pwsh -NoProfile -File eng/Publish.ps1 -Configuration Debug
```

Release is the default. Copy these from `artifacts/dist/release/` into a stable folder, such as
`C:\Tools\CheatEngine.Mcp`:

| Deployment item                        | How it is used                                                                                                                                                                                                     |
|----------------------------------------|--------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| `CheatEngine.Mcp/` folder              | The plugin. Load its `CheatEngine.Mcp.Plugin.dll` in Cheat Engine. The folder also holds the plugin's dependencies, native Lua bridge, default `appsettings.json`, README, `LICENSE` and `THIRD-PARTY-NOTICES.md`. |
| `CheatEngine.Mcp.Gateway.exe`          | Set this as the MCP server command in your AI client. It runs independently of Cheat Engine's .NET host configuration.                                                                                             |
| `LICENSE` and `THIRD-PARTY-NOTICES.md` | The license and the third-party notices for the gateway, a self-contained executable that includes parts of the .NET runtime. `THIRD-PARTY-NOTICES.md` reproduces every license text it cites.                     |

That is the whole release. The documents and workflows that agents need ship inside the plugin and the gateway as MCP
resources and prompts, so there is nothing else to install; see
[Knowledge, prompts and live resources](#knowledge-prompts-and-live-resources).

Keep the plugin folder intact and `CheatEngine.Mcp.Plugin.dll` unrenamed: CE loads it through its generated entry point,
with the `deps.json`, `runtimeconfig.json` and dependencies beside it. You can also use the files directly from
`artifacts/dist/release/`, but close CE and stop the gateway before rebuilding or replacing files they have loaded.

### 2. Prepare Cheat Engine's .NET host

Use **Cheat Engine 7.7 x64**. The plugin runs inside CE and needs the **x64 .NET 10 runtime, ASP.NET Core 10 runtime,
and Windows Desktop 10 runtime**. The .NET SDK is only needed to build from source. Check installed runtimes with:

```powershell
dotnet --list-runtimes
```

The output must include `Microsoft.NETCore.App 10.0.x`, `Microsoft.AspNetCore.App 10.0.x`, and
`Microsoft.WindowsDesktop.App 10.0.x`. Missing components are available from
the [.NET 10 downloads](https://dotnet.microsoft.com/en-us/download/dotnet/10.0) page; choose Windows x64.

Close all CE processes and back up `ce.runtimeconfig.json` beside the CE executable, commonly under
`C:\Program Files\Cheat Engine`. Update that file to select .NET 10. Preserve unrelated settings; a typical
configuration is:

```json
{
  "runtimeOptions": {
    "tfm": "net10.0",
    "rollForward": "LatestMinor",
    "frameworks": [
      { "name": "Microsoft.NETCore.App", "version": "10.0.0" },
      { "name": "Microsoft.WindowsDesktop.App", "version": "10.0.0" },
      { "name": "Microsoft.AspNetCore.App", "version": "10.0.0" }
    ]
  }
}
```

Editing a file under Program Files may need an administrator editor. A file that still selects `net9.0` and `9.0.0` will
not select .NET 10 merely because .NET 10 is installed. Restart CE after changing this host configuration. The
standalone gateway does not use CE's runtime configuration.

### 3. Enable the DLL in Cheat Engine

1. Start the x64 Cheat Engine executable.
2. Open **Edit → Settings → Plugins**, choose **Add new**, and select `CheatEngine.Mcp.Plugin.dll` in your deployed
   `CheatEngine.Mcp` folder.
3. Tick its checkbox to enable it and accept the settings dialog.

The backend starts automatically on enable. **MCP: Enabled** appears in CE's menu bar once the listener is running and
the instance is published. Click it to see the instance name, listening address, and log location. This reports the CE
backend's state; it does not mean an AI client is currently connected.

Disabling the plugin changes the menu to **MCP: Disabled**. A server startup failure shows **MCP: Start failed**. The
status item remains available until CE closes and is reused on the next enable; its details do not depend on the
disabled plugin. Load or configuration failures before the module starts can prevent the indicator from appearing.

You do not need a fixed port or a configuration file for the default setup. Each CE instance must have the plugin
enabled. Leave CE open while using its tools.

The plugin loads its dependencies from its own folder; nothing is extracted or cached elsewhere. It does not open or
attach to a target automatically.

### 4. Connect your AI client

Run the AI client and CE on the same Windows machine under the same user account. The MCP transport is **stdio**: the AI
client launches `CheatEngine.Mcp.Gateway.exe` and communicates with that process. No MCP URL, port, API key, or OAuth
login is needed for this setup. Double-clicking the EXE does not configure an AI client.

For **Codex**, run this in PowerShell, replacing the example path with your actual deployment path:

```powershell
codex mcp add cheatengine -- "C:\Tools\CheatEngine.Mcp\CheatEngine.Mcp.Gateway.exe"
codex mcp list
```

Alternatively, add this table to `%USERPROFILE%\.codex\config.toml` (or the config under your custom `CODEX_HOME`).
Update an existing `cheatengine` table rather than adding a duplicate:

```toml
[mcp_servers.cheatengine]
command = "C:/Tools/CheatEngine.Mcp/CheatEngine.Mcp.Gateway.exe"
```

Restart your Codex session/app after changing the connection. `codex mcp list` confirms registration; the tool call
in [First use](#first-use) verifies that the gateway can actually discover CE. See
the [official Codex MCP documentation](https://developers.openai.com/codex/mcp) for client configuration options.

For **other clients using an `mcpServers` JSON configuration**, merge this entry into the client's MCP settings:

```json
{
  "mcpServers": {
    "cheatengine": {
      "command": "C:/Tools/CheatEngine.Mcp/CheatEngine.Mcp.Gateway.exe"
    }
  }
}
```

Use your client's equivalent stdio/command settings if it has a different configuration format. Keep one `cheatengine`
connection even when you use several CE processes. A browser-only or remote client needs a Windows-local execution
environment capable of launching this EXE.

## First use

Ask your AI client:

> Use the Cheat Engine MCP to list running instances, then show the selected target and plugin version for each. Do not
> change memory yet.

The agent should call `instance_list`, then use the live schema to inspect the available runtime and process tools with
the returned `instanceId`. A connected gateway can return an empty instance list when no CE backend is enabled.
Discovery
returns the **CE process ID**; the selected-target tool reports the separate **target process ID**. No selected target
is
normal before you attach one.

Select a process in CE's process picker, or tell the AI the exact target process name/PID and the intended CE instance
so it can use the process-attach tool that the live schema exposes. Then give a concrete task, for example:

- "In instance game-a, show the address list and identify which numeric records are frozen."
- "For my test program in game-a, add a Dword record named Health at the address I provide, set it to 100, and freeze
  it."
- "Set game-a's attached test program to half speed, then restore normal speed when we finish."

Use a real, verified address for memory tasks; the agent should not invent an address from an example. The
[cheat tables](srcs/CheatEngine.Mcp.Resources/Knowledge/Documents/cheat-tables.md) and
[speedhack](srcs/CheatEngine.Mcp.Resources/Knowledge/Documents/speedhack.md) documents give the exact tools and cleanup
for these tasks, and the [tool map](srcs/CheatEngine.Mcp.Resources/Knowledge/Documents/tool-map.md) lists every tool
with its purpose and the gate it needs. The agent reads the same documents through MCP.
Unsafe Lua, Auto Assembler, target-code execution, and kernel access are enabled by default, and `runtime_get_info`
reports the state of each switch in `gates`. Set the corresponding `Mcp:Enable*` setting to `false` when a session must
not expose that capability; see [Configuration](#configuration).

## Knowledge, prompts and live resources

The server documents itself to the agent through MCP. Every backend and the gateway serve the same knowledge without an
instance; `resources/list`, `resources/templates/list` and `prompts/list` show the complete set.

- **Documents** are Markdown resources at `cheatengine://docs/{slug}`: the session rules and workflow index
  (`workflows`), the tool map (`tool-map`), safety, configuration, value scans, pointers, the debugger, Auto Assembler,
  cheat tables, game engines, Lua, kernel access, errors and connection troubleshooting.
- **Prompts** are guided workflows such as `attach_and_orient`, `find_known_value`, `find_writer`, `pointer_scan` and
  `cleanup_session`. A prompt renders the steps with the inputs you give it and links the documents it relies on. Its
  body is also a resource, such as `cheatengine://docs/workflows/find-writer`.
- **Live resources** are private, uncached, read-only JSON snapshots of one instance, each equal to the result of a
  read-only tool: its runtime overview, process, modules, memory regions, address-list records, structures and more.
  A backend serves them under `cheatengine://instance/...`. Through the gateway, the resource list shows the fixed ones
  (runtime, process, threads, jobs, scanners, debugger and more) of every instance the gateway has confirmed, through
  `instance_list`, a read of `cheatengine://instances` or a routed call, as `cheatengine://instances/<instanceId>/...`
  titled with the instance name and CE process ID. Once a client has listed resources, the gateway sends it
  `notifications/resources/list_changed` when an instance appears, stops or restarts. The parameterized ones stay
  templates, such as `cheatengine://instances/{instanceId}/modules{?offset,limit}`. `cheatengine://instances` lists the
  instances like `instance_list`, without tokens.
- **Completion**: clients that support argument completion complete the enumerated prompt arguments and, in the live
  templates, `instanceId` (the instances the gateway verified in the last 10 seconds), then module, structure, scanner
  and pointer-scan names read from that instance. A completion never fails: it offers nothing when no process is
  attached, Cheat Engine is busy or the answer is slow.

Clients surface these differently: many offer prompts as slash commands and let you attach resources to the
conversation, and the agent can read any resource itself. To begin, run the `attach_and_orient` prompt, or ask the agent
to read `cheatengine://docs/getting-started`. The Markdown sources are in
[`srcs/CheatEngine.Mcp.Resources/Knowledge`](srcs/CheatEngine.Mcp.Resources/Knowledge), the documents in `Documents/`
and the workflow bodies in `Workflows/`, where you can read them on GitHub.

## Multiple Cheat Engine instances

Open several CE processes and enable the plugin in each. Keep `Mcp:Port` at its default `0` so each backend receives a
free port. One gateway discovers all of them; each tool call identifies its destination with `instanceId`.

Default names contain the CE process ID. For friendly labels, launch each CE from a separate PowerShell window, changing
the path for your installation:

```powershell
# PowerShell window A
$env:MCP_INSTANCE_NAME = 'game-a'
& 'C:\Program Files\Cheat Engine\cheatengine-x86_64.exe'
```

```powershell
# PowerShell window B
$env:MCP_INSTANCE_NAME = 'game-b'
& 'C:\Program Files\Cheat Engine\cheatengine-x86_64.exe'
```

These environment values apply to processes launched from those shells; they do not rename an already running CE. Attach
each instance to its intended target and ask the AI to refresh `instance_list`. A display name can repeat, so route
with the returned ID. Restarting CE or disabling/re-enabling the plugin creates a new ID. Two instances attached to the
same target can still change that shared target's memory.

## Configuration

Defaults work without creating a settings file. For changes, create or edit
`%APPDATA%\CheatEngine.Mcp\appsettings.json`. The plugin folder's own `appsettings.json` holds the shipped defaults and
is
replaced by updates, so keep your changes in the user file. Disable and re-enable the plugin after editing settings. For
instance-specific settings, set an absolute `MCP_DATA_DIRECTORY` in that CE process's launch environment.

Configuration is read once per enable, in this order (later sources win):

1. Built-in defaults.
2. `appsettings.json` in the plugin folder.
3. `%APPDATA%/CheatEngine.Mcp/appsettings.json`, or `appsettings.json` in `MCP_DATA_DIRECTORY`.
4. `MCP_HOST`, `MCP_PORT`, `MCP_INSTANCE_NAME`, and `MCP_INSTANCE_DIRECTORY` environment overrides.

```json
{
  "Mcp": {
    "Host": "127.0.0.1",
    "Port": 0,
    "InstanceName": "game-a",
    "ServerName": "CheatEngine.Mcp",
    "EnableUnsafeLua": true,
    "EnableAutoAssembler": true,
    "EnableTargetCodeExecution": true,
    "EnableKernelAccess": true,
    "Logging": {
      "MinimumLevel": "Information"
    },
    "Files": {
      "AllowedRoots": []
    }
  },
  "CheatEngineClient": {
    "AllowedTableRoots": []
  }
}
```

The four capability gates, `EnableUnsafeLua`, `EnableAutoAssembler`, `EnableTargetCodeExecution` and
`EnableKernelAccess`, default to `true`. Set one to `false` to refuse its tools with `capability_disabled`; the tools
stay listed, and `runtime_get_info` (`gates`) and `runtime_get_overview` (`runtime.gates`) report each switch as a
boolean. The [tool map](srcs/CheatEngine.Mcp.Resources/Knowledge/Documents/tool-map.md) names the gate of each tool.
The gates are exposure switches, not a sandbox: see
[what each gate covers](srcs/CheatEngine.Mcp.Resources/Knowledge/Documents/safety.md#gates-are-switches-not-a-sandbox).
Existing settings files with `false` values override the defaults; remove those overrides or set them to `true`, then
disable and re-enable the plugin.

`Mcp:Files:AllowedRoots` lists the folders that tools may write host files to, such as memory dumps; it is empty by
default, which refuses every write. `CheatEngineClient:AllowedTableRoots` does the same for table load and save. Paths
must be absolute and local, and the discovery and MCP data directories are always refused. `Mcp:Logging:MinimumLevel`
sets the plugin log level. `Mcp:Execution` holds the dispatch and job limits, such as `MaxConcurrentDispatches` (4 by
default) and the job lifetimes (120 seconds by default, at most 300); the
[configuration document](srcs/CheatEngine.Mcp.Resources/Knowledge/Documents/configuration.md) lists every key.

`Port: 0` allocates an available port for each plugin. The host must be `127.0.0.1`. A fixed nonzero port is optional
but must be unique across running instances. `InstanceName` is a display label; by default it contains the CE process
ID. For different labels with one shared plugin folder, set `MCP_INSTANCE_NAME` separately in each CE process's launch
environment, or use separate `MCP_DATA_DIRECTORY` settings directories. Restart the plugin to apply settings.

Invalid settings fail the enable before any backend starts: a non-loopback host, a port outside 0-65535, an empty server
or instance name, an instance name over 128 characters, or a relative instance directory. A non-numeric `MCP_PORT` is
rejected rather than replaced by another port. The backend ignores ambient ASP.NET Core settings in CE's environment,
such as `ASPNETCORE_URLS`, `ASPNETCORE_ENVIRONMENT` or `Kestrel__Endpoints__*`; only `Mcp` settings choose its address.

Discovery records live in `%LOCALAPPDATA%/CheatEngine.Mcp/instances`. They contain per-activation access tokens used by
the gateway; do not share those files. An absolute `MCP_INSTANCE_DIRECTORY` override must agree between plugins and
gateway; the gateway also accepts `--instance-directory <path>`. Both run as the same Windows user. Authentication
isolates backend calls from unauthenticated HTTP clients; it is not a boundary against other applications running as
that user.

Call `instance_list`, choose the intended name/CE PID, and pass its exact `instanceId` to every CE tool. Labels may
repeat; IDs are unambiguous and change on every plugin activation. There is no shared selected-instance state, automatic
fallback, or automatic retry. A stopped instance fails its calls without redirecting them to another CE. Independent
instances have independent CE state, but attaching both to the same target still allows both to change that target.

Each CE process logs to `%APPDATA%/CheatEngine.Mcp/CheatEngine.Mcp.<pid>.log` so instances do not compete for one file.
Client lifecycle and HTTP logging share that file through the plugin's own background writer, which keeps it open until
a backend that is still shutting down has finished. Transport and protocol categories never log below `Information`,
whatever `Mcp:Logging:MinimumLevel` says.

`MCP_DATA_DIRECTORY` can select an absolute directory for user settings and logs. The automated live runner uses private
data and discovery directories inside its test run.

## Troubleshooting

| Symptom                                           | What to check                                                                                                                                                                                                                            |
|---------------------------------------------------|------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| CE refuses to load the plugin                     | Use CE x64, load `CheatEngine.Mcp.Plugin.dll` from the intact plugin folder, check all three x64 .NET 10 runtimes, and check CE's own `ce.runtimeconfig.json`. Restart CE after changing it.                                             |
| The gateway EXE waits in a console                | It is a stdio MCP server. Configure it as the AI client's command; the client starts it and sends protocol requests.                                                                                                                     |
| `instance_list` returns an empty list             | Enable the plugin in an open CE process. Run CE and the gateway as the same Windows user and check that any `MCP_INSTANCE_DIRECTORY` overrides agree.                                                                                    |
| One CE works but another cannot start its backend | Leave `Port` at `0`, or give each instance a unique fixed port. Check that the plugin is enabled in both.                                                                                                                                |
| An instance becomes unavailable                   | Call `instance_list` again. Restart or re-enable creates a new ID; do not reuse old instance or resource IDs.                                                                                                                            |
| A tool reports no target                          | Select the intended target in CE or use the process-attach tool shown by the live schema with its exact PID/name and the chosen CE instance ID.                                                                                          |
| Lua execution or Auto Assembler is refused        | Both are enabled by default, and `runtime_get_info` shows each switch in `gates`. Check plugin-adjacent and user settings for explicit `false` overrides, then reload the plugin after changing them.                                    |
| A table file is refused                           | Check `CheatEngineClient:AllowedTableRoots` and use an absolute table path.                                                                                                                                                              |
| Enabling fails with an options error              | Correct the named `Mcp` setting or `MCP_*` variable (a non-integer `MCP_PORT` fails with a format error that does not name it), then enable the plugin again.                                                                            |
| A call or resource read fails with `internal`     | Quote its `errorId` (`error.details.errorId` of a tool result, `error.data.errorId` of a resource read) and search that CE instance's plugin log for it; the entry names the tool or resource and the exception type, never its message. |

Plugin logs are `%APPDATA%\CheatEngine.Mcp\CheatEngine.Mcp.<CE PID>.log`, or beneath `MCP_DATA_DIRECTORY` when set. A
load failure before Client starts may occur before that log exists. When an enable fails, CheatEngine.SDK writes the
reason to the Windows debug output, which a viewer such as Sysinternals DebugView shows; the plugin log may not contain
it. Check gateway startup errors in the AI client's MCP diagnostics. Discovery records contain authentication tokens;
do not paste their contents into reports. More diagnostic detail is in the
[connection troubleshooting document](srcs/CheatEngine.Mcp.Resources/Knowledge/Documents/connection-troubleshooting.md),
which a connected agent can also read as `cheatengine://docs/connection-troubleshooting`.

### Updating or removing the installation

To update, finish target cleanup, close CE, stop the MCP connection in your AI client, and replace the whole plugin
folder
and the gateway EXE together, from the same build; do not mix files from two builds in the plugin folder. Restart CE,
enable the plugin, reconnect the client, and refresh `instance_list`. Settings in the user file survive updates.

To remove it, undo task-owned freezes, speed changes, and debugger state first. Disable/remove the plugin in CE, close
CE, and remove the MCP server configuration from your AI client. You can then delete the plugin folder and the gateway
EXE. Settings and logs remain in the documented user directory unless you choose to remove them. Plugin disable alone
does not undo every CE-owned change.

## Tools and ownership

The [tool map](srcs/CheatEngine.Mcp.Resources/Knowledge/Documents/tool-map.md) (`cheatengine://docs/tool-map`) lists
every tool of the v2 contract by domain, with its purpose and gate.
The live MCP schema is authoritative for the effective tool names, parameter types, defaults, results, and annotations
of this build.
`instance_list` is gateway-local; every listed Cheat Engine tool requires `instanceId`.
The [cheat tables](srcs/CheatEngine.Mcp.Resources/Knowledge/Documents/cheat-tables.md#activate-freeze-and-deactivate)
and [speedhack](srcs/CheatEngine.Mcp.Resources/Knowledge/Documents/speedhack.md#restore-normal-speed) documents cover
adding records, freezing, unfreezing, and restoring speed.

The [value scans](srcs/CheatEngine.Mcp.Resources/Knowledge/Documents/value-scans.md),
[pointers](srcs/CheatEngine.Mcp.Resources/Knowledge/Documents/pointers.md) and
[debugger](srcs/CheatEngine.Mcp.Resources/Knowledge/Documents/debugger.md) documents cover
unknown-initial/changed/increased/range value scans, pointer maps and rescans, write/access collectors, break-and-trace,
and register editing. Value scans use the same tools for both modes: omit `scannerName` (or use `main`) for the visible
CE scan tab, or choose another name for an independent Client session. Main scans update CE's native controls and result
list; use `scan_get_status` to wait for completion and `scan_list_results` to read either MCP-started or
manually started UI scans. `scan_list_scanners` lists main and the independent sessions. The UI adapter uses fixed Lua
through Client because Client 1.0 has no UI-scan API. Independent scans and pointer memory access use typed Client APIs;
debugger callbacks use fixed CE Lua operations. Pointer maps are bounded in-memory MCP snapshots, not CE's native
pointer-map files. Debugger captures and traces are jobs with a finite lifetime (120 seconds by default, at most 300):
`runtime_stop_job` ends one early, and a finished job stays pollable until its lifetime ends.

- Start with `instance_list`, then inspect the live runtime and process tools on the chosen instance.
- Process selection, typed memory, pointers, AOB scans, inspection, and address-table tools use Client contracts.
- Independent value scans, allocations, assembly, and Auto Assembler retain Client experimental/capability checks. An
  API's presence does not establish that the current host supports it.
- Arbitrary `lua_execute` and Client Auto Assembler patches require their respective configuration flags. These flags
  are not a sandbox: dedicated tools can write memory, launch/inject code, control the debugger, access files, and alter
  host state. Table load/save uses configured allowed roots.
- Independent named scans, allocations, registered symbols, and patches belong to one enable epoch, and so do the
  breakpoints, the Mono collector attachment, a pause and a speedhack speed other than 1 that MCP set: each is a
  tracked resource that `runtime_list_resources` lists. The `main` scanner belongs to CE: it survives plugin disable,
  follows the visible CE scan tab, and is never destroyed by MCP. `scan_reset` on main explicitly clears its visible
  results through CE's New Scan action. Wait for an active UI scan, or stop it with `scan_stop`, before switching
  targets. Before switching processes, use `runtime_release_resources`; it releases in reverse creation order and stops
  on incomplete cleanup. A repeated request for the selected PID preserves resources.
- Failed releases report recovery details and keep retryable handles. If Cheat Engine changes targets outside MCP,
  inspect cleanup outcomes and perform manual recovery when reported. Never reuse identifiers after disable/re-enable.

The Lua adapter encodes arguments as data, runs a protected call inside the Client activation/main-thread boundary, and
copies bounded results into the tool's structured result. A Lua failure includes `hostEffect`: an operation that started
may have partially changed the
host. It is unsafe to blindly retry a failed mutation. Requests are limited to 8 MiB; copied Lua results are limited to
65,536 items, depth 16, and 4 MiB of strings. Individual tools apply tighter limits.

Client leases are activation-owned. Lua-created global structures, address-list changes, comments, breakpoints, and
debugger/speedhack state are Cheat Engine-owned state. They can persist after plugin disable; use each explicit
delete/remove/resume tool or Cheat Engine itself to recover. Injected libraries have host/target lifetimes. A DBVM
watch is a job polled with `kernel_poll_watch`: `runtime_stop_job`, the end of its lifetime and plugin shutdown run its
cleanup, which disables the watch. Inspection bounds limit returned data; some CE APIs internally enumerate a larger
collection first. Long native calls run synchronously and cannot be interrupted by an HTTP cancellation.

## Migration from CeMCP 1.x

This is a breaking remake. The old CESDK submodule, source compilation, Costura single-DLL packaging, static tools, WPF
UI, and old project/test paths have been removed. The plugin is deployed as the CheatEngine.Client staged plugin folder,
not as a single packaged DLL.

The tool surface is rebuilt around Client high-level APIs for supported operations. Additional debugger, DBVM,
injection, process-control, Structure Dissect, RTTI, protection, file-memory, and code-analysis tools use fixed Lua
operations through `ICheatEngineClient.Lua`. Bindings follow the installed Cheat Engine `celua.txt`; unavailable host
APIs return errors. The complete public surface is in the
[tool map](srcs/CheatEngine.Mcp.Resources/Knowledge/Documents/tool-map.md), and each tool's description is in the live
schema.

Tool parameters and responses have changed where Client ownership requires it: allocations have names, scans are bounded
sessions, patches return lease IDs, record content and activation are separate tools, and `lua_execute` returns bounded
execution results.

## Build from source

Install .NET SDK **10.0.401** and PowerShell 7, then run from this repository:

```powershell
dotnet restore CheatEngine.Mcp.slnx --locked-mode
dotnet build CheatEngine.Mcp.slnx --no-restore
dotnet test --solution CheatEngine.Mcp.slnx --no-restore --no-build --filter-not-trait Category=LiveQualification --filter-not-trait Category=NativeLua --fail-skips on
pwsh -NoProfile -File eng/Publish.ps1 -Configuration Release
```

The distributable files are under `artifacts/dist/release/`.
A normal `dotnet build` prepares development outputs.
`eng/Publish.ps1` stages the plugin folder through the Client's plugin deployment, publishes the self-contained Windows
x64 Native AOT gateway, runs its MCP smoke check, copies the plugin's `README.md` into the plugin folder, and copies
`LICENSE` and `THIRD-PARTY-NOTICES.md` into the plugin folder and beside the gateway. It refuses any other file in
`artifacts/dist/<configuration>/` and removes the `skills/` and `licenses/` folders of earlier layouts.
Publishing uses the checked-in NuGet lock files.
CI publishes the same layout for Debug and Release.

The plugin references **[CheatEngine.Client 1.0.0](https://www.nuget.org/packages/CheatEngine.Client/1.0.0)** through
NuGet. Its package metadata identifies source revision `f88de3d843252c9139f08c71531a02f03c0516bb`. No submodule checkout
is required. The plugin also directly references **CheatEngine.SDK 2.0.0**, as required by Client, for its generated
entry point and native bridge.

Dependency versions are centralized in `Directory.Packages.props` and resolved in the checked-in NuGet lock files. Test
dependencies follow the Client's xUnit v3/Microsoft.Testing.Platform profile. Existing remote Sonar project identifiers
are retained; renaming a local project does not rename the hosted service.

Code follows Client conventions: C# 14, file-scoped namespaces, tabs, explicit types, centralized package versions,
locked restore, and output under `artifacts/`. The plugin is a framework-dependent managed component that CE loads
through the SDK-generated entry point; Client owns activation and cleanup. This is not a Native AOT binary.

### Project layout

| Project                                                            | Role                                                                                                                                                                                                                                                                        |
|--------------------------------------------------------------------|-----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| [`libs/CheatEngine.Mcp.Core`](libs/CheatEngine.Mcp.Core)           | Shared library on CheatEngine.Client: composition builders, tool execution, target leases and the Lua adapter.                                                                                                                                                              |
| [`srcs/CheatEngine.Mcp.Tools`](srcs/CheatEngine.Mcp.Tools)         | The Cheat Engine tools, registered by `AddTools()`.                                                                                                                                                                                                                         |
| [`srcs/CheatEngine.Mcp.Resources`](srcs/CheatEngine.Mcp.Resources) | The knowledge base and the MCP resources, registered by `AddResources()`: the `Knowledge/Documents` and `Knowledge/Workflows` Markdown served as `cheatengine://docs/...` documents and workflow bodies, and the live `cheatengine://instance/...` resources and templates. |
| [`srcs/CheatEngine.Mcp.Prompts`](srcs/CheatEngine.Mcp.Prompts)     | The static workflow prompts, one per workflow body of the knowledge base, and the server instructions, registered by `AddPrompts()`.                                                                                                                                        |
| [`srcs/CheatEngine.Mcp.Hosting`](srcs/CheatEngine.Mcp.Hosting)     | The loopback backend host, its options, instance discovery and the stdio gateway routing.                                                                                                                                                                                   |
| [`srcs/CheatEngine.Mcp.Plugin`](srcs/CheatEngine.Mcp.Plugin)       | The Cheat Engine plugin: composition root, lifecycle module, status menu and logging.                                                                                                                                                                                       |
| [`srcs/CheatEngine.Mcp.Gateway`](srcs/CheatEngine.Mcp.Gateway)     | The gateway executable's composition root.                                                                                                                                                                                                                                  |

The plugin and the gateway compose the same primitives with the same builder calls, so the gateway's catalog always
matches what a backend serves. Architecture tests enforce the project graph.

## Verification

Tests follow the Client and SDK setup: xUnit v3 on Microsoft.Testing.Platform, with the shared profile in
`eng/Tests.props`. The portable suite uses deterministic Client doubles plus real loopback HTTP discovery/start/stop
tests. It does not require Cheat Engine and does not prove native host behavior. CI excludes
`Category=LiveQualification` and `Category=NativeLua`; selected tests fail rather than skip when required inputs are
missing. Reviewed snapshots in `tests/CheatEngine.Mcp.Tests/Contract/Golden` pin the published tools, resources, prompts
and `initialize` results; regenerate them only for an intended contract change, with `CHEATENGINE_MCP_UPDATE_GOLDEN=1`,
as [Golden files](CONTRIBUTING.md#golden-files) describes.

For an automated live run, close other Cheat Engine instances and run:

```powershell
$env:CHEATENGINE_MCP_LIVE_QUALIFICATION = 'I_AUTHORIZE_CE77_LIVE_PROBES_ON_A_DISPOSABLE_TARGET'
pwsh -NoProfile -File eng/Publish.ps1 -Configuration Release
dotnet test --project tests/CheatEngine.Mcp.Tests -c Release --filter-trait Category=LiveQualification --fail-skips on
Remove-Item Env:CHEATENGINE_MCP_LIVE_QUALIFICATION
```

`LiveQualification/` contains the C# runner and serial fixture. Like the Client, it requires the exact acknowledgement,
refuses `CI=true`, and fails immediately with the command above when explicitly selected without authorization. No
PowerShell wrapper, plugin installation, or manual enabling is required. Publish the matching configuration before the
live test (`Debug` for a Debug test run); the regular solution build and the publish step are separate.

The runner creates two private copies of installed Windows x64 Cheat Engine 7.7+ under
`%LOCALAPPDATA%/CheatEngine.Mcp.LiveQualification/runs/<run>`, selects .NET 10 only in those copies, and loads the
plugin automatically. Each gets its own disposable memory target, label, and automatic loopback port; one stdio gateway
routes calls through a private discovery directory. Existing CE processes are refused, never closed. The installed host
is not modified. Optional `CHEATENGINE_MCP_LIVE_QUALIFICATION_CE_DIRECTORY` selects another installation;
`CHEATENGINE_MCP_LIVE_QUALIFICATION_RUN_ROOT` selects another absolute run directory outside the repository and apart
from the installation.

The private copy disables existing autorun scripts except the stock `celib.lua`, `monoscript.lua`, and `SpeedhackV3.lua`
needed for speedhack. These must match the hashes reviewed in `LiveSandboxSession.cs`; missing or changed scripts fail
before launching CE. Review newer stock scripts before updating those hashes.

Local test helpers adapted from Client's MIT-licensed test suite preserve CE settings, restore and verify them after
shutdown, and retain a recovery marker if restoration fails. Their provenance is recorded
in [the infrastructure notice](tests/CheatEngine.Mcp.Tests/LiveQualification/Infrastructure/NOTICE.md); they do not
require a Client source checkout. A later run restores a leftover backup and stops so the recovery is visible. Plugin
settings and logs stay in the run folder. Reports contain the exact host version/hash, plugin hash, individual checks,
and cleanup results. Local reports and backups stay outside the checkout.

The live scenario checks all tool names, loaded plugin file names, runtime evidence, target attachment, typed memory
reads/writes, the isolation of the main and named scanners, disassembly with a retained allocation, address-list
add/update/delete, actual freeze/unfreeze behavior, and speedhack setting/readback. It
verifies that changing A leaves B's separate target and table unchanged, then stops B and verifies A remains available
while B calls fail. Speedhack readback does not measure timing accuracy. This smoke test is separate from the Client's
qualification against its pinned host build; it does not qualify every MCP tool, debugger backend, or DBVM. Ordinary CI
excludes live tests.

Check formatting with:

```powershell
dotnet format style CheatEngine.Mcp.slnx --no-restore --severity warn --verify-no-changes
dotnet format whitespace CheatEngine.Mcp.slnx --no-restore --verify-no-changes
```

Optional standalone Lua tests (`Category=NativeLua`) use a Lua 5.3 DLL without loading Cheat Engine:

```powershell
$env:CHEATENGINE_MCP_LUA53_PATH = "C:\Program Files\Cheat Engine\lua53-64.dll"
dotnet test --solution CheatEngine.Mcp.slnx --filter-trait Category=NativeLua --fail-skips on
```

They run the protected adapter and the tools' fixed Lua scripts against stubs of the Cheat Engine functions they call,
so they check the scripts and the copied results, not Cheat Engine itself. Deployable build artifacts are the
plugin folder, the gateway EXE, `LICENSE` and `THIRD-PARTY-NOTICES.md` under `artifacts/dist/<configuration>/`.

## Attribution

This remake builds on the
original [ce-mcp contributors](https://github.com/ShadowNineX/ce-mcp/graphs/contributors), [CheatEngine.Client](https://github.com/CheatEngineNet/CheatEngine.Client), [CheatEngine.SDK](https://github.com/CheatEngineNet/CheatEngine.SDK),
and Cheat Engine. [`LICENSE`](LICENSE) and [`THIRD-PARTY-NOTICES.md`](THIRD-PARTY-NOTICES.md) remain authoritative.
