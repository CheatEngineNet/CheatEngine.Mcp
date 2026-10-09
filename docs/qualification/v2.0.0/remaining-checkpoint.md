# Remaining qualification checkpoint

Date: 2026-10-08.
Status: Reviewed local evidence; native corruption remains a release blocker. No roadmap closure or publication is claimed.

## Candidate identity

The tested checkout was clean at `d3984574e0d0757beebd3ea36deb6f9bef7358d1`.
The tested binaries identify as `2.0.0-beta.2+d3984574e0d0757beebd3ea36deb6f9bef7358d1`.
The staged Release plugin SHA-256 is `C87E44C8D68C54A660E1CFDB9208D411253D6AEE5BA9C5DFDBA8B41BF0682A12`.
The staged Release gateway SHA-256 is `F52824F10B0C33775B5B8BB9C8C9B95094A1CDA73AF51DD0B8C87B60C98F1DC8`.
The local Release ZIP SHA-256 is `F08D289243A285C294F386DD85331BEFB82F206602E289F9882191E2584AD37B`.
The ZIP contains five flat deployment files and is 17,717,063 bytes.
The candidate remains beta-versioned and is not an RC or stable release payload.
The d398457 logs identify the test process as `net10.0|x64`, but these receipts do not emit the exact Windows build or .NET runtime patch.
Promotion into tracked release evidence must join those environment fields from the reviewed candidate preflight rather than infer them here.

## Offline and package evidence

| Evidence | Result | Safe source |
| --- | --- | --- |
| Locked restore | Passed for the nine-project solution restore. | `artifacts/issue-edits/d398457-restore.log` |
| Debug build | Passed with zero warnings and zero errors in 8.34 seconds. | `artifacts/issue-edits/d398457-Debug-build.log` |
| Release build | Passed with zero warnings and zero errors in 7.58 seconds. | `artifacts/issue-edits/d398457-Release-build.log` |
| Debug portable tests | Passed 4,062 of 4,062 with zero failures and zero skips in 1 minute 41.709 seconds. | `artifacts/issue-edits/d398457-Debug-tests.log` |
| Release portable tests | Passed 4,062 of 4,062 with zero failures and zero skips in 1 minute 48.044 seconds. | `artifacts/issue-edits/d398457-Release-tests.log` |
| NativeLua | Passed 618 of 618 with zero failures and zero skips in 2.845 seconds. | `artifacts/issue-edits/d398457-native-lua.log` |
| Format | Style and whitespace commands completed with empty logs. | `artifacts/issue-edits/d398457-format-style.log` and `d398457-format-whitespace.log` |
| Release packaged DLL | The actual Release DLL passed both isolated reflection comparisons with 193 tools, 43 resources, 15 templates, and 43 prompts, followed by `PLUGIN_BUNDLE_PROBE_OK`. | `artifacts/issue-edits/d398457-publish-release.log` |
| Release Native AOT gateway | Native code generation and the executable MCP checks completed for the recorded Release gateway bytes. | `artifacts/issue-edits/d398457-publish-release.log` |
| Release distribution | The five staged files, embedded identities, ZIP inventory, and checksum verification passed. | `artifacts/issue-edits/d398457-publish-release.log`, `d398457-package.log`, and `d398457-stage.log` |

The recorded Release distribution contains `CheatEngine.Mcp.dll`, `CheatEngine.Mcp.Gateway.exe`, `LICENSE`, `README.md`, and `THIRD-PARTY-NOTICES.md`.
Debug publication and its actual packaged-DLL, Native AOT, and distribution checks remain pending.
These results do not qualify native workflows, installation, interactive clients, CI, soak, or a stable release.

## Native lifecycle and workflow result

Evidence ID `LIFECYCLE-X64-01` is run `20261008T005457Z-838e` against the recorded `d3984574e0d0757beebd3ea36deb6f9bef7358d1` Release plugin and gateway hashes.
The native host was Cheat Engine x64 `7.7.1.10828` with executable SHA-256 `CF6CCC664A90C23531EC20F5C41458CFDC1D04679D6D05ED94C06BCECF95544F`.
The run used two private hosts, two owned x64 targets, the ModelContextProtocol .NET test client, and protocol `2025-06-18`.
The native summary leaves `TargetSha256` empty, so this checkpoint does not claim a target-binary hash.
The test process reported failure after 19.257 seconds, with the failing test body reporting 18.373 seconds.

The packaged client checks reached 55 resources, 26 templates, and 43 prompts, but this is not interactive-client qualification.
All four execution gates were disabled for the workflow run.
The bounded memory cases passed integer round trips, partial batch reporting, a 16-byte snapshot, SHA-256 hashing, file-policy refusal, protection restoration, snapshot deletion, and both owned frees.
The independent scanner passed `unknown`, `changed`, `increased`, `decreased`, `reset`, and `between` transitions, then deleted the scanner and freed its owned region.
The bounded AOB cases found the exact pattern in 25 milliseconds, the wildcard pattern in 24 milliseconds, and the `int32` value pattern in 25 milliseconds, each as one exact unique target-verified result.

The run then failed before the control-flow-graph fixture allocation completed.
`memory_allocate` returned `host_refused` with message `The Lua stack does not contain the inputs required by the protected operation.`.
The recorded operation was `Dispatcher.Invoke`, `hostEffect` was `unknown`, and `retryable` was `false`.
The CFG cleanup receipt records `freed=false` and zero cleanup failures because no owned CFG allocation was obtained.
The run stopped both owned hosts and targets, observed enabled and disabled lifecycle indicators for both hosts, restored user state, preserved the source installation, and reported no cleanup failures.
The summary correctly records `passed=false`.
This failed run is evidence only for the completed memory, scanner, AOB, and cleanup cases, and it is not lifecycle-wide or catalog-wide qualification.

## x64 performance evidence

The performance command passed one aggregate test in 1 minute 50.909 seconds.
Each run used two private Cheat Engine instances and measured full stdio request time, response parsing, and value assertion after five unrecorded warm-up pairs.
Each run recorded 100 `memory_read int32` calls and 30 `memory_read_batch` calls with 16 `int32` requests per batch.
The p95 calculation used nearest rank, and candidate limits used the maximum p95 across the three published beta.2 baseline runs.

| Run | Package | Short p50 / p95 / max in ms | Batch p50 / p95 / max in ms | Outcome |
| --- | --- | --- | --- | --- |
| `20261008T005646Z-8411` | Published beta.2 baseline 1 | 9.2240 / 10.5650 / 62.5282 | 9.0115 / 10.4505 / 10.7427 | Passed; cleanup restored and source unchanged. |
| `20261008T005705Z-1b65` | Published beta.2 baseline 2 | 9.7902 / 10.9172 / 61.0560 | 9.3845 / 10.7567 / 11.0492 | Passed; cleanup restored and source unchanged. |
| `20261008T005723Z-469f` | Published beta.2 baseline 3 | 9.7945 / 11.8246 / 61.5980 | 7.2177 / 9.7169 / 10.0798 | Passed; cleanup restored and source unchanged. |
| `20261008T005741Z-c9b1` | `d398457` candidate 1 | 9.5969 / 11.8029 / 45.0871 | 9.5876 / 10.4264 / 10.7751 | Both p95 decisions passed; cleanup restored and source unchanged. |
| `20261008T005800Z-d335` | `d398457` candidate 2 | 9.1233 / 11.7610 / 44.2625 | 8.3695 / 9.4961 / 9.5099 | Both p95 decisions passed; cleanup restored and source unchanged. |
| `20261008T005819Z-99f0` | `d398457` candidate 3 | 6.9373 / 8.3891 / 34.2782 | 6.2625 / 7.4094 / 7.8098 | Both p95 decisions passed; cleanup restored and source unchanged. |

