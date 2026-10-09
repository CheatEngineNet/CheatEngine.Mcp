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
- Internal tool errors include the logged correlation ID; non-object details are preserved under `details.value` alongside `errorId`.
- `record_create` and `table_save` advertise their potentially destructive writes. `record_set_script` declares and checks its Auto Assembler requirement before dispatch; `record_clear` includes the missing recovery hint when that switch is off.
- Record activation/deletion/clear and individual or bulk patch release advertise `may_prompt`, because script execution can block CE or show dialogs.
- `dotnet_get_method_parameters` now describes `elementType` as the collector's metadata-constant code and `index` as its returned array position; declared types come from the optional signature, and the collector can return incomplete or placeholder entries.
- Additional effect annotations cover fresh code-search jobs, destructive file dumps, host-file API lookup, consuming DBVM polls and potentially blocking module-directory reads.
- The prerelease resource URI `cheatengine://instance/modules/{module}/exports{?offset,limit}` is removed: full export-directory reads can block CE and must not be prefetched as a short resource. Use the unchanged `module_list_exports` tool explicitly; module-list and module-detail resources remain available.
- Unavailable memory/kernel status and imported pointer job IDs are omitted instead of fabricated as false or empty text. Detached scanner results omit the process ID; failed module-preference and speedhack probes report refusal. An unavailable post-change speedhack observation omits `hooksInstalled` while retaining the restore resource.
- Module, region, thread, registered-symbol and breakpoint resources now use five-second prepared snapshots. Call the corresponding explicit list/get tool before reading its resource; cold, expired or target-mismatched snapshots report `invalid_state`. Module-name completion also requires a fresh attached-target module list. The explicit enumeration tools declare `blocking_native`; their result limits do not bound CE's underlying enumeration.
- `structure_get` bounds global definition lookup and reads only the requested element page. Thread collection now owns and disposes the StringList required by CE. Failed cleanup of a pointer-reference range search retains one reservation and refuses another search until recovery.

The optional-field omissions, removed exports URI and explicit resource-preparation requirement above are beta-to-stable compatibility exceptions that still require final stable contract review (requirement 2.07). Clients must distinguish omitted status/job fields from observed false or empty values, use `module_list_exports` for exports, and prepare snapshots before navigation. The stable contract has not frozen, and the beta tags are unchanged. The [MCP-only closeout](mcp-closeout.md) permits administrative issue closure without stable sign-off.

The approved compiler-only phases passed on the exact local candidate and environment recorded in [compiler-checkpoint.md](compiler-checkpoint.md), with separate x86 and x64 disposable targets.
Both immediate exports survived both CE shutdowns; raw artifacts were retained at the observation points with distinct per-instance temporary roots.
Shared-temp expiry, unavailable prerequisites, reload, and separate injection remain unqualified, as does the final stable package.

An uninstrumented local candidate also exposed native Lua dispatch corruption, including a private-host crash.
Later instrumented runs did not reproduce that failure, but they do not establish a production fix.
Stable publication remains blocked on resolving it and completing the missing qualification in [remaining-checkpoint.md](remaining-checkpoint.md).

## Support and limitations

The package remains Windows x64 with an x64 Cheat Engine 7.7 host and a self-contained x64 gateway.
The plugin requires x64 .NET 10, ASP.NET Core 10, and Windows Desktop 10 runtimes.
Target architecture and optional compiler, collector, debugger, kernel, and DBVM availability are separate from plugin-host support.
Use the [support matrix](support-matrix.md) and [evidence ledger](release-signoff.md) for exact tested environments and outstanding qualification.

Instruction text is authoritative for supplied pointer analysis; optional instruction bytes validate length and supported address-size facts and are not an independent decoder.
Post-execution register facts and indexed operands can be uncertain or dynamic.
Absolute pointer roots must be refreshed after a target restart; symbolic roots resolve against the currently selected target.
An unreadable final value does not turn a resolved pointer address into a failed chain.

## Upgrade from beta.2 or a folder deployment

1. Preserve the existing matching plugin and gateway pair as a rollback copy, along with personal configuration and CE's runtime configuration.
2. Disable the plugin, close Cheat Engine, and stop the MCP client's gateway before replacing loaded files.
3. Install the plugin DLL and gateway from the same verified package into a new directory.
4. Replace the previous entry for this plugin when its path changes, including a folder deployment, before selecting the new single `CheatEngine.Mcp.dll`; keep unrelated plugin entries.
5. Point the MCP client at the new gateway executable and enable the plugin.
6. Discover a fresh `instanceId` using `instance_list`, then verify `runtime_get_info` before attaching to a target.

Keep personal settings in the existing user data directory; no configuration migration or new dependency is introduced by these changes.
The four capability switches remain enabled by default and are exposure controls, not a sandbox.
File and table allowed roots remain empty by default.
Do not overwrite or delete unrelated CE plugins, tables, records, or settings during an upgrade.

To roll back, close both processes, replace this plugin's CE entry with its previous entry, restore the matching gateway registration and any deliberately changed configuration, and rediscover the new activation.
Do not force replacement of locked files or reuse an old activation identifier.
The [installation recovery guide](../../../srcs/CheatEngine.Mcp.Resources/Knowledge/Documents/connection-troubleshooting.md#several-instances-updates-and-removal) covers a locked gateway, partial upgrade, rollback and removal.
These are migration instructions; clean-install, upgrade, rollback, and supported-client qualification must still be recorded on the final package.

## Release identity

The final notes must name the clean source commit, exact version, qualified environments, public ZIP, and checksum file.
Existing beta tags remain unchanged; stable publication uses a new annotated `v2.0.0` tag.
No local working-build evidence is a claim that stable assets have been published.
