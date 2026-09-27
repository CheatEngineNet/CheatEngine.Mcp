## Summary

<!-- What changes and why. Link the issue it resolves, if any. -->

## Kind of change

- [ ] Bug fix
- [ ] New or changed tool, resource or prompt (MCP contract change)
- [ ] Configuration, packaging or dependency change
- [ ] Documentation or operator skill only
- [ ] Build, tests or CI only

## Gates

The gates are defined in the Gates section of CONTRIBUTING.md. Tick what you ran locally.

- [ ] V: `dotnet restore CheatEngine.Mcp.slnx --locked-mode`, `dotnet build CheatEngine.Mcp.slnx --no-restore` and the portable tests
- [ ] V+: V, `dotnet format style` and `dotnet format whitespace` with `--verify-no-changes`, the Release build and `pwsh -NoProfile -File eng/Publish.ps1`
- [ ] VN: the NativeLua suite with `CHEATENGINE_MCP_LUA53_PATH` (required when a fixed Lua script, the Lua runtime, the job kernel or the status indicator changes)
- [ ] AOT: Native AOT gateway publication and its executable MCP smoke check
- [ ] VL: live qualification, only at a maintainer's request, announced before the run, with Cheat Engine closed and the disposable target
- [ ] Not run: VL (the default)

## Contract and golden files

- [ ] No file under `tests/CheatEngine.Mcp.Tests/Contract/Golden` changed
- [ ] Golden files regenerated with `CHEATENGINE_MCP_UPDATE_GOLDEN=1`, every line of the diff reviewed, and the change explained below

<!-- For a contract change: which tools, parameters, results, annotations or files changed, and why. -->

## Documentation

- [ ] The affected pages under `docs/` are updated
- [ ] The operator skill (`skills/cheatengine-mcp`, including `references/tool-catalog.md`) is updated when tools or configuration changed
- [ ] `THIRD-PARTY-NOTICES.md` and `licenses/` are updated when a shipped package changed
- [ ] Nothing to update

## Safety

- [ ] No token, discovery record, personal path or e-mail address in code, tests, logs, screenshots or this description
- [ ] `skills/cheatengine-mcp/references/local-cheat-engine.md` is not included
- [ ] No live qualification ran without being announced first
- [ ] Cheat Engine is still reached only through `CheatEngine.Client`, with `CheatEngine.SDK` referenced directly by the plugin project alone
- [ ] Formatted with `dotnet format` (not only an IDE formatter)
