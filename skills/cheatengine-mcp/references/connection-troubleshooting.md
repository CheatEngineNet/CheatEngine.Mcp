# Connection setup and troubleshooting

The deployment has a plugin folder and an executable. Cheat Engine loads `CheatEngine.Mcp.Plugin.dll` from the `CheatEngine.Mcp` folder; the AI client's local stdio MCP connection starts `CheatEngine.Mcp.Gateway.exe`. The gateway discovers enabled CE backends under the same Windows user. It does not launch CE, enable plugins, or select a target.

## Initial setup

1. Use Windows x64 and Cheat Engine 7.7 x64. The plugin requires x64 `Microsoft.NETCore.App`, `Microsoft.AspNetCore.App`, and `Microsoft.WindowsDesktop.App` on version 10. Check `dotnet --list-runtimes`.
2. Inspect `ce.runtimeconfig.json` beside the actual CE executable. It must select `net10.0` and those three frameworks at `10.0.0` with compatible patch roll-forward. Having version 10 installed does not change a host configuration pinned to version 9. Back up the file before an authorized edit, preserve unrelated settings, and restart CE to apply the change. Program Files edits may need elevation.
3. In CE's plugin settings, add `CheatEngine.Mcp.Plugin.dll` from the deployed plugin folder and enable its checkbox. Keep the folder intact and the DLL unrenamed. Enable starts the backend automatically; the default port is `0` for automatic allocation. Check **MCP: Enabled** in CE's menu bar and click it for the instance name, listening address, and log path. This indicates a running CE backend, not a connected AI client. Disabling changes it to **MCP: Disabled**; a server startup failure shows **MCP: Start failed**. The passive status item stays until CE closes and is reused on re-enable. Failures before module activation may leave no indicator.
4. Configure a local stdio MCP server whose command is the absolute path to `CheatEngine.Mcp.Gateway.exe`. Let the AI client launch it. It has no interactive console UI and needs no MCP URL or API key.
5. Call `list_instances`, retain the intended returned `instanceId`, and call `get_plugin_version`, `get_runtime_info`, and `get_current_process` using that ID. A CE process ID identifies the host; the target process ID is separate. A host can be discovered without a target selected.

For Codex, a registration command is:

```powershell
codex mcp add cheatengine -- "C:\Tools\CheatEngine.Mcp\CheatEngine.Mcp.Gateway.exe"
codex mcp list
```

Replace the example path with the installed EXE. `mcp list` confirms registration, not backend health. Restart the client session if its tool set has not refreshed, then test discovery. See [official Codex MCP configuration](https://developers.openai.com/codex/mcp). Other clients may use an `mcpServers` JSON entry or their own stdio settings UI.

## Diagnose the failed stage

| Stage | Evidence and next check |
| --- | --- |
| Gateway does not start | Inspect the configured executable path and the AI client's MCP startup errors. Use the published EXE, which includes its runtime. |
| Gateway starts, no CE instances | Confirm CE is open with the plugin enabled; check the host runtime configuration and plugin logs. Verify same Windows user and matching instance-directory overrides. |
| Discovery is incomplete | `discoveryIncomplete: true` means its deadline expired. Retry discovery before deciding that a backend is absent. |
| Only one of several instances starts | Check for duplicated fixed ports. Use default port `0`; enable the plugin in each CE process. |
| Instance ID fails after restart | Rediscover the host; restarting or re-enabling creates a new ID. Do not redirect a failed mutation to another instance. |
| Host is available, target operations fail | Inspect `get_current_process` and capability/error details. Attach only the intended process and release owned target resources before switching. |
| Policy rejects a tool | Check allowed table roots or the specific optional execution setting. Address-list and speedhack tools do not need arbitrary Lua enabled. |
| Plugin cannot load, no plugin log | Loading can fail before Client logging starts. Verify .NET host selection, that the plugin folder is complete and from one build, the DLL name, and any CE load error first. |
| Enable fails with an options error | Correct the named `Mcp` setting or `MCP_*` variable: the host must be `127.0.0.1`, the port 0-65535, names non-empty (instance name at most 128 characters), and the instance directory absolute. |

Do not expose the authenticated HTTP backend as a public endpoint or change its loopback default to repair discovery. Do not retry a mutation merely because the transport timed out: it may have executed. Inspect the same instance's state.

## Settings and logs

- Settings: the plugin folder's `appsettings.json` holds the shipped defaults and is replaced by updates; keep changes in `%APPDATA%\CheatEngine.Mcp\appsettings.json`. Environment overrides apply last. `MCP_DATA_DIRECTORY` can select an absolute user settings/log directory. Disable and re-enable to reload settings. The backend ignores ambient ASP.NET Core variables such as `ASPNETCORE_URLS` or `Kestrel__Endpoints__*`.
- Loaded files: `get_plugin_version.location` and `runtimeLocation` both identify the loaded `CheatEngine.Mcp.Plugin.dll`.
- Logs: `CheatEngine.Mcp.<CE PID>.log` in that user settings/log directory. The gateway's diagnostics go to stderr, normally captured by the AI client.
- Discovery: `%LOCALAPPDATA%\CheatEngine.Mcp\instances`, or an absolute `MCP_INSTANCE_DIRECTORY` set consistently for plugins and gateway. Records contain authentication tokens; inspect only non-secret metadata needed for diagnosis and never share raw records.

## Multiple instances and updates

Set `MCP_INSTANCE_NAME` separately in each CE launch environment for friendly labels; existing processes do not inherit later environment changes. Names may repeat. Every tool call still uses its own discovered `instanceId`, and resource IDs stay with that instance. Two hosts attached to the same target can change shared target memory.

Before replacing the deployed plugin folder and EXE, close CE and stop the client's gateway connection. Replace the whole folder and the EXE from the same build, never individual DLLs, then restart and rediscover. User settings outside the plugin folder are preserved.

Before disabling or removing the plugin, explicitly undo task-owned address-list freezes, speed changes, and debugger state. `release_target_resources` handles Client-owned leases but does not undo every CE-owned effect. Preserve unrelated address records and other user state.
