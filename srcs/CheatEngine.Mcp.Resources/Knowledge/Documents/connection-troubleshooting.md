# Connection setup and troubleshooting

How the Cheat Engine (CE) plugin, the gateway and the AI client connect, how to set them up, and what to check when
`instance_list` finds nothing, an instance becomes unavailable or a call times out. Every setting is in
[configuration](configuration.md); every failure kind is in [errors and recovery](errors-and-recovery.md).

## How the pieces connect

```text
AI client --stdio--> CheatEngine.Mcp.Gateway.exe --HTTP on 127.0.0.1, bearer token--> plugin backend in CE --> target
```

- **Distribution.** One folder holds:
  - `CheatEngine.Mcp/`, the plugin: `CheatEngine.Mcp.Plugin.dll`, its dependency and runtime configuration files,
    `appsettings.json`, the native Lua bridge, a README and the license files;
  - `CheatEngine.Mcp.Gateway.exe`, a Native AOT executable that needs no .NET runtime;
  - `LICENSE` and `THIRD-PARTY-NOTICES.md`.
- **Plugin.** Each enable starts one backend in that CE process. It listens on `127.0.0.1` (a free port by default),
  answers only requests that carry that activation's new random bearer token, and writes a discovery record into the
  per-user instance registry. A disable deletes the record and stops the backend. Enabling attaches to no process.
- **Gateway.** The AI client starts it as a local stdio MCP server. It reads the registry, confirms each backend's
  identity and forwards each call to the one instance named by its `instanceId`, never to another. It does not launch
  CE, enable the plugin or choose a target.
- **Knowledge.** The gateway serves the `cheatengine://docs/...` documents and the workflow prompts itself, even when CE
  is not running. If the client can read `cheatengine://docs/connection-troubleshooting`, the client-gateway link
  works, and any remaining problem lies between the gateway and CE.

## Set up once

1. **Platform.** Use Windows x64 and Cheat Engine 7.7 x64. `dotnet --list-runtimes` must show the x64
   `Microsoft.NETCore.App`, `Microsoft.AspNetCore.App` and `Microsoft.WindowsDesktop.App` runtimes, version 10.
2. **CE's host configuration.** `ce.runtimeconfig.json`, next to the CE executables, selects the .NET runtime that CE
   loads plugins into. It must select `net10.0` and list all three frameworks at version `10.0.0` with a roll-forward
   policy such as `LatestMinor`. Installing .NET 10 does not fix a file that pins another major version (`Minor` and
   `LatestMinor` never cross a major version) or leaves out ASP.NET Core. To fix it, with the user's consent: close CE,
   back the file up, edit only `runtimeOptions`, restart CE. A Program Files install may need an elevated editor.
3. **Enable the plugin.** In CE, open **Edit > Settings > Plugins**, choose **Add new**, select
   `CheatEngine.Mcp.Plugin.dll` in the deployed `CheatEngine.Mcp` folder, tick its checkbox and close the dialog. Keep
   the folder complete and the DLL name unchanged: CE needs the files next to it.
4. **The status item.** CE's main menu bar shows **MCP: Starting**, then **MCP: Enabled** or **MCP: Start failed**,
   and **MCP: Disabled** after a disable. Click it for the instance name, the listening address (when enabled) and the
   log file name. **Enabled** means that the backend accepts requests, not that an AI client is connected. The item
   stays until CE closes and is reused by the next enable. An enable that fails before the backend starts (an invalid
   setting, a load error) adds no item, or leaves an earlier item unchanged.
5. **Register the gateway** in the AI client as a local stdio MCP server whose command is the absolute path of
   `CheatEngine.Mcp.Gateway.exe`. It needs no URL, API key or console window; the client starts and stops it. Use a
   short server key such as `cheatengine`: clients prefix each tool name with it (Claude Code calls a tool
   `mcp__<key>__<tool>`), some model APIs cap a tool name at 64 characters, and this server's tool names may use up
   to 40.
6. **Verify.** Call `instance_list()` and keep the `instanceId` of the intended CE. Then:
   - `runtime_get_info(instanceId="<instanceId>")` returns `hostVersion` (CE), `pluginVersion`, `pluginFileName` and
     `gates`, the four `Mcp:Enable...` switches of this activation as booleans;
   - `runtime_get_overview(instanceId="<instanceId>")` returns the target (`process` with `isOpen`), `resourceCount`
     and `jobCount`.

   Continue with [attach and orient](../Workflows/attach-and-orient.md) or [getting started](getting-started.md).

