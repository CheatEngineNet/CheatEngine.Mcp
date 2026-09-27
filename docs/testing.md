# Testing

This page describes how CheatEngine.Mcp is tested: the test projects, the three test categories, the doubles and harnesses, the golden snapshots and the gates a change must pass.
The commands are also in [CONTRIBUTING.md](../CONTRIBUTING.md#build-and-test); how much evidence each category gives is in [Compatibility](compatibility.md#qualification-levels).

> **Status.** This page describes the 2.0.0 (v2) test strategy.
> Anything marked **(v2, in progress)** is an adopted design that the code does not implement yet.

## Test projects

| Project | Role |
|---|---|
| `tests/CheatEngine.Mcp.Tests` | The single test project: xUnit v3 on Microsoft.Testing.Platform, an executable (`OutputType=Exe`) set up by [`eng/Tests.props`](../eng/Tests.props) with the code coverage, TRX report, GitHub Actions report, hang dump and crash dump extensions. It references every product project. See its [README](../tests/CheatEngine.Mcp.Tests/README.md). |
| `tests/CheatEngine.Mcp.LiveTarget` | A tiny x64 process that live qualification attaches to. The test project builds it without referencing its assembly. See its [README](../tests/CheatEngine.Mcp.LiveTarget/README.md). |

The test project's folders mirror the product:

| Folder | Covers |
|---|---|
| `Core/` | Contract rules, error mapping, filters, JSON, value helpers, dispatch, feature gates, jobs, target resources, file paths, table inspection and the fixed-Lua runtime, with doubles. |
| `Tools/` | Tool behavior over the Client double, tool construction, scans, pointer scans, tables and debugger workflows. |
| `Hosting/` | The backend host, instance registry, gateway routing, connection pool, options, token-safe logging and a token-leak check across the backend and the gateway at `Trace`. |
| `Plugin/` | The activation lifecycle, configuration precedence, feature and file options, runtime identity and the plugin log. |
| `Architecture/` | The project graph and composition rules; see [Development](development.md#dependency-rules). |
| `Contract/` | Golden snapshots, composition parity between the plugin and the gateway, tool contract checks, the skill catalog and the third-party notices. |
| `NativeLua/` | Fixed Lua scripts run against a real Lua 5.3 DLL. |
| `LiveQualification/` | The two-instance live scenario and its opt-in policy; `Infrastructure/` holds helpers adapted from CheatEngine.Client, with provenance in its `NOTICE.md`. |
| `Support/` | Shared doubles, harnesses and assertions. |

## Categories

| Category | How it is marked | Where it runs | What it needs |
|---|---|---|---|
| Portable | No `Category` trait | Every CI run and every local run | Nothing beyond the SDK. |
| NativeLua | `[Trait("Category", "NativeLua")]` on the partial class `NativeLuaToolRuntimeTests` | Locally | `CHEATENGINE_MCP_LUA53_PATH` set to a Lua 5.3 x64 DLL, such as Cheat Engine's `lua53-64.dll`. |
| LiveQualification | `[Trait("Category", "LiveQualification")]` on `McpLiveQualificationTests` | Locally, on explicit opt-in | An installed Cheat Engine 7.7 x64, a published distribution and the acknowledgement variable. |

Tests that cannot run in an environment are excluded by trait, never skipped: every command passes `--fail-skips on`, so a skip is a failure.
The NativeLua fixture fails with a clear message when `CHEATENGINE_MCP_LUA53_PATH` is missing, rather than skipping.
`LiveQualificationOptInTests` has no category: the opt-in policy itself is tested portably.

Offline tests are never evidence of native host behavior: a green portable or NativeLua run says nothing about what Cheat Engine does.

## Running tests

Portable suite, as CI runs it:

```powershell
dotnet restore CheatEngine.Mcp.slnx --locked-mode
dotnet build CheatEngine.Mcp.slnx --no-restore
dotnet test --solution CheatEngine.Mcp.slnx --no-restore --no-build --filter-not-trait Category=LiveQualification --filter-not-trait Category=NativeLua --fail-skips on
```

NativeLua suite:

```powershell
$env:CHEATENGINE_MCP_LUA53_PATH = 'C:\Program Files\Cheat Engine\lua53-64.dll'
dotnet test --project tests/CheatEngine.Mcp.Tests --filter-trait Category=NativeLua
Remove-Item Env:CHEATENGINE_MCP_LUA53_PATH
```

One class or one test, with the xUnit v3 filters of Microsoft.Testing.Platform:

```powershell
dotnet test --project tests/CheatEngine.Mcp.Tests --filter-class CheatEngine.Mcp.Tests.Contract.ContractSnapshotTests
dotnet test --project tests/CheatEngine.Mcp.Tests --filter-method *PluginOutput_StagedDeploymentFiles_MatchGoldenManifest
```

Because `global.json` selects Microsoft.Testing.Platform, these options go straight after `dotnet test`, without a `--` separator.

## Doubles and harnesses

Prefer these over mocking Cheat Engine internals; each lives in `tests/CheatEngine.Mcp.Tests/Support`.

| Helper | Use it to |
|---|---|
| `ClientTestDouble` | Build an `ICheatEngineClient`, or any Client interface, as a `DispatchProxy` whose properties you supply; its default dispatcher runs callbacks inline, as if on Cheat Engine's main thread. |
| `RecordingDispatcher` (`DispatchDoubles.cs`) | Count dispatch admissions, record the tokens each dispatch observed, and refuse admission the way the Client does once its token is cancelled. |
| `TestActivation` | Build the plugin's real activation services over a Client double, validated like Client Hosting validates them (`ValidateOnBuild`, `ValidateScopes`), with `Mcp:*` settings; read the plugin log at the end. |
| `TestMcpPipeline` | Run a real MCP server composed with `WithCheatEnginePrimitives` and a real client over in-memory pipes, and call tools with exact JSON arguments; it asserts the error envelope shape. |
| `GatewayTestHost` and `FakeBackend` (`GatewayTestServers.cs`) | Drive the real gateway router over loopback HTTP, against backend doubles that authenticate like the real backend and record what the gateway forwards. |
| `StateTestHarness` | Run the state ledger, target resources and job registry over a Client double with canned Lua answers. |
| `TestComposition` | Reuse the production compositions of the plugin and the gateway, so tests never restate which primitives exist. |
| `LogCapture` | Capture every log entry with its category, message, structured values and exception, for example to prove that no token is logged. |
| `LuaFixedScriptAssert` | Assert that a fixed Lua body never compiles code or reads a caller-chosen global. |
| `GoldenFile` | Compare contract output with a reviewed snapshot. |
| `SerialTestGroup` | Serialize tests that touch process-wide state; NativeLua tests run in it. |
| `TestJsonContext`, `StateTestJsonContext` | Source-generated JSON for the shapes tests read, so tests need no reflection-based JSON. |

The test project will also turn reflection-based JSON off (`JsonSerializerIsReflectionEnabledByDefault=false`) once the last pre-2.0.0 tool is gone (v2, in progress).

## Golden snapshots

`tests/CheatEngine.Mcp.Tests/Contract/Golden` holds the reviewed public contract:

| File | Produced by |
|---|---|
| `backend-tools.json`, `backend-initialize.json` | A real backend over loopback, and the schema-only catalog, which must match it. |
| `gateway-tools.json`, `gateway-initialize.json` | The gateway catalog, and the built gateway executable over stdio with no instance running. |
| `plugin-files.txt` | The top-level `*.dll`, `*.json` and `*.pdb` files of the plugin build output, which the Client deployment copies: 26 files. |

Planned additions are `tool-summary.txt` (names, annotations and dispatch classes), `open-world-tools.txt`, and the resource, template and prompt listings (v2, in progress).

To change a golden file on purpose:

```powershell
$env:CHEATENGINE_MCP_UPDATE_GOLDEN = '1'
dotnet test --project tests/CheatEngine.Mcp.Tests --filter-namespace CheatEngine.Mcp.Tests.Contract
Remove-Item Env:CHEATENGINE_MCP_UPDATE_GOLDEN
dotnet test --project tests/CheatEngine.Mcp.Tests --filter-namespace CheatEngine.Mcp.Tests.Contract
git diff tests/CheatEngine.Mcp.Tests/Contract/Golden
```

- In update mode a golden test writes its file and passes, so always run the tests again without the variable.
- Review every line of the diff and explain it in the pull request: the goldens are the 2.0.0 contract.
- Never edit a golden file by hand, and never merge two golden diffs by hand; regenerate after the merge.

## Writing tests

- Use xUnit v3 `[Fact]` and `[Theory]`, and name tests `Subject_Condition_Outcome`, for example `ThirdPartyNotices_RemovedPackage_IsNotListed`.
- Test behavior through the Client double and real loopback transport, not by mocking Cheat Engine internals.
- For a gated tool, assert that a disabled gate refuses the call with `capability_disabled` and makes zero mutating Client calls.
- For a fixed Lua script, add a portable `LuaFixedScriptAssert.NeverLoadsCode` check and a NativeLua test that compiles it and runs it against stubs.
- For a failure, assert the error envelope: `kind`, `operation`, `hostEffect` and `retryable`.
- Never put a real token, a personal path or an e-mail address in a test or a fixture.

## Live qualification

`McpLiveQualificationTests.OneGateway_TwoOwnedInstances_RoutesStateAndSurvivesOneHostShutdown` follows the CheatEngine.Client serial fixture and fail-fast acknowledgement pattern.

What it does:

- It copies the installed Cheat Engine into two private installations, with only the reviewed stock autorun scripts (`celib.lua`, `monoscript.lua`, `SpeedhackV3.lua`), each checked against a pinned SHA-256; a mismatch stops the run until someone reviews the script.
- It installs the published plugin folder into each copy, starts one [live target](../tests/CheatEngine.Mcp.LiveTarget/README.md) per copy, and routes both instances through one stdio gateway.
- It covers memory, the address list, freeze, speedhack readback, the disassembly columns and the value scanners, and checks that the instances stay isolated and that one keeps working after the other stops.
- It backs up Cheat Engine's user settings and restores them afterwards, with the helpers adapted from CheatEngine.Client.
- It records the Cheat Engine version and hashes, the plugin hash and every check in a report under `%LOCALAPPDATA%\CheatEngine.Mcp.LiveQualification\runs`, or under `CHEATENGINE_MCP_LIVE_QUALIFICATION_RUN_ROOT`, an absolute folder outside the repository.

Rules:

- A maintainer asks for the run, and it is announced before it starts.
- No Cheat Engine, CE tutorial or DebugView process may be running; the runner refuses to start and never stops them itself.
- It refuses to run when `CI=true`.
- Publish the matching configuration first: the test reads `artifacts/dist/release` for a Release test build and `artifacts/dist/debug` for a Debug one.
- `CHEATENGINE_MCP_LIVE_QUALIFICATION_CE_DIRECTORY` selects another installation; the runner never edits it.
- Kernel, hypervisor and code-execution tools are never exercised live, and Mono only after a human review.

```powershell
pwsh -NoProfile -File eng/Publish.ps1
$env:CHEATENGINE_MCP_LIVE_QUALIFICATION = 'I_AUTHORIZE_CE77_LIVE_PROBES_ON_A_DISPOSABLE_TARGET'
dotnet test --project tests/CheatEngine.Mcp.Tests -c Release --filter-trait Category=LiveQualification --fail-skips on
Remove-Item Env:CHEATENGINE_MCP_LIVE_QUALIFICATION
```

## Gates

| Gate | Commands | When |
|---|---|---|
| V | Locked restore, Debug build, portable tests | Every change. |
| V+ | V, `dotnet format style` and `dotnet format whitespace` with `--verify-no-changes`, Release build, `pwsh -NoProfile -File eng/Publish.ps1`, and a reviewed golden diff | Every pull request; CI runs everything except the review. |
| VN | The NativeLua suite | Every change to a fixed Lua script, the Lua runtime, the job kernel or the status indicator. |
| AOT | The Native AOT gateway publication, then the `GatewayExecutableTests` smoke test with `CHEATENGINE_MCP_GATEWAY_EXECUTABLE` pointing at the published executable | (v2, in progress) |
| VL | Live qualification | On a maintainer's request, announced before each run. |

## Continuous integration

- [`build.yml`](../.github/workflows/build.yml) runs on pushes and pull requests to `main` on `windows-latest`: locked restore, Debug build, portable tests, both format checks, Release build, and the Debug and Release distributions, which it uploads as the `CheatEngine.Mcp-debug` and `CheatEngine.Mcp-release` artifacts.
- [`sonarqube.yml`](../.github/workflows/sonarqube.yml) builds and runs the portable tests under the SonarQube Cloud scanner.
- Neither workflow runs NativeLua or live qualification.
