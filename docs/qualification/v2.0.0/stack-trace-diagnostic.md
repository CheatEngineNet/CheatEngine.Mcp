# Private stack trace diagnostic - 2026-10-09

Status: the user separately approved one instrumented session, and run `20261009T111321Z-688b` passed. This is private diagnostic evidence, not a production fix or stable-release candidate. The four approved ordinary-SDK sessions and this one instrumented session are complete. No further live run is scheduled or authorized by this one-session approval.

## Prepared workload

Run exactly one `ResourcePreludeMemoryNamedScanThenAob` session through the maintained Release live harness, using two private CE copies and two owned x64 targets. Preserve the existing read-only resource/runtime prelude, bounded memory/named-scan/AOB operations, 64-byte non-executable post-AOB allocation/free and final sentinel reads. All four optional gates remain false. The private harness creates each host's absolute trace directory before launch and forwards the trace flag only for the diagnostic scenario. Normal environment scrubbing removes inherited trace flags.

The harness retains its existing five-minute host lifetime, refusal while user CE/DebugView processes are running, ownership-based cleanup, user-settings restoration and source-installation checks. The runner requires the exact prepared hashes and one new session, then decodes both hosts' trace files and checks their process identities, package identity, gates, restoration and recorded-process absence. Missing, invalid, exhausted or frozen traces require review. There is no automatic retry or expansion to another workload.

Execution entry point: `artifacts/issue-edits/Invoke-StackTraceLive.ps1 -Execute`. Without `-Execute`, the script only checks the prepared hashes; that dry preflight passed for **235 inputs**. `goal-stack-trace-run-manifest.json` holds the absolute inputs, selected case and hashes. No execution approval is inferred from the dry preflight.

## Instrumentation and interpretation

SDK source was archived from `325c47b573f8bd39a247f1d0101f110fa36c1696`, then modified in `artifacts/dependencies/sdk-stack-trace`. Source manifests retain the base archive identity and hashes for all 1,285 source entries, including changed/new files. The private MCP snapshot derives from the dirty `0f722ef7d6b37eee0985073dfe594c6559b31c14` working tree: 849 source entries, with explicit private package/version/lockfile/notices and harness changes. Neither source identity represents a clean published commit.

The private SDK measures actual Lua stack heights separately from saved frame heights around dispatch, callback disposal, frame restoration, AOB acquisition and scanner waits. The protected-operation refusal records the exact existing guard observation and required inputs before the original exception. Dispatch/parent identities, OS and managed threads, runtime epoch/generation and nesting depth allow correlation across the known synchronization boundary. Different callback/provider Lua pointers are expected under CE's per-thread state contract and do not alone show a defect.

Tracing adds native `lua_gettop` measurements and changes timing. It can therefore perturb the reproduction and can itself encounter an invalid state. A numeric probe-intent record precedes each added measurement; a missing result can also mean concurrent exhaustion or freezing and does not alone prove a native fault. Ordinary probes stop when full/frozen. A below-saved-height observation is a diagnostic anomaly to inspect, not independently a proven corruption cause.

The writer uses a fixed file mapping: 128-byte header, 32,768 ordinary 96-byte slots and one dedicated first-anomaly slot. Slots never overwrite earlier evidence; their sequence is published last. Managed trace setup/write errors disable tracing without replacing SDK operation results or cleanup. Existing files are never overwritten. The first-anomaly slot is retained even after ordinary overflow. The reader omits unpublished slots and reports incomplete admitted records. Evidence survives the tested abrupt process termination; machine-crash/power-loss durability is not claimed.

## Verified package identities

| Input | SHA-256 |
| --- | --- |
| Private SDK `2.0.1-mcpstacktrace.1` NuGet package | `5B9501B338F52253CB012AACBD906878C4FF479A11346C6A02638D7C44688365` |
| Private MCP `2.0.0-mcpstacktrace.1` plugin | `86CBA116A248F11728D36A4112E310B0C81D8FFF2E1DEFD40F8C06048C3E5605` |
| Private Native AOT gateway | `B400424A25392DAF3B8E86C12B5CF23BD5364D198E691A2A8CD3BFE47EA5C350` |
| Original published SDK 2.0.0 native bridge | `B008C8D8C136187F241542E6223DC0831999D8300DC2C4C01E1CF49F6FBA7698` |
| Reviewed installed CE x64 executable | `CF6CCC664A90C23531EC20F5C41458CFDC1D04679D6D05ED94C06BCECF95544F` |

The package directory is `artifacts/mcp-stack-trace/artifacts/dist/stack-trace1`. PE resource inspection extracted/decompressed and compared all seven embedded SDK assemblies plus the native bridge against the private NuGet package. The bridge hash was also checked against the published SDK 2.0.0 bytes. `goal-stack-trace-provenance.json` records these results. Product files and dependency references in the primary checkout were not changed for this diagnostic.

