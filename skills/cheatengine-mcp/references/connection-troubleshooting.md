# Connection setup and troubleshooting

The deployment has a plugin folder and an executable. Cheat Engine loads `CheatEngine.Mcp.Plugin.dll` from the
`CheatEngine.Mcp` folder; the AI client's local stdio MCP connection starts `CheatEngine.Mcp.Gateway.exe`. The gateway
discovers enabled CE backends under the same Windows user. It does not launch CE, enable plugins, or select a target.

## Initial setup

1. Use Windows x64 and Cheat Engine 7.7 x64. The plugin requires x64 `Microsoft.NETCore.App`,
   `Microsoft.AspNetCore.App`, and `Microsoft.WindowsDesktop.App` on version 10. Check `dotnet --list-runtimes`.
2. Inspect `ce.runtimeconfig.json` beside the actual CE executable. It must select `net10.0` and those three frameworks
   at `10.0.0` with compatible patch roll-forward. Having version 10 installed does not change a host configuration
   pinned to version 9. Back up the file before an authorized edit, preserve unrelated settings, and restart CE to apply
   the change. Program Files edits may need elevation.
3. In CE's plugin settings, add `CheatEngine.Mcp.Plugin.dll` from the deployed plugin folder and enable its checkbox.
   Keep the folder intact and the DLL unrenamed. Enable starts the backend automatically; the default port is `0` for
   automatic allocation. Check **MCP: Enabled** in CE's menu bar and click it for the instance name, listening address,
   and log path. This indicates a running CE backend, not a connected AI client. Disabling changes it to **MCP:
   Disabled**; a server startup failure shows **MCP: Start failed**. The passive status item stays until CE closes and
   is reused on re-enable. Failures before module activation may leave no indicator.
4. Configure a local stdio MCP server whose command is the absolute path to `CheatEngine.Mcp.Gateway.exe`. Let the AI
   client launch it. It has no interactive console UI and needs no MCP URL or API key. Use a short server key such as
   `cheatengine`: some clients prefix tool names with it (Claude Code exposes `mcp__<key>__<tool>`) under a 64-character
   limit, and tool names use up to 40.
5. Call `instance_list`, retain the intended returned `instanceId`, and call `runtime_get_info` (versions, `pluginFileName`,
   gates) and `runtime_get_overview` using that ID. A CE process ID identifies the host; the target process ID is
   separate. A host can be discovered without a target selected. Continue with [workflows](workflows.md).

For Codex, a registration command is:

```powershell
codex mcp add cheatengine -- "C:\Tools\CheatEngine.Mcp\CheatEngine.Mcp.Gateway.exe"
codex mcp list
```

