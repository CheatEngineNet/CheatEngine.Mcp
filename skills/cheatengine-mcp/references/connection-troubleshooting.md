# Connection setup and troubleshooting

The deployment has two files. Cheat Engine loads `CheatEngine.Mcp.dll`; the AI client's local stdio MCP connection starts `CheatEngine.Mcp.Gateway.exe`. The gateway discovers enabled CE backends under the same Windows user. It does not launch CE, enable plugins, or select a target.

## Initial setup

1. Use Windows x64 and Cheat Engine 7.7 x64. The plugin requires x64 `Microsoft.NETCore.App`, `Microsoft.AspNetCore.App`, and `Microsoft.WindowsDesktop.App` on version 10. Check `dotnet --list-runtimes`.
2. Inspect `ce.runtimeconfig.json` beside the actual CE executable. It must select `net10.0` and those three frameworks at `10.0.0` with compatible patch roll-forward. Having version 10 installed does not change a host configuration pinned to version 9. Back up the file before an authorized edit, preserve unrelated settings, and restart CE to apply the change. Program Files edits may need elevation.
3. In CE's plugin settings, add the deployed `CheatEngine.Mcp.dll` and enable its checkbox. Preserve the DLL filename. Enable starts the backend automatically; the default port is `0` for automatic allocation. Check **MCP: Enabled** in CE's menu bar and click it for the instance name, listening address, and log path. This indicates a running CE backend, not a connected AI client. Disabling changes it to **MCP: Disabled**; a server startup failure shows **MCP: Start failed**. The passive status item stays until CE closes and is reused on re-enable. Failures before module activation may leave no indicator.
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
| Plugin cannot load, no plugin log | Bootstrap can fail before Client logging starts. Verify .NET host selection, DLL name, cache permissions, and any CE load error first. |

Do not expose the authenticated HTTP backend as a public endpoint or change its loopback default to repair discovery. Do not retry a mutation merely because the transport timed out: it may have executed. Inspect the same instance's state.

## Settings, logs, and cache

- Settings: optional `appsettings.json` beside the original plugin DLL, then `%APPDATA%\CheatEngine.Mcp\appsettings.json`; environment overrides apply last. `MCP_DATA_DIRECTORY` can select an absolute user settings/log directory. Disable and re-enable to reload settings.
- Logs: `CheatEngine.Mcp.<CE PID>.log` in that user settings/log directory. The gateway's diagnostics go to stderr, normally captured by the AI client.
- Discovery: `%LOCALAPPDATA%\CheatEngine.Mcp\instances`, or an absolute `MCP_INSTANCE_DIRECTORY` set consistently for plugins and gateway. Records contain authentication tokens; inspect only non-secret metadata needed for diagnosis and never share raw records.
- Extracted payload: `%LOCALAPPDATA%\CheatEngine.Mcp\cache\<payload SHA256>`, or an absolute `MCP_BUNDLE_CACHE_DIRECTORY`. `get_plugin_version.location` identifies the original DLL; `runtimeLocation` identifies the extracted runtime.

Treat cached payloads as immutable. Missing, changed, or unexpected files make cache reuse fail. For an authorized repair, close every CE process using that version, remove only the affected hash directory, restart CE, and enable the plugin to extract a fresh copy. Do not edit cached settings, replace individual SDK DLLs, or delete the entire user-data directory as a cache repair.

## Multiple instances and updates

Set `MCP_INSTANCE_NAME` separately in each CE launch environment for friendly labels; existing processes do not inherit later environment changes. Names may repeat. Every tool call still uses its own discovered `instanceId`, and resource IDs stay with that instance. Two hosts attached to the same target can change shared target memory.

Before replacing the deployed DLL and EXE, close CE and stop the client's gateway connection. Replace both files from the same build, then restart and rediscover. New payloads get a new cache directory. User settings remain outside it.

Before disabling or removing the plugin, explicitly undo task-owned address-list freezes, speed changes, and debugger state. `release_target_resources` handles Client-owned leases but does not undo every CE-owned effect. Preserve unrelated address records and other user state.