## Completed offline checks

- [x] SDK Release build: zero warnings/errors.
- [x] Focused managed recorder tests: **9 passed, 0 failed, 0 skipped**; NativeLua excluded.
- [x] Actual opt-in facade in an owned helper process: terminate with the mapping open and undisposed, then recover **16 committed records, 0 incomplete**, with matching PID, sequence, context, saved/actual values and synthetic state. Disabled tracing, existing-file collision and missing-directory containment also passed.
- [x] Private local publish: isolated actual-DLL checks in two fresh contexts, each **193 tools, 43 resources, 14 templates, 43 prompts**; Native AOT gateway build and no-instance smoke; five-file distribution.
- [x] Portable MCP regressions: initial **4,134 passed, 2 failed, 0 skipped** out of 4,136. Both failures were the private SDK version missing from notices. Corrected the private notices, retained licenses/attribution and reran the complete notices class: **3 passed, 0 failed, 0 skipped**. No later full-suite pass is claimed.
- [x] Script syntax and no-execution hash preflight passed.
- [x] Independent Astra high review: resolved managed-fault cleanup interference, mapping cleanup, first-anomaly accounting, the final-slot probe edge and mapped-memory disposal admission. Final source, package and runner review found no remaining actionable findings in this scope; it does not establish the native root cause.

Logs and manifests are retained under `artifacts/issue-edits/goal-stack-trace-*`; source and binaries remain local artifacts. The historical crash, production fix, full lifecycle reruns and remaining issue gates are still unresolved. No issue closure, commit, push, tag or release follows from this evidence.

## Approved instrumented session

After the explicit one-session approval, the prepared command completed with **1 passed, 0 failed, 0 skipped**, total 18.433 seconds. Run `20261009T111321Z-688b` used the exact plugin/gateway hashes above. The resource prelude, memory/named-scan/AOB sequence, post-AOB allocation/free and final sentinel checks passed. The allocation was 64 bytes and non-executable; release reported complete success. All four optional gates were false. User state was restored, source installation was unchanged, cleanup failures were empty, and all four recorded owned process IDs were absent afterward.

| Observation | Host A | Host B |
| --- | --- | --- |
| Committed trace records | 1,753 | 121 |
| Paired synchronized dispatches | 71 | 5 |
| Observed frame restorations | 74, including 3 AOB frames | 5 |
| Underflow, restoration mismatch, protected-input refusal or probe failure | 0 | 0 |
| Overflow, incomplete or frozen records | 0 | 0 |

All 76 synchronized callback entries were depth 1 with no parent. The 200 depth-2 inline scopes were ordinary child calls inside their callbacks. Host A's three AOB waits lasted **4.8749, 2.4261 and 4.7970 ms**, each beginning and ending at stack height 0. No traced synchronized callback or inline call reentered during those waits. Different callback/provider Lua pointers remain expected; no pointer-equality requirement is inferred.

Independent Astra review checked raw trace/summary hashes, event pairing, state/thread identity, nesting, stack observations and cleanup. No actionable discrepancy was found. This establishes balanced observations at the instrumented boundaries in one successful run. It does not reproduce the historical crash, identify its cause, exercise the synchronized-reentry hypothesis or rule out uninstrumented CE callbacks.

Evidence: `goal-stack-trace-live-20261009T111321Z-688b.json`, decoded `goal-stack-trace-20261009T111321Z-688b-{A,B}.json`, and `goal-stack-trace-analysis-20261009T111321Z-688b.json`. The original preparation manifest intentionally retains its preparation-time authorization flag; subsequent execution was authorized explicitly in the conversation.

### Offline trace validator controls

`StackTraceValidation.ps1` classifies decoded measurements, incomplete/frozen/overflow evidence, paired AOB waits and synchronized callback entries strictly inside a wait on the same OS thread. It reports parent/depth correlation without treating ordinary inline nesting or unequal caller/provider state pointers as corruption. It is an observation classifier, not a complete Lua execution proof.

`Test-StackTraceValidation.ps1` passed **11 synthetic controls and both recorded host snapshots**: balanced measurements, actual nested thunk with differing expected provider/callback pointers, timestamp ties at either wait boundary resolved by sequence order, pre-restore underflow, post-restore mismatch, unknown-height handling, unrelated-thread exclusion, interrupted records, ordinary inline nesting, and diagnostic evidence categories. The two recorded snapshots produced no findings/evidence/reentry and three paired waits total. Logs and script hashes are in `goal-stack-trace-validator-tests.log` and `goal-stack-trace-validator-hashes.json`.

The remaining diagnostic gap is an evidence-backed reproduction of the original failure or synchronized reentry; another identical successful baseline would not resolve it. No additional live workload has been scheduled.
