# Repository Guidelines

## Project

`CheatEngine.Mcp` is a Windows x64, .NET 10 / C# 14 managed Cheat Engine 7.7 plugin with a stdio MCP gateway. Each CE process enables its own authenticated loopback HTTP backend. One gateway routes requests by immutable instance ID. JSON configuration replaces the old WPF/menu flow. Disabling withdraws discovery and stops admission before Client-owned cleanup.

## Layout and conventions

- `src/CheatEngine.Mcp/`: plugin, activation module, HTTP server, options, logging, schema transform, and tools.
- `src/CheatEngine.Mcp.Gateway/`: stdio discovery, schema routing, and authenticated backend forwarding.
- `tests/CheatEngine.Mcp.Tests/`: xUnit v3 tests on Microsoft.Testing.Platform.
- `CheatEngine.Client`: NuGet package pinned centrally in `Directory.Packages.props`; no source checkout or submodule is required.
- `tests/CheatEngine.Mcp.Tests/LiveQualification/Infrastructure/`: local test helpers adapted from Client, with provenance in `NOTICE.md` and the full license under `licenses/`.
- `skills/cheatengine-mcp/`: shipped operator skill and exact tool catalog.
- `artifacts/`: build/test output.
- `Directory.Build.props`, `Directory.Packages.props`, `global.json`: centralized build/package/SDK policy.

Follow `.editorconfig`, adapted from CheatEngine.Client: tabs in C#, explicit types, file-scoped namespaces, braces, C# 14, nullable analysis, and warnings as errors. Preserve licenses and unrelated user work.

## Architecture

`Plugin : CheatEngineClientPlugin` configures one activation provider and registers `McpModule`. The module starts `McpServer` with that activation's Client instance and optional unsafe/Auto Assembler facades. HTTP never creates another Client activation.

`McpStatusIndicator` updates one CE-owned menu item for backend enable, disable, and startup failure. Its click handler is pure Lua and its passive disabled state survives until CE closes. Client 1.0 has no UI API; SDK dispatch admission opens after enable and closes before disable callbacks, while protected SDK Lua admission spans those callbacks. `LuaToolRuntime.ShowPluginStatus` is a narrow, main-thread-only SDK UI exception; disabling only updates an existing status item. Never extend it to tool calls, target work, or arbitrary lifecycle scripts. Keep the native live indicator checks when changing this boundary.

Tools are public instance classes with DI constructors and `McpServerToolType`. Every tool method and input parameter needs a description. Explicitly register containers with `WithToolsAndSchemaTransform<T>()`; its singleton lifetime retains tool-owned state and the schema transform protects nullable/numeric compatibility. Bind the DI-owned singleton with the instance overload of `McpServerTool.Create`: its target-factory overload disposes the target after each call. Gateway schema-only discovery must not construct Client tools.

`ToolCatalog` holds the shared explicit registrations. Gateway schemas add required `instanceId` to all CE tools; `list_instances` is gateway-local. Never introduce a shared selected-instance setting, automatic fallback, or mutation retry. Display names may duplicate. Registry records bind activation identity, PID/start time, endpoint, and token; verify backend identity before routing and never expose tokens in tool results or test receipts. Disable proxy/redirect following for backend connections.

Run each compound CE operation through `ToolExecution.Run`, which dispatches the entire body through Client and passes its stopping token. Keep target inspection and use in the same dispatch. Prefer typed Client APIs whenever available. For additional documented celua.txt features use LuaToolRuntime: fixed implementation-owned scripts and separately encoded a[] arguments through ICheatEngineClient.Lua. Only the runtime adapter may access SDK Lua state; never interpolate caller input as source or expose opaque Lua objects. Keep Lua result copies bounded.

The pinned Client 1.0 / SDK 2.0 disassembly columns are misordered on the installed CE 7.7 line. Retain typed instruction bytes and lengths, but obtain named display columns through protected Client Lua using CE's actual `extra, opcode, bytes, address` return order. Keep the native Lua column assertions and live known-instruction probe when changing this correction.

Value-scan tools share `scannerName`: `main` (default) operates CE's currently visible scan tab; other names retain independent Client sessions. `MainScanner` uses fixed Client-dispatched Lua to drive native scan controls and read the borrowed found list. Never destroy/reinitialize GUI scan objects, retain them across calls, replace CE scan callbacks, or wait on UI scans. Poll status instead. Main survives disable; independent scans retain Client lease cleanup. A running main scan blocks MCP target changes. Keep native UI result/count/manual-scan/isolation checks when changing this boundary.

