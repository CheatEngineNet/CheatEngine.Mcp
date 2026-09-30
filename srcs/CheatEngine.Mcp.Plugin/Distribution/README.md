# CheatEngine.Mcp plugin

This distribution contains one Cheat Engine plugin DLL, `CheatEngine.Mcp.dll`, and a separate gateway executable.
The plugin embeds its managed dependencies and native Lua bridge using Costura/Fody; no SDK or Core DLLs, plugin
dependency manifest, or plugin runtime configuration file need to be installed beside it.
Costura extracts the native bridge to its per-user temporary cache when loading it.

## Requirements

Use Cheat Engine 7.7 x64 on Windows x64.
Ensure the x64 .NET 10 installation supplies `Microsoft.NETCore.App`, `Microsoft.AspNetCore.App`, and
`Microsoft.WindowsDesktop.App`.
The plugin uses the .NET and ASP.NET Core frameworks; Cheat Engine's managed host also needs the Windows Desktop
framework.
Cheat Engine's `ce.runtimeconfig.json` must select .NET 10 and include `Microsoft.NETCore.App`,
`Microsoft.AspNetCore.App`, and `Microsoft.WindowsDesktop.App` in `runtimeOptions.frameworks`.
Back up that file before changing it, and close Cheat Engine first.

## Enable

In Cheat Engine, open **Edit > Settings > Plugins**, choose **Add new**, and select `CheatEngine.Mcp.dll` in this
folder.
Enable its checkbox and close the dialog.
The plugin then starts a local authenticated MCP backend and publishes its instance for the gateway.
It does not attach to a target process on enable.

The AI client must start `CheatEngine.Mcp.Gateway.exe` from this distribution as a local stdio MCP server.
Use `instance_list` to find the current `instanceId`, then pass that ID to each routed tool call.
Keep the gateway and Cheat Engine under the same Windows user.

## Configuration and updates

Defaults are built in; no configuration file is needed for the default setup.
Put personal settings in `%APPDATA%\CheatEngine.Mcp\appsettings.json` or set `MCP_DATA_DIRECTORY` to an absolute data
directory.
Host-file writes require `Mcp:Files:AllowedRoots`; table load and save require `CheatEngineClient:AllowedTableRoots`.
File reads still require an absolute local path accepted by the path policy, which refuses the data and instance
directories.
The `Mcp:EnableUnsafeLua`, `Mcp:EnableAutoAssembler`, `Mcp:EnableTargetCodeExecution` and `Mcp:EnableKernelAccess`
switches are on by default; set one to `false` to refuse its tools, and read their state in the `gates` of
`runtime_get_info`.
Disable and re-enable the plugin after changing its settings.
Logs are written to the data directory as `CheatEngine.Mcp.<CE PID>.log`.

To update, disable the plugin, close Cheat Engine and the gateway, then replace `CheatEngine.Mcp.dll` and
`CheatEngine.Mcp.Gateway.exe` from the same release. Remove an old folder-based plugin entry before adding this DLL.
Keep personal settings in the user data directory.

Use the plugin only with software you own or are authorized to inspect or modify.
The `LICENSE` and `THIRD-PARTY-NOTICES.md` files in this folder describe the redistributed components;
`THIRD-PARTY-NOTICES.md` reproduces every license text it cites.
