# CheatEngine.Mcp.Tests

`CheatEngine.Mcp.Tests` is the repository's integration and contract test project.
It verifies the Core, Tools, Resources, Prompts, Hosting, Plugin, and Gateway projects as one published MCP system.

The project targets .NET 10, uses xUnit v3 on Microsoft.Testing.Platform, and inherits its test-runner configuration from [`eng/Tests.props`](../../eng/Tests.props).
It references the product projects directly and builds [CheatEngine.Mcp.LiveTarget](../CheatEngine.Mcp.LiveTarget/README.md) without referencing that executable's assembly.

## Run the tests

Use the SDK pinned by [`global.json`](../../global.json).
Run the normal portable suite from the repository root:

```powershell
dotnet test --solution CheatEngine.Mcp.slnx --no-restore --no-build --filter-not-trait Category=LiveQualification --filter-not-trait Category=NativeLua --fail-skips on
```

Run `dotnet restore CheatEngine.Mcp.slnx --locked-mode` and `dotnet build CheatEngine.Mcp.slnx --no-restore` first when the solution has not already been restored and built.
`--fail-skips on` makes an accidental skipped test fail, so an unavailable environment must be excluded by its trait instead of hidden by a skip.

### Seeded fuzz tests

The portable suite includes bounded, deterministic generated cases without an extra fuzzing dependency.
`Fuzzing/` covers integer encoding, invalid value inputs, hexadecimal parsing, pointer-scan file schemas, and 192 generated valid/malformed IL2CPP dump cases.
`MemoryInspectionFuzzTests` covers UTF-8/UTF-16LE string spans and pagination, malformed byte sequences, and x86/x64 vtable reads.
`PointerBatchFuzzTests` covers generated signed-offset graphs, unreadable prefixes, exact hop output, and read budgets.

These tests use fixed seeds and independent byte or graph expectations. Reproduce a failure with the reported seed and case index before changing the generator.
All loops and allocations are bounded, honor test cancellation, and run in the normal portable CI suite.
They exercise managed input handling and Client doubles; they do not replace native host qualification or sustained coverage-guided fuzzing.

### Native Lua tests

The `NativeLua` category loads fixed Lua scripts into a real x64 Lua 5.3 DLL while stubbing Cheat Engine APIs.
Set `CHEATENGINE_MCP_LUA53_PATH` to the DLL before running the category:

```powershell
$env:CHEATENGINE_MCP_LUA53_PATH = 'C:\Program Files\Cheat Engine\lua53-64.dll'
dotnet test --project tests/CheatEngine.Mcp.Tests --filter-trait Category=NativeLua --fail-skips on
Remove-Item Env:CHEATENGINE_MCP_LUA53_PATH
```

Run this category when changing fixed Lua source, the Lua runtime, the job kernel, or the plugin status indicator.

### Live qualification

The `LiveQualification` category launches real local Cheat Engine copies and attaches them only to the disposable live target.
It is opt-in, refuses to run in CI, requires Cheat Engine and DebugView to be closed, and writes its private copies, backups, and reports outside the checkout.

