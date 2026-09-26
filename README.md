# CheatEngine.Mcp

A Windows x64 Cheat Engine plugin and MCP gateway built on [CheatEngine.Client](https://github.com/CheatEngineNet/CheatEngine.Client). One MCP connection can control multiple named Cheat Engine instances.

Each Cheat Engine process loads its own plugin, with its own Client activation and target state. Enabling publishes an authenticated loopback HTTP backend on an automatically assigned port. The stdio gateway discovers those backends and routes each tool call using an explicit `instanceId`. Disabling withdraws discovery, closes request admission, and lets Client release its resources without blocking Cheat Engine's main thread.

## Build

Install .NET SDK **10.0.401**, then run from this repository:

```powershell
git submodule update --init --recursive
dotnet restore CheatEngine.Mcp.slnx --locked-mode
dotnet build CheatEngine.Mcp.slnx --no-restore
dotnet test --solution CheatEngine.Mcp.slnx --no-restore --no-build --filter-not-trait Category=LiveQualification --filter-not-trait Category=NativeLua --fail-skips on
dotnet build CheatEngine.Mcp.slnx -c Release --no-restore
```

The Git submodule at `CheatEngine.Client/` is pinned to `f88de3d843252c9139f08c71531a02f03c0516bb`, the upstream main revision selected for this migration. Project references compile that exact source. The plugin also directly references **CheatEngine.SDK 2.0.0** for its generated entry point and native bridge.

The runtime dependency graph uses current stable releases. Test dependencies follow the pinned Client's xUnit v3/Microsoft.Testing.Platform profile. Existing remote Sonar project identifiers are retained; renaming a local project does not rename the hosted service.

Code follows Client conventions: C# 14, file-scoped namespaces, tabs, explicit types, centralized package versions, locked restore, and output under `artifacts/`. The MCP boundary uses reflection and is a framework-dependent managed plugin, not a Native AOT binary.

## Install and configure

Use Cheat Engine **7.7 x64**, .NET 10, and the ASP.NET Core 10 runtime. The host's `ce.runtimeconfig.json` must select compatible .NET 10 frameworks as described by the Client deployment guide.

Copy the **entire** `artifacts/bin/CheatEngine.Mcp.Gateway/release/` folder to a dedicated directory. Keep the gateway executable, `CheatEngine.Mcp.dll`, both applications' `.deps.json` and `.runtimeconfig.json`, all Client/SDK/dependency assemblies, and `cheatengine-sdk-lua-bridge.dll` together. Select `CheatEngine.Mcp.dll` in each Cheat Engine process's plugin settings and enable it. Do not copy only the DLL.

Configure a single stdio MCP server in your MCP client, pointing its command to `CheatEngine.Mcp.Gateway.exe`. For clients using the common JSON configuration format:

```json
{
  "mcpServers": {
    "cheatengine": {
      "command": "C:/Tools/CheatEngine.Mcp/CheatEngine.Mcp.Gateway.exe"
    }
  }
}
```

Replace the example path with your deployment directory. The MCP client starts the gateway; each CE process runs its own plugin backend. The gateway does not launch CE or choose a target automatically. The bundled [AI skill](skills/cheatengine-mcp/SKILL.md) documents instance selection and tool workflows; install that folder into your AI client's skill directory if it supports skills.

Configuration is read once per enable, in this order:

1. Built-in defaults.
2. `appsettings.json` beside the plugin.
3. `%APPDATA%/CheatEngine.Mcp/appsettings.json`.
4. `MCP_HOST`, `MCP_PORT`, `MCP_INSTANCE_NAME`, and `MCP_INSTANCE_DIRECTORY` environment overrides.

```json
{
  "Mcp": {
    "Host": "127.0.0.1",
    "Port": 0,
    "InstanceName": "game-a",
    "ServerName": "CheatEngine.Mcp",
    "EnableUnsafeLua": false,
    "EnableAutoAssembler": false
  },
  "CheatEngineClient": {
    "AllowedTableRoots": []
  }
}
```

`Port: 0` allocates an available port for each plugin. The host must be `127.0.0.1`. A fixed nonzero port is optional but must be unique across running instances. `InstanceName` is a display label; by default it contains the CE process ID. For different labels with one shared plugin folder, set `MCP_INSTANCE_NAME` separately in each CE process's launch environment, or use separate `MCP_DATA_DIRECTORY` settings directories. Restart the plugin to apply settings.

Discovery records live in `%LOCALAPPDATA%/CheatEngine.Mcp/instances`. They contain per-activation access tokens used by the gateway; do not share those files. An absolute `MCP_INSTANCE_DIRECTORY` override must agree between plugins and gateway; the gateway also accepts `--instance-directory <path>`. Both run as the same Windows user. Authentication isolates backend calls from unauthenticated HTTP clients; it is not a boundary against other applications running as that user.

Call `list_instances`, choose the intended name/CE PID, and pass its exact `instanceId` to every CE tool. Labels may repeat; IDs are unambiguous and change on every plugin activation. There is no shared selected-instance state, automatic fallback, or automatic retry. A stopped instance fails its calls without redirecting them to another CE. Independent instances have independent CE state, but attaching both to the same target still allows both to change that target.

Each CE process logs to `%APPDATA%/CheatEngine.Mcp/CheatEngine.Mcp.<pid>.log` so instances do not compete for one file. Client lifecycle and HTTP logging share that process's isolated NLog factory whose lifetime covers asynchronous shutdown.

`MCP_DATA_DIRECTORY` can select an absolute directory for user settings and logs. The automated live runner uses it to keep its plugin data inside the test run.

## Tools and ownership

The [catalog](skills/cheatengine-mcp/references/tool-catalog.md) lists **140 gateway tools**: `list_instances` plus **139 CE tools**, each with a required `instanceId`. Use live MCP schemas for parameter types and defaults. The [address-list and speedhack guide](skills/cheatengine-mcp/references/address-list-and-speedhack.md) covers adding records, freezing, unfreezing, and restoring speed.

The [scanning and debugging guide](skills/cheatengine-mcp/references/scanning-and-debugging.md) covers unknown-initial/changed/increased/range value scans, pointer maps and rescans, write/access collectors, break-and-trace, and register editing. Value scans and pointer memory access use Client APIs; debugger callbacks use fixed CE Lua operations. Pointer maps are bounded in-memory MCP snapshots, not CE's native pointer-map files. Debugger captures/traces have explicit stop, finite lifetime, and a 30-second result-retention window after completion/expiry.

- Start with `list_instances`, then `get_runtime_info`, `get_plugin_version`, and `get_current_process` on the chosen instance.
- Process selection, typed memory, pointers, AOB scans, inspection, and address-table tools use Client contracts.
- Value scans, allocations, assembly, and Auto Assembler retain Client experimental/capability checks. An API's presence does not establish that the current host supports it.
- Arbitrary `execute_lua` and Client Auto Assembler patches require their respective configuration flags. These flags are not a sandbox: dedicated tools can write memory, launch/inject code, control the debugger, access files, and alter host state. Table load/save uses configured allowed roots.
- Named scans, allocations, registered symbols, and patches belong to one enable epoch. Before switching processes, use `release_target_resources`; it releases in reverse creation order and stops on incomplete cleanup. A repeated request for the selected PID preserves resources.
- Failed releases report recovery details and keep retryable handles. If Cheat Engine changes targets outside MCP, inspect cleanup outcomes and perform manual recovery when reported. Never reuse identifiers after disable/re-enable.

The Lua adapter encodes arguments as data, runs a protected call inside the Client activation/main-thread boundary, and copies bounded results. It returns `{ success, result }`; direct Lua calls preserve multiple return values as arrays, including null slots. A Lua failure includes `hostEffect`: an operation that started may have partially changed the host. It is unsafe to blindly retry a failed mutation. Requests are limited to 8 MiB; copied Lua results are limited to 65,536 items, depth 16, and 4 MiB of strings. Individual tools apply tighter limits.

Client leases are activation-owned. Lua-created global structures, address-list changes, comments, breakpoints, and debugger/speedhack state are Cheat Engine-owned state. They can persist after plugin disable; use each explicit delete/remove/resume tool or Cheat Engine itself to recover. Injected libraries have host/target lifetimes. DBVM watch captures are timed (at most five seconds) and disable their watch before returning; a failed cleanup reports the watch ID for manual recovery. Inspection bounds limit returned data; some CE APIs internally enumerate a larger collection first. Long native calls run synchronously and cannot be interrupted by an HTTP cancellation.

## Migration from CeMCP 1.x

This is a breaking remake. The old CESDK submodule, source compilation, Costura single-DLL packaging, static tools, WPF UI, and old project/test paths have been removed.

The tool surface is rebuilt around Client high-level APIs for supported operations. Additional debugger, DBVM, injection, process-control, Structure Dissect, RTTI, protection, file-memory, and code-analysis tools use fixed Lua operations through `ICheatEngineClient.Lua`. Bindings follow the installed Cheat Engine `celua.txt`; unavailable host APIs return errors. The complete public surface and each tool description are in the catalog.

Tool parameters and responses have changed where Client ownership requires it: allocations have names, scans are bounded sessions, patches return lease IDs, record content and activation are separate tools, and `execute_lua` reports execution status without serializing Lua return values.

## Verification

Tests follow the Client and SDK setup: xUnit v3 on Microsoft.Testing.Platform, with the shared profile in `eng/Tests.props`. The portable suite uses deterministic Client doubles plus real loopback HTTP discovery/start/stop tests. It does not require Cheat Engine and does not prove native host behavior. CI excludes `Category=LiveQualification` and `Category=NativeLua`; selected tests fail rather than skip when required inputs are missing.

For an automated live run, close other Cheat Engine instances and run:

```powershell
$env:CHEATENGINE_MCP_LIVE_QUALIFICATION = 'I_AUTHORIZE_CE77_LIVE_PROBES_ON_A_DISPOSABLE_TARGET'
dotnet test --project tests/CheatEngine.Mcp.Tests -c Release --filter-trait Category=LiveQualification --fail-skips on
Remove-Item Env:CHEATENGINE_MCP_LIVE_QUALIFICATION
```

`LiveQualification/` contains the C# runner and serial fixture. Like the Client, it requires the exact acknowledgement, refuses `CI=true`, and fails immediately with the command above when explicitly selected without authorization. No PowerShell wrapper, plugin installation, or manual enabling is required. The test build supplies the complete plugin folder from this checkout, since MCP is deployed as a plugin folder rather than a NuGet package.

The runner creates two private copies of installed Windows x64 Cheat Engine 7.7+ under `%LOCALAPPDATA%/CheatEngine.Mcp.LiveQualification/runs/<run>`, selects .NET 10 only in those copies, and loads the plugin automatically. Each gets its own disposable memory target, label, and automatic loopback port; one stdio gateway routes calls through a private discovery directory. Existing CE processes are refused, never closed. The installed host is not modified. Optional `CHEATENGINE_MCP_LIVE_QUALIFICATION_CE_DIRECTORY` selects another installation; `CHEATENGINE_MCP_LIVE_QUALIFICATION_RUN_ROOT` selects another absolute run directory outside the repository and apart from the installation.

The private copy disables existing autorun scripts except the stock `celib.lua`, `monoscript.lua`, and `SpeedhackV3.lua` needed for speedhack. These must match the reviewed hashes from CE 7.7.1.10828; missing or changed scripts fail before launching CE. Review newer stock scripts before updating those hashes in `LiveSandboxSession.cs`.

The Client's registry and application-data backup helpers preserve CE settings, restore and verify them after shutdown, and retain a recovery marker if restoration fails. A later run restores a leftover backup and stops so the recovery is visible. Plugin settings and logs stay in the run folder. Reports contain the exact host version/hash, plugin hash, individual checks, and cleanup results. Local reports and backups stay outside the checkout.

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

These validate the protected adapter and copied-result contracts, not CE functions. Build artifacts are complete deployment folders.

## Attribution

This remake builds on the original [ce-mcp contributors](https://github.com/ShadowNineX/ce-mcp/graphs/contributors), [CheatEngine.Client](https://github.com/CheatEngineNet/CheatEngine.Client), [CheatEngine.SDK](https://github.com/CheatEngineNet/CheatEngine.SDK), and Cheat Engine. Existing license files remain authoritative.
