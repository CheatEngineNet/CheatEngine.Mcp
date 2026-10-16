# Full tool qualification

The current catalog contains 193 backend tools and the gateway's `instance_list` tool.
Every tool needs a reviewed success case with known inputs, an independent result or side-effect oracle,
and cleanup verification. Catalog discovery, routing, disabled-feature refusals and compilation of a fixed
Lua body do not establish successful native execution.

## Checklist

- [x] Enumerate the complete advertised catalog.
- [x] Check routing, missing/unknown instance refusal and stale-identity refusal for every backend tool.
- [x] Check gateway-local discovery with zero and two instances.
- [x] Run the portable Release suite; final results are recorded below.
- [x] Run the portable Debug suite with the same exclusions.
- [x] Run standalone Lua 5.3 with CE API doubles (651 passed, zero failed or skipped).
- [x] Add and run result assertions for the Lua, table-save and code-view adapters (12 passed).
- [x] Add seven record adapter cases and four code/process adapter cases; run both focused classes.
- [x] Fix the `code_get_comments` array argument expansion found by those tests.
- [x] Fix `process_list_threads` to use CE's documented zero-based StringList indexer; verify empty, truncated and error cleanup paths.
- [x] Prepare eight additional bounded live cases and review their assertions.
- [x] Attempt the private x64 lifecycle session against the published beta.3 bundle.
- [x] Verify both private hosts stopped, user state was restored and the installed CE files stayed unchanged.
- [ ] Qualify same-process plugin reload: the Settings bridge observes zero plugin rows after `loadPlugin` succeeded.
- [x] Add a separate disabled-capability workflow scenario so independent restart checks can complete.
- [x] Run the eight added live checks and six bounded workflows, then target, gateway and host restarts.
- [x] Map a positive production-adapter result/effect assertion for every backend tool.
- [x] Add SDK provider-stack containment and normal-path preservation regressions.
- [x] Exercise the SDK dispatcher with rooted worker/main coroutines, including queued-stop cleanup.
- [x] Prepare, review and run a bounded provider trace replay; retain non-reproduction as diagnostic evidence only.
- [x] Add direct adapter checks for managed collectors, debugger callbacks, execution, kernel calls, patches and table loading using doubles.
- [x] Update to published Client 1.1.0 and SDK 2.0.1; regenerate portable and AOT lockfiles.
- [x] Require reviewed positive adapter mappings for every name in the production tool catalog.
- [x] Add execution-argument, conversion/calculation and pointer-file boundary/failure tests.
- [x] Measure handwritten product C# coverage using fresh portable and NativeLua reports.
- [x] Rebuild and repeat the disabled-capability workflows on the updated dependency bundle.
- [ ] Supply native managed collector, Mono, debugger event, compiler/ABI, PDB/export, table and patch fixtures.
- [ ] Qualify successful gated operations and DBK/DBVM operations in their required environments.
- [ ] Qualify all tools on the exact replacement product bundle after fixes, including restart and cleanup.

The machine-readable report contains one row for each of the 194 tools. It keeps routing, tests using
doubles, direct fixed-Lua bodies and live results separate. The reviewed
[`behavior-evidence.json`](behavior-evidence.json) maps positive adapter evidence for all 193 backend names.
Each mapped test lists its exact required TRX case names; every case must occur once and pass.
Passing only one branch of a parameterized test cannot qualify another branch's tool.
These cases use Client or CE API doubles, including real standalone Lua where applicable. They are partial
cases, not complete behavior partitions or native CE qualification. The gateway's local discovery checks
bring the advertised catalog total to 194. A live mapping requires its own concrete oracle and cleanup receipts.

## Automated results from 2026-10-16

The dependency refresh uses the published NuGet packages `CheatEngine.Client` **1.1.0** and
`CheatEngine.SDK` **2.0.1**. Both normal and AOT lockfiles were regenerated. Debug and Release builds
completed with zero warnings or errors. The isolated bundled-DLL reflection/load probe and the published
Native AOT gateway smoke check also passed.

Fresh final TRX reports contain:

| Suite | Passed | Failed | Skipped |
| --- | ---: | ---: | ---: |
| Portable Release | 5,314 | 0 | 0 |
| Portable Debug | 5,314 | 0 | 0 |
| Standalone NativeLua | 651 | 0 | 0 |
| Opt-in live Workflows session | 1 | 0 | 0 |

The generated report verifies routing for all **194** advertised tools, positive adapter evidence for
all **193** backend tools, and the exact required parameterized cases. The added
`AllToolBehaviorEvidenceTests` derives its names from the production catalog and checks that every
adapter mapping references an existing public test, source file, oracle and case list. This structural
guard does not execute the mapped test or prove its outcome; the report checks the fresh TRX results.
The report records input hashes but does not independently attest that a supplied TRX came from current source.