Follow [Live qualification](../../CONTRIBUTING.md#live-qualification) exactly, including publishing the matching configuration and supplying the required acknowledgement environment variable.
Do not include this category in an ordinary local test run.

The compiler-only qualification is a separately approved scenario. It is never selected by ordinary VL runs: set `CHEATENGINE_MCP_LIVE_QUALIFICATION_SCENARIO` to `compiler` and `CHEATENGINE_MCP_LIVE_CODE_EXECUTION_QUALIFICATION` to its exact acknowledgement from [the compiler policy](../../docs/qualification/all-tools/compiler-probe-policy.md). It refuses CI, a non-clean/unidentified candidate, a saved Cheat Engine `Don't use tempdir` override, or a missing reviewed Release distribution before a private run directory or Cheat Engine process is created. It compiles only the policy's fixed payloads and does not authorize injection or invocation.

The `assembler` and `debugger` scenarios also require their own exact acknowledgement in addition to the ordinary
live opt-in. Their fixed cases and cleanup requirements are described in
[the additional native cases](../../docs/qualification/all-tools/additional-native-cases.md). They use the same
private hosts and disposable targets. Ordinary workflows retain all four optional gates disabled.

The `inspection` scenario runs the existing process/thread, memory-read, utility, assembly-byte and
reversible symbol-preference checks independently of the full workflow. It uses the ordinary live
acknowledgement, disables all four optional gates, and verifies gateway restart and owned-target health.
It does not establish completion of the full workflow or plugin reload.

The `state-workflows` scenario separately runs the existing record/structure/symbol state, owned pointer,
roadmap/file/table, code-analysis, module-export, dissection and signature cases, followed by target,
gateway and host restarts and persisted-note checks. It uses the ordinary live acknowledgement and all
four optional gates disabled. File/table access is confined to this run's roadmap and pointer fixtures.
It does not replay the failing resource/scanner prefix or qualify the full workflow, debugger or plugin reload.

The `process-create` scenario uses the ordinary live acknowledgement with all four optional gates disabled. It also
requires the installed .NET Framework compiler to build its fixed owned fixture. It produces its own session receipts.
The `mono` scenario uses the privately extracted x64 Mono runtime and a dedicated fixed-Mono acknowledgement;
only target-code execution is enabled. See the [runner contracts](../../docs/qualification/all-tools/runners.md).
The report's `-LiveSummaries` parameter combines separate sessions only when their product binaries match;
each tool's operation and cleanup receipts must belong to the same passing session.

## Test layout

| Path | Scope |
| --- | --- |
| `Architecture/` | Project graph, layering, composition, deployment, and repository rules. |
| `Contract/` | MCP catalog, initialization, schema, composition, license, and deployment snapshots. |
| `Core/` | Primitive composition, execution, state, JSON, target handling, and shared utilities. |
| `Fuzzing/` | Seeded value, parser, and pointer-file boundary cases. |
| `Tools/` | Tool-domain behavior, validation, result contracts, jobs, and resource ownership. |
| `Resources/` | Knowledge documents, resource metadata, live-resource projections, URI validation, and completion. |
| `Prompts/` | Workflow prompt schemas, completion, validation, rendering, and linked documentation. |
| `Hosting/` | Backend host lifecycle, instance discovery, gateway routing, resource lists, completions, and correlation. |
| `Plugin/` | Plugin composition, configuration, lifecycle, logging, and deployment behavior. |
| `NativeLua/` | Fixed Lua scripts exercised by the real Lua runtime. |
| `LiveQualification/` | Maintainer-authorized native qualification against separate Cheat Engine instances and disposable targets. |
| `Support/` | Client doubles, loopback hosts, composition helpers, logging captures, golden-file support, and fixtures. |

Tests that need serial access share the named serial collections in `Support/` and `LiveQualification/`.
Prefer these existing fixtures and the Client contract doubles over mocks of Cheat Engine internals.
`Support/McpFileScratch.cs` owns the shared fixed-drive file fixture, and `Support/NativeLuaProbeOperation.cs` owns arbitrary Lua probes used only by tests.
The `LiveSandboxSession` partial files group startup, hosts, targets, distribution, evidence, cleanup, safety, and driver generation while retaining one session owner.

## Contract snapshots

`Contract/Golden/` contains reviewed snapshots of observable MCP behavior, including tool, resource, prompt, initialization, and packaged-plugin output.
The test suite compares output to those files by default.

`AllToolBehaviorEvidenceTests` requires a positive adapter mapping for every production backend tool,
checks the referenced test methods and rejects duplicate evidence identities. Catalog-derived
`AllToolGatewayQualificationTests` exercise routing and admission for every backend tool, plus
gateway discovery. See the [double audit](../../docs/qualification/all-tools/test-audit.md) for
per-tool outcomes and the distinction from native-host qualification.

Regenerate a snapshot only for an intended, reviewed contract change:

```powershell
$env:CHEATENGINE_MCP_UPDATE_GOLDEN = '1'
dotnet test --solution CheatEngine.Mcp.slnx --no-restore --no-build --filter-not-trait Category=LiveQualification --filter-not-trait Category=NativeLua --fail-skips on
Remove-Item Env:CHEATENGINE_MCP_UPDATE_GOLDEN
dotnet test --solution CheatEngine.Mcp.slnx --no-restore --no-build --filter-not-trait Category=LiveQualification --filter-not-trait Category=NativeLua --fail-skips on
```

Review every changed file in `Contract/Golden/` before submitting the change.
Update the relevant knowledge base and project README when the changed behavior is user-visible.

## Writing tests

Use xUnit v3 `Fact` and `Theory` tests with names in the `Subject_Condition_Outcome` form.
Make assertions against observable contracts, result schemas, host effects, resource ownership, and cleanup behavior.

Use `TestActivation`, `TestMcpPipeline`, `GatewayTestServers`, `ClientTestDouble`, and other support helpers before adding new test infrastructure.
Prefer real loopback MCP transport when testing host and gateway behavior.

A tool guarded by a capability needs a test that proves it makes no mutating Client call while the gate is disabled.
Every fixed Lua script needs both a portable test and a NativeLua test.
Do not treat portable tests as evidence of native Cheat Engine host compatibility.

The [full-tool qualification checklist](../../docs/qualification/all-tools/README.md) and its reviewed
per-tool evidence map distinguish routing, adapter results using doubles, direct Lua-body results and live
host results. Generate the evidence report with `eng/Write-AllToolQualificationReport.ps1` after collecting
the relevant TRX files. A catalog-wide routing pass alone does not establish that every backend tool works.

For repository-wide build, formatting, package, and contribution requirements, see [CONTRIBUTING.md](../../CONTRIBUTING.md).
