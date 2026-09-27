# Packaging

This page describes what CheatEngine.Mcp ships, how `eng/Publish.ps1` builds it, and the build guards that keep the plugin folder loadable by Cheat Engine.
To install the result, follow [Getting started](getting-started.md); for the runtime design behind it, see [Architecture](architecture.md#why-a-plugin-folder-and-not-one-dll).

> **Status.** This page describes the 2.0.0 (v2) packaging.
> Anything marked **(v2, in progress)** is an adopted design that the code does not implement yet.
> Today the gateway is a self-contained single-file executable; the Native AOT gateway is (v2, in progress).

## The distribution

`pwsh -NoProfile -File eng/Publish.ps1` publishes Release; `-Configuration Debug` publishes Debug.
The result is `artifacts/dist/<release|debug>/`:

```text
artifacts/dist/release/
  CheatEngine.Mcp/              the plugin folder, copied as a whole into Cheat Engine's plugin location
  CheatEngine.Mcp.Gateway.exe   the stdio gateway that AI clients launch
  LICENSE                       the notices of the gateway, which carries the .NET runtime
  THIRD-PARTY-NOTICES.md
  licenses/
  skills/cheatengine-mcp/       the operator skill, installed separately into the AI client
```

- The script refuses to run when the distribution folder holds anything else; move foreign files out first.
- It rebuilds the plugin folder and the skill folder from empty, so no stale file survives an update.
- The skill never goes into the plugin folder, and `references/local-cheat-engine.md` is never distributed: the script skips it and fails if a copy appears anywhere in the distribution.
- CI publishes both configurations and uploads them as the `CheatEngine.Mcp-debug` and `CheatEngine.Mcp-release` workflow artifacts.

## The plugin folder

The plugin folder is the CheatEngine.Client staged deployment.
`eng/Publish.ps1` builds it with:

```powershell
dotnet build srcs/CheatEngine.Mcp.Plugin -c Release -p:RestoreLockedMode=true -p:ContinuousIntegrationBuild=true "-p:CheatEnginePluginOutputPath=<dist>/CheatEngine.Mcp"
```

The Client's deployment validates the plugin profile (CECLIENT010 to CECLIENT016) and copies every top-level `*.dll`, `*.json` and `*.pdb` file of the plugin output.
That gives 26 files, the list reviewed in the [`plugin-files.txt`](../tests/CheatEngine.Mcp.Tests/Contract/Golden/plugin-files.txt) golden file:

| Group | Files |
|---|---|
| The plugin | `CheatEngine.Mcp.Plugin.dll`, `CheatEngine.Mcp.Plugin.deps.json`, `CheatEngine.Mcp.Plugin.runtimeconfig.json`, `appsettings.json` |
| CheatEngine.Mcp libraries | `CheatEngine.Mcp.Core.dll`, `CheatEngine.Mcp.Hosting.dll`, `CheatEngine.Mcp.Tools.dll`, `CheatEngine.Mcp.Resources.dll`, `CheatEngine.Mcp.Prompts.dll` |
| CheatEngine.Client | `CheatEngine.Client.Abstractions.dll`, `CheatEngine.Client.Core.dll`, `CheatEngine.Client.Extensions.DependencyInjection.dll`, `CheatEngine.Client.Fluent.dll`, `CheatEngine.Client.Hosting.dll` |
| CheatEngine.SDK | `CheatEngine.SDK.dll`, `CheatEngine.SDK.Abi.dll`, `CheatEngine.SDK.Annotations.dll`, `CheatEngine.SDK.Engine.dll`, `CheatEngine.SDK.Hosting.dll`, `CheatEngine.SDK.Lua.dll`, `CheatEngine.SDK.Lua.Interop.dll`, and the native `cheatengine-sdk-lua-bridge.dll` |
| MCP SDK | `ModelContextProtocol.dll`, `ModelContextProtocol.Core.dll`, `ModelContextProtocol.AspNetCore.dll`, `Microsoft.Extensions.AI.Abstractions.dll` |

The script then adds `README.md`, `LICENSE`, `THIRD-PARTY-NOTICES.md` and the `licenses/` folder.
Today `README.md` is the repository README; a README written for the plugin folder, `srcs/CheatEngine.Mcp.Plugin/Distribution/README.md`, is (v2, in progress).

Before it finishes, the script checks that:

- Every runtime, native, resource and runtime-target asset named in `CheatEngine.Mcp.Plugin.deps.json` is in the folder, because Cheat Engine resolves the plugin through that file.
- `CheatEngine.Mcp.Plugin.runtimeconfig.json`, `cheatengine-sdk-lua-bridge.dll`, `appsettings.json` and `README.md` are present.
- Every license text that `THIRD-PARTY-NOTICES.md` names under `licenses/` ships, both in the plugin folder and beside the gateway.
- The folder contains no `.pdb` file.

Keep the folder intact and never rename `CheatEngine.Mcp.Plugin.dll`: Cheat Engine loads it through the entry point that the SDK generates, and the other files are found beside it.
The Cheat Engine host still needs the .NET 10 runtimes it loads plugins with, including ASP.NET Core and Windows Desktop; see [Compatibility](compatibility.md).

## Why not a single DLL

A single-file plugin was investigated and is not possible with Cheat Engine 7.7.0.10621:

- Cheat Engine loads a .NET plugin only through hostfxr (`hdt_load_assembly_and_get_function_pointer`, entry point `CESDK.CESDK.CEPluginInitialize`), and hostfxr hosts only framework-dependent components, with their `deps.json`, `runtimeconfig.json` and dependency assemblies beside them.
- A Native AOT DLL would have to use Cheat Engine's native `CEPlugin_*` route, and Cheat Engine unloads such plugins with `FreeLibrary` when a plugin is added or removed, which .NET does not support. The SDK documents a NativeAOT plugin DLL as not supported and reports CESDK9102, an error here because warnings are errors.
- CECLIENT013 to CECLIENT015 require `CheatEngine.SDK.dll`, `CheatEngine.Client.Hosting.dll` and `cheatengine-sdk-lua-bridge.dll` as separate files, and the SDK loads the native Lua bridge with `NativeLibrary.Load` from the assembly directory.
- Trimming requires a self-contained publish, and single-file publishing requires an executable (NETSDK1102, NETSDK1099).
- Kestrel exists only in the ASP.NET Core shared framework, which Cheat Engine's runtime must provide.
- Merged or packed assemblies, an extracting loader or a hand-written loader would break the `deps.json` contract and the Client's deployment checks; the project never reintroduces them.

The project ships a minimal folder instead: it went from 34 files to 26 by embedding symbols and replacing NLog with the plugin's own log writer.

## Symbols and reproducible paths

- `DebugType=embedded` and `EmbedUntrackedSources=true` in [`Directory.Build.props`](../Directory.Build.props) put the symbols inside every assembly: stack traces keep file and line numbers, and no `.pdb` ships.
- `eng/Publish.ps1` passes `ContinuousIntegrationBuild=true`, which CI also sets through `CI=true`; source paths are then mapped to `/_/`, so no local path, such as a user profile, reaches a shipped assembly.
- Builds are `Deterministic`.

## Build guards

The repository guards live in [`Directory.Build.targets`](../Directory.Build.targets) and fail the build with their code:

| Code | Applies to | Fails when |
|---|---|---|
| CEMCP001 | The plugin | The plugin would ship an app-local copy of an assembly that the .NET or ASP.NET Core shared framework provides. Such a copy would give one type two identities inside Cheat Engine. The fix is a `FrameworkReference` and no direct framework `PackageReference`, never a removal. |
| CEMCP002 | Every project | `CheatEngineClientPluginProject` is set on a project other than the plugin (`McpPluginProject`), or the plugin does not set it. |
| CEMCP003 | Every project | A project other than the plugin references `CheatEngine.SDK` directly. |
| CEMCP004 | The plugin | A dependency asset would land in a subfolder, such as `runtimes/<rid>/` or a culture folder, which the Client deployment does not copy. |
| CEMCP005 | Every project under `libs/` and `srcs/` | `IsAotCompatible` is off, or reference AOT and trim verification is off in a project without a `FrameworkReference` to `Microsoft.AspNetCore.App`. |
| CEMCP006 | The plugin | The plugin is made Native AOT, self-contained, single-file, trimmed or ReadyToRun, or loses `DebugType=embedded`. |
| CEMCP007 | The gateway | The gateway is published any way other than Native AOT (v2, in progress). |
| CEMCP008 | The gateway publish | The .NET runtime notices required beside the Native AOT executable are missing (v2, in progress). |

The CheatEngine.Client package adds its own plugin profile checks (CECLIENT001 to CECLIENT017), and CECLIENT010 to CECLIENT016 for a staged deployment through `CheatEnginePluginOutputPath`.

## The gateway

Today:

- `eng/Publish.ps1` publishes `srcs/CheatEngine.Mcp.Gateway` with the `Standalone` profile: `win-x64`, self-contained, single-file with native libraries extracted at startup, not trimmed.
- The profile writes to `artifacts/publish/CheatEngine.Mcp.Gateway/<configuration>-standalone/`, and the script copies the executable into the distribution.
- The executable is about 108 MB because it bundles the .NET and ASP.NET Core runtimes; it needs no .NET installation on the AI client's machine.
- Its `LICENSE`, `THIRD-PARTY-NOTICES.md` and `licenses/`, including the .NET runtime notices, sit beside it.

Native AOT gateway (v2, in progress):

- The gateway project sets `PublishAot` and `InvariantGlobalization` in its project file, so the tests run with the same switches, and a `NativeAot` publish profile (`OptimizationPreference=Size`, `TrimmerSingleWarn=false`) replaces `Standalone`.
- Every product project is already analyzed for AOT and trimming (CEMCP005). Trial ILC compilations of the gateway produced executables of about 14 to 19 MB, which still crash at startup until the pre-2.0.0 tools and their reflection-based JSON are gone.
- `eng/Publish.ps1` will check the MSVC toolchain (`vswhere.exe` on `PATH`, `vcvarsall`, the Windows SDK), verify that the executable is native code without .NET metadata, and keep the native `.pdb` in `artifacts/symbols`, outside the distribution.
- CI will run the `GatewayExecutableTests` smoke test, with `CHEATENGINE_MCP_GATEWAY_EXECUTABLE` pointing at the published executable, against a size budget of 64 MiB, later tightened to the measured size plus 20 percent.
- The plugin is never published this way: CEMCP006 forbids it.

## Third-party notices

- [`THIRD-PARTY-NOTICES.md`](../THIRD-PARTY-NOTICES.md) at the repository root lists every package the plugin folder and the gateway redistribute, and the .NET runtime inside the gateway, with the license texts under [`licenses/`](../licenses).
- It ships twice, in the plugin folder and beside the gateway, together with `LICENSE` and `licenses/`.
- `ThirdPartyNoticesTests` fails when a package in the plugin or gateway `deps.json` has no `| <id> | <version> |` row with its exact version, and when NLog, which the plugin no longer ships, is mentioned again.
- When the pinned SDK changes the runtime version, update the runtime table and copy the runtime license and notice files again, as the file explains.
- The `NOTICE.md` in `tests/CheatEngine.Mcp.Tests/LiveQualification/Infrastructure` records the provenance of the test helpers adapted from CheatEngine.Client; they never ship.

## Checklist for a packaging change

- Run `pwsh -NoProfile -File eng/Publish.ps1` for both configurations and read the file list it prints.
- Regenerate and review `plugin-files.txt` when the plugin output changes; see [Testing](testing.md#golden-snapshots).
- Update `THIRD-PARTY-NOTICES.md` and `licenses/` when a shipped package or the runtime changes.
- Commit the lock files that changed; CI restores with `--locked-mode`.
- Never add an extracting loader, a merged assembly or a Native AOT plugin, and never claim Native AOT loading of the plugin.
