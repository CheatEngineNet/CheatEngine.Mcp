# CheatEngine.Mcp plugin

This folder is the complete Cheat Engine plugin deployment.
Keep every file together, and load `CheatEngine.Mcp.Plugin.dll` from this folder without renaming it.
The DLL alone cannot load because Cheat Engine uses its dependency and runtime configuration files and the native Lua bridge beside it.

## Requirements

Use Cheat Engine 7.7 x64 on Windows x64.
Ensure the x64 .NET 10 installation supplies `Microsoft.NETCore.App`, `Microsoft.AspNetCore.App`, and `Microsoft.WindowsDesktop.App`.
The plugin uses the .NET and ASP.NET Core frameworks; Cheat Engine's managed host also needs the Windows Desktop framework.
Cheat Engine's `ce.runtimeconfig.json` must select .NET 10 and include `Microsoft.NETCore.App`, `Microsoft.AspNetCore.App`, and `Microsoft.WindowsDesktop.App` in `runtimeOptions.frameworks`.
Back up that file before changing it, and close Cheat Engine first.

## Enable

In Cheat Engine, open **Edit > Settings > Plugins**, choose **Add new**, and select `CheatEngine.Mcp.Plugin.dll` in this folder.
Enable its checkbox and close the dialog.
The plugin then starts a local authenticated MCP backend and publishes its instance for the gateway.
It does not attach to a target process on enable.

The AI client must start `CheatEngine.Mcp.Gateway.exe` from the parent distribution folder as a local stdio MCP server.
Use `instance_list` to find the current `instanceId`, then pass that ID to each routed tool call.
Keep the gateway and Cheat Engine under the same Windows user.

## Configuration and updates

The shipped `appsettings.json` contains defaults and is replaced when the plugin is updated.
Put personal settings in `%APPDATA%\CheatEngine.Mcp\appsettings.json` or set `MCP_DATA_DIRECTORY` to an absolute data directory.
Host-file writes require `Mcp:Files:AllowedRoots`; table load and save require `CheatEngineClient:AllowedTableRoots`.
File reads still require an absolute local path accepted by the path policy, which refuses the data and instance directories.
Disable and re-enable the plugin after changing its settings.
Logs are written to the data directory as `CheatEngine.Mcp.<CE PID>.log`.

To update, disable the plugin, close Cheat Engine and the gateway, then replace this entire folder and `CheatEngine.Mcp.Gateway.exe` from the same release.
Do not mix individual DLLs from different releases.

Use the plugin only with software you own or are authorized to inspect or modify.
The `LICENSE`, `THIRD-PARTY-NOTICES.md`, and `licenses/` directory in this folder describe the redistributed components.
