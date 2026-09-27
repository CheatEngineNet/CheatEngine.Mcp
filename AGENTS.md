# Repository guidance for contributors and coding agents

## Scope and working tree

- This file applies to the whole repository. A nested `AGENTS.md` may add rules for its directory.
- Preserve changes that are already present in the working tree. Do not reset, discard, reformat wholesale, commit,
  push, publish, install the plugin, or start Cheat Engine unless the maintainer explicitly asks.
- Keep a change focused. Update documentation, the operator skill, generated contract snapshots, and tests when their
  public behaviour changes.

## Architecture and dependencies

- `CheatEngine.Client` is the only Cheat Engine NuGet package used by Core. Only `srcs/CheatEngine.Mcp.Plugin` may
  reference `CheatEngine.SDK` directly, because it owns the SDK entry point and native bridge.
- Compose primitives only through `AddTools()`, `AddResources()`, and `AddPrompts()`. Do not use assembly scanners,
  `WithTools<T>`, or `ActivatorUtilities`.
- Keep Core, Tools, Resources, and Prompts free of ASP.NET Core and host UI dependencies. Hosting routes primitive
  metadata; the Plugin and Gateway are the composition roots.
- Prefer typed `CheatEngine.Client` operations. A capability absent from Client may use a fixed, bounded Lua body
  through the Client dispatch boundary. Never interpolate caller data into Lua source.
- Use source-generated JSON metadata for public inputs and outputs. Changes must remain compatible with
  reflection-disabled JSON and Native AOT analysis of the gateway.

## v2 contract and migration

- `libs/CheatEngine.Mcp.Core/Contract/CheatEngineToolNames.cs` is the reviewed v2 name inventory: gateway-local
  `instance_list` plus 172 backend names in 21 domains. `AddTools()` registers every backend v2 name.
- `tools/list` and its generated schemas are the authority for exact arguments, results, annotations, and descriptions
  in the installed build. Do not introduce aliases or restore historic tool names.
- Add a v2 tool in its domain builder with its JSON context, contract annotations, capability requirement, bounded
  result shape, error/host-effect semantics, and resource/job ownership. Update the historic-name migration table only
  when an older public name needs an explicit documented replacement or exclusion.
- Keep static prompts and `cheatengine://docs/...` resources aligned with the operator skill. Live resources must
  project one read-only tool result and use the gateway instance routing rules.

## Safety and host state

- Work only with software the operator is authorized to inspect or modify. Never add features, examples, or tests for
  bypassing anti-cheat, DRM, licences, online economies, or other users' data.
- Treat memory writes, records, patches, Lua, Auto Assembler, injection, process creation, file writes, debugger
  actions, DBK, and DBVM as host-affecting work. Represent partial or unknown effects honestly and provide a cleanup
  path.
- Respect `Mcp` feature gates and file-root policies. Never log or expose discovery records, bearer tokens, personal
  paths, or secrets read from target memory.
- Long work must be a bounded job that can be polled and stopped. Track target-owned resources and release them safely
  before a process transition.

## Validation

Run the smallest relevant checks first. Before handing off a product change, normally run:

```powershell
dotnet restore CheatEngine.Mcp.slnx --locked-mode
dotnet build CheatEngine.Mcp.slnx --no-restore
dotnet test --solution CheatEngine.Mcp.slnx --no-restore --no-build --filter-not-trait Category=LiveQualification --filter-not-trait Category=NativeLua --fail-skips on
dotnet format style CheatEngine.Mcp.slnx --no-restore --severity warn --verify-no-changes
dotnet format whitespace CheatEngine.Mcp.slnx --no-restore --verify-no-changes
```

- A public primitive, prompt, resource, schema, packaging, or configuration change needs a deliberate update and review
  of the matching golden files. Set `CHEATENGINE_MCP_UPDATE_GOLDEN=1` only for that regeneration, then run the test
  again with the variable removed.
- Run the NativeLua suite when changing a fixed Lua script, Lua runtime, job kernel, or status indicator. Live
  qualification requires its explicit acknowledgement and an installed Cheat Engine 7.7 x64; it is a separate manual
  gate.
- Markdown is UTF-8 without a BOM, CRLF, one sentence per line, and uses relative links. Mark adopted v2 design that is
  not implemented as `(v2, in progress)`.

## Useful references

- [CONTRIBUTING.md](CONTRIBUTING.md) has the complete build, package, test, and pull-request rules.
- [docs/development.md](docs/development.md) explains composition and the test architecture.
- [docs/configuration.md](docs/configuration.md) documents the supported settings and precedence.
- [docs/reference/tools.md](docs/reference/tools.md) explains how the frozen v2 inventory relates to the running
  catalogue.
