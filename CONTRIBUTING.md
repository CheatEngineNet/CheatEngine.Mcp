# Contributing to CheatEngine.Mcp

Thank you for helping.
This page covers what you need to build, test and submit a change.
The binding rules for code and agents are in [AGENTS.md](AGENTS.md); the background is in [Development](docs/development.md), [Testing](docs/testing.md) and [Packaging](docs/packaging.md).

> **Status.** The repository is moving to the 2.0.0 (v2) contract.
> Anything marked **(v2, in progress)** is an adopted design that the code does not implement yet.

## Ground rules

- Keep to responsible use: features, examples and tests target software you own or are authorized to modify, and the project does not accept work whose purpose is to bypass anti-cheat, DRM or license checks; see [SECURITY.md](SECURITY.md) and [docs/security.md](docs/security.md).
- Cheat Engine is reached only through the `CheatEngine.Client` NuGet package; the plugin project alone also references `CheatEngine.SDK` directly, for the generated entry point and the native Lua bridge. No source checkout, submodule or other Cheat Engine wrapper.
- Never commit `skills/cheatengine-mcp/references/local-cheat-engine.md`: it describes your own installation, it is ignored by Git, and `eng/Publish.ps1` refuses to distribute it. Start from `local-cheat-engine.example.md` instead.
- Never expose a bearer token: not in code, tests, logs, screenshots, issues or pull requests. The same goes for discovery records under `%LOCALAPPDATA%\CheatEngine.Mcp\instances`, personal paths and e-mail addresses.
- Preserve licenses, notices and provenance headers, and unrelated work you find in the tree.
- Report vulnerabilities privately as described in [SECURITY.md](SECURITY.md), never in a public issue or pull request.

## Prerequisites

| Tool | Why |
|---|---|
| Windows x64 | The plugin, the native Lua bridge and the live target are x64-only. |
| .NET SDK 10.0.401, exactly | [`global.json`](global.json) pins it with `rollForward: disable` and `allowPrerelease: false`; `dotnet --version` in the repository root must print `10.0.401`. |
| PowerShell 7 (`pwsh`) | Runs [`eng/Publish.ps1`](eng/Publish.ps1). |
| Visual Studio or the Visual Studio Build Tools with the C++ desktop workload, and `vswhere.exe` on `PATH` | Only for publishing the Native AOT gateway, which needs the MSVC linker and the Windows SDK (v2, in progress). Today's gateway is a self-contained single-file executable and needs no C++ toolchain. `vswhere.exe` normally lives in `%ProgramFiles(x86)%\Microsoft Visual Studio\Installer`. |
| A Lua 5.3 x64 DLL, such as Cheat Engine's `lua53-64.dll` | Only for the NativeLua tests. |
| Cheat Engine 7.7.0.10621 x64 with the .NET 10 runtimes it needs | Only for live qualification; see [Compatibility](docs/compatibility.md). |

`global.json` also selects Microsoft.Testing.Platform as the test runner, so `dotnet test` takes the `--solution`, `--project` and `--filter-*` options shown below.