The selected baseline short p95 was 11.8246 milliseconds, producing a candidate limit of 111.8246 milliseconds.
The selected baseline batch p95 was 10.7567 milliseconds, producing a candidate limit of 16.1351 milliseconds.
Every candidate run passed both recorded p95 limits.
Every baseline and candidate summary records `passed=true`, `userStateRestored=true`, `sourceInstallationUnchanged=true`, and zero cleanup failures.
The published beta.2 baseline plugin SHA-256 is `5699ECA2ED06461846A977802DF3E4FA7BF52159F50AA1446C4819908A7692AE`.
The published beta.2 baseline gateway SHA-256 is `2E0E7F8512D8A8AAC0197E982C7660BFA4336EBDB8981543817B709381C83885`.
These measurements qualify only the two recorded x64 memory workloads.
They do not qualify regions, scans, pointers, symbols, structures, dissection, instance discovery, busy responses, jobs, completions, live resources, blocking-native calls, UI responsiveness, resource recovery thresholds, or the two-hour soak.

## Diagnostic candidate 207d6fd

The diagnostic candidate is commit `207d6fdb62c4874185ba843f00add85da1438d1f` with product version `2.0.0-beta.2+207d6fdb62c4874185ba843f00add85da1438d1f`.
The staged Release plugin SHA-256 is `8588CBAC6665CAB094AC40DA0A6B9257835F7B93B5931E6DB1E2EDB9E45F1E26`.
The staged Release gateway SHA-256 is `BD9D4DFAA3A535A0AA388AA570A19E3A2B6D3B17BE09E74888716A28DBCA10F0`.
The local Release ZIP SHA-256 is `AC1E71B09F101BD9A347A7045F52A9795D1F076C1E4D617A305E9E6E847B513E`, and the ZIP is 17,726,054 bytes.
The Release build passed with zero warnings and zero errors in 7.20 seconds.
The Release portable suite passed 4,066 of 4,066 tests with zero failures and zero skips in 1 minute 50.370 seconds.
The actual Release DLL passed two isolated reflection comparisons with 193 tools, 43 resources, 15 templates, and 43 prompts, followed by `PLUGIN_BUNDLE_PROBE_OK`.
The Release Native AOT gateway build and the five-file flat ZIP inventory, checksum, and package verification passed.
These offline and package results are recorded in `artifacts/issue-edits/207d6fd-build.log`, `207d6fd-tests.log`, `207d6fd-publish-release.log`, `207d6fd-package.log`, and `207d6fd-stage.log`.

Evidence ID `DIAGNOSTIC-X64-01` is native run `20261008T010811Z-ded4` against these exact Release plugin and gateway hashes.
The native host was Cheat Engine x64 `7.7.1.10828` with executable SHA-256 `CF6CCC664A90C23531EC20F5C41458CFDC1D04679D6D05ED94C06BCECF95544F`.
The run again used two private hosts, two owned x64 targets, the ModelContextProtocol .NET test client, and protocol `2025-06-18`.
The summary leaves `TargetSha256` empty, so this checkpoint does not claim a target-binary hash.
The memory cases and their cleanup passed before the crash, including protection restoration, snapshot deletion, both owned frees, and zero memory cleanup failures.
The scanner completed the `unknown`, `changed`, `increased`, `decreased`, `reset`, and `between` transitions, then deleted the scanner and freed its owned region with zero scanner cleanup failures.

Host A then crashed during `AobTools.FindValue` with fatal error `0xC0000005`.
The recorded fatal path begins `LuaApi.lua_settop`, `LuaState.SetTop`, `LuaFrame.Dispose`, `MainThreadDispatcher.Dispatch`, and the client dispatch path before reaching `AobTools.FindValue`.
The intended diagnostic 3005 was not reached, so this candidate did not establish a root cause or a fix.
The test body failed after 20.882 seconds, and the assembly failed after 21.596 seconds.
No successful AOB result is claimed for this run.

The summary records `userStateRestored=true`, `sourceInstallationUnchanged=true`, and `passed=false`.
AOB cleanup attempted to free its owned region and recorded one failure; successful release was not confirmed before owned-target shutdown.
The sole `cleanupFailures` entry says host A lacks the expected enabled and disabled MCP menu status indicators because the host crashed.
Host B recorded both MCP and SDK enabled and disabled lifecycle indicators, while host A recorded neither complete indicator pair.
Both host-stop receipts are present.
The parent post-run CIM inventory found no owned Cheat Engine or LiveTarget processes left running.
That process inventory is separate from the failed lifecycle-evidence cleanup check and does not turn the run into a cleanup pass.
This production-package failure remains unresolved.

## Private dispatcher instrumentation

The bounded diagnostic package uses MCP product base `4c0698981f50ee4867bc1efe08a62867fd5c89c3` and the latest harness commit `09d76d7defec28756e621e167731f47efaff2f96`.
The product base passed 4,074 of 4,074 Release portable tests with zero failures and zero skips.
The plugin privately pins `CheatEngine.SDK` version `2.0.1-mcpdiagnostic.1` from SDK base `325c47b573f8bd39a247f1d0101f110fa36c1696` with reviewed local instrumentation.
The private SDK package SHA-256 is `1BFC46AA5377464351B2F50967C0BEC23D7DB90A25B7AB7EDF2EE1EC1CFC9B2C`.
The SDK tracked patch SHA-256 is `682DA09724C37C9AC401951A76EFD63B27F675C7C81FFF667F4F063547F79FD7`, and the new trace-source SHA-256 is `4177B62FCD29AA5C465B45DC0DF19EFE65DF98810F75F70B77164AA2983F5773`.
The exact embedded SDK assembly hashes are recorded in `artifacts/issue-edits/dispatch-diagnostic-provenance.json`.
Strict SDK build and pack completed with zero errors, and the focused SDK suites passed 11 `MainThreadTests` plus 53 `AobBoundedScanTests` with zero failures or skips.
The private MCP package passed both isolated packaged-DLL comparisons, `PLUGIN_BUNDLE_PROBE_OK`, Native AOT publication, and five-file ZIP verification.
Its plugin SHA-256 is `3906E707D8C11947664E384FCA09E43CB1EA2451368D48E87D78C08D5485314A`, gateway SHA-256 is `A04CF2303D77BF7B2F560F1ABFC876C00D57C990330565599BE2981543D184C8`, and ZIP SHA-256 is `0F5A84AD54421AC06CF869F2ADBEE6E7B1C0322E8F63ADAA22613CCEC6BDAF8E`.

The first diagnostic attempt, run `20261008T014302Z-328d`, failed before either host or target launched because the staging predicate did not admit the explicitly selected diagnostic configuration.
That failed attempt records `passed=false`, `userStateRestored=true`, `sourceInstallationUnchanged=true`, and an empty `cleanupFailures` array.
The shared `RequiresCapabilityConfiguration` predicate was corrected in harness commit `09d76d7defec28756e621e167731f47efaff2f96`; 83 focused portable tests passed, and the bounded correction received independent Astra review.

Three subsequent fresh x64 diagnostic cases passed against the same private instrumented package.

| Run | Case | Completed evidence | Cleanup |
| --- | --- | --- | --- |
| `20261008T014451Z-4fb2` | `AobOnly` | Two AOB tool calls completed three exact, unique, bounded, target-verified native scans. | State restored, source unchanged, both host lifecycle pairs observed, cleanup failures empty. |
| `20261008T014508Z-1e67` | `NamedScanThenAob` | The six named-scan transitions completed before the same two AOB calls and three native scans. | State restored, source unchanged, both host lifecycle pairs observed, cleanup failures empty. |
| `20261008T014525Z-4e08` | `MemoryNamedScanThenAob` | The bounded memory cases, six named-scan transitions, two AOB calls, and three native scans completed. | State restored, source unchanged, both host lifecycle pairs observed, cleanup failures empty. |

