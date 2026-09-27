# Troubleshooting

A request travels through four stages: the AI client starts the gateway, the gateway discovers the Cheat Engine (CE) backends, the backend runs the tool inside CE, and CE acts on the target.
Find the first stage that fails, then read its section.
For settings and environment variables, see [configuration.md](configuration.md); for the components, see [architecture.md](architecture.md).

## Find the failing stage

| Symptom | Likely stage | Go to |
| --- | --- | --- |
| No **MCP:** item in CE's menu bar after enabling | Plugin load | [The plugin does not load](#the-plugin-does-not-load) |
| **MCP: Start failed** | Backend start | [The status item](#the-status-item) |
| The client reports that the `cheatengine` server failed to start | Gateway | [The gateway does not start](#the-gateway-does-not-start) |
| `instance_list` returns no instances | Discovery | [instance_list is empty](#instance_list-is-empty) |
| `discoveryIncomplete: true` | Discovery | [instance_list is empty](#instance_list-is-empty) |
| A call fails with `instance_unavailable` | Routing | [An instance becomes unavailable](#an-instance-becomes-unavailable) |
| A call fails with `capability_disabled` | Backend | [capability_disabled](#capability_disabled) |
| A file or table path is refused | Backend | [Refused file paths](#refused-file-paths) |
| A call fails with `timeout` | Backend or CE | [Timeouts: the outcome is unknown](#timeouts-the-outcome-is-unknown) |
| `busy`, `not_attached` or `target_changed` | Target | [Target and concurrency errors](#target-and-concurrency-errors) |
| "Cannot have more than 128 tools per request" in VS Code | Client | [Client-specific problems](#client-specific-problems) |

A quick split: ask the agent to read the resource `cheatengine://docs/connection-troubleshooting`.
The gateway serves it without any CE instance, so if it works, the client and gateway are fine and the problem is on the CE side.

## Tool errors

When a tool fails, the result has `isError: true` and its text holds an error object:

```json
{"error":{"kind":"capability_disabled","message":"...","operation":"...","hostEffect":"not_started","retryable":false,"hint":"..."}}
```

`kind` says what went wrong, `hostEffect` says whether anything may have changed in CE or the target, and `hint` suggests the next step.
The [errors and recovery guide](../skills/cheatengine-mcp/references/errors-and-recovery.md) lists every kind and host effect.

## The plugin does not load

When the plugin fails to load, CE shows no **MCP:** item and the plugin may write no log at all, because loading fails before logging starts.
Check, in this order:

1. You run the x64 CE executable, `cheatengine-x86_64.exe`, version 7.7 (the qualified build is 7.7.0.10621).
2. You added `CheatEngine.Mcp.Plugin.dll` from the deployed `CheatEngine.Mcp` folder, and the file is not renamed.
3. The plugin folder is complete and comes from one build: `CheatEngine.Mcp.Plugin.deps.json`, `CheatEngine.Mcp.Plugin.runtimeconfig.json`, `cheatengine-sdk-lua-bridge.dll` and the other DLLs sit beside the plugin DLL. Replace the whole folder rather than single files.
4. `dotnet --list-runtimes` lists `Microsoft.NETCore.App`, `Microsoft.AspNetCore.App` and `Microsoft.WindowsDesktop.App`, each at 10.0.x, for x64.
5. CE's `ce.runtimeconfig.json` selects .NET 10 and lists all three frameworks (next section).
6. You restarted CE after changing the runtimes or the runtime configuration.

Note any error message CE shows when you tick the plugin's checkbox, and include it in a report.

## ce.runtimeconfig.json

CE starts a single .NET runtime from `ce.runtimeconfig.json`, beside `cheatengine-x86_64.exe` (by default in `C:\Program Files\Cheat Engine`).
Only one runtime can run in a process, and a plugin that needs a framework the runtime did not start with cannot load ([Microsoft: native hosting limitations](https://learn.microsoft.com/dotnet/core/tutorials/netcore-hosting#limitations)).

Typical mistakes:

- The file still selects an older version, such as `net9.0` or `9.0.0`. Installing .NET 10 does not change it.
- `Microsoft.AspNetCore.App` is missing from `frameworks`. The plugin's backend needs it.
- The file is not valid JSON after the edit, for example a missing comma.
- The edit went to another CE installation than the one you start.
- A CE reinstall or update replaced the edited file.

[getting-started.md](getting-started.md#3-let-cheat-engine-load-aspnet-core) shows the exact edit.
Close every CE process before you edit the file, and restart CE afterwards.

## The status item

The plugin adds one item to CE's main menu bar. Click it to see the instance name, listening address and log file path.

| Item | Meaning | What to do |
| --- | --- | --- |
| (none, or unchanged from an earlier enable) | The plugin did not load, or the enable failed before its backend module started, for example on an invalid setting. | See [The plugin does not load](#the-plugin-does-not-load) and [Settings errors](#settings-errors). |
| **MCP: Starting** | The backend is starting. | Wait a moment; it should turn into Enabled or Start failed. |
| **MCP: Enabled** | The backend is listening and published. | Nothing. It does not mean that an AI client is connected. |
| **MCP: Disabled** | The plugin was disabled. | Enable it again in **Edit > Settings > Plugins**. The item stays until CE closes. |
| **MCP: Start failed** | The backend could not start. | Read the log for the error. A common cause is a fixed `Mcp:Port` already in use; leave the port at `0`. Then enable the plugin again. |

## Settings errors

Settings are read once at each enable, and invalid settings fail the enable before any backend starts.
The error message names each rejected setting, for example:

- `Mcp:Host must be 127.0.0.1; the gateway connects to private local backends.`
- `Mcp:Port must be between 0 and 65535 (0 selects a free port).`
- `Mcp:InstanceName is required.` or `Mcp:InstanceName must be at most 128 characters.`
- `Mcp:InstanceDirectory must be an absolute directory.`
- `Mcp:Logging:MinimumLevel must be Trace, Debug, Information, Warning, Error, Critical or None.`

Two environment variables fail differently: a non-numeric `MCP_PORT` fails with a format error instead of picking another port, and a relative `MCP_DATA_DIRECTORY` is rejected because it must be absolute.
Fix the setting in `%APPDATA%\CheatEngine.Mcp\appsettings.json` (or in `MCP_DATA_DIRECTORY`) or the `MCP_*` variable, then disable and enable the plugin.
The backend ignores ambient ASP.NET Core settings in CE's environment, such as `ASPNETCORE_URLS` or `Kestrel__Endpoints__*`.

## The gateway does not start

- Check the path in your client's server entry: it must be the absolute path of `CheatEngine.Mcp.Gateway.exe`, with backslashes escaped in JSON.
- Read the client's MCP log; the gateway writes its diagnostics to stderr, which the client captures ([clients.md](clients.md)).
- The gateway accepts only its documented arguments. An unknown argument, or a relative `--instance-directory` or `MCP_INSTANCE_DIRECTORY`, stops it at startup.
- The executable is self-contained; it needs no .NET runtime.
- Double-clicking the executable opens a console that waits for MCP messages; that is normal. Configure it as your client's server command instead.
- A running gateway locks the file: stop the client's server before you replace the executable.

## instance_list is empty

The gateway works but found no enabled backend. Check:

1. CE is running and shows **MCP: Enabled**. A gateway with no CE open correctly returns an empty list.
2. CE and the AI client run under the **same Windows account**. The registry lives in that account's `%LOCALAPPDATA%\CheatEngine.Mcp\instances`; a CE started under another account publishes into that account's profile instead.
3. Any registry override agrees: `MCP_INSTANCE_DIRECTORY` or `Mcp:InstanceDirectory` on the plugin side, and `--instance-directory` or `MCP_INSTANCE_DIRECTORY` on the gateway side, must name the same absolute directory ([multi-instance.md](multi-instance.md#moving-the-registry)).
4. If you use several CE processes, each has the plugin enabled and each shows **MCP: Enabled**.

`discoveryIncomplete: true` means the discovery deadline expired before every record was checked.
The list holds only the instances verified in time; call `instance_list` again before you conclude that an instance is missing.

Stale records from crashed CE processes are ignored automatically.
Do not delete or edit registry files to fix discovery, and never paste their content anywhere: they contain authentication tokens.

## An instance becomes unavailable

`instance_unavailable` means the gateway could not reach or verify the instance named by `instanceId`:

- **CE restarted, or the plugin was disabled and enabled again.** Each activation gets a new `instanceId`, and the gateway rejects the old one because the backend's identity no longer matches the record. Call `instance_list` and use the new ID. Every job, patch, record and scanner ID from the old activation is invalid too.
- **The plugin was disabled or CE closed.** Enable it again, then rediscover.
- **The connection was lost during a call.** The call may have run; read the state of the same instance before repeating anything.

The gateway never redirects a failed call to another instance, even one with the same display name; see [multi-instance.md](multi-instance.md).

## capability_disabled

Four gates switch groups of tools on or off. All four are enabled by default:

| Setting | Governs |
| --- | --- |
| `Mcp:EnableUnsafeLua` | Arbitrary Lua (`lua_execute`), Lua inside Auto Assembler scripts, tables that carry Lua. |
| `Mcp:EnableAutoAssembler` | Auto Assembler checks, scripts and code patches. |
| `Mcp:EnableTargetCodeExecution` | Code execution and injection in the target, `process_create`, speedhack changes, Mono attach and invoke, the VEH debugger. |
| `Mcp:EnableKernelAccess` | The `kernel_*` tools (DBK driver and DBVM) and the kernel debugger interface. |

`capability_disabled` means a setting turned the gate off; the error's `hint` names it, and `runtime_get_info` reports all four.
Look for an explicit `false` in the plugin folder's `appsettings.json` and in your user `appsettings.json`.
Changing a gate is your decision: edit the setting, then disable and enable the plugin.
The gates are exposure switches, not a sandbox; see [security.md](security.md).

## Refused file paths

- **Writes** (memory dumps, saved files) need a root listed in `Mcp:Files:AllowedRoots`, which is empty by default, so every write is refused until you add one.
- **Cheat tables** need a root in `CheatEngineClient:AllowedTableRoots`.
- Every path must be absolute and on a local fixed disk. UNC paths, `\\?\` and `\\.\` device paths, alternate data streams and paths through links or reparse points are refused.
- The discovery registry directory and the MCP data directory are always refused, whatever the roots say.

Add the narrowest folder that works, never a whole drive or a system folder, then disable and enable the plugin ([configuration.md](configuration.md)).

## Timeouts: the outcome is unknown

The gateway waits 45 seconds per call by default (`--call-timeout-seconds` or `MCP_GATEWAY_CALL_TIMEOUT_SECONDS`), then returns `timeout` with `hostEffect: unknown`.
The backend may still be running the operation, or may have finished it.

- Do not repeat a mutation after a timeout: it may already have happened.
- Look at CE: an open dialog (a prompt from CE itself, for example when a table or DBVM asks for confirmation) or a long native operation can hold its main thread.
- Read the state of the same instance with read-only tools (`memory_read`, `record_get`, `asm_list_patches`, `runtime_list_resources`, `runtime_get_overview`) and decide from what you see.
- Long operations such as pointer scans and debugger captures run as jobs: start them, then poll. Polling is always safe to repeat.

Clients have their own timeouts too; keep them longer than the gateway's, for example Codex's `tool_timeout_sec` (60 seconds by default).
A timeout reported by the client instead of the gateway also means an unknown outcome.

## Target and concurrency errors

- `not_attached`: the instance has no target. Attach one with `process_list` and `process_attach`, or select it in CE.
- `target_changed`: the target changed under the call, for example in CE's own process picker. Check `process_get_current`.
- `busy`: each instance runs at most four operations at once, so send calls to one instance one after another. A running main scan or owned resources also block a target change; the error's `details` lists the blockers. Wait for or stop the scan, or release resources with `runtime_release_resources`, before switching targets.

## Client-specific problems

| Client | Problem | Fix |
| --- | --- | --- |
| VS Code | "Cannot have more than 128 tools per request" | The catalog has about 170 tools. Deselect tools in the tools picker or enable `github.copilot.chat.virtualTools.threshold` ([clients.md](clients.md#vs-code-github-copilot)). |
| Claude Desktop | The server never appears (Microsoft Store build) | The app may read `%LOCALAPPDATA%\Packages\Claude_pzs8sxrjxfjjc\LocalCache\Roaming\Claude\claude_desktop_config.json` instead of the file **Edit Config** opens ([clients.md](clients.md#claude-desktop)). |
| Claude Desktop, Claude Code | A tool name is rejected as longer than 64 characters | Use a short server key such as `cheatengine` ([clients.md](clients.md#use-the-server-key-cheatengine)). |
| Any | Tools work, but prompts or resources are missing | Some clients expose only tools. Everything stays reachable through tools, and the guides also ship in the skill's `references/` folder. |
| Any | The tool list did not change after an update | Restart the client, or reconnect the server from its MCP panel. |

Where each client shows the gateway's output:

- Claude Code: `claude mcp list`, `claude mcp get cheatengine`, or `/mcp` in a session.
- Claude Desktop: `%APPDATA%\Claude\logs\mcp-server-cheatengine.log` and `mcp.log`.
- VS Code: **MCP: List Servers**, select `cheatengine`, **Show Output**.
- Codex: `codex mcp list`, or `/mcp` in the terminal UI.
- Cursor: the Output panel, **MCP Logs**.

## Logs

Each CE process writes its own plugin log:

```text
%APPDATA%\CheatEngine.Mcp\CheatEngine.Mcp.<CE process ID>.log
```

With an absolute `MCP_DATA_DIRECTORY`, the log goes to that directory instead.
The status item's details show the exact path.

- The file rolls over at 10 MiB. Five archives are kept, `CheatEngine.Mcp.<CE process ID>.1.log` being the newest.
- The level is `Information` by default. Set `Mcp:Logging:MinimumLevel` to `Trace`, `Debug`, `Information`, `Warning`, `Error`, `Critical` or `None`, then disable and enable the plugin:

  ```json
  {
    "Mcp": {
      "Logging": {
        "MinimumLevel": "Debug"
      }
    }
  }
  ```

- At `Trace`, the log can contain tool arguments and results, including memory contents. Lower the level again after diagnosis and review the file before you share it.
- Transport and protocol categories never log below `Information`, and tokens are never logged.
- A failure while CE loads the plugin can happen before the log exists.

The gateway has no log file; it writes diagnostics to stderr, which your AI client captures.

## Reporting a problem

Include the CE version, the plugin version from `runtime_get_info`, the client and its version, the failing stage, the error's `kind`, `hostEffect` and `message`, and the relevant log lines.
Remove the following before you share anything:

- Registry files from `%LOCALAPPDATA%\CheatEngine.Mcp\instances` and any token. Never attach them.
- Personal paths and user names, if you do not want them public.
- Memory contents or process names that you consider private.

Report security issues privately as described in [security.md](security.md).
