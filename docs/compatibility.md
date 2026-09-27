# Compatibility

This page lists the platforms, hosts and packages CheatEngine.Mcp is built for, and how much evidence backs each one.
To install a supported combination, follow [Getting started](getting-started.md); to register the gateway in an AI client, see [Clients](clients.md).

> **Status.** This page describes the 2.0.0 (v2) line.
> Anything marked **(v2, in progress)** is an adopted design that the code does not implement yet.

## Qualification levels

| Level | Name | What it establishes | How it runs |
|---|---|---|---|
| C0 | Build and offline tests | The solution builds on the pinned SDK with warnings as errors and passes the format checks and the portable test suite: Client contract doubles, real loopback HTTP discovery, start and stop, architecture rules, and golden snapshots of the gateway and backend `tools/list` and `initialize` and of the plugin file list. | Every CI run, on GitHub's `windows-latest` image. The commands are in [AGENTS.md](../AGENTS.md#commands). |
| C1 | NativeLua | The fixed Lua scripts and the protected adapter run against a real Lua 5.3 DLL, such as CE's `lua53-64.dll`, with CE functions stubbed: script compilation, bounded result copying, the JSON writer, the unsafe-Lua wrapper, the job kernel, the status script, the main scanner, the debugger workflows and the disassembly columns. | Locally only, with `CHEATENGINE_MCP_LUA53_PATH` set and `--filter-trait Category=NativeLua`. CI excludes these tests rather than skipping them. |
| C2 | Live two-instance qualification | The published plugin folder loads in two private copies of an installed CE, each attached to its own disposable target, and one stdio gateway routes both. | Locally, on explicit opt-in (see below). |
| C3 | Manual | A person exercised the component by hand, in a real CE session or AI client, and recorded what worked. | A compatibility report. |
| C4 | Untested | There is no evidence either way. It may work. | Nothing. |

The C2 scenario checks the gateway's tool list and instance list, the loaded plugin file names and runtime evidence, attaching to the target, typed memory reads and writes, address-list add, update and delete, real freeze and unfreeze, speedhack set and readback, the disassembly column order on a known instruction, and the main and independent value scanners.
It also checks that a change in instance A leaves instance B's target and table untouched, and that A keeps working after B stops while B's calls fail.

How to read a level:

- A level holds only for the exact versions and file hashes it ran against; the live report records the CE version and SHA-256, the plugin hash, every check and every cleanup result.
- C0 and C1 never prove native host behavior, and a CI run or a Native AOT publish is never host qualification.
- C2 covers the scenario above, not every tool: the debugger, code execution, injection and Mono are not exercised live, and kernel, hypervisor and code-execution tools never will be.
- The levels in the matrix below are the highest level the repository's own tooling can establish for each component, not a record of a particular run.

To run C2, close every other CE instance, publish the matching configuration, and opt in explicitly:

```powershell
$env:CHEATENGINE_MCP_LIVE_QUALIFICATION = 'I_AUTHORIZE_CE77_LIVE_PROBES_ON_A_DISPOSABLE_TARGET'
pwsh -NoProfile -File eng/Publish.ps1
dotnet test --project tests/CheatEngine.Mcp.Tests -c Release --filter-trait Category=LiveQualification --fail-skips on
Remove-Item Env:CHEATENGINE_MCP_LIVE_QUALIFICATION
```

- The runner refuses to start when `CI=true`, when another CE process is running, or without the exact acknowledgement.
- It copies the installed CE into private folders and never edits the installation; `CHEATENGINE_MCP_LIVE_QUALIFICATION_CE_DIRECTORY` selects another installation, and `CHEATENGINE_MCP_LIVE_QUALIFICATION_RUN_ROOT` another absolute run folder outside the repository.
- It backs up and restores CE's user settings, and keeps reports and backups under `%LOCALAPPDATA%\CheatEngine.Mcp.LiveQualification\runs`, outside the checkout.

## Support matrix