The authoritative summaries and per-host diagnostic logs are under `C:\Users\Shadow\AppData\Local\Packages\OpenAI.Codex_2p2nqsd0c76g0\LocalCache\Local\CheatEngine.Mcp.LiveQualification\runs\<run-id>`.
The parent post-run CIM inventory found no owned Cheat Engine or LiveTarget processes left after these attempts.
The trace field named `top` records the frame's saved top, not a contemporaneous Lua stack top measurement.
The private `.1` package also used the SDK tag's checked-in native Lua bridge, SHA-256 `889DC4C231D182F9B7BAA9E29880555AAD949F42023DDE232FE327542C3C5387`, whereas the published SDK 2.0.0 package contains bridge `B008C8D8C136187F241542E6223DC0831999D8300DC2C4C01E1CF49F6FBA7698`.
Both managed instrumentation and native bridge bytes therefore changed in these three runs. The added logging can change timing, and different bridge hashes alone do not establish a functional difference or ABI defect.
These passing diagnostic cases neither isolate the effect of instrumentation nor identify the root cause or prove a production fix.
Each of the three passing summaries explicitly records `stableQualification=false`.
A controlled follow-up restored the exact published bridge while retaining the managed trace patch, as recorded below.

## Published-bridge diagnostic follow-up

Private SDK `2.0.1-mcpdiagnostic.2` retains the managed trace patch and embeds the exact published `B008C8D8C136187F241542E6223DC0831999D8300DC2C4C01E1CF49F6FBA7698` native bridge.
The final bundled plugin's seven SDK assemblies and native bridge were compared byte-for-byte with the private package. The private package SHA-256 is `E28B6AA10006698BCBBE4672814C5DD519E79414D4A38AA57AD62270F57F034C`.
The plugin SHA-256 is `D75E915607687E97CB332E0E3C17EA2017A7876641B72E032A8C7C834A982E62`, gateway SHA-256 is `47CD2721DBD8E4FDB10C09F12966C77484BE42FDCDC1B77921AF6FFB8E9370D0`, and ZIP SHA-256 is `A661362E1B17990C5BE4CB54653A9C727FC754E33873D19660D2F72C2BBCFC8A`.
Both isolated packaged-DLL checks, the Native AOT gateway checks, and ZIP staging passed. After replacing stale private test-output bridge copies, the 11 dispatcher and 53 bounded-AOB standalone tests passed against verified `B008` files.
Product source remains `4c0698981f50ee4867bc1efe08a62867fd5c89c3`; harness source is `0f722ef7d6b37eee0985073dfe594c6559b31c14`, whose exact-case admission change passed 93 portable harness tests and independent Astra review.

Run `20261008T015810Z-9f16` executed only `MemoryNamedScanThenAob` and passed. Both live owned CE processes were separately observed loading bridge bytes with the exact published hash.
The summary records three native scans, `stableQualification=false`, restored user state, unchanged source installation, and no cleanup failures. No owned CE or target processes remained afterward.

Run `20261008T015918Z-ee72` restored the original lifecycle ordering using the same private `.2` package. All six workflow groups completed: memory, named scanner, AOB, control-flow graph, modules, and structures.
It then failed on the first plugin-disable cycle. The driver reported `expected exactly one owned plugin row`, and the harness timed out waiting for its response.
This run records `passed=false`, restored user state, unchanged source installation, and no cleanup failures. Both owned hosts and targets stopped; both hosts supplied enabled/disabled lifecycle indicators.
The subsequent offline harness correction initializes the settings form before reloading its contents, retains exact owned-row validation, and returns bounded error receipts immediately. Its initial build and six standalone bridge tests passed before live work was stopped; the correction has not been verified in CE.
Offline Astra review then identified byte truncation that could split a UTF-8 character in an error receipt. The formatter now converts error details to bounded printable ASCII before truncation. The added multibyte regression is compiled but has not been executed under the user's stop constraint.

The initial attempt to select the full lifecycle was refused before host launch because the private PowerShell runner retained an empty diagnostic-case variable. The runner was corrected to remove an unset variable; this admission refusal is separate from the native run above.
These results do not resolve the earlier uninstrumented production corruption. Per-event instrumentation still changes timing, and no production fix is claimed.
At the user's request, live CE, native diagnostic, and debugger work is stopped. Further work is restricted to offline review, edits, and ordinary build checks; no additional native validation is implied by the prepared harness changes.

## Pending work and candidate boundary

The diagnostic candidate `207d6fd` has one failed native x64 run and is not a root-cause fix.
The reviewed bounded diagnostic mechanism has now run, but production lifecycle x64 remains blocked on mechanism-level diagnosis and an uninstrumented production candidate rerun through the original ordering.
Lifecycle x86, the broader workflow matrix, two-hour soak, installation, upgrade, rollback, named interactive clients, CI and analysis, extended compiler lifetime cases, and managed injection remain pending.
Debug publication remains pending.
The final version change, exact stable package rebuild, final native and installation repetition, tag, draft upload, publication, public download, and shipped first-use smoke remain pending.
Only the narrowly completed subcases above may be checked off. The broader workflow, lifecycle, and release requirements remain open.

## Follow-up preparation

The lifecycle harness preserves the original workflow failure alongside cleanup faults and admits success only after gateway cleanup.
Cleanup receipt flags say attempted rather than claiming successful release from an allocation-created flag.
Focused portable tests enforce the fixed soak counts and diagnostic scenario admission.
The isolated dispatcher diagnostic passed its three `.1` cases and the single controlled `.2` case. The `.2` full-order run reached all six workflow groups but failed its first lifecycle transition. Root-cause proof, production fix, production-package qualification, and verification of the latest lifecycle harness correction remain open.

## Offline goal checkpoint

The all-issues goal is active for #28, #31 and #33.
This checkpoint describes local working-tree changes on `0f722ef7d6b37eee0985073dfe594c6559b31c14`; they are not a clean release candidate or a published package.
Live CE, NativeLua, native diagnostic, debugger, extended compiler and injection execution remain stopped.

An error-contract audit found that internal tool errors with array or scalar context logged an ID without returning it.
The correction preserves that context under `details.value` beside `errorId`, replaces non-string reserved IDs without duplicate properties, and preserves all other envelope fields.
The 22 focused correlation cases passed; the six host-effect values now have direct and routed MCP coverage, including exact envelope and logged-ID equality.
The requirement-to-test mapping is in [contract-checkpoint.md](contract-checkpoint.md).

The configuration reference now lists the C# compiler gate and explains private native-dispatch log events accurately.
The support-report checklist covers versions, operation/error ID, gates and redacted logs without requesting tokens or discovery contents.
Upgrade guidance covers a matching plugin/gateway pair, old plugin-entry replacement, locked files, partial upgrades, rollback and uninstall while retaining user data.
The reviewed release-note draft includes the unresolved production crash and does not claim stable publication.
Eight #28 documentation, harness-control and unsupported-disposition criteria are complete; broader native and release gates remain open.

Validation caught an oversized knowledge page and the intended resource-size snapshot changes.
The page was shortened within its existing budget; only the three changed document sizes changed in the two resource snapshots.
It also caught a stale Costura bundle after an ordinary incremental dependency update: normal outputs were current but the single plugin DLL still embedded the old Resources assembly.
`McpTrackBundledDependencies` now supplies copy-local implementation files as compiler freshness inputs, allowing Fody to weave fresh plugin IL when a dependency implementation or embedded document changes.
The controlled offline check compared all five embedded product assemblies byte-for-byte by SHA-256 against current outputs after a resource-only edit, a no-op build and restoration.
All phases passed, the edited source was restored exactly, and the no-op build preserved the plugin output timestamp.
The report is `artifacts/issue-edits/incremental-bundle-verification.json`.
After the build-rule correction, all five actual single-DLL bundle tests passed again in both Release and Debug, including isolated catalog equality and deliberate mismatch rejection.
Those focused results are in `artifacts/issue-edits/goal-final-{debug,release}-bundle-tests.log`.

