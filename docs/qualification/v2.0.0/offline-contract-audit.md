# Offline contract audit

Date: 2026-10-09.
Scope: working-tree source, public contract metadata, result mappings and portable checks.
This is not installed-collector, native workflow, timing, or final-package qualification.

## Coverage

The metadata inventory is `tests/CheatEngine.Mcp.Tests/Contract/Golden/tool-summary.txt`: 193 backend tools and the gateway's `instance_list`.
The remaining-domain reviewer traced all 162 tools outside the previously reviewed Asm, Record, Runtime and Table domains.
The result-shape reviewer covered the other domains and gateway; the managed collector review is recorded in [contract-checkpoint.md](contract-checkpoint.md).

| Domain | Backend tools | Metadata and result review |
| --- | ---: | --- |
| Aob | 3 | An indeterminate scan qualifies its empty result with `exact`, `unique` and `targetVerified`; no additional mismatch. |
| Asm | 8 | Earlier patch-release `may_prompt` correction; optional diagnostics and documented empty hook-template bytes retained. |
| Code | 14 | Each search creates a fresh job, so its starter is non-idempotent. Pinned dissector source confirms nil tables mean no recorded references, strings or functions. |
| Debugger | 18 | Feature-dependent branches traced; invalid state, optional registers and loss/truncation qualifiers distinguish unavailable observations. |
| DotNet | 10 | Constant metadata codes and collector array positions no longer promise declared parameter types or ordinals; see the pinned-source analysis. |
| Exec | 7 | Fixed execution requirement and conditional kernel compiler requirement match the implementation; unavailable paths/diagnostics remain optional. |
| Kernel | 7 | Watch polling drains a native log and can evict retained events. Initialization probes must distinguish unavailable from an observed false. |
| Lua | 2 | API lookup reads the host installation's `celua.txt`, so it is open-world. Return and search truncation/counts qualify their arrays. |
| Memory | 19 | Dump overwrite is destructive. The system-module probe must distinguish unavailable from false; read/batch errors already preserve unavailable values. |
| Module | 5 | Import/export enumeration parses the full directory in one dispatch before paging, so it is `host_scan`. The SDK requires a string path; no separate unavailable-path sentinel was established. |
| Mono | 15 | Fixed and conditional execution requirements traced; fields, optional method facts and object reads reviewed against the pinned public collector source. |
| Pointer | 16 | Imported maps/scans have no job, so their job ID is omitted; live capture/search results retain the actual ID. |
| Process | 9 | Target state is qualified by `isOpen`; optional process facts and file size remain unavailable when unobserved. |
| Record | 14 | Earlier destructive-write, AA requirement and prompting corrections; empty dropdown text remains a valid value. |
| Runtime | 6 | Earlier bulk-release prompting correction; empty jobs/resources are valid and process facts are qualified by attachment state. |
| Scan | 8 | A detached main scanner has no process ID. FoundList returns formatted strings, including read-failure markers, without a separate readability flag. |
| Speedhack | 2 | An unavailable symbol probe must not become observed absence or decide whether a mutation is needed. |
| Structure | 15 | Unreadable values are omitted and comparisons classify them. The CE bridge returns field names as strings; an empty name does not establish missing metadata. |
| Symbol | 10 | A non-table module-preference result must be a refusal, distinct from a valid empty list. |
| Table | 3 | Earlier save-overwrite correction; opaque format explicitly qualifies zero inspection counts. |
| Util | 2 | Pure calculations/conversions preserve valid zero and empty results. |

Gateway forwarding preserves the backend result; `instance_list` qualifies incomplete discovery explicitly.
The audit treats a legitimate documented sentinel differently from a false observation. Defensive fallbacks alone are not proof that a native API can actually omit its promised data.

Pinned public source confirms that [`CheckAddress`](https://github.com/cheat-engine/cheat-engine/blob/ec45d5f47f92a239ba0bf51ec5d04a7509c3fd37/Cheat%20Engine/DissectCodeThread.pas#L494-L576) returns false when no references are recorded, and the [Lua wrapper](https://github.com/cheat-engine/cheat-engine/blob/ec45d5f47f92a239ba0bf51ec5d04a7509c3fd37/Cheat%20Engine/LuaDissectCode.pas#L144-L271) uses nil for these empty lists. Those normalizations remain unchanged.
[`FoundList.Value`](https://github.com/cheat-engine/cheat-engine/blob/ec45d5f47f92a239ba0bf51ec5d04a7509c3fd37/Cheat%20Engine/LuaFoundlist.pas#L82-L100) always returns text, and [its reader](https://github.com/cheat-engine/cheat-engine/blob/ec45d5f47f92a239ba0bf51ec5d04a7509c3fd37/Cheat%20Engine/foundlisthelper.pas#L626-L665) includes formatted failure markers. The result description now states that a formatted or empty value is not proof of readability and directs callers to `memory_read` to verify it.

## Corrections and verification boundaries

Six more tools receive effect-metadata corrections: `code_start_search`, `memory_dump_to_file`, `lua_find_api`, `kernel_poll_watch`, `module_list_exports` and `module_list_imports`.
The kernel exception is explicit in contract validation: this tool must be destructive, non-read-only and non-idempotent, while passive poll tools retain their existing rule.
Recovery guidance and the legacy tool map distinguish the native-log drain from the passive retained-page primitive.
The module classification is conservative source review, not a measured duration or a claim that the work was made faster.
Its exports resource projection is removed before contract freeze because the unchanged resource validator admits only short source tools. Explicit `module_list_exports` calls remain available; the prerelease URI removal and replacement are recorded in the release notes.

The result corrections cover imported pointer job IDs, memory/kernel status probes, detached scanner IDs, module-preference failures and speedhack probe failures.
The prepared fixed-Lua regressions subsequently passed in the explicitly authorized standalone Release NativeLua suite: 626 passed, zero failures/skips. These run against a standalone Lua 5.3 runtime with CE-shaped host stubs; they do not qualify actual CE operations or collector behavior. Live CE, target attachment, native diagnostic, debugger and extended compiler/injection work remain stopped.
The .NET parameter correction preserves raw payload fields and documents unrecoverable collector ambiguity; it does not synthesize types from signature text.

Both Debug and Release portable suites passed 4,135 tests; builds, style and whitespace checks passed. Independent Astra final review found no remaining actionable findings in this scoped patch and its evidence; details are recorded in [remaining-checkpoint.md](remaining-checkpoint.md).
Roadmap rows 3.03 and 3.06 still require the applicable native and exact-candidate evidence before final contract freeze.