Additional tests pin execution argument types and numeric limits, indexed validation errors, timeout
endpoints and symbol-lookup failure semantics. Conversion/calculator tests cover byte order, fixed scalar
values, operator precedence, invalid expressions and resource limits. Pointer-file tests cover schema
and snapshot validation, incomplete coverage, chunked nonseekable reads, stream ownership and failures
before writes. These checks use deterministic inputs and result, error or side-effect assertions.

Combined portable and standalone Lua coverage reaches **20,193 / 21,369 handwritten product C# lines
(94.50%)**. Hits are unioned by source file and line; generated files and test code are excluded.
This percentage does not measure embedded Lua branch coverage or prove native host behavior.

| Product project | Covered / instrumented lines | Line coverage |
| --- | ---: | ---: |
| Core | 4,357 / 4,581 | 95.11% |
| Gateway entry point | 9 / 10 | 90.00% |
| Hosting | 1,228 / 1,348 | 91.10% |
| Plugin | 794 / 851 | 93.30% |
| Prompts | 704 / 709 | 99.29% |
| Resources | 222 / 222 | 100.00% |
| Tools | 12,879 / 13,648 | 94.37% |

The separate static source-to-test pairing analyzer could not parse this repository's C# 14 extension
blocks with its bundled Roslyn 4.14. Its partial classifications are not used as coverage evidence.

The current private x64 run `20261016T174241Z-e914` passed the eight additional checks, six bounded
workflows and one restart each of target A, gateway and host A. All four optional gates were explicitly
disabled, and zero plugin reload cycles were requested. The session passed in about 37 seconds, restored
user state, left the source installation unchanged and reported no cleanup failures. The runner also
confirmed that no recorded owned processes remained. Its 813 prepared inputs matched their manifest.

The report verifies **37 backend tools** against this run's exact primary and cleanup receipts.
The other **156 backend tools** still lack native success evidence; gateway-local `instance_list` has
its separate discovery tests. Same-process plugin reload and the native fixtures listed above remain
unqualified. `nativeQualificationComplete` remains false.

Current private bundle SHA-256 values:

- DLL: `8C01D51F78642FF036E4616032A541D5DE2BD534006D9FF51C8C97B81FC33AE8`
- Gateway: `664EB1CD2756ED076C443446A6D09A151CEB30CA43BB69BC2392555C3BEFD2E3`

Fresh TRX and Cobertura files are under `artifacts/test-results/all-tools-20261016/` in
`portable-reviewed`, `portable-debug-reviewed`, `native-reviewed` and `live`. The per-tool report is
`tool-report-final/tool-results.json`, and the deduplicated line totals are in
`product-coverage-final.json`. These local artifacts and raw native logs are not committed.
Installed and released binaries have not been replaced by this work.

## Automated results from 2026-10-11

The final portable suites passed in both Debug and Release: 5,046 tests each, zero failures or skips.
The final standalone NativeLua suite passed 651 tests, zero failures or skips. The report generated from
the Release and NativeLua TRX files verifies positive adapter evidence for all 193 backend tools.
It also verifies routing for all backend tools and the gateway's own discovery cases.

[`live-evidence.json`](live-evidence.json) maps 37 positive native tool cases from the reviewed workflow
session below. Each case requires its primary receipt and any mapped cleanup receipt, along with a passed
session, restored user state, an unchanged source installation and no cleanup failures. These 37 cases
do not establish native qualification of the entire catalog.

## Failure observed on 2026-10-11

The ordinary x64 lifecycle session used the published `2.0.0-beta.3+059f249cc3293dff65b19ca257ecfe196d20aac1`
plugin and gateway, with all four optional gates disabled. Reading the second instance's runtime resource
failed with `The Lua stack does not contain the inputs required by the protected operation.`
The stack trace reaches `LuaProtectedApi.PushBytes`, `LuaState.TryGetGlobal` and
`MainThreadDispatcher.Dispatch` before the MCP operation body starts. This identifies the failure
boundary; it does not prove its underlying cause. No automatic native retry was made.

The private run is `20261011T134351Z-8024`. Its final summary reports `passed: false`,
`userStateRestored: true`, `sourceInstallationUnchanged: true` and no cleanup failures.
The eight added live cases were not reached in that run. Raw logs and discovery material stay
in the private run directory; they are not committed.

The `code_get_comments` fix is currently a source change. The installed/released beta.3 bundle has not
been replaced by this work. The regression checks two ordered addresses through the actual managed
adapter and real Lua 5.3 with CE API doubles, avoiding the array-shape bypass in the previous compile-only case.

## Later native results from 2026-10-11

The ordinary SDK candidate first reached the new checks in `20261011T144953Z-1ce8`, exposing an incorrect
test assumption that a target selected before plugin activation must have a positive selection epoch.
Client legitimately starts at zero; the test now checks a nonnegative epoch and verifies that setting the
same pointer size preserves it.