Nonincremental Debug and Release builds succeeded without warnings or errors, and both full portable suites passed 4,097 tests with zero failures or skips after the correlation and documentation corrections.
Logs are `artifacts/issue-edits/goal-verified-{debug,release}-{build,tests}.log`.
Style and whitespace verification and `git diff --check` passed.
Independent Astra source review cleared the correlation, documentation and incremental-build changes after all reported findings were addressed.
The new native multibyte receipt regression remains compiled but unexecuted, and the earlier production Lua corruption remains unresolved.
No push, tag, release or additional native run was performed.

## Offline contract and startup follow-up - 2026-10-09

The previous goal turn made concrete progress with source fixes, portable evidence and reviewed issue updates.
This continuation remains a local working tree on `0f722ef7d6b37eee0985073dfe594c6559b31c14`; no clean candidate, CI publication or native rerun is implied.

The 12 new exact-value cases passed through actual .NET MCP serialization and loopback gateway forwarding with controlled target/backend doubles.
They cover signed and unsigned 64-bit extrema, values above JavaScript's exact integer range, maximum hexadecimal addresses and pointers, negative offsets, and distinct zero/empty/unavailable values.
The gate-before-effect additions passed with 42 assembly/table cases, 177 record cases and 10 process-auto-attach cases.
The XML table fixtures verify that they were actually inspected; opaque fixtures separately check all three documented exposure requirements.
Mixed record batches prove a later gated record cannot leave earlier records created or deleted, and process tests prove no open script was reached before refusal.

The 12 lifecycle tests passed, including two new real-module checks.
An occupied loopback port refuses startup, leaves no discovery record, and can be rebound after the occupying listener stops.
A file in place of the log directory does not stop the actual authenticated backend; logging stays best-effort, the file is preserved, and disable withdraws discovery and closes the listener.
These cases are portable backend/module evidence, not exact-package installation in CE.

Independent Astra review identified and cleared eight effect-metadata corrections: two destructive flags (`record_create`, `table_save`), a fixed AA requirement (`record_set_script`), and five `may_prompt` classes (patch release, bulk resource release, record activation, deletion and clear).
The script-editing runtime gate also runs before dispatch.
`record_clear` now includes the standard capability recovery hint; its new NativeLua assertion is compiled but remains unexecuted.
Tool descriptions, the tool map, safety reference, AA guidance and cleanup workflow explain the same script-dialog behavior.
The catalog, resource and summary golden changes are intentional and reviewed.

The source/evidence crosswalk is in [contract-checkpoint.md](contract-checkpoint.md).
Full-domain nullable-result and metadata audits remain incomplete; these focused additions do not close roadmap section 3, the broader installation gate, #31 or #33.
The real-CE failure, final candidate, native/compiler lifetime matrix, soak, installation/client qualification and release dependencies recorded above remain open.

Final validation for this continuation: Debug and Release builds both succeeded with zero warnings and errors.
Each full portable suite passed 4,126 tests with zero failures and skips, excluding `LiveQualification` and `NativeLua`.
The logs are `artifacts/issue-edits/goal-contract-{debug,release}-{build,tests}.log`.
An initial Release run found only an AA guide size overrun (24 bytes); the new paragraph was shortened within the unchanged budget, its resource snapshots were recaptured, and the complete Release suite then passed.
Only eight intended tools changed in the two tool catalogs and summary; the six resource-size changes match edited knowledge documents, including the three previous checkpoint changes.
Style, whitespace verification and `git diff --check` passed.
Final Astra re-review found no remaining actionable findings in this scoped patch and its evidence after correcting the upstream parameter-sentinel interpretation; the remaining collector ambiguity is recorded in the contract checkpoint.
The pinned read-only collector source copies and blob identities are retained under `artifacts/issue-edits/collector-ec45d5f/source-manifest.json`.
Five completed offline subcases were appended to each of #28 and #33 and verified by exact body readback; all broader checkboxes and all three issues remain open.
No push, tag, release or native execution occurred.

## Full offline contract audit - 2026-10-09

The next continuation completed the remaining source review across all 193 backend tools and the gateway's discovery/result forwarding. Coverage and native-source limitations are recorded in [offline-contract-audit.md](offline-contract-audit.md).
It corrected six more tool-effect annotations, six unavailable-result mappings, the .NET collector parameter descriptions and the scan-value readability description.
The slow module-exports resource projection was removed while its explicit tool and the resource dispatch guard remain intact; current snapshots contain 43 backend resources, 14 templates and 43 prompts.

The speedhack follow-up preserves restore tracking when a post-change symbol observation is unavailable, probes before releasing a prior resource, and reports prior restore effects honestly if a later operation refuses.
Failed cleanup details contain the resource ID and release outcome instead of invented observed values.
An internal marker preserves the kernel result's object shape when every probe is unavailable; the public typed result omits that marker and all unavailable fields.

Debug and Release builds succeeded with zero warnings/errors; each full portable suite passed 4,135 tests with no failures or skips.
Build/test logs are `artifacts/issue-edits/goal-contract2-{debug,release}-{build,tests}.log`. Style, whitespace verification and `git diff --check` passed.
An initial catalog run correctly refused the slow exports-resource projection, and the first full Release run found a stale generic poll-annotation fixture. Both were corrected before the passing full runs; the failed logs are retained separately.
The focused speedhack suite passed 20 tests. NativeLua regression edits were compiled but not executed, and `LiveQualification` and `NativeLua` remain excluded from portable runs.
Independent Astra final review found no remaining actionable findings in the scoped source changes, snapshots, validation logs or issue draft. It verified all 27 changed tool entries per catalog, all 11 rendered document sizes and the single removed resource template; this does not qualify native behavior or a final candidate.

The reviewed changes intentionally affect beta compatibility: unavailable status/job fields can be omitted and the exports URI is removed. They are documented explicitly in the release notes, acceptance and support policy; requirement 2.07 is reopened pending their final contract disposition rather than claiming additive compatibility.
Six completed offline subcases were appended to each of #28 and #33 and verified by exact body readback. The compatibility-policy checkbox in #28 was reopened to match requirement 2.07, and its earlier completion paragraph was qualified as historical. #31 was reviewed and left unchanged; all three issues remain open.
The original real-CE failure, native/compiler qualification, soak, installation/client checks, final candidate and release gates remain open. No commit, push, tag, release or native execution occurred in this continuation.

## Offline package refresh - 2026-10-09

The reviewed contract changes were rebuilt through `eng/Publish.ps1` in both Debug and Release, with locked restore, into new local distribution directories. Each actual single plugin DLL passed cold loading and full catalog equality in two isolated contexts: 193 tools, 43 resources, 14 templates and 43 prompts. The bundle check resolves the embedded bridge export without invoking Lua or enabling a CE plugin.

Both Native AOT gateways passed the maintained no-instance startup smoke. The exact published executable bytes also passed `GatewayExecutable_StdioWithoutInstances_MatchesGoldenSnapshots` in each configuration: one test passed with zero failures/skips each. This compares initialization, tools, resources/templates and prompts, and reads embedded knowledge and a rendered prompt against an empty private instance registry. The published executable temporarily replaced only its corresponding test-output gateway; hashes verified both the executed bytes and exact restoration afterward.

`eng/Release.ps1` verified each five-file ZIP, embedded dependency inventory, notices, checksums and matching plugin/gateway product versions. `eng/Stage-QualificationPackage.ps1` verified and extracted each archive to a new path containing spaces; every staged file hash matches its distribution file. Independent packaging into a second empty directory produced the same ZIP hash in each configuration. This proves same-input archive reproducibility, not independent compiler/build reproducibility or installation in CE.

| Configuration | Plugin SHA-256 | AOT gateway SHA-256 | ZIP SHA-256 | ZIP bytes |
| --- | --- | --- | --- | ---: |
| Release | `1620DD5978C4996BA80DEB5B81403EB3B695B3D029C1D1D533A3F39254D5A97F` | `ADB97797600614CEF21C7F5E2352F95E9102A3D4DFCE48DA4F5B19335E65CA88` | `E2F2EDC8CDA3A5EB4C152B642F35A9C06762CBF7EF9100F2C4D5671BD1F9DB0E` | 17,752,137 |
| Debug | `E6E00295DB20F07E2C413019368B549565E0AE7F112C6E112FA90CC431F8FBA7` | `5509B7209D29C35CDA3A5710F20BC452FBB595E3CC532093F3C278FF854D22F0` | `AF62B5E0AA7309984B9107E72906AE0DE3766CE0A9A0C8C59EA638B268B94918` | 26,031,849 |