Replace the example path with the installed EXE. `mcp list` confirms registration, not backend health. Restart the
client session if its tool set has not refreshed, then test discovery.
See [official Codex MCP configuration](https://developers.openai.com/codex/mcp). Other clients may use an `mcpServers`
JSON entry or their own stdio settings UI.

The gateway serves the `cheatengine://docs/...` resources and the workflow prompts itself, so they work with no CE
instance running; `cheatengine://instances` lists instances like `instance_list`. Reading
`cheatengine://docs/connection-troubleshooting` successfully proves that the client reaches the gateway, which separates
gateway problems from backend problems.

## Diagnose the failed stage

| Stage                                                | Evidence and next check                                                                                                                                                                                                                                                                            |
|------------------------------------------------------|----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| Gateway does not start                               | Inspect the configured executable path and the AI client's MCP startup errors. Use the published EXE; it is self-contained and needs no separate .NET runtime.                                                                                                                                     |
| Gateway starts, no CE instances                      | Confirm CE is open with the plugin enabled; check the host runtime configuration and plugin logs. Verify same Windows user and matching instance-directory overrides.                                                                                                                              |
| Discovery is incomplete                              | `discoveryIncomplete: true` means its deadline expired. Retry discovery before deciding that a backend is absent.                                                                                                                                                                                  |
| Only one of several instances starts                 | Check for duplicated fixed ports. Use default port `0`; enable the plugin in each CE process.                                                                                                                                                                                                      |
| `instance_unavailable`, or an ID fails after restart | Rediscover with `instance_list`; restarting CE or re-enabling the plugin creates a new ID and invalidates old job, patch and record IDs. Do not redirect a failed mutation to another instance.                                                                                                    |
| Host is available, target operations fail            | Inspect `runtime_get_overview`, `process_get_current` and the error `kind` (`not_attached`, `target_changed`, `unsupported`). Attach only the intended process and release owned target resources with `runtime_release_resources` before switching.                                               |
| `capability_disabled`                                | The `hint` names the gate: `Mcp:EnableUnsafeLua`, `Mcp:EnableAutoAssembler`, `Mcp:EnableTargetCodeExecution` or `Mcp:EnableKernelAccess`; `runtime_get_info` reports all four. Fixed-script tools do not need `Mcp:EnableUnsafeLua`. Do not change an explicit user setting without authorization. |
| File or table path refused                           | Writes need a root in `Mcp:Files:AllowedRoots` (empty by default); tables need `CheatEngineClient:AllowedTableRoots`. Paths must be absolute and local; UNC, device paths, reparse points, the instance registry and the MCP data directory are always refused.                                    |
| `process_save_file` refuses the destination          | Choose a new absent path under `Mcp:Files:AllowedRoots`. The tool refuses in-place saves and existing files, lets CE write a protected `.partial` file, then atomically publishes it only after a successful save.                                                                                 |
| Call returns `timeout`                               | The gateway waited its call timeout (45 s by default; `--call-timeout-seconds` or `MCP_GATEWAY_CALL_TIMEOUT_SECONDS`). The outcome is unknown: check CE for an open dialog or a long operation and inspect state before repeating anything.                                                        |
| `busy` on many calls                                 | Each instance runs at most 4 dispatches at once; serialize calls to one instance. A running main scan or live owned resources also return `busy` with blockers in `details`.                                                                                                                       |
| Plugin cannot load, no plugin log                    | Loading can fail before Client logging starts. Verify .NET host selection, that the plugin folder is complete and from one build, the DLL name, and any CE load error first.                                                                                                                       |
| Enable fails with an options error                   | Correct the named `Mcp` setting or `MCP_*` variable: the host must be `127.0.0.1`, the port 0-65535, names non-empty (instance name at most 128 characters), and the instance directory absolute.                                                                                                  |
| Tools work, resources or prompts missing             | Some clients expose only tools, or only concrete resources without templates. Everything stays reachable through tools; the docs also ship in the skill's `references/` folder.                                                                                                                    |

Do not expose the authenticated HTTP backend as a public endpoint or change its loopback default to repair discovery. Do
not retry a mutation merely because the transport timed out: it may have executed. Inspect the same instance's state;
see [errors-and-recovery](errors-and-recovery.md).

## Settings and logs

- Settings: for configuration keys, precedence and every `MCP_*` environment variable, see docs/configuration.md in the
  repository. An activation reads its settings once at enable; disable and re-enable the plugin to apply changes. The
  backend ignores ambient ASP.NET Core variables such as `ASPNETCORE_URLS` or `Kestrel__Endpoints__*`.
- Loaded files: `runtime_get_info` reports the non-sensitive `pluginFileName` and `runtimeFileName`, plus the versions of the loaded `CheatEngine.Mcp.Plugin.dll`.
- Logs: `CheatEngine.Mcp.<CE PID>.log` in `%APPDATA%\CheatEngine.Mcp`, or in the absolute `MCP_DATA_DIRECTORY` when set.
  The gateway's diagnostics go to stderr, normally captured by the AI client.
- Discovery: `%LOCALAPPDATA%\CheatEngine.Mcp\instances`, or an absolute `MCP_INSTANCE_DIRECTORY` set consistently for
  plugins and gateway. Records contain authentication tokens; inspect only non-secret metadata needed for diagnosis and
  never share raw records.

## Multiple instances and updates

Set `MCP_INSTANCE_NAME` separately in each CE launch environment for friendly labels; existing processes do not inherit
later environment changes. Names may repeat. Every tool call still uses its own discovered `instanceId`, and resource
IDs stay with that instance. Two hosts attached to the same target can change shared target memory.

Before replacing the deployed plugin folder and EXE, close CE and stop the client's gateway connection. Replace the
whole folder and the EXE from the same build, never individual DLLs, then restart and rediscover. The plugin folder's
`appsettings.json` holds shipped defaults and is replaced by updates; user settings outside the plugin folder are
preserved.

Before disabling or removing the plugin, run [cleanup_session](workflows/cleanup-session.md) to undo task-owned freezes,
patches, speed changes, pause and debugger state. `runtime_release_resources` handles owned leases, but CE-owned state
such as the address list, structures and breakpoints survives a disable. Preserve unrelated address records and other
user state.
