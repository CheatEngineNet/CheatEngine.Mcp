# Getting started

This guide takes you from a fresh download to a first AI session that controls Cheat Engine through CheatEngine.Mcp.
It covers one Cheat Engine (CE) instance; [multi-instance.md](multi-instance.md) covers several.

> [!WARNING]
> Use CheatEngine.Mcp only on software you own or are authorized to modify: your own programs, offline or single-player games, the CE tutorial, disposable test targets.
> Anti-cheat systems can detect Cheat Engine and ban accounts, and a mistake can crash the target or, with kernel tools, the whole machine.
> Every capability gate is enabled by default and the gates are not a sandbox.
> Read [security.md](security.md) before you start.

## What you need

| Requirement | Details |
| --- | --- |
| Windows | Windows 10 or 11, x64. |
| Cheat Engine | Cheat Engine 7.7 x64. The qualified host is `cheatengine-x86_64.exe` version 7.7.0.10621, the build that CheatEngine.Client 1.0.0 is qualified against. Other 7.7 builds may work but are not qualified. |
| .NET runtimes (x64) | `Microsoft.NETCore.App` 10.0.x, `Microsoft.AspNetCore.App` 10.0.x and `Microsoft.WindowsDesktop.App` 10.0.x. The plugin runs inside CE and uses all three. |
| CE runtime configuration | CE's `ce.runtimeconfig.json` must select .NET 10 and list `Microsoft.AspNetCore.App` (step 3). |
| AI client | Any MCP client that can launch a local stdio server on the same machine, under the same Windows user as CE. See [clients.md](clients.md). |

The Windows x64 Native AOT gateway executable is self-contained and needs no .NET runtime.
The .NET SDK is needed only to build from source.

## 1. Get the files

Use a release build, or publish one from a checkout of this repository:

```powershell
pwsh -NoProfile -File eng/Publish.ps1
```

Publishing needs the .NET SDK 10.0.401, PowerShell 7 and, for the Native AOT gateway, the Visual Studio C++ build tools.
The output is `artifacts/dist/release/` (`-Configuration Debug` writes `artifacts/dist/debug/`):

| Item | Purpose |
| --- | --- |
| `CheatEngine.Mcp/` | The plugin folder. CE loads `CheatEngine.Mcp.Plugin.dll` from it. It also holds the plugin's `deps.json`, `runtimeconfig.json`, dependencies, native Lua bridge, shipped `appsettings.json`, README and licenses. |
| `CheatEngine.Mcp.Gateway.exe` | The stdio MCP server that your AI client starts. |
| `skills/cheatengine-mcp/` | The optional operator skill for your AI client. It is not part of the plugin. |
| `LICENSE`, `THIRD-PARTY-NOTICES.md`, `licenses/` | License texts for the gateway and its bundled components. |