The embedded product identity is `2.0.0-beta.2+0f722ef7d6b37eee0985073dfe594c6559b31c14`, but these bytes include the uncommitted working-tree changes. They are not the published beta.2, a clean candidate, or a stable release. The local package manifest records 527 product-source file hashes and the retained product diff hash to make this boundary explicit.

Evidence is under `artifacts/issue-edits/`: `goal-contract2-publish-{debug,release}.log`, `goal-contract2-aot-{debug,release}-golden.log`, `goal-contract2-aot-golden-receipts.json`, `goal-contract2-package-{debug,release}.log`, the corresponding `-repro.log` files, `goal-contract2-stage-{debug,release}.log`, and `goal-contract2-package-manifest.json`.

Requirement 2.09 is corrected from pass to partial: defining the soak duration does not supply its missing two-hour execution result. No broader qualification checkbox is completed by this offline package refresh. At this package checkpoint, live CE, NativeLua, native diagnostic, debugger and extended compiler/injection work remained stopped; the later standalone-only authorization is recorded below. No commit, push, tag, upload or release occurred.

## Authorized standalone NativeLua regressions - 2026-10-09

The user subsequently authorized the prepared standalone NativeLua regression suite in Release mode, explicitly without launching CE or attaching to a target. The earlier compiled-only and native-stop statements describe their historical checkpoints; this narrow permission now allows standalone NativeLua regression checks. It does not resume live CE, native diagnostic, debugger, extended compiler or injection qualification.

An initial run lacked `CHEATENGINE_MCP_LUA53_PATH` and failed the explicit runtime prerequisite. The next run used `C:\Program Files\Cheat Engine\lua53-64.dll`, SHA-256 `C95DCDFA0F60F97B43D970D77FD1BB907AF4DE04B500A3C89A99600B20B35BD2`, set only for the command process and restored afterward. It passed 625 of 626 cases and exposed an incorrect test expectation: restoring an already-hooked speedhack fixture must make no new hook attempt.

`SpeedhackV2_SymbolProbeFailsAfterRestoringSpeed_ReturnsAnUnknownObservation` now verifies one speed-setting call, zero hook attempts, and both requested and target speed equal to 1 while the unavailable post-change observation remains omitted. Only the assertion and its comment changed; product source and the packaged bytes above are unchanged. The Release test rebuild passed with zero warnings/errors, and the complete authorized suite then passed **626/626**, zero failures/skips, in 3.152 seconds.

| Prepared behavior now executed | Test evidence within the passing suite |
| --- | --- |
| Unknown memory/kernel status and detached scanner ID | `AddressExtras_StubbedCheatEngine_ReportsSystemModulesAndRttiClasses`, `KernelStatus_ReportsBooleanStatesAndOmitsUnavailableOrInvalidAnswers`, `ScanV2_MainScannerWithoutAPositiveProcessId_IsNotAttached` |
| Missing module-preference list differs from a valid empty list | `ModuleSymbolGetModulePreference_Stubbed_CopiesAtMost1024NamesAndRefusesAMissingHostList` |
| Unavailable speedhack observations preserve mutation/restore reporting | `SpeedhackV2_SymbolProbeUnavailable_RefusesStateAndChangesBeforeAnyMutation`, `SpeedhackV2_SymbolProbeFailsAfterChangingSpeed_TracksTheRestoreAndReturnsAnUnknownObservation`, `SpeedhackV2_SymbolProbeFailsAfterRestoringSpeed_ReturnsAnUnknownObservation` |
| Record-clear refusal supplies the capability recovery hint | `RecordClear_ActiveAutoAssemblerWithoutGate_LeavesTheAddressListUntouched` |
| Bounded lifecycle error text remains printable ASCII | `LivePluginBridge_LongMultibyteError_RemainsBoundedAscii` |

Command: `dotnet test --solution CheatEngine.Mcp.slnx -c Release --no-restore --no-build --filter-trait Category=NativeLua --filter-not-trait Category=LiveQualification --fail-skips on`, with the explicit process-local Lua DLL path above.
Logs/receipts: `artifacts/issue-edits/goal-contract2-native-fix-build.log`, `goal-contract2-native-lua-release.log`, `goal-contract2-native-lua-runtime.json`; the initial `-missing-path.log` and `-stale-assertion.log` failures are retained separately.

These checks execute fixed Lua against CE-shaped stubs in a standalone runtime. They do not demonstrate a fix for the production dispatch crash or qualify real host lifecycle, collectors, debugger/injection/compiler workflows, installation, soak, or a clean final package. All three issues remain open.

Changed-test style and whitespace verification and `git diff --check` passed. Independent Astra final review verified the package/source/hash evidence, the corrected assertion, the 626-pass result and the updated documentation/issue draft, with no remaining actionable findings in this scope. It explicitly retained the real-CE and final-candidate limits.
Five completed subcases were appended to both #28 and #33 and verified by exact body readback and OPEN state. #31 was reread and remains unchanged with its compiler-lifetime/injection and final contract/CI requirements open. The current GitHub issue list still contains exactly #28, #31 and #33, and the all-issues goal remains active.

## Authorized bounded CE investigation - 2026-10-09

After reviewing [the concrete proposal](next-live-investigation.md), the user explicitly authorized bounded memory/scanner/AOB investigation in private CE copies and owned x64 targets, with all four optional capability gates disabled. This is separate from the earlier standalone Lua permission. It does not authorize the full lifecycle/workflow matrix, debugger, extended compiler/injection, kernel/DBVM, soak, or publication.

The existing `MemoryNamedScanThenAob` diagnostic case ran in two fresh sessions against the unchanged ordinary-SDK Release distribution from the offline package refresh. Both private plugin copies and the gateway in each run match the recorded package hashes; the unfinished private diagnostic SDK/MCP clones were not used.

| Run ID | Case | Reported total test duration | Result |
| --- | --- | ---: | --- |
| `20261009T101356Z-aa03` | `MemoryNamedScanThenAob` | 18.922 seconds | Passed; all gates false; user state restored, source installation unchanged, zero cleanup failures. |
| `20261009T101855Z-5bcc` | Same case, fresh session | 18.323 seconds | Passed with the same gate and cleanup results. |

Each case exercised the existing bounded memory and named-scanner workflows followed by two AOB tool calls containing three native scans, then checked the original scalar sentinel in both targets. Every recorded owned host/target PID was absent in the post-run process check. Both host logs contain enabled and disabled lifecycle indicators for this single activation; repeated plugin cycles were not exercised.

The immediately preceding preflight recorded Windows 11 Pro x64 `10.0.26300`, CE `7.7.1.10828`, and CE executable SHA-256 `CF6CCC664A90C23531EC20F5C41458CFDC1D04679D6D05ED94C06BCECF95544F`. An installed-runtime inventory was retained; it is not a measurement of the CLR patch loaded by CE. The native summary still contains no target-binary hash, so these results do not supply that missing identity fact.

Evidence: `artifacts/issue-edits/goal-live-resume-preflight.json`, `goal-live-resume-runtimes.log`, `goal-live-resume-memory-scan-aob.log`, `goal-live-resume-memory-scan-aob-repeat.log`, and `goal-live-resume-evidence.json`. The evidence extract retains source summary/receipt hashes, exact package identities, workload/gate results, cleanup fields and the recorded-PID absence check. Raw private run directories remain outside the repository.

These passes do not establish a root cause or production fix. The original `d398457` failure occurred at the next CFG fixture allocation after AOB; the later `207d6fd` failure occurred inside AOB value scanning. The reduced diagnostic case does not reproduce the entire original lifecycle ordering, and the production release blocker remains open.

