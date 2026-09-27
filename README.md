# CheatEngine.Mcp

A Windows x64 Cheat Engine plugin and MCP gateway built on [CheatEngine.Client](https://github.com/CheatEngineNet/CheatEngine.Client). One MCP connection can control multiple named Cheat Engine instances.

Each Cheat Engine process loads its own plugin, with its own Client activation and target state. Enabling publishes an authenticated loopback HTTP backend on an automatically assigned port. The stdio gateway discovers those backends and routes each tool call using an explicit `instanceId`. Disabling withdraws discovery, closes request admission, and lets Client release its resources without blocking Cheat Engine's main thread.

[Install and connect](#install-and-connect) · [First use](#first-use) · [Multiple instances](#multiple-cheat-engine-instances) · [Configuration](#configuration) · [Troubleshooting](#troubleshooting) · [Build from source](#build-from-source) · [Tests](#verification)

## Install and connect

### 1. Get the two deployment files

Download **CheatEngine.Mcp-2.0.0-beta.1-win-x64.zip** from the [2.0.0-beta.1 prerelease](https://github.com/CheatEngineNet/CheatEngine.Mcp/releases/tag/v2.0.0-beta.1) and extract it into a stable folder, such as `C:\Tools\CheatEngine.Mcp`. The ZIP includes both binaries, the separate AI skill, setup instructions, and licenses. This is a beta release for Windows x64 and Cheat Engine 7.7; see the release notes for validation scope and known limitations.

The prebuilt download does not require the .NET SDK or PowerShell. Continue with [Prepare Cheat Engine's .NET host](#2-prepare-cheat-engines-net-host) to install the required runtimes and configure CE.

To build the two files yourself, publish from this repository with .NET SDK **10.0.401** and PowerShell 7 installed:

```powershell
pwsh -NoProfile -File eng/Publish.ps1
# Or build the Debug distribution:
pwsh -NoProfile -File eng/Publish.ps1 -Configuration Debug
```

Release is the default. Copy these files from `artifacts/dist/release/` into a stable folder, such as `C:\Tools\CheatEngine.Mcp`:

| File | How it is used |
| --- | --- |
| `CheatEngine.Mcp.dll` | Load this plugin in Cheat Engine. It contains the runtime, Client, SDK, native bridge, and licenses. |
| `CheatEngine.Mcp.Gateway.exe` | Set this as the MCP server command in your AI client. It includes its own .NET runtime. |

The release also contains `skills/cheatengine-mcp/`, an optional separate AI skill. Install it in your AI client's skill directory as described below; it is not embedded in the DLL or needed beside the plugin.

Keep the DLL filename unchanged: CE uses its file and assembly identity to resolve the managed entry point. You can also use the files directly from `artifacts/dist/release/`, but close CE and stop the gateway before rebuilding or replacing files they have loaded.

### 2. Prepare Cheat Engine's .NET host

Use **Cheat Engine 7.7 x64**. The plugin runs inside CE and needs the **x64 .NET 10 runtime, ASP.NET Core 10 runtime, and Windows Desktop 10 runtime**. The .NET SDK is only needed to build from source. Check installed runtimes with:

```powershell
dotnet --list-runtimes
```

The output must include `Microsoft.NETCore.App 10.0.x`, `Microsoft.AspNetCore.App 10.0.x`, and `Microsoft.WindowsDesktop.App 10.0.x`. Missing components are available from the [.NET 10 downloads](https://dotnet.microsoft.com/en-us/download/dotnet/10.0) page; choose Windows x64.

Close all CE processes and back up `ce.runtimeconfig.json` beside the CE executable, commonly under `C:\Program Files\Cheat Engine`. Update that file to select .NET 10. Preserve unrelated settings; a typical configuration is:

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

Editing a file under Program Files may need an administrator editor. A file that still selects `net9.0` and `9.0.0` will not select .NET 10 merely because .NET 10 is installed. Restart CE after changing this host configuration. The standalone gateway does not use CE's runtime configuration.

### 3. Enable the DLL in Cheat Engine

1. Start the x64 Cheat Engine executable.
2. Open **Edit → Settings → Plugins**, choose **Add new**, and select your deployed `CheatEngine.Mcp.dll`.
3. Tick its checkbox to enable it and accept the settings dialog.

The backend starts automatically on enable. **MCP: Enabled** appears in CE's menu bar once the listener is running and the instance is published. Click it to see the instance name, listening address, and log location. This reports the CE backend's state; it does not mean an AI client is currently connected.

Disabling the plugin changes the menu to **MCP: Disabled**. A server startup failure shows **MCP: Start failed**. The status item remains available until CE closes and is reused on the next enable; its details do not depend on the disabled plugin. Bootstrap or configuration failures before the module starts can prevent the indicator from appearing.

You do not need a fixed port or a configuration file for the default setup. Each CE instance must have the plugin enabled. Leave CE open while using its tools.

On first load, the DLL extracts its verified dependencies into `%LOCALAPPDATA%\CheatEngine.Mcp\cache\<payload SHA256>`. No additional DLLs need to be copied beside it. The plugin does not open or attach to a target automatically.

### 4. Connect your AI client

Run the AI client and CE on the same Windows machine under the same user account. The MCP transport is **stdio**: the AI client launches `CheatEngine.Mcp.Gateway.exe` and communicates with that process. No MCP URL, port, API key, or OAuth login is needed for this setup. Double-clicking the EXE does not configure an AI client.

For **Codex**, run this in PowerShell, replacing the example path with your actual deployment path:

```powershell
codex mcp add cheatengine -- "C:\Tools\CheatEngine.Mcp\CheatEngine.Mcp.Gateway.exe"
codex mcp list
```

Alternatively, add this table to `%USERPROFILE%\.codex\config.toml` (or the config under your custom `CODEX_HOME`). Update an existing `cheatengine` table rather than adding a duplicate:

```toml
[mcp_servers.cheatengine]
command = "C:/Tools/CheatEngine.Mcp/CheatEngine.Mcp.Gateway.exe"
```

Restart your Codex session/app after changing the connection. `codex mcp list` confirms registration; the tool call in [First use](#first-use) verifies that the gateway can actually discover CE. See the [official Codex MCP documentation](https://developers.openai.com/codex/mcp) for client configuration options.

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

Use your client's equivalent stdio/command settings if it has a different configuration format. Keep one `cheatengine` connection even when you use several CE processes. A browser-only or remote client needs a Windows-local execution environment capable of launching this EXE.

## First use

Ask your AI client:

> Use the Cheat Engine MCP to list running instances, then show the selected target and plugin version for each. Do not change memory yet.

The agent should call `list_instances`, then pass a returned `instanceId` to `get_current_process` and `get_plugin_version`. A connected gateway can return an empty instance list when no CE backend is enabled. Discovery returns the **CE process ID**; `get_current_process` reports the separate **target process ID**. No selected target is normal before you attach one.

Select a process in CE's process picker, or tell the AI the exact target process name/PID and the intended CE instance so it can use `open_process`. Then give a concrete task, for example:

- "In instance game-a, show the address list and identify which numeric records are frozen."
- "For my test program in game-a, add a Dword record named Health at the address I provide, set it to 100, and freeze it."
- "Set game-a's attached test program to half speed, then restore its previous speed when we finish."

Use a real, verified address for memory tasks; the agent should not invent an address from an example. See the [address-list and speedhack guide](skills/cheatengine-mcp/references/address-list-and-speedhack.md) for exact tools and cleanup, and the [tool catalog](skills/cheatengine-mcp/references/tool-catalog.md) for all **142 gateway tools**. Lua execution, Auto Assembler patches, and the dedicated tools are enabled by default; no execution opt-in is needed.

### Optional AI skill

The [cheatengine-mcp skill](skills/cheatengine-mcp/SKILL.md) teaches instance selection and tool workflows. It ships separately from the plugin. Copy the complete `skills/cheatengine-mcp` folder from the release output (`artifacts/dist/release/skills/cheatengine-mcp/`) or this repository into your AI client's skill directory. The release excludes local-machine notes.

For Codex, the user-wide location is `%USERPROFILE%\.agents\skills\cheatengine-mcp\SKILL.md`; a repository-scoped installation goes under `.agents/skills/cheatengine-mcp/`. Keep the accompanying `references/` and `agents/` folders. The skill supplements the MCP connection; installing it alone does not register the gateway. See the [official skill locations](https://developers.openai.com/codex/skills).

## Multiple Cheat Engine instances

Open several CE processes and enable the plugin in each. Keep `Mcp:Port` at its default `0` so each backend receives a free port. One gateway discovers all of them; each tool call identifies its destination with `instanceId`.

Default names contain the CE process ID. For friendly labels, launch each CE from a separate PowerShell window, changing the path for your installation:

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

These environment values apply to processes launched from those shells; they do not rename an already running CE. Attach each instance to its intended target and ask the AI to refresh `list_instances`. A display name can repeat, so route with the returned ID. Restarting CE or disabling/re-enabling the plugin creates a new ID. Two instances attached to the same target can still change that shared target's memory.

## Configuration

Defaults work without creating a settings file. For changes, create or edit `%APPDATA%\CheatEngine.Mcp\appsettings.json`, or place an optional `appsettings.json` beside your original plugin DLL. Disable and re-enable the plugin after editing settings. For instance-specific settings, set an absolute `MCP_DATA_DIRECTORY` in that CE process's launch environment.

Configuration is read once per enable, in this order:

1. Bundled defaults.
2. Optional `appsettings.json` beside the original plugin wrapper DLL.
3. `%APPDATA%/CheatEngine.Mcp/appsettings.json`.
4. `MCP_HOST`, `MCP_PORT`, `MCP_INSTANCE_NAME`, and `MCP_INSTANCE_DIRECTORY` environment overrides.

```json
{
  "Mcp": {
    "Host": "127.0.0.1",
    "Port": 0,
    "InstanceName": "game-a",
    "ServerName": "CheatEngine.Mcp",
    "EnableUnsafeLua": true,
    "EnableAutoAssembler": true
  },
  "CheatEngineClient": {
    "AllowedTableRoots": []
  }
}
```

`EnableUnsafeLua` and `EnableAutoAssembler` default to `true`. You can explicitly set either to `false` to disable that capability. Existing settings files with `false` values override the new defaults; remove those overrides or set them to `true`, then disable and re-enable the plugin.

`Port: 0` allocates an available port for each plugin. The host must be `127.0.0.1`. A fixed nonzero port is optional but must be unique across running instances. `InstanceName` is a display label; by default it contains the CE process ID. For different labels with one shared plugin folder, set `MCP_INSTANCE_NAME` separately in each CE process's launch environment, or use separate `MCP_DATA_DIRECTORY` settings directories. Restart the plugin to apply settings.

Discovery records live in `%LOCALAPPDATA%/CheatEngine.Mcp/instances`. They contain per-activation access tokens used by the gateway; do not share those files. An absolute `MCP_INSTANCE_DIRECTORY` override must agree between plugins and gateway; the gateway also accepts `--instance-directory <path>`. Both run as the same Windows user. Authentication isolates backend calls from unauthenticated HTTP clients; it is not a boundary against other applications running as that user.

Call `list_instances`, choose the intended name/CE PID, and pass its exact `instanceId` to every CE tool. Labels may repeat; IDs are unambiguous and change on every plugin activation. There is no shared selected-instance state, automatic fallback, or automatic retry. A stopped instance fails its calls without redirecting them to another CE. Independent instances have independent CE state, but attaching both to the same target still allows both to change that target.

Each CE process logs to `%APPDATA%/CheatEngine.Mcp/CheatEngine.Mcp.<pid>.log` so instances do not compete for one file. Client lifecycle and HTTP logging share that process's isolated NLog factory whose lifetime covers asynchronous shutdown.

`MCP_DATA_DIRECTORY` can select an absolute directory for user settings and logs. `MCP_BUNDLE_CACHE_DIRECTORY` selects the absolute extracted-payload cache root. Keep settings editable at the original plugin path or user-data path; treat an extracted cache payload as read-only. The automated live runner uses private data and cache directories inside its test run.

## Troubleshooting

| Symptom | What to check |
| --- | --- |
| CE refuses to load the plugin | Use CE x64, keep the filename `CheatEngine.Mcp.dll`, check all three x64 .NET 10 runtimes, and check CE's own `ce.runtimeconfig.json`. Restart CE after changing it. |
| The gateway EXE waits in a console | It is a stdio MCP server. Configure it as the AI client's command; the client starts it and sends protocol requests. |
| `list_instances` returns an empty list | Enable the plugin in an open CE process. Run CE and the gateway as the same Windows user and check that any `MCP_INSTANCE_DIRECTORY` overrides agree. |
| One CE works but another cannot start its backend | Leave `Port` at `0`, or give each instance a unique fixed port. Check that the plugin is enabled in both. |
| An instance becomes unavailable | Call `list_instances` again. Restart or re-enable creates a new ID; do not reuse old instance or resource IDs. |
| A tool reports no target | Select the intended target in CE or use `open_process` with its exact PID/name and the chosen CE instance ID. |
| Lua execution or Auto Assembler is refused | Both are enabled by default. Check plugin-adjacent and user settings for explicit `false` overrides, then reload the plugin after changing them. |
| A table file is refused | Check `CheatEngineClient:AllowedTableRoots` and use an absolute table path. |
| Cache integrity check fails | Close all CE processes using that version, remove only the affected hash directory under the cache root, restart CE, and enable the plugin. Never edit files inside a cache version. |

Plugin logs are `%APPDATA%\CheatEngine.Mcp\CheatEngine.Mcp.<CE PID>.log`, or beneath `MCP_DATA_DIRECTORY` when set. A load failure before Client starts may occur before that log exists. Check gateway startup errors in the AI client's MCP diagnostics. Discovery records contain authentication tokens; do not paste their contents into reports. More diagnostic detail is in the [connection troubleshooting guide](skills/cheatengine-mcp/references/connection-troubleshooting.md).

### Updating or removing the installation

To update, finish target cleanup, close CE, stop the MCP connection in your AI client, and replace both deployment files together. Restart CE, enable the plugin, reconnect the client, and refresh `list_instances`. A new payload uses a new cache directory automatically. Keep your settings outside the cache so updates preserve them.

To remove it, undo task-owned freezes, speed changes, and debugger state first. Disable/remove the plugin in CE, close CE, and remove the MCP server configuration from your AI client. You can then delete the two deployed files. Settings, logs, and cached versions remain in the documented user directories unless you choose to remove them. Plugin disable alone does not undo every CE-owned change.

## Tools and ownership

The [catalog](skills/cheatengine-mcp/references/tool-catalog.md) lists **142 gateway tools**: `list_instances` plus **141 CE tools**, each with a required `instanceId`. Use live MCP schemas for parameter types and defaults. The [address-list and speedhack guide](skills/cheatengine-mcp/references/address-list-and-speedhack.md) covers adding records, freezing, unfreezing, and restoring speed.

The [scanning and debugging guide](skills/cheatengine-mcp/references/scanning-and-debugging.md) covers unknown-initial/changed/increased/range value scans, pointer maps and rescans, write/access collectors, break-and-trace, and register editing. Value scans use the same tools for both modes: omit `scannerName` (or use `main`) for the visible CE scan tab, or choose another name for an independent Client session. Main scans update CE's native controls and result list; use `get_memory_scan_status` to wait for completion and `get_memory_scan_results` to read either MCP-started or manually started UI scans. `list_memory_scanners` lists main and the independent sessions. The UI adapter uses fixed Lua through Client because Client 1.0 has no UI-scan API. Independent scans and pointer memory access use typed Client APIs; debugger callbacks use fixed CE Lua operations. Pointer maps are bounded in-memory MCP snapshots, not CE's native pointer-map files. Debugger captures/traces have explicit stop, finite lifetime, and a 30-second result-retention window after completion/expiry.

- Start with `list_instances`, then `get_runtime_info`, `get_plugin_version`, and `get_current_process` on the chosen instance.
- Process selection, typed memory, pointers, AOB scans, inspection, and address-table tools use Client contracts.
- Independent value scans, allocations, assembly, and Auto Assembler retain Client experimental/capability checks. An API's presence does not establish that the current host supports it.
- Arbitrary `execute_lua` and Client Auto Assembler patches require their respective configuration flags. These flags are not a sandbox: dedicated tools can write memory, launch/inject code, control the debugger, access files, and alter host state. Table load/save uses configured allowed roots.
- Independent named scans, allocations, registered symbols, and patches belong to one enable epoch. The `main` scanner belongs to CE: it survives plugin disable, follows the visible CE scan tab, and is never destroyed by MCP. `reset_memory_scan` on main explicitly clears its visible results through CE's New Scan action. Wait for or cancel an active UI scan before switching targets. Before switching processes, use `release_target_resources`; it releases in reverse creation order and stops on incomplete cleanup. A repeated request for the selected PID preserves resources.
- Failed releases report recovery details and keep retryable handles. If Cheat Engine changes targets outside MCP, inspect cleanup outcomes and perform manual recovery when reported. Never reuse identifiers after disable/re-enable.

The Lua adapter encodes arguments as data, runs a protected call inside the Client activation/main-thread boundary, and copies bounded results. It returns `{ success, result }`; direct Lua calls preserve multiple return values as arrays, including null slots. A Lua failure includes `hostEffect`: an operation that started may have partially changed the host. It is unsafe to blindly retry a failed mutation. Requests are limited to 8 MiB; copied Lua results are limited to 65,536 items, depth 16, and 4 MiB of strings. Individual tools apply tighter limits.

Client leases are activation-owned. Lua-created global structures, address-list changes, comments, breakpoints, and debugger/speedhack state are Cheat Engine-owned state. They can persist after plugin disable; use each explicit delete/remove/resume tool or Cheat Engine itself to recover. Injected libraries have host/target lifetimes. DBVM watch captures are timed (at most five seconds) and disable their watch before returning; a failed cleanup reports the watch ID for manual recovery. Inspection bounds limit returned data; some CE APIs internally enumerate a larger collection first. Long native calls run synchronously and cannot be interrupted by an HTTP cancellation.

## Migration from CeMCP 1.x

This is a breaking remake. The old CESDK submodule, source compilation, Costura single-DLL packaging, static tools, WPF UI, and old project/test paths have been removed. The current distributable plugin DLL is a verified extracting wrapper, not Costura packaging.

The tool surface is rebuilt around Client high-level APIs for supported operations. Additional debugger, DBVM, injection, process-control, Structure Dissect, RTTI, protection, file-memory, and code-analysis tools use fixed Lua operations through `ICheatEngineClient.Lua`. Bindings follow the installed Cheat Engine `celua.txt`; unavailable host APIs return errors. The complete public surface and each tool description are in the catalog.

Tool parameters and responses have changed where Client ownership requires it: allocations have names, scans are bounded sessions, patches return lease IDs, record content and activation are separate tools, and `execute_lua` reports execution status without serializing Lua return values.

## Build from source

Install .NET SDK **10.0.401** and PowerShell 7, then run from this repository:

```powershell
dotnet restore CheatEngine.Mcp.slnx --locked-mode
dotnet build CheatEngine.Mcp.slnx --no-restore
dotnet test --solution CheatEngine.Mcp.slnx --no-restore --no-build --filter-not-trait Category=LiveQualification --filter-not-trait Category=NativeLua --fail-skips on
pwsh -NoProfile -File eng/Publish.ps1 -Configuration Release
```

The distributable files are under `artifacts/dist/release/`. A normal `dotnet build` prepares development outputs; `eng/Publish.ps1` also creates the self-contained gateway and stages the two deployment files plus the separate `skills/cheatengine-mcp/` folder. Publishing uses the checked-in NuGet lock files. CI publishes the same layout for Debug and Release.

The plugin references **[CheatEngine.Client 1.0.0](https://www.nuget.org/packages/CheatEngine.Client/1.0.0)** through NuGet. Its package metadata identifies source revision `f88de3d843252c9139f08c71531a02f03c0516bb`. No submodule checkout is required. The plugin also directly references **CheatEngine.SDK 2.0.0**, as required by Client, for its generated entry point and native bridge.

Dependency versions are centralized in `Directory.Packages.props` and resolved in the checked-in NuGet lock files. Test dependencies follow the Client's xUnit v3/Microsoft.Testing.Platform profile. Existing remote Sonar project identifiers are retained; renaming a local project does not rename the hosted service.

Code follows Client conventions: C# 14, file-scoped namespaces, tabs, explicit types, centralized package versions, locked restore, and output under `artifacts/`. The plugin is a framework-dependent managed component packaged inside an extracting loader. The loader forwards to the SDK-generated entry point; Client owns activation and cleanup. This is not a Native AOT binary.

## Verification

Tests follow the Client and SDK setup: xUnit v3 on Microsoft.Testing.Platform, with the shared profile in `eng/Tests.props`. The portable suite uses deterministic Client doubles plus real loopback HTTP discovery/start/stop tests. It does not require Cheat Engine and does not prove native host behavior. CI excludes `Category=LiveQualification` and `Category=NativeLua`; selected tests fail rather than skip when required inputs are missing.

For an automated live run, close other Cheat Engine instances and run:

```powershell
$env:CHEATENGINE_MCP_LIVE_QUALIFICATION = 'I_AUTHORIZE_CE77_LIVE_PROBES_ON_A_DISPOSABLE_TARGET'
pwsh -NoProfile -File eng/Publish.ps1 -Configuration Release
dotnet test --project tests/CheatEngine.Mcp.Tests -c Release --filter-trait Category=LiveQualification --fail-skips on
Remove-Item Env:CHEATENGINE_MCP_LIVE_QUALIFICATION
```

`LiveQualification/` contains the C# runner and serial fixture. Like the Client, it requires the exact acknowledgement, refuses `CI=true`, and fails immediately with the command above when explicitly selected without authorization. No PowerShell wrapper, plugin installation, or manual enabling is required. Publish the matching configuration before the live test (`Debug` for a Debug test run); the regular solution build and the publish step are separate.

The runner creates two private copies of installed Windows x64 Cheat Engine 7.7+ under `%LOCALAPPDATA%/CheatEngine.Mcp.LiveQualification/runs/<run>`, selects .NET 10 only in those copies, and loads the plugin automatically. Each gets its own disposable memory target, label, and automatic loopback port; one stdio gateway routes calls through a private discovery directory. Existing CE processes are refused, never closed. The installed host is not modified. Optional `CHEATENGINE_MCP_LIVE_QUALIFICATION_CE_DIRECTORY` selects another installation; `CHEATENGINE_MCP_LIVE_QUALIFICATION_RUN_ROOT` selects another absolute run directory outside the repository and apart from the installation.

The private copy disables existing autorun scripts except the stock `celib.lua`, `monoscript.lua`, and `SpeedhackV3.lua` needed for speedhack. These must match the reviewed hashes from CE 7.7.1.10828; missing or changed scripts fail before launching CE. Review newer stock scripts before updating those hashes in `LiveSandboxSession.cs`.

Local test helpers adapted from Client's MIT-licensed test suite preserve CE settings, restore and verify them after shutdown, and retain a recovery marker if restoration fails. Their provenance is recorded in [the infrastructure notice](tests/CheatEngine.Mcp.Tests/LiveQualification/Infrastructure/NOTICE.md); they do not require a Client source checkout. A later run restores a leftover backup and stops so the recovery is visible. Plugin settings and logs stay in the run folder. Reports contain the exact host version/hash, plugin hash, individual checks, and cleanup results. Local reports and backups stay outside the checkout.

The live scenario checks all tool names, loaded plugin paths, runtime evidence, target attachment, typed memory reads/writes, address-list add/update/delete, actual freeze/unfreeze behavior, and speedhack setting/readback. It verifies that changing A leaves B's separate target and table unchanged, then stops B and verifies A remains available while B calls fail. Speedhack readback does not measure timing accuracy. This smoke test is separate from the Client's qualification against its pinned host build; it does not qualify every MCP tool, debugger backend, or DBVM. Ordinary CI excludes live tests.

Check formatting with:

```powershell
dotnet format style CheatEngine.Mcp.slnx --no-restore --severity warn --verify-no-changes
dotnet format whitespace CheatEngine.Mcp.slnx --no-restore --verify-no-changes
```

Optional standalone Lua bridge tests use a Lua 5.3 DLL without loading Cheat Engine:

```powershell
$env:CHEATENGINE_MCP_LUA53_PATH = "C:\Program Files\Cheat Engine\lua53-64.dll"
dotnet test --solution CheatEngine.Mcp.slnx --filter-trait Category=NativeLua --fail-skips on
```

These validate the protected adapter and copied-result contracts, not CE functions. Deployable build artifacts are the two files under `artifacts/dist/<configuration>/`.

## Attribution

This remake builds on the original [ce-mcp contributors](https://github.com/ShadowNineX/ce-mcp/graphs/contributors), [CheatEngine.Client](https://github.com/CheatEngineNet/CheatEngine.Client), [CheatEngine.SDK](https://github.com/CheatEngineNet/CheatEngine.SDK), and Cheat Engine. Existing license files remain authoritative.