Registration examples (replace the path with the installed executable; gateway options go after the path):

```powershell
codex mcp add cheatengine -- "C:\Tools\CheatEngine.Mcp\CheatEngine.Mcp.Gateway.exe"
claude mcp add cheatengine -- "C:\Tools\CheatEngine.Mcp\CheatEngine.Mcp.Gateway.exe"
```

Clients configured with an `mcpServers` JSON file take an entry like this (the exact layout varies by client):

```json
{
  "mcpServers": {
    "cheatengine": {
      "command": "C:\\Tools\\CheatEngine.Mcp\\CheatEngine.Mcp.Gateway.exe",
      "args": []
    }
  }
}
```

The gateway accepts only `--instance-directory <absolute path>` and `--call-timeout-seconds <5-3600>`, each also
settable through `MCP_INSTANCE_DIRECTORY` and `MCP_GATEWAY_CALL_TIMEOUT_SECONDS`; an argument beats its variable. Any
other argument, a missing value, a relative directory or an out-of-range timeout stops it at startup. The timeout
bounds each routed tool call and each resource read. A client's server list shows that the server is registered, not
that a backend is healthy. Restart the client session when new tools do not appear.

## Discovery and instance ids

- **The result.** `instance_list()` returns `instances` (`instanceId`, `name`, `processId`, `pluginVersion`), ordered by
  name, and `discoveryIncomplete`. The `processId` is CE's own process, not the target's; an instance is listed with or
  without a target.
- **What is listed.** Only backends that answer and confirm the identity in their record. Records of a CE that closed
  or crashed are ignored: the gateway checks the process id and start time. Every routed call checks the identity
  again.
- **Incomplete discovery.** `discoveryIncomplete: true` means that the 10-second budget ran out. Call `instance_list()`
  again before you conclude that an instance is missing.
- **Lifetime.** An `instanceId` (`ce-<CE process id>-<32 hex digits>`) lasts one plugin activation; a re-enable or a
  CE restart creates a new one. There is no default instance.
- **Names** may repeat (default `Cheat Engine <CE process id>`). Choose by `processId`, or ask the user.
- **As resources.** `cheatengine://instances` returns the same list. Once the gateway has confirmed an instance in this
  session (through `instance_list()`, a read of `cheatengine://instances`, or a routed call or read), its resource list
  also shows that instance's concrete live resources: runtime, process, threads, jobs, resources, patches, speedhack,
  scanners, debugger, pointer maps and pointer scans, such as `cheatengine://instances/{instanceId}/runtime` with the
  real id. Each is titled `<title> (CE '<name>', pid <CE process id>)` and is never cached. The parameterised ones
  (modules, memory, records, structures and others) stay in the template list. About 1-2 s after an instance appears,
  goes away or restarts, the gateway sends `notifications/resources/list_changed` to a client that has listed
  resources.
- **Completion.** In a client's template picker, `{instanceId}` offers only instances verified in the last 10 s, so
  call `instance_list()` first if it offers nothing. `{module}`, `{structure}`, `{scannerName}` and `{scanName}` then
  complete from the chosen instance. A completion never contacts an unverified instance and offers nothing on any
  failure: no process, CE busy, or a slow answer.
- **Tokens.** Each registry record holds its backend's endpoint and bearer token. No tool result or resource returns
  them, and the logs leave them out. Never open, copy or quote a record; to diagnose, check only that one exists.

## Diagnose by symptom

**The client shows no CheatEngine.Mcp tools, or the server does not start**

- Check that the registered command is the absolute path of an existing `CheatEngine.Mcp.Gateway.exe`.
- Read the client's MCP startup log. The gateway writes its diagnostics to stderr, which clients usually capture.
- An unknown argument, a relative `--instance-directory` or `MCP_INSTANCE_DIRECTORY`, or a timeout outside 5-3600 stops
  the gateway at startup.
- Restart the client session after you change the registration.

**`instance_list` returns no instances**

