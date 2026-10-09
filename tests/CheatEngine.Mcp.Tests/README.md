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

The compiler-only qualification is a separately approved scenario. It is never selected by ordinary VL runs: set `CHEATENGINE_MCP_LIVE_QUALIFICATION_SCENARIO` to `compiler` and `CHEATENGINE_MCP_LIVE_CODE_EXECUTION_QUALIFICATION` to its exact acknowledgement from [the compiler policy](../../docs/qualification/v2.0.0/compiler-probe-policy.md). It refuses CI, a non-clean/unidentified candidate, a saved Cheat Engine `Don't use tempdir` override, or a missing reviewed Release distribution before a private run directory or Cheat Engine process is created. It compiles only the policy's fixed payloads and does not authorize injection or invocation.

## Test layout

| Path | Scope |
| --- | --- |
| `Architecture/` | Project graph, layering, composition, deployment, and repository rules. |
| `Contract/` | MCP catalog, initialization, schema, composition, license, and deployment snapshots. |
| `Core/` | Primitive composition, execution, state, JSON, target handling, and shared utilities. |
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

## Contract snapshots

`Contract/Golden/` contains reviewed snapshots of observable MCP behavior, including tool, resource, prompt, initialization, and packaged-plugin output.
The test suite compares output to those files by default.

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

For repository-wide build, formatting, package, and contribution requirements, see [CONTRIBUTING.md](../../CONTRIBUTING.md).