### Exact post-AOB allocation probe

Independent Astra source review confirmed that the final scalar reads exercise the common Lua dispatcher/frame, but not the target allocation and ownership operation that failed in `d398457`. The diagnostic harness now allocates 64 non-executable bytes immediately after AOB cleanup and frees the named allocation in `finally` after confirmed acquisition. It records both results and preserves original and cleanup exceptions. No CFG writes, graph analysis, product changes or additional capability gates were introduced. Astra reviewed the probe before execution and found no actionable issues in that scope.

Run `20261009T102831Z-f606` passed the augmented `MemoryNamedScanThenAob` case: **1 passed, 0 failed, 0 skipped**, total 18.317 seconds. The allocation returned size 64 and `executable=false`; release returned `kind=released`, `hostEffect=completed`, `isComplete=true`, and no manual recovery requirement. All four gates remained false. Memory/scanner/AOB/probe cleanup reported zero failures; both private hosts and targets stopped, the user state was restored and the source installation remained unchanged. All four recorded PIDs were absent afterward.

The same plugin and gateway hashes from the package refresh were used. Evidence: `artifacts/issue-edits/goal-live-post-aob-allocation.log` and `goal-live-post-aob-evidence.json`; the latter records the harness source, summary and receipt hashes. Release build and scoped whitespace verification passed after formatting the new receipt objects; the initial formatting diagnostics remain in separate logs. This additional pass covers the missing post-AOB allocation operation but still does not identify or fix the historical crash or qualify the full original lifecycle sequence.

### Original read-only prelude restored

Source comparison showed that both historical failed lifecycle runs also called `VerifyBothAsync` and `LiveResourceQualification.VerifyAsync` after attaching the targets and before the workflow prefix. The separate `ResourcePreludeMemoryNamedScanThenAob` case restores those calls before the bounded memory/scanner/AOB and post-AOB allocation/free sequence. The prelude reads runtime/gate identity and scalar sentinels in each owned target, lists static catalogs, reads documentation and runtime resources, renders a prompt and checks instance completions. It does not execute the rendered debugger workflow. Runtime resource observation includes the fixed-Lua ledger snapshot; it does not create state or release resources. Astra reviewed this path and the four-file harness change before execution with no actionable findings.

Run `20261009T103236Z-b6c6` passed: **1 passed, 0 failed, 0 skipped**, total 18.519 seconds. The prelude completed with 55 resources, 25 templates and 43 prompts. The original exports template was removed by the earlier reviewed contract change; the historical prelude only listed it and never invoked native export parsing. The 64-byte non-executable post-AOB allocation and completed release succeeded. All four gates were false; workflow and probe cleanup reported zero failures, user state was restored, source installation was unchanged, and every recorded owned host/target PID was absent afterward.

Release build and scoped style/whitespace verification passed. The focused `LiveQualificationOptInTests` suite passed **50/50**, zero failures/skips, including the new exact-selector theory case. No full-suite rerun is claimed after this harness-only addition. Evidence: `artifacts/issue-edits/goal-live-prelude-{build,optin-tests,style,whitespace,run}.log`, `goal-live-prelude-evidence.json`, `goal-live-prelude-harness.patch` and `goal-live-prelude-source-hashes.json`.

This restores the historical native call order through the previously failing allocation using the current package, but does not reproduce historical bytes or timing. The result narrows the reproduction gap; it does not establish a root cause, demonstrate a production fix or qualify subsequent workflow/lifecycle transitions.

Independent Astra final review matched all four retained raw summaries and receipt hashes to their extracts, the latest harness hashes and patch, build/test/format logs, package identities and documentation. It found no actionable issues in this continuation's evidence. Four completed diagnostic subcases were appended to both #28 and #33 and verified by exact body readback; their original incomplete gates remain unchecked and both issues remain OPEN. #31 was reviewed and remains OPEN with its compiler/injection and final contract/CI requirements unchanged. A fresh product recheck confirmed all 527 source hashes and the Release plugin/gateway hashes still match the offline package manifest.

### Mechanism-level evidence still missing

The pinned SDK source review did not establish a root cause. The earlier refusal is the `LuaProtectedApi.ValidateInvocation` guard where required inputs exceed the observed Lua stack height. The later AV occurs while restoring the outer `MainThreadDispatcher` frame; the crash site does not establish which earlier operation corrupted state.

An older passing instrumented `.2` run, `20261008T015810Z-9f16`, records different callback/worker and AOB-provider Lua state pointers in dispatches 63 and 64. Distinct coroutine pointers can share a Lua VM, so this observation alone is not a defect. Existing trace `top` fields contain saved frame heights, not contemporaneous measurements, and cannot prove balanced execution.

A next diagnostic, if needed after confirming the CE coroutine contract, must distinguish actual and saved stack heights before frame restoration, around `synchronize` and callback disposal, and at the precise insufficient-input guard. It should record state/thread/dispatch identity with bounded storage and survive a host crash. A new isolated SDK diagnostic package with the exact published native bridge would require preparation, offline review and separate execution authorization; the current passing ordinary-SDK runs do not authorize or validate it.

Follow-up authoritative-source review explains the pointer difference: CE's `lua_synchronize` retains the caller's `L` as `syncvm`, and its synchronized callback uses that captured state. The plugin state getter instead returns a Lua state associated with the current OS thread. Different pointers are therefore expected in this path and are not evidence of an invalid state. CE's main-thread `memscan_waitTillDone` loop calls `CheckSynchronize` while waiting, which supplies a concrete reentrancy boundary for the proposed stack-balance trace. These facts narrow the diagnostic design; they do not prove a reentrancy defect in the installed build.