Any editor works, but see [Formatting](#formatting) before you commit from JetBrains Rider.

## Build and test

Run these from the repository root; they are the commands of [AGENTS.md](AGENTS.md#commands) and of the CI workflow:

```powershell
dotnet restore CheatEngine.Mcp.slnx --locked-mode
dotnet build CheatEngine.Mcp.slnx --no-restore
dotnet test --solution CheatEngine.Mcp.slnx --no-restore --no-build --filter-not-trait Category=LiveQualification --filter-not-trait Category=NativeLua --fail-skips on
dotnet build CheatEngine.Mcp.slnx -c Release --no-restore
pwsh -NoProfile -File eng/Publish.ps1
dotnet format style CheatEngine.Mcp.slnx --no-restore --severity warn --verify-no-changes
dotnet format whitespace CheatEngine.Mcp.slnx --no-restore --verify-no-changes
```

- Warnings are errors, code style is enforced in the build, and NuGet audit fails the restore on high and critical advisories.
- `eng/Publish.ps1` publishes Release by default; pass `-Configuration Debug` for the Debug distribution. Its output layout is in [Packaging](docs/packaging.md).
- `--fail-skips on` turns a skipped test into a failure: tests that cannot run in an environment are excluded by trait, never skipped.

### NativeLua tests

The NativeLua tests run the fixed Lua scripts against a real Lua 5.3 DLL with Cheat Engine functions stubbed.
They are excluded from CI and fail, rather than skip, when the DLL is missing:

```powershell
$env:CHEATENGINE_MCP_LUA53_PATH = 'C:\Program Files\Cheat Engine\lua53-64.dll'
dotnet test --project tests/CheatEngine.Mcp.Tests --filter-trait Category=NativeLua
Remove-Item Env:CHEATENGINE_MCP_LUA53_PATH
```

Run them whenever you change a fixed Lua script, the Lua runtime, the job kernel or the status indicator.

### Live qualification

Live qualification starts real Cheat Engine processes, so it follows strict rules:

- Run it only when a maintainer asks for it, and announce each run before you start it.
- Close every Cheat Engine instance, the CE tutorials and DebugView first; the runner refuses to start while one of them is running and never stops them for you.
- It only attaches to its own disposable target, [`tests/CheatEngine.Mcp.LiveTarget`](tests/CheatEngine.Mcp.LiveTarget/README.md).
- It never runs in CI: it refuses when `CI=true`.
- Publish the matching configuration first, then opt in with the exact acknowledgement:

```powershell
pwsh -NoProfile -File eng/Publish.ps1
$env:CHEATENGINE_MCP_LIVE_QUALIFICATION = 'I_AUTHORIZE_CE77_LIVE_PROBES_ON_A_DISPOSABLE_TARGET'
dotnet test --project tests/CheatEngine.Mcp.Tests -c Release --filter-trait Category=LiveQualification --fail-skips on
Remove-Item Env:CHEATENGINE_MCP_LIVE_QUALIFICATION
```

- The runner copies the installed Cheat Engine into two private folders and never edits the installation; `CHEATENGINE_MCP_LIVE_QUALIFICATION_CE_DIRECTORY` selects another installation.
- It backs up and restores Cheat Engine's user settings, and keeps reports and backups under `%LOCALAPPDATA%\CheatEngine.Mcp.LiveQualification\runs`, outside the checkout.
- It stops when a stock autorun script such as `monoscript.lua` differs from the reviewed version; never update the expected hash without a review.
- Kernel, hypervisor and code-execution tools are never exercised live.

More detail is in [Testing](docs/testing.md#live-qualification).

### Gates

Maintainers name the checks a change must pass with these gates:

| Gate | Contents |
|---|---|
| V | Locked restore, Debug build, portable tests. |
| V+ | V, both `dotnet format` checks, Release build, `eng/Publish.ps1`, and a reviewed diff of the golden files. |
| VN | The NativeLua suite. |
| AOT | Native AOT publication of the gateway and its executable smoke test (v2, in progress). |
| VL | Live two-instance qualification, announced, with Cheat Engine closed. |

A pull request needs V+ at least, and VN when it touches Lua.

## Packages and lock files

- Package versions are managed centrally in [`Directory.Packages.props`](Directory.Packages.props), with transitive pinning; a `PackageReference` never carries a `Version`.
- Every project commits its `packages.lock.json`, and CI restores with `--locked-mode`, so a lock file that does not match the project graph fails the build.
- To change a version, edit `Directory.Packages.props`, run `dotnet restore CheatEngine.Mcp.slnx --force-evaluate`, and commit every lock file that changed, including those of projects that only reference the changed project.
- [`nuget.config`](nuget.config) clears every source except nuget.org.
- Never add a direct `PackageReference` to a package that the .NET or ASP.NET Core shared framework already provides; use a `FrameworkReference`. The CEMCP001 guard fails the build when the plugin would ship such a copy.
- The `Microsoft.Extensions.*` and `Microsoft.DiaSymReader` pins follow the Client package and the runtime of the pinned SDK; change them together with the SDK.
- A new package that ships in the plugin folder or the gateway needs a row in [`THIRD-PARTY-NOTICES.md`](THIRD-PARTY-NOTICES.md) and its license text under `licenses/`; `ThirdPartyNoticesTests` and `eng/Publish.ps1` check both. It also changes the `plugin-files.txt` golden file.
- Dependabot pull requests may update `Directory.Packages.props` without every downstream lock file; run the `--force-evaluate` restore on the branch and push the lock files.

## Formatting

[`.editorconfig`](.editorconfig) is the single source of formatting rules: tabs in C#, CRLF line endings, explicit types, file-scoped namespaces, braces, and C# 14 extension blocks for builders.
The build enforces the style (`EnforceCodeStyleInBuild`), and CI runs both `dotnet format` checks.

Run the formatter before every commit:

```powershell
dotnet format style CheatEngine.Mcp.slnx --no-restore --severity warn
dotnet format whitespace CheatEngine.Mcp.slnx --no-restore
```

JetBrains Rider's own formatter disagrees with `.editorconfig`: files reformatted by Rider failed the build with IDE0055 (formatting) and IDE0048 (parentheses).
If you use Rider, run `dotnet format` afterwards, or disable Rider's reformat-on-save for this solution.

## Golden files

`tests/CheatEngine.Mcp.Tests/Contract/Golden` holds reviewed snapshots of the public contract: the gateway and backend `tools/list` and `initialize` responses, and the plugin output file list.
A golden mismatch means the contract changed.

- Regenerate goldens only for an intended change: set `CHEATENGINE_MCP_UPDATE_GOLDEN=1`, run the tests once, remove the variable, and run the tests again without it.
- Review every line of `git diff tests/CheatEngine.Mcp.Tests/Contract/Golden` and explain the change in the pull request.
- A tool, resource, prompt or configuration change also updates the operator skill (`skills/cheatengine-mcp`, including `references/tool-catalog.md`, which `SkillCatalogTests` checks) and the affected pages under `docs/`.

## Writing tests

- xUnit v3 `[Fact]` and `[Theory]` tests, named `Subject_Condition_Outcome`, for example `MinimumLevel_TransportCategory_IsNeverBelowInformation`.
- Prefer the Client contract doubles and real loopback transport over mocking Cheat Engine internals.
- A gated tool gets a test proving zero mutating Client calls when its gate is off.
- Every fixed Lua script gets a portable check and a NativeLua test.
- Do not equate offline tests with native host verification.

[Testing](docs/testing.md) describes the doubles, harnesses and categories.

## Branches, commits and pull requests

- `main` is the default branch and the target of every pull request; never push to it directly.
- Branch from `main` with a short topic name such as `feat/pointer-batch`, `fix/scan-status` or `docs/testing`.
- Keep one topic per pull request, and keep formatting-only changes separate from behavior changes.
- Write commit subjects in the imperative, in sentence case, for example `Fix debugger captures and guard unsafe VEH reattachment`.
- Fill in the [pull request template](.github/PULL_REQUEST_TEMPLATE.md): the gates you ran, whether goldens changed and why, and which docs you updated.
- CI (`.github/workflows/build.yml`) runs the V+ commands on every pull request to `main`, and SonarQube analyzes it; both must pass.
- AI coding agents never commit, push or publish without the maintainer's explicit authorization.

## Documentation

- One fact has one home: link to it instead of repeating it.
- Markdown files are UTF-8 without a byte order mark, ASCII, with CRLF line endings, one sentence per line, and relative links.
- Mark adopted designs that the code does not implement yet as "(v2, in progress)", and remove the mark when the code lands.

## License

CheatEngine.Mcp is licensed under the [MIT License](LICENSE); by contributing, you agree that your contribution is licensed under the same terms.
Third-party components keep their own licenses, listed in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