- Check that CE is running, the plugin checkbox is ticked and the status item shows **MCP: Enabled**. Otherwise see
  "The plugin does not load or start" below.
- Run CE and the AI client as the same Windows user: the registry (`%LOCALAPPDATA%\CheatEngine.Mcp\instances`) is per
  user.
- The plugin (`Mcp:InstanceDirectory` or `MCP_INSTANCE_DIRECTORY`) and the gateway (`--instance-directory` or
  `MCP_INSTANCE_DIRECTORY`) must use the same registry directory. A running process does not see an environment
  variable set after it started.
- If `discoveryIncomplete` is true, call `instance_list()` again.

**Only one of several CE processes appears**

- Enable the plugin in each CE process: each enabled CE is one instance.
- Only one CE at a time can listen on a fixed `Mcp:Port` or `MCP_PORT`; the others show **MCP: Start failed**. Keep the
  default port 0, which picks a free port for each instance.

**`instance_unavailable`**

- With `hostEffect` `not_started`, nothing reached CE. The id is stale (CE closed, or the plugin was disabled or
  enabled again), or the backend refused the connection, missed its 10-second identity check or reported another
  identity. Call `instance_list()` again, and confirm the new id with the user. Never send the call to another
  instance.
- With `hostEffect` `unknown`, the connection failed after the call was sent, so the call may have run. Do not repeat a
  mutation. Rediscover, then inspect the state with read-only tools.
- The error is never `retryable`: rediscover instead of resending the call.
- A new activation does not accept the ids of the old one: jobs, patches, allocations and every other id or name it
  issued (resource ids read `kind-namespace-number`). Record ids are CE's own and stay valid while CE runs, until a
  table load.
- Lua state left by an earlier activation (a speed other than 1, MCP's pause, MCP-set breakpoints, an unfinished Lua
  job, a failed cleanup) shows as `orphaned` in `runtime_list_resources()`. It blocks a target change. Release it only
  with the user's consent, at the end of cleanup or before a target switch:
  `runtime_release_resources(includeOrphans=true)` also releases everything the new activation holds.

**`stopping`**

- The plugin activation is stopping or has ended (a disable, or CE closing). With `not_started` the call never reached
  CE; with `unknown` it may have run. Wait for the user to enable the plugin again, then call `instance_list()`: the
  id changes.

**`timeout`**

- From the gateway: the call outlasted its call timeout, 45 s by default, set with `--call-timeout-seconds` or
  `MCP_GATEWAY_CALL_TIMEOUT_SECONDS` (5-3600). The error has `hostEffect` `unknown`, is not `retryable` and is never
  resent: the operation may still be running or may have completed. A resource read that times out changed nothing;
  read it again once the instance answers.
- With `hostEffect` `completed`, a bounded read stopped at its budget and changed nothing. Narrow it and read again.
- Ask the user to look for an open CE dialog. The tool's `_meta` `cheatengine/dispatchClass` tells what to expect:
  - `may_prompt` tools can wait for the user;
  - `blocking_native` tools can block CE for seconds;
  - `host_scan` tools grow with the target.
- Inspect the state with read-only tools before you repeat anything. Use the job tools (`*_start_*`, then the matching
  poll) for long work. Raise the timeout only for work that you know is long.
- The AI client may also end a call on its own timer (Codex: `tool_timeout_sec`, 60 s by default); a gateway timeout
  above that limit does not help.

**`busy`**

- `Mcp:Execution:MaxConcurrentDispatches` calls (4 by default) already run on that instance. Send calls to one
  instance one at a time. A client's template picker completing `{module}` or `{structure}` takes a slot for a moment
  too. The error is retryable: repeat the identical call once, later.
- `Mcp:Execution:MaxJobs` jobs (16 by default) are already retained, finished ones included. `runtime_list_jobs()`
  lists them; stop finished jobs with `runtime_stop_job(jobId="<jobId>")`, or wait for their lifetime to end.
- `process_attach`, `process_create` and `process_open_file` are refused while an MCP-owned resource holds state (a
  patch, allocation, named scanner, symbol, breakpoint, running job, MCP's pause, a speed other than 1, the Mono
  attachment, or an orphan or failed cleanup of an earlier activation) or while the main scan runs. The `details` field
  lists the blockers, and `runtime_list_resources()` lists the resources. With the user's consent, release them with
  `runtime_release_resources()` (orphans need `includeOrphans=true`), and wait for the main scan
  (`scan_get_status()`) or stop it (`scan_stop()`), then repeat the call. An entry that needs manual recovery stays
  listed until the user recovers it and it is acknowledged; see [errors and recovery](errors-and-recovery.md) and
  [cleanup_session](../Workflows/cleanup-session.md).

