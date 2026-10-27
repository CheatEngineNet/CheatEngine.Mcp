# Repository Guidelines

Follow [CONTRIBUTING.md](CONTRIBUTING.md) for binding rules and complete verification gates.

## Project Structure & Module Organization

- `libs/CheatEngine.Mcp.Core/`: shared contracts, execution, jobs, Lua support, and target ownership.
- `srcs/`: Plugin and Gateway composition roots, Hosting transport, and domain-based Tools, Resources, and Prompts projects.
- `srcs/CheatEngine.Mcp.Resources/Knowledge/`: embedded Markdown documents and workflows.
- `tests/CheatEngine.Mcp.Tests/`: architecture, contract, behavior, NativeLua, and live qualification tests; snapshots live in `Contract/Golden/`.
- `tests/CheatEngine.Mcp.LiveTarget/`: disposable native qualification target.
- `eng/`: build, packaging, and qualification scripts; `externals/`: pinned Client and SDK source submodules; `artifacts/`: generated outputs.

## Build, Test, and Development Commands

Use Windows x64, PowerShell 7, and the exact SDK pinned in `global.json`.
Run from the repository root:

```powershell
git submodule update --init --recursive
dotnet restore CheatEngine.Mcp.slnx --locked-mode
dotnet build CheatEngine.Mcp.slnx --no-restore
dotnet test --solution CheatEngine.Mcp.slnx --no-restore --no-build --filter-not-trait Category=LiveQualification --filter-not-trait Category=NativeLua --fail-skips on
dotnet build CheatEngine.Mcp.slnx -c Release --no-restore
pwsh -NoProfile -File eng/Publish.ps1
dotnet format style CheatEngine.Mcp.slnx --no-restore --severity warn --verify-no-changes
dotnet format whitespace CheatEngine.Mcp.slnx --no-restore --verify-no-changes
```

These initialize dependencies, restore locked packages, build, run portable tests, publish the distribution, and check formatting.
Publishing the Native AOT gateway requires MSVC and the Windows SDK.
Follow [README.md](README.md) to install the plugin and configure the stdio gateway locally.

## Coding Style & Naming Conventions

Follow `.editorconfig`: C# tabs (width 4), explicit types, file-scoped namespaces, and opening braces on new lines.
Use two-space indentation for project/configuration files, CRLF endings, PascalCase types/members, and `_camelCase` private fields.
Use `dotnet format`; build warnings are errors.

## Testing Guidelines

Use xUnit v3 on Microsoft.Testing.Platform; name tests `Subject_Condition_Outcome`.
Cover observable behavior, capability refusals, ownership, and cleanup.
Lua changes require NativeLua tests with `CHEATENGINE_MCP_LUA53_PATH` configured.
Live qualification requires a maintainer request and the documented isolated harness.
Distinguish portable, Lua-runtime, and native-host evidence.
Review every intentional golden-file change.

## Commit & Pull Request Guidelines

Use imperative, sentence-case subjects, e.g. `Fix pointer-map width`.
Keep PRs focused, target `main`, link relevant issues, and complete `.github/PULL_REQUEST_TEMPLATE.md`.
Record verification gates, contract changes, and documentation updates.
PRs require V+; Lua changes also require VN.
Agents need explicit authorization to commit, push, or publish.

## Architecture & Safety

Prefer typed Client operations; only Plugin references SDK runtime projects directly.
Preserve source-generated JSON, capability gates, and pinned submodules.
Never interpolate caller data into Lua source.
Preserve unrelated edits and follow [SECURITY.md](SECURITY.md); exclude tokens and discovery records from contributions.