Run `20261011T145414Z-f9a6` then found a production bug: `process_list_threads` read `list.Strings[i]`,
but the installed CE reference documents `list[i]` (or `list.String[i]`). The previous double supplied
the unsupported property. The fix uses `list[i]`, and regression doubles expose only numeric entries.
All 637 standalone Lua tests passed after that fix; further adapter additions bring the final total to 651.

Candidate 2 ran all eight added checks and six workflows successfully in `20261011T150242Z-86a0`,
then failed at the separate Plugin Manager bridge: its checklist remained empty. The earlier experiment
that retained the empty `Plugins64` key failed the same way. Installed CE 7.7 internals are not available;
we have no supported Lua workaround for host-driven disable/re-enable. The exact plugin-row guard stays intact.

The dedicated `workflows` scenario passed in `20261011T151957Z-8cfc` in 33.5 seconds. It explicitly
stages all four optional gates disabled, checks the eight tools and six workflows, restarts target A,
restarts the gateway, then restarts host A while checking host B and rejecting stale identities. It records
zero plugin cycles. Its final summary reports `passed: true`, `userStateRestored: true`,
`sourceInstallationUnchanged: true` and an empty cleanup-failure list. No recorded owned processes remained.

That private candidate uses SDK `2.0.1-mcpqualification.1` with the provider guards below. Its bundled DLL
and AOT gateway passed isolated catalog/load probes and gateway smoke checks. DLL SHA-256:
`F0DA5938E8CABE34C7BED9CCEE16FFE92C5731D9E37A9F2546835576DA594D68`; gateway SHA-256:
`E8547E93BF97BE9D0EFA07E225A27A4A6B41000454834CBF7B03A7FD10664C44`.
The private runner verified 930 input hashes before launch. This candidate is neither installed nor released.

Its ordinary native bridge uses the checked-in SDK binary (`889DC4C2...`), whereas the diagnostic package
used the released CI-built binary (`B008C8D8...`). Their recorded native source/build fingerprints match;
the binaries are not byte-identical. PE and package audits passed. Successful private runs do not establish
that the original released-bundle dispatcher failure is resolved.

## SDK investigation from 2026-10-11

The SDK source now refuses a provider state with a negative stack top before stamp verification or dispatch.
It also verifies that stamp checking preserves the stack top. Both failures release the operation admission
through the existing exception path; they do not reset the stack or retry the provider. These checks contain
an invalid state. They do not fix or explain the original corruption.

Standalone SDK evidence: 278 native-Lua tests passed, 14 main-thread/coroutine tests passed, and the focused
provider/runtime run passed 14 tests. The rooted-coroutine fixture models the published CE 7.5 topology;
it does not establish the internals of the installed CE 7.7 host.

The private diagnostic package `CheatEngine.SDK.2.0.1-mcpprovidertrace.2` was used in
`20261011T144032Z-1764`. The bounded resource/memory/scanner/AOB replay passed, restored user state,
left the installation unchanged and stopped all recorded owned processes. Host A recorded 3,593 trace
events and host B recorded 266. All 397 provider-returned states had nonnegative tops and stamp checking
preserved them. Neither trace froze, overflowed nor contained an incomplete record. The failure did not
reproduce; this is not root-cause resolution or full native qualification.

An earlier candidate replay, `20261011T143815Z-b0c8`, also passed and restored state, but no trace was
created because the private harness had not forwarded the trace opt-in to its sanitized child environment.
That harness omission was corrected and reviewed before the recorded replay. Neither replay reached the
eight additional lifecycle checks. Raw traces, host logs and discovery material remain private.

Client's dispatcher delegates to synchronous SDK invocation. No Client production change is supported by
the observed failure. Installed and released binaries remain unchanged.

## Reproduce the evidence report

Build and run the repository's portable and NativeLua categories as documented in
`tests/CheatEngine.Mcp.Tests/README.md`, with TRX reports enabled. Then run:

```powershell
./eng/Write-AllToolQualificationReport.ps1 `
  -GatewayTrx artifacts/test-results/all-tools-20261016/portable-reviewed/CheatEngine.Mcp.Tests_net10.0_x64.trx `
  -PortableTrx artifacts/test-results/all-tools-20261016/portable-reviewed/CheatEngine.Mcp.Tests_net10.0_x64.trx `
  -NativeLuaTrx artifacts/test-results/all-tools-20261016/native-reviewed/CheatEngine.Mcp.Tests_net10.0_x64.trx `
  -OutputDirectory artifacts/test-results/all-tools-20261016/tool-report-final
```

The output is `tool-results.json` inside that output directory. Supply `-LiveSummary` with the private
session's `summary.json` to include its outcome. A receipt from a failed session is marked
`observed_in_failed_session`, never `verified`. Missing or duplicate routing cases leave their row
incomplete. `nativeQualificationComplete` stays false until the full per-tool native acceptance matrix
is implemented and reviewed; this report does not infer qualification from suite counts.