Pinned public-source references (`ec45d5f47f92a239ba0bf51ec5d04a7509c3fd37`, not a byte-for-byte claim about installed CE): [capture caller state](https://github.com/cheat-engine/cheat-engine/blob/ec45d5f47f92a239ba0bf51ec5d04a7509c3fd37/Cheat%20Engine/LuaHandler.pas#L3456-L3494), [invoke the synchronized callback](https://github.com/cheat-engine/cheat-engine/blob/ec45d5f47f92a239ba0bf51ec5d04a7509c3fd37/Cheat%20Engine/LuaCaller.pas#L358-L386), [per-thread state getter](https://github.com/cheat-engine/cheat-engine/blob/ec45d5f47f92a239ba0bf51ec5d04a7509c3fd37/Cheat%20Engine/LuaHandler.pas#L188-L208), [plugin getter export](https://github.com/cheat-engine/cheat-engine/blob/ec45d5f47f92a239ba0bf51ec5d04a7509c3fd37/Cheat%20Engine/pluginexports.pas#L2622-L2635), and [scan wait synchronization](https://github.com/cheat-engine/cheat-engine/blob/ec45d5f47f92a239ba0bf51ec5d04a7509c3fd37/Cheat%20Engine/LuaMemscan.pas#L145-L159).

## Private stack trace prepared offline - 2026-10-09

The [stack trace diagnostic ledger](stack-trace-diagnostic.md) records the new private SDK/MCP source identities, exact package hashes, one-session proposal and completed offline checks. The SDK Release build passed; all 9 managed recorder tests passed. An owned helper process was terminated with its mapping open: all 16 committed records survived, and disabled/collision/missing-directory cases passed. Package inspection confirmed all seven embedded SDK assemblies and the unchanged published native bridge. Actual-DLL reflection and AOT smoke passed. The private MCP portable suite passed 4,134 checks with two notices-version failures; the corrected notices class then passed all 3 cases. No full-suite rerun after that documentation-only fix is claimed.

The prepared runner passed syntax and no-execution preflight for 235 exact input hashes. Independent Astra high review verified the repaired source, package and runner with no remaining actionable findings in that scope. The user then explicitly approved one instrumented session; its result follows. #28, #31 and #33 remain open.

### One approved instrumented session

Run `20261009T111321Z-688b` passed the exact prepared case: **1 passed, 0 failed, 0 skipped**, 18.433 seconds. Host A retained 1,753 records covering 71 synchronized dispatches and three AOB frames; B retained 121 records covering five dispatches. All 79 observed frame restorations matched their saved heights. There were no recorded pre-restore underflows, protected-input refusals, failed probes, overflows, incomplete records or frozen traces. The post-AOB 64-byte non-executable allocation/free passed; gates remained false, user state was restored, installation unchanged, cleanup failures empty, and all four recorded owned PIDs were absent.

Astra independently verified the trace interpretation: the depth-2 observations were ordinary inline child calls, not synchronized reentry. Three AOB waits took 4.8749, 2.4261 and 4.7970 ms with top 0 at both boundaries and no recorded SDK callback reentry. This is a balanced baseline, not a reproduction or production fix; it does not rule out other host callbacks. The [diagnostic ledger](stack-trace-diagnostic.md#approved-instrumented-session) records exact identities and evidence. This one-session authorization is consumed; no further live session is scheduled. The offline trace validator then passed 11 synthetic positive/negative controls and both recorded host snapshots, including actual nested-thunk detection, stack mismatch/interruption cases and expected pointer differences. The original failure/reentry reproduction remains open.

Final Astra review confirmed the validator's sequence tie-breaking and completed evidence ledger with no remaining actionable findings in that scope. The executed diagnostic and validator subcases were checked off in #28 and #33, with exact normalized body readback and both issues still open; #31 remains open unchanged. Rechecking all 527 primary product-source hashes and the ordinary Release plugin/gateway confirmed that this private diagnostic did not change them.

### Deterministic standalone reentry cases prepared

The [nested-dispatch regression ledger](nested-dispatch-regression.md) records two new standalone native-Lua cases in a separate, uninstrumented SDK archive at `325c47b573f8bd39a247f1d0101f110fa36c1696`. They force a nested synchronized callback during an active protected host-wait call, exercise nonzero root-stack restoration on success and managed exception, and assert both worker sentinels and exact exception ownership. Actual Lua access is serialized; worker epilogues are released and joined one at a time after the outer callback unwinds.

The Release test-module build passed with zero warnings/errors. Source comparison verified 1,277 entries with only two new test files and the original published native bridge replacement. The no-execution runner verified 1,485 input hashes; its 60-second deadline terminates only the owned test process. Independent Astra high review found no remaining actionable findings in the source, lifetime handling, runner or hashes.

The user then approved one standalone execution. Run `885121c89ef846fead7b76804b21e5f3` passed both exact cases: **2 passed, 0 failed, 0 skipped**, 237 ms. The owned process exited 0 and was absent afterward; no timeout, runner failure or cleanup failure occurred. All prepared hashes still matched. This proves the tested simulated-host restoration paths, not a real-CE reproduction or production fix. No further native execution is scheduled. #28, #31 and #33 remain open.

Independent Astra final evidence review was clear. The three completed preparation/execution/review subcases were checked off in #28 and #33, and exact body readback preserved the original incomplete acceptance gates. Source review identified the maintained full `lifecycle` scenario as the remaining path through the historical Settings/Plugins disable failure and restart matrix; its [ordinary-SDK preparation](lifecycle-resume.md) is being reviewed separately, without implying execution authorization.

### Ordinary-SDK lifecycle reached the remaining bridge failure

After Release build and independent Astra preparation review passed, the user explicitly approved one full x64 `lifecycle` session with the same ordinary-SDK distribution. Run `20261009T120128Z-8f44` completed the prelude and all six workflow groups, then failed at the first plugin-disable request with `expected exactly one owned plugin row`. The bounded bridge error receipt now reports the real refusal immediately, but its earlier form-initialization correction did not resolve checklist population. The test reports **0 passed, 1 failed, 0 skipped**, 18.441 seconds.

All four gates stayed disabled. Mutating workflow cleanup failures were zero, user state was restored, the source installation stayed unchanged, and all four recorded owned host/target PIDs were absent. All 820 reviewed inputs still matched. Exact hashes and limits are in the [executed lifecycle ledger](lifecycle-resume.md#approved-execution-result). Plugin cycles and restarts remain unqualified; no production stack-crash fix is claimed. This one-session approval is consumed. The next work is offline investigation of the real Settings/Plugins population event, preserving exact owned-row validation.

Independent Astra final review confirmed that failed result and its limits. The completed six-group workflow and cleanup/evidence subcases were checked off in #28 and #33; exact issue readback preserved the incomplete lifecycle gates. A subsequent [retained-key experiment](lifecycle-key-fix.md) was prepared on the mistaken premise that checklist population was inside the `OpenKey(Plugins64, false)` branch. Release build, both 859-hash preflights and independent Astra source/runner review passed, but that review missed the incorrect block interpretation.

The user approved the eight standalone bridge cases followed conditionally by one lifecycle session. The standalone stage passed **8/8**, zero failures/skips. Run `20261009T121623Z-71c7` then completed all six workflows but failed the first disable with measured `count=0`, despite the `keyRetained=true` receipt: **0 passed, 1 failed, 0 skipped**. Preserving the empty key is insufficient. All gates stayed false, cleanup and settings restoration passed, both test processes and all four host/target PIDs were absent, and both 859-file manifests still matched. Astra independently confirmed the evidence and its limits. Exact artifacts and hashes are in the [conditional execution ledger](lifecycle-key-fix.md#approved-conditional-execution). No plugin cycle/restart or historical stack-failure resolution is claimed; this approval is consumed and further diagnosis proceeds offline.

Exact pinned-source inspection then established that `FillCheckListBox` is outside the registry-key conditional. The researcher and Astra confirmed the correction. The unsupported key creation/retention change was rolled back; the measured-count receipt and zero-row regression remain. CE's reload has early returns and a broad exception handler, so the zero-row result does not identify the failing path. Original run artifacts and both tested source snapshots are retained; no post-rollback native result is claimed.

The rollback Release build and whitespace checks passed; all 527 product-source hashes and ordinary distribution plugin/gateway hashes remained unchanged. Final Astra review was clear after correcting the consolidated live-counter fields from the retained TRX. #28 and #33 now check off the eight bridge cases and verified evidence while preserving incomplete lifecycle gates; exact body readback passed. #31 was reviewed and remains open unchanged. The active goal remains unfinished; no issue closure, push or release is claimed.

### Settings observation prepared offline

Further source and harness review found no separate Settings UI refresh path or specific mismatched registry value explaining the zero-row failure. The [Settings observation preparation](settings-observation.md) now captures the existing form's runtime class and the checklist count before reload in the bounded post-reload row-count refusal. Exact owned-row/name guards and the original registry handling remain unchanged. Two new standalone theory cases check distinct pre/post counts, class text and refusal before input/application, bringing the selected bridge scope to ten cases.

Release build passed with zero warnings/errors, both no-execution manifests verified 859 inputs including 527 unchanged product sources, and independent Astra high review found no actionable issues. The proposed sequence is ten standalone cases followed conditionally by one unchanged ordinary-SDK x64 lifecycle attempt. Neither stage is executed or authorized yet. This observation can narrow the early-class and list-change hypotheses but cannot identify a swallowed exception or resolve an unchanged zero-row list on its own; it is not a product fix.

### Isolated performance preparation

While the Settings decision remains pending, the [isolated performance checkpoint](performance-preparation.md) expands the 9.01 runner and fixes a separately discovered pointer-reference cleanup-retention gap. The primary source, ordinary package and prepared Settings inputs remain unchanged. Release build passed with zero warnings/errors; final performance/lifecycle managed checks passed 20/20, and the separate pointer regression run passed 130/130, all without skips. All actionable Astra findings in this staged scope were repaired, and final Astra high review was clear. A retained 856-file source inventory identifies exactly six changed/new files and hashes the evidence.

The completed offline preparation subcases were checked off in #28 and #33 with exact body readback; integration and native acceptance remain unchecked. Both issues remain open, and a fresh open-issue list still contains #28, #31 and #33. No native performance run, full-suite rerun, issue closure, commit, push or release occurred.

### Isolated admission preparation

The [admission checkpoint](admission-preparation.md) adds two independent gateway processes, fixed concurrent batches and UI/resource/completion observations in a second isolated snapshot. Astra found and the implementation repaired missing observer-versus-batch overlap checks and loss of original exception diagnostics. The final Release build has zero warnings/errors; 52 selected managed tests passed with no skips. Astra's second pass found no remaining actionable correctness findings in this scope. The 861-file source inventory identifies seven changed/new files relative to the first reviewed snapshot.

#28 and #33 now check off this managed preparation, with exact body readback and incomplete native gates preserved. Both 859-input Settings no-execution preflights still pass. Package-matched SDK/Client source confirms that module and region caps apply only after native enumeration, including module-detail navigation's lookup. The separate navigation repair is recorded below; integration and native 9.01/9.03/9.05 acceptance remain open.

### Isolated prepared navigation repair

The [prepared navigation checkpoint](navigation-preparation.md) redirects module/detail/region resources and module completion to five-second, process/epoch/version-checked snapshots from explicit source tools. Its navigation paths contain no module/section/region enumeration, and cold/stale reads fail with preparation hints. The explicit tools still perform native enumeration. Latest module details are retained with detached sections arrays; fresh module completion does not extend snapshot lifetime through an outer cache. Descriptions document the intentional preparation requirement while resource URIs and schemas remain unchanged.

The final isolated Release build passed with zero warnings/errors and all **4,207 portable tests passed**, with zero failures/skips. Earlier focused 214/214 and non-capture contract 38/38 checks also passed. The first broad run found a pointer-documentation lint failure and ten packaging-fixture failures caused by missing source-revision metadata; the documentation and isolated build command were corrected. No packaging behavior changed. The 863-file inventory records 26 modified and two new files relative to the reviewed admission snapshot. Astra's source and final evidence reviews are scoped clear; the final review independently verified source/manifest hashes, counters, build identity and constrained golden changes.

The primary product sources, ordinary distribution and both 859-input Settings preparations remain unchanged. A fresh issue review still shows #28, #31 and #33 open; #31 retains its compiler/shared-temp/export/injection and final stable-evidence gates. Integration, candidate packaging, native performance/admission/lifecycle acceptance and publication remain incomplete. No additional native session, commit, push or release occurred.

The completed offline navigation subcase is now checked off in #28 and #33. Exact issue body readback passed for both edits (64,475 and 26,313 characters respectively), preserving the unchecked integration and native acceptance gates. #31 remains open unchanged. The Settings-observation approval remains pending; no earlier consumed approval was reused.

### Navigation package and remaining route repairs

The [first navigation package](navigation-package.md) passed the maintained local publish, two isolated exact-DLL catalog checks, Native AOT gateway smoke, deterministic repeated ZIP creation and exact five-file staging. Independent Astra high evidence review was clear. It is an uncommitted development payload with the base beta.2 version and cannot replace the published beta.2 assets. It excludes the follow-up below and retains earlier dispatch labels, so it requires rebuilding before final qualification.

The [navigation follow-up](navigation-followup.md) adds five-second prepared thread/symbol/breakpoint resources, corrects the caller-owned thread StringList API, and bounds indexed structure lookup and page access. Six explicit whole-enumeration preparations now honestly declare `blocking_native`; their resource helpers declare `short` and are validated through explicit prepared-projection metadata. Astra found and the implementation repaired symbol publication ordering, a missing process facade in the native test fixture, and a resource-only source-class validation gap. The new thread/structure Lua regressions compile but remain unexecuted.

Final Release build passed with zero warnings/errors. All 213 focused cases passed; catalog capture passed 38 cases, and the final non-capture portable suite passed **4,234/4,234**, zero failures/skips. The first broad run's single stale script-count assertion was corrected from 18 to 19 and the complete suite rerun. Final Astra high source, golden and evidence reviews were scoped clear, including all 867 source hashes, the 863 prior hashes and both frozen 859-input Settings inventories. Evidence: `artifacts/performance-preparation/navigation-followup-evidence.json`.

Completed package and offline follow-up subcases are checked off in #28 and #33. Exact normalized issue readback passed (64,632 and 26,470 characters), preserving all incomplete native/integration/release gates. A fresh review confirmed #31 still requires compiler/prerequisite, shared-temp expiry/reload, delayed export, injection and final stable evidence. All three issues remain open; the goal is not complete. No primary product integration, new native execution, commit, push or publication occurred.

The follow-up was subsequently [packaged locally](navigation-package.md#latest-reviewed-follow-up-package) from a fresh hash-matched copy of all 867 reviewed files. Both exact-DLL probes, Native AOT empty-registry smoke, two identical ZIP builds and maintained five-file staging passed. Final Astra high package evidence review was scoped clear; it independently verified both ZIPs, source and file hashes, versions, logs and both frozen Settings inventories. This supersedes the first development package for the repaired navigation source, but remains an uncommitted beta.2-version payload with no stable identity or native qualification claim. Evidence: `artifacts/candidate-preparation/followup-package-evidence.json`.

The latest local-package subcase is also checked off in #28 and #33. Exact readback passed at 64,861 and 26,699 characters. Native Lua regressions, primary integration, final source/version identity, native acceptance and release remain open; #31 remains unchanged. The Settings-observation approval is still pending, and no consumed approval was reused.

### Standalone navigation Lua cases prepared

The [five-case navigation regression](navigation-native-regression.md) is now prepared against the already reviewed Release test module. It exercises thread StringList parsing/destruction and the new bounded structure definition/page script in an ordinary standalone Lua bridge with a simulated host. The runner restricts the exact five fact names, requires all five passed with no skips, uses a 60-second owned-process deadline and isolates temporary files. It has no CE launch, real target, debugger or follow-on session.

The no-execution preflight and independent Astra high preparation review verified 1,088 files, including all 867 reviewed sources and 216 test-output files. The reviewed portable test module is unchanged, so no rebuild or test discovery was needed. Both 859-file Settings inventories also remain unchanged. The separate five-case native execution still requires explicit approval under the user's earlier stop. No additional acceptance gate is checked off on the strength of this unexecuted proposal.

### Matching navigation Debug checks

The [reviewed navigation follow-up](navigation-followup.md#matching-debug-portable-verification) now also has a passing Debug portable run from a separate hash-matched copy of its 867 files. Locked restore and build passed with zero build warnings/errors; all 4,234 cases passed with zero failures/skips in 1 minute 42.351 seconds. The Debug and Release TRX test names match exactly. Evidence: `artifacts/candidate-preparation/debug-followup-evidence.json`.

Both Settings inventories (859 files each) and the navigation native inventory (1,088 files) still match. No primary product source, existing Release test output or pending native input changed. Astra found and verified the repair of a timestamp serialization error in the evidence recorder; final independent Astra high evidence review is scoped clear. Final clean-candidate and native gates remain open.

The completed Debug subcase is checked off in #28 and #33. Exact body readback passed (64,663 and 26,501 characters), preserving all incomplete acceptance gates. Both issues remain open; #31 is unchanged. No native session, commit, push or publication occurred.

### Maintainer-directed MCP closeout

The maintainer subsequently requested focusing on MCP, closing the remaining issues where further work could not proceed, and committing tested code locally to `main`. The [closeout record](mcp-closeout.md) supersedes the earlier keep-open requirement and pending native preparations. It records integration of all 61 reviewed files, fresh 4,234-pass Debug and Release portable runs, clean builds/formatting, both package/AOT probes, local ZIP verification and final Astra review with no actionable findings. Issues #28, #31 and #33 are now verified closed as `not_planned`, with their acceptance checklists unchanged. The historical native defects and unperformed stable qualification remain explicitly deferred; no stable release or push is claimed.
