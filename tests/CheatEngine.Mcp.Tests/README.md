# CheatEngine.Mcp.Tests

The single test project: xUnit v3 on Microsoft.Testing.Platform, configured by [
`eng/Tests.props`](../../eng/Tests.props).
It references every product project and builds the [live target](../CheatEngine.Mcp.LiveTarget/README.md) without
referencing its assembly.
The full guide is [docs/testing.md](../../docs/testing.md).

## Layout

- `Core/`, `Tools/`, `Hosting/`, `Plugin/`: tests that mirror the product projects.
- `Architecture/`: the project graph, composition and repository rules.
- `Contract/`: golden snapshots under `Contract/Golden`, composition parity, tool contract, skill catalog and
  third-party notices.
- `NativeLua/`: fixed Lua scripts against a real Lua 5.3 DLL (`Category=NativeLua`).
- `LiveQualification/`: the two-instance live scenario (`Category=LiveQualification`); `Infrastructure/` holds helpers
  adapted from CheatEngine.Client, with provenance in its `NOTICE.md` and the license in [
  `licenses/CheatEngine.Client.LICENSE`](../../licenses/CheatEngine.Client.LICENSE).
- `Support/`: doubles and harnesses such as `ClientTestDouble`, `TestActivation`, `TestMcpPipeline`, `GatewayTestHost`,
  `FakeBackend` and `GoldenFile`.

## Run

From the repository root:

```powershell
dotnet test --solution CheatEngine.Mcp.slnx --no-restore --no-build --filter-not-trait Category=LiveQualification --filter-not-trait Category=NativeLua --fail-skips on
```

- NativeLua: set `CHEATENGINE_MCP_LUA53_PATH` to a Lua 5.3 x64 DLL, then run
  `dotnet test --project tests/CheatEngine.Mcp.Tests --filter-trait Category=NativeLua`.
- Live qualification: only on a maintainer's request, announced, with Cheat Engine closed;
  see [docs/testing.md](../../docs/testing.md#live-qualification).
- Golden files: regenerate only for an intended contract change with `CHEATENGINE_MCP_UPDATE_GOLDEN=1`, then review the
  diff.

## Rules

- Name tests `Subject_Condition_Outcome`.
- Prefer the Client doubles and real loopback transport over mocking Cheat Engine internals.
- Exclude a test by category, never skip it: every run uses `--fail-skips on`.
- Never commit a real token, a personal path or an e-mail address, not even in a fixture or a golden file.
- Offline tests are not native host verification.