| Component | Supported | Level | Notes |
|---|---|---|---|
| Operating system | Windows, x64 only | C0 in CI; C1 and C2 on the machine that runs them | The plugin, the SDK's native Lua bridge and the live target are x64; CESDK9101 rejects x86 and ARM platform targets. |
| Cheat Engine | 7.7.0.10621 x64, `cheatengine-x86_64.exe` | C2, currently blocked (see [Known issues](#known-issues)) | The host profile qualified by CheatEngine.Client 1.0.0, `ce-7.7.0.10621-x64-managed-hostfxr`. Executable SHA-256: `9727076DA50924E4A097B49A02155E4B34759269C3017FF31375364B8826EB4D`. |
| Other Cheat Engine 7.7.x x64 builds | Not qualified | C4 | The live runner accepts any installed x64 CE 7.7 or later and records its hash, but CheatEngine.Client 1.0.0 targets only 7.7.0.10621. |
| Cheat Engine 7.6 and earlier, 32-bit Cheat Engine | Not supported | None | See [Not supported](#not-supported). |
| 32-bit target processes attached from 64-bit CE | Not qualified | C4 | The live target is x64. |
| .NET runtimes inside CE | `Microsoft.NETCore.App`, `Microsoft.AspNetCore.App` and `Microsoft.WindowsDesktop.App` 10.0.x, x64 | C2 | CE starts one runtime from its `ce.runtimeconfig.json`, which must be edited locally to select .NET 10 (`net10.0`, `LatestMinor`) and all three frameworks; the plugin's own `runtimeconfig.json` requests `Microsoft.NETCore.App` and `Microsoft.AspNetCore.App` 10.0.0, which that runtime must already include. |
| Gateway runtime | None needed | C0; C2 for the published executable | A self-contained Windows x64 Native AOT executable. It never uses CE's runtime configuration. |
| .NET SDK, to build | 10.0.401 exactly | C0 | `global.json` sets `rollForward: disable` and `allowPrerelease: false`; the Client's Lua generator needs the Roslyn 5.9.0 compiler of SDK 10.0.401 or later. |
| CheatEngine.Client | 1.0.0 | C2 | Source revision `f88de3d843252c9139f08c71531a02f03c0516bb`, MIT. The tools opt into its experimental typed APIs (CECLIENT5001 to CECLIENT5004). |
| CheatEngine.SDK | 2.0.0 | C2 | Referenced directly by the plugin only, for the generated entry point and the native Lua bridge; the Client accepts 2.0.0 up to, but excluding, 3.0.0, and the lock files pin 2.0.0. MIT. |
| ModelContextProtocol and ModelContextProtocol.AspNetCore | 2.2.0 | C2 | Apache-2.0. |
| Microsoft.Extensions.AI.Abstractions | 10.10.1 | C2 | A pinned transitive dependency. |
| AI clients | Any MCP client that can launch a local stdio server | C4 unless [Clients](clients.md) records a check | Per-client configuration, limits and known issues are in [Clients](clients.md). |

Package versions are pinned centrally in [`Directory.Packages.props`](../Directory.Packages.props) with transitive pinning, restored from the checked-in lock files in locked mode, and the SDK is pinned in [`global.json`](../global.json).

## MCP protocol versions

| Connection | Protocol version | Level | Notes |
|---|---|---|---|
| AI client to gateway, over stdio | Negotiated by ModelContextProtocol 2.2.0, which supports 2024-11-05, 2025-03-26, 2025-06-18, 2025-11-25 and 2026-07-28 | 2025-06-18: C0 and C2; the others: C4 | The gateway accepts the version its client negotiates; the contract tests and the live client use 2025-06-18. |
| Gateway to backend, over loopback streamable HTTP | Pinned to 2025-06-18 | C0 and C2 | The gateway requests exactly this version, which uses the initialize handshake and skips the SDK's version-discovery probe. `ContractSnapshotTests` compares backend tool listings negotiated with 2025-06-18 and 2026-07-28. |
| Any other client to a backend | Not supported | None | A backend requires its per-activation bearer token and is meant to be reached only through the gateway. |

## Not supported

- **32-bit Cheat Engine or an ARM64 host.** The plugin and the SDK's native Lua bridge are x64 only, a 32-bit CE cannot load them, and the SDK lists x86 and ARM64 hosts as not supported.
- **A Native AOT plugin.** CE 7.7 loads .NET plugins through hostfxr as framework-dependent components and unloads plugins with `FreeLibrary`, which .NET does not support for Native AOT libraries; CESDK9102 and CEMCP006 fail such a build. See [Architecture](architecture.md#why-a-plugin-folder-and-not-one-dll).
- **A single-DLL, merged, self-contained, single-file, trimmed or ReadyToRun plugin.** The plugin is the folder the Client deployment stages; CEMCP006 fails any of these builds, and packing tools or extracting loaders break the `deps.json` contract.
- **Cheat Engine 7.6 and earlier.** CheatEngine.SDK 2.0.0 and CheatEngine.Client 1.0.0 target only CE 7.7's managed hostfxr plugin profile, and the live runner refuses a host older than 7.7.
- **Building with another .NET SDK.** `global.json` disables roll-forward.
- **Mixing files from two builds in the plugin folder, or renaming `CheatEngine.Mcp.Plugin.dll`.** CE resolves the plugin through its generated entry point and its `deps.json`.
- **Backends on a non-loopback address.** `Mcp:Host` must be `127.0.0.1`; see [Security](security.md).
- **Remote or browser-only AI clients** without a Windows environment that can launch the gateway executable next to CE.

## Known issues

### The reviewed `monoscript.lua` hash does not match CE 7.7.0.10621

The live runner disables CE's autorun scripts in its private copies, except three stock scripts whose SHA-256 must match reviewed values in [`LiveSandboxSession.cs`](../tests/CheatEngine.Mcp.Tests/LiveQualification/LiveSandboxSession.cs); a missing or changed script stops the run before CE starts.

| Script | Reviewed SHA-256 | Stock CE 7.7.0.10621 | Match |
|---|---|---|---|
| `celib.lua` | `5871F4E9F6C06B5811C1F4B208B5D6869C5191E7CB5540F2B463D01E5541FAC2` | Same | Yes |
| `SpeedhackV3.lua` | `69DE7EE3F4563005B5BC34A27F720D9D6E714A14FE7D6E1C47D5CF0AECD93D9C` | Same | Yes |
| `monoscript.lua` | `F139E50B788C85A15ACFA92B892FCCBC3DECA6D8D609AD9E5428B4C336C90600` | `28B6B086732A5FFAA9937282B978D914569E508F0A907B466A618B753857F4DE` | No |

Against a stock CE 7.7.0.10621 installation, the live run therefore stops at the `monoscript.lua` check.
The stock script has to be reviewed before its hash replaces the reviewed one; simply updating the hash would skip that review, and Mono support in v2 depends on this script.

### The pinned host profile is not enforced by the MCP live runner

[`CheatEngineProfile`](../tests/CheatEngine.Mcp.Tests/LiveQualification/Infrastructure/CheatEngineProfile.cs) pins CE 7.7.0.10621 and its executable hash, but the MCP live runner describes the installed host instead: it requires x64 CE 7.7 or later and records the version and hash in its report.
Compare the recorded hash with the one in the [support matrix](#support-matrix) before calling a run a qualification of 7.7.0.10621.

### Disassembly columns are misordered by the Client and SDK

CheatEngine.Client 1.0 and CheatEngine.SDK 2.0 return the named disassembly display columns in the wrong order on CE 7.7.
The tools keep the typed instruction bytes and lengths, but read the named columns through protected Lua in CE's actual `extra, opcode, bytes, address` order; NativeLua assertions and a live known-instruction probe guard this correction.

### Client calls are unavailable during enable and disable callbacks

CheatEngine.SDK 2.0.0 admits Client dispatch only after the enable callback returns and closes it before the disable callbacks, and CheatEngine.Client 1.0 has no UI API.
The plugin's status menu item therefore uses one narrow, main-thread-only SDK Lua exception; see [Architecture](architecture.md#enable).
