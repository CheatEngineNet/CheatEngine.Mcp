# CheatEngine.Mcp

CheatEngine.Mcp is a Windows integration that connects MCP-compatible AI clients to **Cheat Engine 7.7 x64**.
It provides structured tools for inspecting processes, analyzing memory and code, and managing Cheat Engine workflows.
A plugin runs inside Cheat Engine; one standalone stdio gateway connects your client to one or more explicitly selected instances.

> Download a matching plugin and gateway from [Releases](https://github.com/CheatEngineNet/CheatEngine.Mcp/releases) and follow that package's instructions.
> This README describes the current source; features may differ from the latest published build.

[Capabilities](#capabilities) · [Installation](#install-and-connect) · [First use](#first-use) · [Configuration](#configuration) · [Troubleshooting](#troubleshooting) · [Development](#build-from-source)

## Capabilities

The current source exposes **206 Cheat Engine tools**, plus the gateway's `instance_list` tool.

| Area | Supported workflows |
| --- | --- |
| Processes and instances | Discover multiple hosts, page a stable process snapshot, select a target, and inspect modules, threads, and regions. |
| Memory | Read and write typed values and batches; sample values over time; compare, copy, hash, and snapshot bounded ranges. |
| Search | Run value scans and AOB searches; discover UTF-8 and UTF-16LE strings with filtering and pagination. |
| Pointers | Follow and validate chains with per-hop evidence; scan, rescan, and manage pointer maps. |
| Code analysis | Disassemble and decode instructions; inspect references, function graphs, and explicitly addressed vtable slots. |
| Structures and records | Define, compare, import, and export layouts; manage address-list records, freezes, and Cheat Engine tables. |
| Symbols and managed runtimes | Resolve and manage symbols; inspect supported Mono and .NET targets. |
| Investigation | Preserve versioned notes with observations, confidence, and module-relative bindings; search imported offline IL2CPP metadata. |
| Debugging and execution | Use supported debugger operations, patches, assembly, Lua, and compiled-code workflows subject to capability settings. |
| Session management | Inspect jobs and retained resources, monitor progress, and release or restore owned state. |

The [tool map](srcs/CheatEngine.Mcp.Resources/Knowledge/Documents/tool-map.md) lists domains and capability requirements.
The live MCP schema defines the exact arguments and results for the connected build.
Runtime-specific tools require the corresponding Cheat Engine and target support; see [Verification](#verification) for the distinction between automated coverage and live qualification.

Feature contracts: [persistent notes](docs/investigation-notes.md), [offline IL2CPP](docs/offline-il2cpp.md),
[structure interchange](docs/structure-interchange.md), and [process pagination](docs/process-pagination.md).

## How it works

```text
AI client --stdio--> CheatEngine.Mcp.Gateway.exe
                         |
                         +-- authenticated loopback HTTP --> plugin in Cheat Engine A
                         +-- authenticated loopback HTTP --> plugin in Cheat Engine B
```

Each enabled plugin starts a backend on `127.0.0.1` with an automatically assigned port and a fresh access token.
It publishes a per-activation discovery record for the current Windows user.
The gateway verifies the backend's identity and routes calls by the explicit `instanceId` returned by `instance_list`.
It does not start Cheat Engine, select a process, or choose an instance for you.
Documents and prompts remain available through the gateway even when Cheat Engine is closed.

The default setup discovers instances automatically: no fixed port, backend URL, or manually configured access token is needed.
Run the gateway and Cheat Engine under the same Windows user account.

## Install and connect

### 1. Get the deployment files

Each packaged release uses `CheatEngine.Mcp-<version>-win-x64.zip`.
The ZIP includes **`CheatEngine.Mcp.dll`**, the gateway executable, installation instructions, and licenses.
Each release has one complete Windows x64 ZIP and `SHA256SUMS.txt`; separate plugin, skill, DLL, or EXE downloads are unnecessary.
When upgrading, disable the plugin, close Cheat Engine and the gateway, and remove the old plugin entry. Replace the plugin DLL and gateway executable with the matching files from one build; do not mix versions. Then add `CheatEngine.Mcp.dll` as described below. An old folder deployment is no longer needed.

To build the same layout from source, use the .NET SDK pinned in [`global.json`](global.json), PowerShell 7, and the Windows C++ build tools required by Native AOT:

```powershell
pwsh -NoProfile -File eng/Publish.ps1 -Configuration Release
```

The result is in `artifacts/dist/release/`:

| Item | Purpose |
| --- | --- |
| `CheatEngine.Mcp.dll` | Cheat Engine plugin, including its managed dependencies and native Lua bridge. |
| `CheatEngine.Mcp.Gateway.exe` | Standalone Windows x64 MCP server launched by your AI client. |
| `README.md` | Installation and configuration instructions. |
| `LICENSE` and `THIRD-PARTY-NOTICES.md` | License and dependency notices for the distribution. |

Copy these items together to a stable location, for example `C:\Tools\CheatEngine.Mcp`.
Keep the DLL name unchanged. No SDK DLL, Core DLL, plugin `.deps.json`, or plugin `.runtimeconfig.json` is installed beside it.
The plugin still needs the installed .NET 10 frameworks described below. Costura extracts the native Lua bridge to its per-user temporary cache when loading it.
A normal solution build creates development outputs; `eng/Publish.ps1` creates the deployable layout and smoke-tests the gateway.
See [Build from source](#build-from-source) for the full development commands.

### 2. Prepare Cheat Engine's .NET host

Use **Cheat Engine 7.7 x64**.
The plugin runs inside Cheat Engine and requires the **x64 .NET 10, ASP.NET Core 10, and Windows Desktop 10 runtimes**.
Check them with `dotnet --list-runtimes`; the output must include `Microsoft.NETCore.App`, `Microsoft.AspNetCore.App`, and `Microsoft.WindowsDesktop.App` at version `10.0.x`.
The gateway executable is self-contained and does not use Cheat Engine's runtime configuration.

Cheat Engine's `ce.runtimeconfig.json`, beside its executable, must select .NET 10 and all three frameworks.
Close Cheat Engine and back up that file before changing it.
Preserve unrelated settings; a typical `runtimeOptions` section is:

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

Restart Cheat Engine after changing this file.
Installing .NET 10 alone does not change a host configuration that selects another major version.

### 3. Enable the plugin

In Cheat Engine, open **Edit > Settings > Plugins**, add the deployed `CheatEngine.Mcp.dll`, and enable it.
The menu bar shows **MCP: Enabled** after the backend starts and its instance is published.
Click the indicator for its instance name, listening address, and log location.
Enabling the plugin does not attach Cheat Engine to a target process.

Enable the plugin separately in every Cheat Engine process you want to use.
Leave the port at its default `0` so each process gets a free port.

### 4. Connect an AI client

Register **one local stdio server** whose command is the absolute path to `CheatEngine.Mcp.Gateway.exe`.
For Codex, for example:

```powershell
codex mcp add cheatengine -- "C:\Tools\CheatEngine.Mcp\CheatEngine.Mcp.Gateway.exe"
codex mcp list
```

For a client that uses an `mcpServers` JSON object:

```json
{
  "mcpServers": {
    "cheatengine": {
      "command": "C:/Tools/CheatEngine.Mcp/CheatEngine.Mcp.Gateway.exe"
    }
  }
}
```

Use your client's equivalent command-based stdio setting if its format differs.
Restart the client session after changing MCP registration.
The client launches the gateway; double-clicking the executable does not configure a client.
A remote or browser-only client needs a Windows-local component capable of launching the gateway.

## First use

Ask the client to **list Cheat Engine instances and inspect the selected target without changing memory**.
The intended call sequence is:

1. `instance_list()` returns verified plugin instances with `instanceId`, display name, Cheat Engine process ID, and plugin version.
2. Choose the intended instance and pass its exact `instanceId` to `runtime_get_info` and `runtime_get_overview`.
3. Check whether Cheat Engine has a target process open.
   The instance's `processId` is Cheat Engine's PID; the target has its own PID.
4. Select a target in Cheat Engine or request a process attach through the tools exposed by the live MCP schema.
   Confirm the target before reading or changing it.

The gateway-local `instance_list` needs no `instanceId`; every routed Cheat Engine tool does.
There is no default or shared selected instance.
An empty list means the gateway is connected but has not found a verified, enabled plugin.
An ID changes when Cheat Engine restarts or the plugin is disabled and enabled again, so refresh `instance_list` before reusing one.

The server also offers the `attach_and_orient` guided prompt and the `cheatengine://docs/getting-started` resource.
The [tool map](srcs/CheatEngine.Mcp.Resources/Knowledge/Documents/tool-map.md) describes tool domains and capability requirements; the live MCP schema is authoritative for this build's exact names and arguments.

## Multiple Cheat Engine instances

Open more than one Cheat Engine process and enable the plugin in each.
One gateway discovers them all.
Display names may repeat, so select by the returned `instanceId` and Cheat Engine PID.

The default name includes the Cheat Engine PID.
To give each process a label, set `MCP_INSTANCE_NAME` **before launching that process**:

```powershell
# First PowerShell window
$env:MCP_INSTANCE_NAME = 'game-a'
& 'C:\Program Files\Cheat Engine\cheatengine-x86_64.exe'
```

```powershell
# Second PowerShell window
$env:MCP_INSTANCE_NAME = 'game-b'
& 'C:\Program Files\Cheat Engine\cheatengine-x86_64.exe'
```

These are display labels, not routing keys.
An instance that stops is never replaced automatically by another one, even when their labels match.
Keep `Mcp:Port` at `0`; a fixed port must be unique across running instances.
The [connection guide](srcs/CheatEngine.Mcp.Resources/Knowledge/Documents/connection-troubleshooting.md) explains discovery and stale IDs in more detail.

## Configuration

The defaults work without creating a file.
To change plugin settings, create `%APPDATA%\CheatEngine.Mcp\appsettings.json` and disable/re-enable the plugin.
The plugin reads, in increasing priority: built-in defaults, an optional `appsettings.json` beside the DLL, the user file, then the dedicated `MCP_*` environment overrides. No settings file is required for the default setup.
It reads settings once per activation.
Keep personal changes in the user file so updates preserve them.

A small optional user file looks like this:

```json
{
  "Mcp": {
    "EnableUnsafeLua": false,
    "Files": {
      "AllowedRoots": ["C:\\CheatEngine\\Files"]
    }
  },
  "CheatEngineClient": {
    "AllowedTableRoots": ["C:\\CheatEngine\\Tables"]
  }
}
```

The four capability switches `EnableUnsafeLua`, `EnableAutoAssembler`, `EnableTargetCodeExecution`, and `EnableKernelAccess` are `true` by default.
Set a switch to `false` to refuse its covered tools; `runtime_get_info.gates` reports the effective values.
The file and table allowed-root lists are empty by default, so host-file writes and table load/save need explicit local folders.
Capability switches refuse covered operations when disabled; they are not a sandbox for enabled operations.
See the [configuration reference](srcs/CheatEngine.Mcp.Resources/Knowledge/Documents/configuration.md) and [safety guide](srcs/CheatEngine.Mcp.Resources/Knowledge/Documents/safety.md) for every key and its effect.

| Override | Used by | When to set it |
| --- | --- | --- |
| `MCP_INSTANCE_NAME` | Plugin | Label one Cheat Engine process before launching it. |
| `MCP_DATA_DIRECTORY` | Plugin | Use a separate absolute directory for that process's user settings and logs. |
| `MCP_INSTANCE_DIRECTORY` | Plugin and gateway | Move the discovery registry; **both sides must use the same absolute directory**. |
| `MCP_HOST`, `MCP_PORT` | Plugin | Listener overrides; keep the loopback host and automatic port for normal use. |
| `MCP_GATEWAY_CALL_TIMEOUT_SECONDS` | Gateway | Set a routed-call timeout from 5 to 3600 seconds; default 45. |

The gateway also accepts `--instance-directory <absolute path>` and `--call-timeout-seconds <5..3600>`.
Arguments override its environment variables.
If you move the registry, set `MCP_INSTANCE_DIRECTORY` for both the Cheat Engine and AI-client processes, or pass the matching directory to the gateway.
Do not set it to an empty value.
The default registry is `%LOCALAPPDATA%\CheatEngine.Mcp\instances`; its records contain access tokens and should stay private to the current user.

## Tools, resources, and prompts

Tools return structured results, including operation-specific failures and partial outcomes.
Some operations change Cheat Engine or target state; consult each tool's annotations, capability requirements, result, and cleanup guidance.

The gateway serves project knowledge as `cheatengine://docs/{slug}` resources and guided prompts even without a running plugin.
Examples include [getting started](srcs/CheatEngine.Mcp.Resources/Knowledge/Documents/getting-started.md), [workflows](srcs/CheatEngine.Mcp.Resources/Knowledge/Documents/workflows.md), and [errors and recovery](srcs/CheatEngine.Mcp.Resources/Knowledge/Documents/errors-and-recovery.md).
With an instance selected, live resources expose read-only snapshots under `cheatengine://instances/{instanceId}/...`.
Clients with resource completion support can suggest recently verified instance IDs and instance-specific names.

Resources created by MCP, such as named scans, jobs, allocations, and patches, have an activation lifetime.
Cheat Engine-owned state can persist after plugin disable.
Before switching targets or ending a session, inspect `runtime_list_resources` and use the relevant release or restore tools.
The [session workflows](srcs/CheatEngine.Mcp.Resources/Knowledge/Documents/workflows.md) describe cleanup and recovery.

## Troubleshooting

| Symptom | Check |
| --- | --- |
| The client shows no CheatEngine.Mcp tools | Confirm the gateway path and stdio registration, then inspect the client's MCP startup log. The gateway writes diagnostics to stderr. |
| `instance_list` is empty | Enable the plugin in Cheat Engine, confirm **MCP: Enabled**, use the same Windows account, and check that both sides use the same instance directory. |
| Cheat Engine cannot load the plugin | Check Cheat Engine 7.7 x64, `CheatEngine.Mcp.dll`, all three x64 .NET 10 runtimes, and CE's `ce.runtimeconfig.json`. |
| One of several instances fails to start | Leave the port at `0`, or give each process a distinct fixed port. |
| A previously working ID is unavailable | Refresh `instance_list`; restart and re-enable create new IDs. Do not redirect a failed call to another instance. |
| A tool reports no target | Select the intended target in Cheat Engine or attach through the chosen instance's process tools. |
| Attaching after opening a file is refused | Select the process in Cheat Engine or restart Cheat Engine before attaching again. The refusal preserves the open file. |
| A setting prevents enable | Correct the named `Mcp` key or `MCP_*` variable, then re-enable the plugin. |
| A routed call times out | Inspect the instance and the target before repeating a mutation; its host effect may be unknown. |

Plugin logs are written per Cheat Engine PID to `%APPDATA%\CheatEngine.Mcp\CheatEngine.Mcp.<pid>.log`, or to `MCP_DATA_DIRECTORY` when set.
A load failure can occur before that log exists.
See [connection troubleshooting](srcs/CheatEngine.Mcp.Resources/Knowledge/Documents/connection-troubleshooting.md) for status details and recovery steps.
Its [support report checklist](srcs/CheatEngine.Mcp.Resources/Knowledge/Documents/connection-troubleshooting.md#support-report-checklist) lists the versions, error fields and redacted excerpts to include when reporting a problem.

### Updating or removing the installation

To update, finish task cleanup, close Cheat Engine, stop the client's MCP connection, and replace the plugin DLL and gateway together from one build.
Restart Cheat Engine, enable the plugin, reconnect the client, and refresh `instance_list`.
User settings survive because they are outside the distribution.
Keep the previous matching package and client registration as a rollback copy.
For a locked gateway, partial upgrade or rollback, follow the [recovery steps](srcs/CheatEngine.Mcp.Resources/Knowledge/Documents/connection-troubleshooting.md#several-instances-updates-and-removal) before resuming work.

To remove the integration, release or manually restore task-owned state, disable the plugin, remove the gateway registration from the AI client, and then delete the deployment files.
Disabling the plugin does not automatically undo every Cheat Engine-owned change.

## Build from source

The repository pins its required .NET SDK in [`global.json`](global.json).
PowerShell 7 and the Windows C++ build tools are needed for the Native AOT distribution.
Clone with `git clone --recurse-submodules`, or initialize the recorded dependency revisions from an existing checkout:

```powershell
git submodule update --init --recursive
dotnet restore CheatEngine.Mcp.slnx --locked-mode
dotnet build CheatEngine.Mcp.slnx -c Release --no-restore
dotnet test --solution CheatEngine.Mcp.slnx -c Release --no-restore --no-build --filter-not-trait Category=LiveQualification --filter-not-trait Category=NativeLua --fail-skips on
pwsh -NoProfile -File eng/Publish.ps1 -Configuration Release
```

Client and SDK build from the Git submodules in [`externals/`](.gitmodules), with exact commits recorded by this repository.
They track upstream `main`; normal builds use the recorded commits and do not download a newer revision.
Other NuGet dependencies are centrally versioned in [`Directory.Packages.props`](Directory.Packages.props) and locked per project.
The plugin is framework-dependent inside Cheat Engine; the gateway is published as a self-contained Windows x64 Native AOT executable.
The [contributor guide](CONTRIBUTING.md#build-and-test) covers CI checks, formatting, contract snapshots, and packaging.

To build and package the standard release downloads, use:

```powershell
pwsh -NoProfile -File eng/Release.ps1
```

This runs the publish pipeline and writes one verified ZIP plus `SHA256SUMS.txt` under `artifacts/releases/<version>/`.
To package an already published distribution without rebuilding it, pass `-DistributionPath artifacts/dist/release`.
For GitHub upload, that prebuilt distribution must also supply its sibling `artifacts/dist/release.release-manifest.json` and an independently reviewed SHA-256 digest; see [Releases](CONTRIBUTING.md#releases).
GitHub upload is explicit; see [Releases](CONTRIBUTING.md#releases).

## Verification

Verification is split by the boundary being exercised:

| Layer | What it verifies |
| --- | --- |
| Portable tests | Tool behavior against Client doubles, input validation, contracts, resource limits, and real local HTTP/stdio routing. |
| Seeded fuzz tests | Bounded generated cases for value encoding, malformed input, pointer files, IL2CPP dumps, string discovery, vtables, and pointer chains. Seeds make failures reproducible. |
| NativeLua tests | The Lua bridge against a Lua 5.3 x64 DLL, without starting Cheat Engine or attaching to a target. |
| Live qualification | Known-value workflows in private Cheat Engine copies with disposable targets, plus isolation, restart, and cleanup checks. Requires explicit opt-in. |
| Distribution checks | Loading the bundled plugin in isolation and checking the published gateway's MCP protocol. |

The [all-tool qualification report](docs/qualification/all-tools/README.md) distinguishes positive adapter evidence, routing checks, expected refusals, and verified native behavior for each tool.
A passing portable suite or successful routing call does not establish native success for every tool.
See the [test project guide](tests/CheatEngine.Mcp.Tests/README.md) and [live qualification instructions](CONTRIBUTING.md#live-qualification) for prerequisites and commands.

For 2.0, native qualification of six optional DBK/DBVM operations is deferred because a suitable
environment is unavailable. They remain unqualified under their existing capability gates;
this is an accepted release limitation. See the [qualification scope](docs/qualification/all-tools/README.md#accepted-20-qualification-limit)
for the affected operations and the remaining release gates.

## Repository layout

| Project | Responsibility |
| --- | --- |
| [Core](libs/CheatEngine.Mcp.Core/README.md) | Shared composition, contracts, target state, and tool execution. |
| [Tools](srcs/CheatEngine.Mcp.Tools/README.md) | MCP tools grouped by Cheat Engine operation. |
| [Resources](srcs/CheatEngine.Mcp.Resources/README.md) | Embedded knowledge and live MCP resources. |
| [Prompts](srcs/CheatEngine.Mcp.Prompts/README.md) | Guided workflows and server instructions. |
| [Hosting](srcs/CheatEngine.Mcp.Hosting/README.md) | Backend hosting, discovery, and gateway routing. |
| [Plugin](srcs/CheatEngine.Mcp.Plugin/README.md) | Cheat Engine entry point, activation lifecycle, Lua bridge, and logging. |
| [Gateway](srcs/CheatEngine.Mcp.Gateway/README.md) | Stdio executable and composition root. |
| [Tests](tests/CheatEngine.Mcp.Tests/README.md) | Contracts, offline integration, NativeLua, and live qualification. |
| [LiveTarget](tests/CheatEngine.Mcp.LiveTarget/README.md) | Disposable process used by live qualification. |

Each linked README documents its own project's layout, dependencies, and contribution boundaries.

## Migration, contributing, and license

Version 2 is a breaking redesign of CeMCP 1.x.
The plugin is now a single bundled DLL paired with the matching gateway executable; tool names and result contracts have changed, and instance routing is explicit.
Consult the [live tool schema](srcs/CheatEngine.Mcp.Resources/Knowledge/Documents/tool-map.md) when migrating scripts or prompts.

Use CheatEngine.Mcp only on software you own or are authorized to modify.
Read [SECURITY.md](SECURITY.md) for the trust boundary and private vulnerability reporting, and [CONTRIBUTING.md](CONTRIBUTING.md) before submitting changes.
The project is licensed under [MIT](LICENSE); dependency licenses and attribution are in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