**`not_attached`, `target_changed` or `unsupported` while the instance answers**

- The connection is fine; the target is the problem. `process_get_current()` shows `isOpen`, `processId` and
  `processName`. Attach only the process the user chose.
- `target_changed`: CE's selected process changed during the work. Confirm the target with the user, then read again.

**`capability_disabled`**

- The message names the setting (`Mcp:EnableUnsafeLua`, `Mcp:EnableAutoAssembler`, `Mcp:EnableTargetCodeExecution` or
  `Mcp:EnableKernelAccess`), and nothing ran. A tool's `_meta` `cheatengine/requires` lists the switches it always
  needs; some tools need one only for certain arguments or content. A switched-off tool stays listed.
- `runtime_get_info()` reports the four switches in `gates` (`autoAssembler`, `unsafeLua`, `targetCodeExecution`,
  `kernelAccess`); `runtime_get_overview()` repeats them in `runtime.gates`. Read them before you plan a gated route.
- All four switches are on by default, so a switch that is off was turned off on purpose. Explain what the call needs
  and let the user decide. Never change the setting yourself. The hint names `appsettings.json`: the user's own file
  (see "Settings, logs and files" below) overrides the shipped one. A change takes effect after a disable and
  re-enable. See [safety](safety.md) and [configuration](configuration.md).

**A file or table path is refused**

- Writes need a directory in `Mcp:Files:AllowedRoots`. The list is empty by default, which refuses every write. Reads
  need no root but follow the path rules below.
- Table loads and saves need a directory in `CheatEngineClient:AllowedTableRoots`, also empty by default, and a file
  name ending in .CT, .XML or .CETRAINER. `table_list_files()` returns the configured `roots`.
- Paths must be absolute local paths with a drive letter. The server always refuses:
  - UNC and device paths, and Windows device names;
  - paths through links, junctions or other reparse points;
  - the MCP data directory and the instance registry.
- `process_save_file(filename="...")` needs a new, absent file under a write root. CE writes a protected temporary
  file, and the server publishes it only after the save succeeds.

**The plugin does not load or start**

- **MCP: Start failed**: the settings were accepted but the backend could not start, for example because a fixed port
  is already in use. Check the plugin log, fix the cause, then disable and re-enable the plugin.
- No status item, or one still showing an earlier state: the load or the enable failed before the backend started.
  Check the host configuration (setup step 2), that the plugin folder is complete and from one build, and the DLL
  name.
- When the plugin loads but an enable fails, CheatEngine.SDK writes the reason to the Windows debug output by default,
  which a viewer such as Sysinternals DebugView shows; the plugin log may not contain it.
- An invalid setting fails the enable with an error that names the setting and its rule:
  - `Mcp:Host` must be `127.0.0.1`; `Mcp:Port` must be 0-65535;
  - `Mcp:ServerName` and `Mcp:InstanceName` must not be empty, the name at most 128 characters;
  - `Mcp:InstanceDirectory` and each `Mcp:Files:AllowedRoots` entry must be absolute, the roots local and not
    repeated;
  - `Mcp:Logging:MinimumLevel` must be a log level name, and the `Mcp:Execution` values must stay within their bounds;
  - `MCP_DATA_DIRECTORY` must be absolute. A non-integer `MCP_PORT` also fails, with a format error that does not
    name the variable.

  Fix the setting, then disable and re-enable the plugin.

**Tools work, but resources or prompts are missing**

- Some clients show only tools, or list concrete resources but not templates. Through the gateway, an instance's
  concrete live resources appear only after `instance_list()` or a routed call has confirmed it; a client that ignores
  `notifications/resources/list_changed` shows them after it lists resources again. The live resources only mirror
  read-only tools, so the tools still cover everything.
- The documents and workflows ship only as MCP resources and prompts. If the client cannot read resources, tell the
  user that this client cannot reach the knowledge base.