Copy these into a stable folder, for example `C:\Tools\CheatEngine.Mcp\`.
This guide uses that folder in every example; replace it with yours.

Keep the plugin folder intact and do not rename `CheatEngine.Mcp.Plugin.dll`.
CE loads the plugin through its generated entry point, with the `deps.json`, `runtimeconfig.json` and every dependency beside it.
Never copy single DLLs between builds.

## 2. Install the .NET runtimes

Check what is installed:

```powershell
dotnet --list-runtimes
```

The output must include these three lines, with any 10.0 patch version:

```text
Microsoft.AspNetCore.App 10.0.x [...]
Microsoft.NETCore.App 10.0.x [...]
Microsoft.WindowsDesktop.App 10.0.x [...]
```

Install any missing runtime from the [.NET 10 download page](https://dotnet.microsoft.com/en-us/download/dotnet/10.0): choose the Windows x64 installers for the .NET Runtime, the ASP.NET Core Runtime and the .NET Desktop Runtime.

## 3. Let Cheat Engine load ASP.NET Core

Cheat Engine starts one .NET runtime for all its managed plugins, using `ce.runtimeconfig.json` beside its executable.
A process can host only one .NET runtime, and a plugin that needs a framework the runtime was not started with cannot load ([Microsoft: native hosting limitations](https://learn.microsoft.com/dotnet/core/tutorials/netcore-hosting#limitations)).
The plugin's backend uses ASP.NET Core, so the file must select .NET 10 and list `Microsoft.AspNetCore.App` next to the other two frameworks.
Installing .NET 10 alone does not change a file that still selects an older version.

This is a local modification of your CE installation, not an installer default.
The CheatEngine.Client qualification host uses the same kind of locally modified file.

1. Close every Cheat Engine process.
2. Find the file beside `cheatengine-x86_64.exe`, by default `C:\Program Files\Cheat Engine\ce.runtimeconfig.json`.
3. Back it up:

   ```powershell
   Copy-Item 'C:\Program Files\Cheat Engine\ce.runtimeconfig.json' "$env:USERPROFILE\ce.runtimeconfig.json.bak"
   ```

4. Open it in an editor running as administrator, because the file is under Program Files:

   ```powershell
   Start-Process notepad.exe -ArgumentList '"C:\Program Files\Cheat Engine\ce.runtimeconfig.json"' -Verb RunAs
   ```

5. Make `runtimeOptions` select `net10.0` and list the three frameworks at version `10.0.0`.
   Keep any other properties the file already has, such as `configProperties`.
   A typical result:

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

   `LatestMinor` makes the host pick the newest installed 10.x runtime with its latest patch; Microsoft describes it as intended for component hosting ([roll-forward values](https://learn.microsoft.com/dotnet/core/versions/selection#framework-dependent-apps-roll-forward)).
6. Save the file and start CE again.

A reinstall or update of Cheat Engine can replace this file; check it again after one.
The gateway does not use this file.

## 4. Install and enable the plugin

1. Start `cheatengine-x86_64.exe` directly.
   The qualification covers this executable, not the `Cheat Engine.exe` launcher or the `cheatengine-x86_64-SSE4-AVX2.exe` variant.
2. Open **Edit > Settings > Plugins** and choose **Add new**.
3. Select `C:\Tools\CheatEngine.Mcp\CheatEngine.Mcp\CheatEngine.Mcp.Plugin.dll`.
4. Tick the plugin's checkbox to enable it and close the settings dialog with **OK**.

Enabling starts the backend immediately.
It listens on `127.0.0.1` on a free port, publishes a discovery record for the gateway, and does not attach to any process.
No configuration file is needed for the default setup; see [configuration.md](configuration.md) for the settings.

### The status menu item

The plugin adds one item to CE's main menu bar that shows the backend state:

| Menu item | Meaning |
| --- | --- |
| **MCP: Starting** | Shown briefly while the backend starts. |
| **MCP: Enabled** | The backend is listening and published. It does not mean an AI client is connected. |
| **MCP: Disabled** | The plugin was disabled; this CE instance accepts no MCP requests. |
| **MCP: Start failed** | The backend could not start. The log names the error. |

Click the item to see the instance name, the listening address and the log file path.
The item stays until CE closes and is reused when you enable the plugin again.
If no item appears at all, the plugin failed before it started; see [troubleshooting.md](troubleshooting.md#the-plugin-does-not-load).

## 5. Register the gateway with your AI client

Your AI client starts `CheatEngine.Mcp.Gateway.exe` and talks to it over stdio.
There is no URL, port, API key or login to configure, and double-clicking the executable does nothing useful.
Register it once under the server key `cheatengine`, for example in Claude Code:

```powershell
claude mcp add --transport stdio --scope user cheatengine -- "C:\Tools\CheatEngine.Mcp\CheatEngine.Mcp.Gateway.exe"
```

[clients.md](clients.md) has the exact configuration for Claude Code, Claude Desktop, VS Code, Codex and Cursor, and the notes on large tool catalogs.
One registration serves every CE instance on the machine.

## 6. First session

Start your AI client with CE open and the plugin enabled.
Choose the target before you ask for changes: the CE tutorial (`Tutorial-x86_64.exe` in the CE folder) is a good first target.

Ask, for example:

> Use the Cheat Engine MCP. List the Cheat Engine instances, show the runtime information of the instance I use, then find the process named Tutorial-x86_64 and attach to it. Do not change memory.

The agent should call these tools in order, passing the returned `instanceId` to every call after the first:

1. `instance_list`: lists the enabled CE instances with their `instanceId`, display name and CE process ID.
   An empty list means no enabled backend was found; see [troubleshooting.md](troubleshooting.md#instance_list-is-empty).
2. `runtime_get_info`: reports the CE and plugin versions, non-sensitive loaded file names and the state of the four capability gates.
3. `process_list`: finds the target, for example with `nameContains`.
4. `process_attach`: attaches CE to the chosen target by `processId`.

`process_get_current` then confirms the attached target.
The process ID from `instance_list` belongs to Cheat Engine itself, not to the target.

Clients that support MCP prompts also offer the `attach_and_orient` prompt, which runs the same steps with a safety check.
The gateway also serves guides as resources such as `cheatengine://docs/workflows`, even when no CE instance is running.

Continue with a concrete task, and give real addresses or values: the agent should never invent an address.
The workflows in the [skill references](../skills/cheatengine-mcp/references/workflows.md) show what a session can do.

## 7. Optional: install the skill

The `cheatengine-mcp` skill teaches the agent instance selection, the workflows and cleanup duties.
It complements the MCP connection and does not register the gateway.
Copy the whole `skills/cheatengine-mcp/` folder, with its `references/` and `agents/` subfolders, into your client's skill directory:

| Client | User-wide location |
| --- | --- |
| Claude Code | `%USERPROFILE%\.claude\skills\cheatengine-mcp\` |
| Codex | `%USERPROFILE%\.agents\skills\cheatengine-mcp\` |

[clients.md](clients.md#install-the-skill) lists the project-level locations.

## Update or remove

Before an update or removal, finish the session's cleanup: undo freezes, patches, speedhack and debugger state that the session created.
Disabling the plugin does not undo changes that Cheat Engine owns, such as address-list records, freezes and breakpoints.

To update:

1. Close CE and stop the MCP server in your AI client (or close the client).
2. Replace the whole plugin folder and the gateway executable with files from the same build.
3. Start CE, check that the plugin is enabled, restart the client and call `instance_list` again.

Your settings in `%APPDATA%\CheatEngine.Mcp\appsettings.json` survive updates; the plugin folder's own `appsettings.json` holds the shipped defaults and is replaced.

To remove:

1. Disable and remove the plugin in **Edit > Settings > Plugins**, then close CE.
2. Remove the `cheatengine` server from your AI client.
3. Delete the plugin folder and the gateway executable.
4. Optionally delete `%APPDATA%\CheatEngine.Mcp` (settings and logs) and restore your `ce.runtimeconfig.json` backup.

## Next steps

- [clients.md](clients.md): per-client configuration and limits.
- [multi-instance.md](multi-instance.md): several CE processes behind one gateway.
- [configuration.md](configuration.md): settings, environment variables and capability gates.
- [security.md](security.md): threat model and responsible use.
- [troubleshooting.md](troubleshooting.md): what to check when something fails.
