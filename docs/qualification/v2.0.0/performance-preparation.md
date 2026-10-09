# Isolated performance preparation

Status: isolated implementation, clean Release build, focused managed regressions and final Astra high review complete. No new native performance, lifecycle, or soak run is authorized or claimed.

The snapshot is `artifacts/performance-preparation/source-f695c724e788433c97cb34470f17278a`. It was copied from the current working tree at HEAD `0f722ef7d6b37eee0985073dfe594c6559b31c14`; it is not a clean release commit. Changes below have not been integrated into the primary source tree or the ordinary product package. The prepared [Settings observation](settings-observation.md) and its two 859-file manifests remain unchanged while its execution decision is pending.

## Workload matrix

The earlier performance runner measured only short scalar reads and 16-item batches. The isolated expansion adds fixed work sizes, warmups and sample counts for larger batches, regions, named scans, AOB, baseline-compatible `pointer_read_chain`, module/symbol resolution, structures, dissection and discovery. It retains three separately started published-beta.2 baseline sessions and three candidate sessions, with exact published baseline plugin/gateway hashes checked before use.

Samples measure complete client workflows, including transport, decoding, assertions and any explicitly included polling/reset/cleanup. They do not isolate CE native execution time. Scan/dissection polling deadlines begin after their start calls return; those calls can themselves block or outlive a client timeout. The current 100 ms short-call allowance and 1.5-times bounded-work threshold remain the planned acceptance policy in [acceptance.md](acceptance.md#performance-acceptance), not measured results.

All 190 shared baseline/current backend input schemas match after ignoring only documentation fields (`title` and `description`). That comparison does not establish output or native-behavior compatibility. The new runner deliberately uses the baseline's single-chain pointer tool, not the newer batch-chain tool.

The first independent Astra review found scanner reuse without reset, incomplete result assertions, mismatched profile validation gaps, ownership recorded too late after mutations, an AOB range extending beyond its stated allocation, and pass recording before gateway disposal. All were repaired. A second pass found that scanner cleanup could replace the original failure, and that a future runtime receipt would incorrectly label measured work as unexecuted. The repaired cleanup helper preserves both errors; its four outcome combinations passed managed regression tests. Final Astra high review found no remaining actionable findings in the prepared matrix and pointer-retention patch.

Pinned public CE source synchronously waits for dissection work inside its Lua binding: [LuaDissectCode.pas, lines 31–83](https://github.com/cheat-engine/cheat-engine/blob/ec45d5f47f92a239ba0bf51ec5d04a7509c3fd37/Cheat%20Engine/LuaDissectCode.pas#L31-L83) calls `waitTillDone` after `dowork`; [DissectCodeThread.pas, lines 420–432](https://github.com/cheat-engine/cheat-engine/blob/ec45d5f47f92a239ba0bf51ec5d04a7509c3fd37/Cheat%20Engine/DissectCodeThread.pas#L420-L432) implements the completion wait. Installed-binary equivalence remains unproven. The harness must retain the owned target buffer through uncertain start, response, poll or clear outcomes and leave its final containment to owned target shutdown. A stop receipt alone does not prove that native work ended.

## Pointer-reference cleanup retention

The resource audit found one concrete retention gap. A live pointer-reference range search creates a temporary value-scan session. If releasing it fails, the tool correctly tracks the session, but previously allowed another range search to append another failed session indefinitely. Lua ledger and managed-job limits do not cap these directly tracked Client leases.

The isolated fix reserves one range-search slot before dispatch. Successful cleanup or failure before creating a session releases that reservation; failed cleanup retains it until the resource's release/acknowledgement callback runs. Identity comparison prevents an older callback from clearing a newer search's reservation. Exact AOB and stored-map paths do not use this temporary session. A failure to register the retained resource conservatively leaves the slot closed; a new activation is the recovery boundary if no resource was registered.

The six focused managed cases passed and cover repeated cleanup-failure refusal, scan failure followed by cleanup failure, acknowledgement, retryable cleanup followed by successful release, reentry, and pre-body dispatch failure. Independent Astra source review found no actionable correctness defect in this scoped fix.

## Managed verification

- Release build: zero warnings and errors (`performance-final-build.log` in the snapshot).
- Final performance/lifecycle helper selection: 20 passed, zero failed/skipped (`performance-final-tests.log` and `artifacts/performance-final-results/*.trx`).
- All pointer regressions: 130 passed, zero failed/skipped (`pointer-managed-regressions.log` and `artifacts/pointer-managed-regressions/*.trx`). This separate run preceded the final performance-only cleanup repair; pointer source and tests did not change afterward.
- Source inventory: 856 verified files, exactly six changed/new files relative to the initial snapshot. Exact source, build-log and TRX hashes are retained in `artifacts/performance-preparation/preparation-evidence.json`.
- Both primary Settings no-execution preflights still verified 859 inputs. No native process was started.

| Requirement | Managed evidence |
| --- | --- |
| Reject missing or mismatched paired workload evidence | `Pair_RefusesBaselineMissingOneMatrixWorkload`, `Pair_RefusesCandidateWithWrongFixedSampleCount`, `Pair_RefusesBaselineWithWrongFixedSampleCount` |
| Preserve a scan failure when cleanup also fails | `RunWithCleanupAsync_PreservesBothFailuresAndAlwaysReleases` (four combinations) |
| Prevent repeated failed pointer scans from accumulating resources | `FailedCleanup_RefusesRepeatedRangeSearchesWithoutAddingResources` (two cases) |
| Permit subsequent searches after confirmed release or acknowledgement | `RetryableCleanup_HoldsReservationUntilReleaseSucceeds`, `AcknowledgedFailedCleanup_AllowsANewRangeSearch` |
| Refuse reentry and recover from pre-body dispatch failure | `ReentrantRangeSearch_IsRefusedBeforeAnotherDispatch`, `DispatchFailure_ReleasesReservationForTheNextRangeSearch` |

Successful commands, run from the isolated snapshot:

```powershell
dotnet build tests/CheatEngine.Mcp.Tests/CheatEngine.Mcp.Tests.csproj -c Release --no-restore -p:EnableSourceControlManagerQueries=false -p:RepositoryCommit=0f722ef7d6b37eee0985073dfe594c6559b31c14 -v:minimal
dotnet artifacts/bin/CheatEngine.Mcp.Tests/release/CheatEngine.Mcp.Tests.dll --filter-class CheatEngine.Mcp.Tests.LiveQualification.LivePerformanceMatrixTests --filter-class CheatEngine.Mcp.Tests.LiveQualification.LiveLifecycleQualificationTests --fail-skips on --report-trx --results-directory artifacts/performance-final-results
dotnet artifacts/bin/CheatEngine.Mcp.Tests/release/CheatEngine.Mcp.Tests.dll --filter-class 'CheatEngine.Mcp.Tests.Tools.Pointer.*' --fail-skips on --report-trx --results-directory artifacts/pointer-managed-regressions
```

The first focused test attempt was 32/33: the retryable-release test used the fixture's default Lua ledger, whose fixed-Lua executor is deliberately unavailable in portable tests. It was corrected by injecting a managed-only `TargetResources` into that one test, after which all 33 focused tests passed. The final cleanup regression addition then passed its 20-test selection. No native substitute, broad native suite, full portable-suite rerun, or candidate-package rebuild is claimed.

## Remaining requirements

- The native matrix is unexecuted; roadmap 9.01 and its performance acceptance remain unchecked.
- Multi-client concurrency/UI and resource/completion measurements are now implemented, managed-tested and Astra-reviewed in a separate [admission preparation](admission-preparation.md) snapshot. Native 9.03/9.05 qualification remains outstanding.
- CE-backed module completion uses dispatch admission, but cached and empty responses cannot prove a fresh native call or a busy refusal. A 750 ms completion wait can return while its native refresh continues.
- Package-matched Client/SDK source confirms that collection caps apply after full native enumeration. A separate [prepared-navigation repair](navigation-preparation.md) now redirects module/detail/region resources and module completion to explicit five-second snapshots and passes 4,207 portable tests. It is not integrated into the primary source/package; explicit source-tool native enumeration remains unbounded by copy/page limits.
- The two-hour soak, lifecycle failure, historical SDK stack failure, broader native/compiler/installation/client matrix, final clean-candidate checks and publication are still outstanding. Issues #28, #31 and #33 cannot be closed from this preparation.