## Settings, logs and files

- **Settings layers**, where the later one wins:
  1. the plugin folder's `appsettings.json` (shipped defaults, replaced by updates);
  2. `%APPDATA%\CheatEngine.Mcp\appsettings.json`, or the one in `MCP_DATA_DIRECTORY`;
  3. `MCP_HOST`, `MCP_PORT`, `MCP_INSTANCE_NAME`, `MCP_INSTANCE_DIRECTORY`.

  An activation reads its settings once, at enable: disable and re-enable the plugin to apply a change. Ambient
  ASP.NET Core variables such as `ASPNETCORE_URLS` are ignored. Every key is in [configuration](configuration.md).
- **Plugin log.** `CheatEngine.Mcp.<CE process id>.log` in `%APPDATA%\CheatEngine.Mcp` or `MCP_DATA_DIRECTORY`. It
  rolls at 10 MiB and keeps five archives (`.1.log` is the newest). `Mcp:Logging:MinimumLevel` sets the level
  (`Information` by default). The gateway writes to stderr, which the AI client captures.
- **Internal errors.** A failure of kind `internal` carries an `errorId`: `details.errorId` in a tool result,
  `error.data.errorId` in a failed resource read. Quote it and ask the user to search the plugin log of that CE
  instance for it (through the gateway the id still comes from the backend). The matching entry names the tool or
  resource, the operation and the exception type, never the message. See [errors and recovery](errors-and-recovery.md).
- **Registry.** `%LOCALAPPDATA%\CheatEngine.Mcp\instances` by default, one token-bearing JSON record per enabled
  activation.
- **Loaded build.** `runtime_get_info()` returns `pluginFileName`, `runtimeFileName` (no directories), `pluginVersion`
  and `hostVersion`. Its `capabilities` are the CE host's; its `gates` are the four `Mcp:Enable...` switches, as
  booleans. No tool reports any other setting, path or limit.

## Several instances, updates and removal

- **One activation per CE process**, with its own `instanceId`, resources and ids. Clean up each instance through its
  own id.
- **Names.** Set `MCP_INSTANCE_NAME` in each CE launch environment to tell instances apart.
- **Shared targets.** Two instances attached to the same target both change its memory. Keep one instance per target.
- **Updates.** Disable the plugin, and close CE and the client's gateway. Replace the whole `CheatEngine.Mcp` folder and
  the executable from the same release, never individual DLLs. Restart, then call `instance_list()` again. Personal
  settings outside the plugin folder are kept.
- **Disable or removal.** Run [cleanup_session](../Workflows/cleanup-session.md) first, with the user's consent. A
  disable stops the activation's jobs and releases its Client leases (allocations, patches, named scanners,
  registered symbols). Effects recorded in CE's Lua state stay: a speed other than 1, MCP's pause, MCP-set breakpoints
  and any failed cleanup. The next activation lists them as `orphaned`. CE-owned state also survives a disable:
  address-list records and their freezes, structures, main scan results and a Mono collector attachment, which the
  next activation does not list.

## Responsible use

The connection is local only: a loopback backend, a per-user registry and a token per activation. Never forward its
port or try to widen `Mcp:Host` (it accepts only `127.0.0.1`) to repair discovery, and never repeat a mutation only
because the transport failed. Use the server only with single-player or offline software that the user owns or may
modify, never with online or competitive games or titles protected by anti-cheat. Change the switches or file roots
only when the user decides to.

## Sources

- Codex MCP configuration: <https://learn.chatgpt.com/docs/extend/mcp?surface=cli>
- Claude Code MCP configuration: <https://code.claude.com/docs/en/mcp>
- MCP transports (stdio): <https://modelcontextprotocol.io/specification/2025-06-18/basic/transports>
- MCP resources and `list_changed`: <https://modelcontextprotocol.io/specification/2025-06-18/server/resources>
- MCP completion: <https://modelcontextprotocol.io/specification/2025-06-18/server/utilities/completion>
- .NET runtime configuration files: <https://learn.microsoft.com/dotnet/core/runtime-config/>
- .NET version selection and roll-forward: <https://learn.microsoft.com/dotnet/core/versions/selection>
- Checking the installed runtimes: <https://learn.microsoft.com/dotnet/core/install/how-to-detect-installed-versions>