Track target leases in `TargetResources` with bounded recovery descriptors. Refuse process switching while resources remain, except a repeated selected PID. Explicit release runs in reverse creation order, stops on incomplete cleanup, retains retryable handles, and reports manual recovery. On disable let Client drain its global lease stack before HTTP DI disposes tools; never synchronously join HTTP workers on CE's main thread.

Responses use `success` plus result fields. Client failures include kind, operation, and hostEffect. Do not report success for an incomplete release or hide partial side effects. Addresses use uppercase hexadecimal.

## Configuration and delivery

Precedence: bundled defaults, optional appsettings.json beside the original plugin wrapper DLL, user appsettings.json, MCP_HOST/MCP_PORT/MCP_INSTANCE_NAME/MCP_INSTANCE_DIRECTORY. Data/log path: %APPDATA%/CheatEngine.Mcp, or the absolute MCP_DATA_DIRECTORY override. The wrapper extracts its verified payload to %LOCALAPPDATA%/CheatEngine.Mcp/cache/<payload SHA256>, or the absolute MCP_BUNDLE_CACHE_DIRECTORY override. Treat extracted version directories as read-only; configuration remains editable only at the original plugin or user-data paths. No legacy configuration fallback. Registry defaults to %LOCALAPPDATA%/CheatEngine.Mcp/instances; gateway and plugins must agree on overrides.

Backends require literal 127.0.0.1, default port zero, and per-activation bearer authentication. Arbitrary Lua and Client Auto Assembler patches require explicit opt-in, but dedicated mutation tools remain powerful and these flags are not a sandbox; table files need allowed roots. Bound transport, reads, scans, identifiers, and retained resources.

Run `pwsh -NoProfile -File eng/Publish.ps1` (Release by default) to create `artifacts/dist/<configuration>/CheatEngine.Mcp.dll` and `CheatEngine.Mcp.Gateway.exe`. The plugin DLL is a verified extracting wrapper containing the runtime, Client, SDK, native bridge, and licenses; preserve the `CheatEngine.Mcp.dll` filename because CE resolves the managed entry point by file and assembly identity. The gateway is a self-contained single executable. Publish also stages the separate `skills/cheatengine-mcp/` folder for optional installation into the AI client; never include the skill in the runtime output or embedded DLL payload. Never restore Costura packaging or claim Native AOT loading. The CE host still needs .NET 10 ASP.NET Core and Windows Desktop runtimes. Keep the direct SDK package reference alongside the Client package: it supplies build-time bootstrap assets.

## Commands

```powershell
dotnet restore CheatEngine.Mcp.slnx --locked-mode
dotnet build CheatEngine.Mcp.slnx --no-restore
dotnet test --solution CheatEngine.Mcp.slnx --no-restore --no-build --filter-not-trait Category=LiveQualification --filter-not-trait Category=NativeLua --fail-skips on
dotnet build CheatEngine.Mcp.slnx -c Release --no-restore
pwsh -NoProfile -File eng/Publish.ps1
dotnet format style CheatEngine.Mcp.slnx --no-restore --severity warn --verify-no-changes
dotnet format whitespace CheatEngine.Mcp.slnx --no-restore --verify-no-changes
```

Tests use xUnit v3 Facts/Theories and behavior-oriented Subject_Condition_Outcome names. Prefer Client contract doubles and real loopback transport checks over mocking CE internals. `LiveQualification/` follows the Client's C# serial fixture and fail-fast acknowledgement pattern: set `CHEATENGINE_MCP_LIVE_QUALIFICATION=I_AUTHORIZE_CE77_LIVE_PROBES_ON_A_DISPOSABLE_TARGET`, publish the matching configuration first, then run `dotnet test --project tests/CheatEngine.Mcp.Tests -c Release --filter-trait Category=LiveQualification --fail-skips on`. It automatically loads the two-file distribution into two private CE copies, launches separate owned targets, routes both through one stdio gateway, and restores CE user settings using the locally adapted Client backup/recovery helpers. Optional `CHEATENGINE_MCP_LIVE_QUALIFICATION_CE_DIRECTORY` overrides the installed source; never edit that source. Reports/backups stay outside the checkout under `%LOCALAPPDATA%/CheatEngine.Mcp.LiveQualification/runs`. No other CE instance may be running. The live scenario covers memory, address-list/freeze and speedhack readback, not full Client host qualification. NativeLua tests require the documented DLL fixture and are excluded from portable CI, rather than skipped. Do not equate offline tests with native host verification.

Update the skill/catalog when tool contracts or configuration change. Never package or commit references/local-cheat-engine.md. No commit/push/deployment without user authorization.
