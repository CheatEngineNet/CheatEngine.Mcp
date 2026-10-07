# Stable v2.0.0 release notes draft

Status: Unpublished draft; the current working build still identifies itself as `2.0.0-beta.2`.
Publication requires the final candidate gates in [release-signoff.md](release-signoff.md).

## Changes since beta.2

- `pointer_get_access_info` analyzes supplied x86 or x64 instruction and register facts and explains effective addresses, uncertainty, and pointer-search guidance without live symbol lookup.
- `pointer_read_chains` validates bounded candidate chains with separate resolution, comparison, and optional final-value outcomes, cancellation progress, and target-change guards.
- `exec_compile_csharp` wraps CE's compiler and immediately exports its temporary assembly through the existing file policy; compilation remains separate from injection.
- The backend catalog contains 193 tools; gateway discovery adds `instance_list`.
- Publication compares the actual bundled plugin against reviewed tool, resource, template, and prompt snapshots.
- Build CI now runs the portable suite in Debug and Release.
- The private native fixture supports separate x86 and x64 runs and target restart checks.

The C# compiler's native prerequisites and temporary-file lifetime remain pending the separately approved [compiler qualification policy](compiler-probe-policy.md).
These notes must not describe that feature as natively qualified until its evidence is recorded.

## Support and limitations

The package remains Windows x64 with an x64 Cheat Engine 7.7 host and a self-contained x64 gateway.
The plugin requires x64 .NET 10, ASP.NET Core 10, and Windows Desktop 10 runtimes.
Target architecture and optional compiler, collector, debugger, kernel, and DBVM availability are separate from plugin-host support.
Use the [support matrix](support-matrix.md) and evidence ledger for exact tested environments and outstanding qualification.

Instruction text is authoritative for supplied pointer analysis; optional instruction bytes validate length and supported address-size facts and are not an independent decoder.
Post-execution register facts and indexed operands can be uncertain or dynamic.
Absolute pointer roots must be refreshed after a target restart; symbolic roots resolve against the currently selected target.
An unreadable final value does not turn a resolved pointer address into a failed chain.

## Upgrade from beta.2 or a folder deployment

1. Preserve the existing matching plugin and gateway pair as a rollback copy, along with personal configuration and CE's runtime configuration.
2. Disable the plugin, close Cheat Engine, and stop the MCP client's gateway before replacing loaded files.
3. Install the plugin DLL and gateway from the same verified package into a new directory.
4. Remove an obsolete folder-deployment plugin entry before selecting the new single `CheatEngine.Mcp.dll`.
5. Point the MCP client at the new gateway executable and enable the plugin.
6. Discover a fresh `instanceId` using `instance_list`, then verify `runtime_get_info` before attaching to a target.

Keep personal settings in the existing user data directory; no configuration migration or new dependency is introduced by these changes.
The four capability switches remain enabled by default and are exposure controls, not a sandbox.
File and table allowed roots remain empty by default.
Do not overwrite or delete unrelated CE plugins, tables, records, or settings during an upgrade.

To roll back, close both processes, restore the previous matching plugin/gateway paths and any deliberately changed configuration, and rediscover the new activation.
Do not force replacement of locked files or reuse an old activation identifier.
These are migration instructions; clean-install, upgrade, rollback, and supported-client qualification must still be recorded on the final package.

## Release identity

The final notes must name the clean source commit, exact version, qualified environments, public ZIP, and checksum file.
Existing beta tags remain unchanged; stable publication uses a new annotated `v2.0.0` tag.
No local working-build evidence is a claim that stable assets have been published.
