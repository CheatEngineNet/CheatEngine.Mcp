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
The added logging can change timing, so these passing diagnostic cases neither identify the root cause nor prove a production fix.
Each of the three passing summaries explicitly records `stableQualification=false`.
The full original-order instrumented probe remains pending.

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
The isolated dispatcher diagnostic has now passed its three bounded fresh-session cases, while the original full ordering, root-cause proof, production fix, and production-package qualification remain open.
